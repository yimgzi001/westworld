import importlib.util, pathlib, tempfile, unittest, uuid, json, io, threading, time
from unittest.mock import patch
spec = importlib.util.spec_from_file_location("controller", pathlib.Path(__file__).with_name("controller.py"))
c = importlib.util.module_from_spec(spec); spec.loader.exec_module(c)
OPTIONS = {"model":"gpt-6.1-terra","reasoning_effort":"none"}
SCHEMA = {"type":"object","properties":{}}
DECISION = {"goal":"inspect","steps":[]}
def body(status="completed", role="assistant"):
    return {"status":status,"model":"gpt-6.1-terra","usage":{"input_tokens":12,"output_tokens":5,"total_tokens":17},
            "output":[{"type":"message","role":role,"content":[{"type":"output_text","text":json.dumps(DECISION)}]}]}
class Tests(unittest.TestCase):
    def test_payload_exact(self):
        calls=[]
        def transport(payload,key,timeout):
            calls.append(payload); self.assertEqual(timeout,45); return body()
        d,a=c.infer("same prompt",SCHEMA,"fake-test-only",**OPTIONS,transport=transport)
        self.assertEqual(d,DECISION); self.assertEqual(a["usage"]["total_tokens"],17)
        self.assertEqual(calls[0]["input"],"same prompt")
        self.assertEqual(calls[0]["text"]["format"]["schema"],SCHEMA)
        self.assertFalse(calls[0]["store"]); self.assertEqual(calls[0]["reasoning"],{"effort":"none"})
        self.assertNotIn("fake-test-only",json.dumps(a))
    def test_dynamic_survival_input_uses_explicit_cache_mode(self):
        def transport(payload, key, timeout):
            self.assertEqual(payload["prompt_cache_options"], {"mode": "explicit"})
            self.assertEqual(payload["input"], "current visible terrain")
            result = body()
            result["usage"]["input_tokens_details"] = {"cached_tokens": 0, "cache_write_tokens": 0}
            return result

        decision, audit = c.infer("current visible terrain", SCHEMA, "fake-test-only", **OPTIONS,
                                  transport=transport, implicit_cache=False)
        self.assertEqual(decision, DECISION)
        self.assertEqual(audit["cacheMode"], "explicit")
        self.assertEqual(audit["usage"]["input_tokens_details"], {"cached_tokens": 0, "cache_write_tokens": 0})
    def test_incomplete_usage(self):
        d,a=c.infer("p",SCHEMA,"unused",**OPTIONS,transport=lambda *a:body("incomplete"))
        self.assertIsNone(d); self.assertEqual(a["usage"]["input_tokens"],12)
    def test_nonassistant(self):
        d,a=c.infer("p",SCHEMA,"unused",**OPTIONS,transport=lambda *a:body(role="user"));self.assertIsNone(d)
    def test_refusal(self):
        b=body();b["output"][0]["content"]=[{"type":"refusal","refusal":"not logged"}]
        d,a=c.infer("p",SCHEMA,"unused",**OPTIONS,transport=lambda *a:b);self.assertIsNone(d);self.assertEqual(a["error"],"model_refusal")
    def test_timeout_sanitized(self):
        def transport(*a):raise TimeoutError("secret header do not log")
        d,a=c.infer("p",SCHEMA,"unused",**OPTIONS,transport=transport);self.assertIsNone(d);self.assertNotIn("secret",json.dumps(a))
    def test_bounds(self):
        for prompt in ["","x"*32769]:
            with self.assertRaises(c.ControllerError):c.infer(prompt,SCHEMA,"unused",**OPTIONS)
        with self.assertRaises(c.ControllerError):c.infer("p",SCHEMA,"unused",**OPTIONS,timeout=46)
        with self.assertRaises(c.ControllerError):c.validate_decision({"goal":"x","steps":[{}]*5})
    def test_survival_run_can_follow_host_without_budget(self):
        root = pathlib.Path("unused")
        with patch.object(c, "_file_mode") as run:
            with self.assertRaisesRegex(c.ControllerError, "run_bound"):
                c.file_mode(root, "fake-test-only", root, **OPTIONS, max_requests=13)
            with self.assertRaisesRegex(c.ControllerError, "run_bound"):
                c.file_mode(root, "fake-test-only", root, **OPTIONS, total_seconds=651)
            for budget in ({"max_requests": 0}, {"total_seconds": -1}):
                with self.assertRaisesRegex(c.ControllerError, "run_bound"):
                    c.file_mode(root, "fake-test-only", root, **OPTIONS, survival_run=True, **budget)
            c.file_mode(root, "fake-test-only", root, **OPTIONS, survival_run=True,
                        max_requests=None, total_seconds=None, parallelism=2, lifecycle=object())
            run.assert_called_once()
            self.assertIsNone(run.call_args.kwargs["max_requests"])
            self.assertIsNone(run.call_args.kwargs["total_seconds"])
    def test_ids(self):
        r={"protocol":"westworld-tool-file-v1","prompt":"p","responseSchema":SCHEMA}
        r.update({k:str(uuid.uuid4()) for k in c.IDS})
        self.assertEqual(c.request_envelope(r),{k:r[k] for k in c.IDS})
        r["requestId"]="bad"
        with self.assertRaises(c.ControllerError):c.request_envelope(r)
    def test_duplicates(self):
        with self.assertRaises(c.ControllerError):c.decode('{"goal":"x","goal":"y"}')
    def test_atomic(self):
        with tempfile.TemporaryDirectory() as d:
            path=pathlib.Path(d)/"response.json";c.atomic_json(path,DECISION)
            self.assertEqual(json.loads(path.read_text()),DECISION);self.assertFalse(path.with_suffix(".tmp").exists())
            with self.assertRaises(c.ControllerError):c.atomic_json(path,DECISION)
    def test_redirect_no_forward(self):
        handler=c.NoRedirect()
        request=c.urllib.request.Request(c.URL,headers={"Authorization":"Bearer fake-test-only"})
        for code in [301,302,303,307,308]:
            with self.assertRaisesRegex(c.ControllerError,"redirect_rejected"):
                handler.redirect_request(request,None,code,"redirect",{},"https://other.example")
    def test_http_error_no_body(self):
        class Opener:
            def open(self,request,timeout):
                raise c.urllib.error.HTTPError(c.URL,401,"error",{},None)
        with patch.object(c.urllib.request,"build_opener",return_value=Opener()):
            with self.assertRaisesRegex(c.ControllerError,"http_status_401"):c.http_post({},"unused",45)
    def test_supported_https_opener(self):
        class Response:
            status=200
            def __enter__(self):return self
            def __exit__(self,*a):pass
            def geturl(self):return c.URL
            def read(self,n):
                self.bound=n
                return json.dumps(body()).encode()
        response=Response()
        class Opener:
            def open(self,request,timeout):
                self.request=request;self.timeout=timeout
                return response
        opener=Opener()
        with patch.object(c.urllib.request,"build_opener",return_value=opener) as build:
            self.assertEqual(c.http_post({},"fake-test-only",45)["status"],"completed")
            self.assertTrue(any(isinstance(x,c.NoRedirect) for x in build.call_args.args))
            self.assertFalse(any(isinstance(x,c.urllib.request.ProxyHandler) for x in build.call_args.args))
        self.assertEqual(opener.request.full_url,c.URL)
        self.assertEqual(opener.timeout,45)
        self.assertEqual(response.bound,c.MAX_BYTES+1)
    def test_file_once(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d)/"control";root.mkdir();req=root/"001";req.mkdir()
            r={"protocol":"westworld-tool-file-v1","prompt":"p","responseSchema":SCHEMA,**{k:str(uuid.uuid4()) for k in c.IDS}}
            (req/"request.json").write_text(json.dumps(r))
            with patch.object(c,"infer",return_value=(DECISION,{"usage":{"total_tokens":17}})) as call:
                c.file_mode(root,"unused",pathlib.Path(d)/"audit.jsonl",**OPTIONS,max_requests=1,total_seconds=1)
                self.assertEqual(call.call_count,1)
            result=json.loads((req/"response.json").read_text())
            self.assertEqual({k:result[k] for k in c.IDS},{k:r[k] for k in c.IDS})
            self.assertEqual(result["decision"],DECISION)

    def test_host_exit(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d)/"control";root.mkdir()
            (root.parent/"exit.txt").write_text("0")
            with patch.object(c,"infer") as call:
                c.file_mode(root,"unused",pathlib.Path(d)/"audit",**OPTIONS,total_seconds=1)
                call.assert_not_called()
    def test_getpass_no_plaintext_fallback(self):
        with patch.object(c, "configured_key", return_value=None), patch.object(
                c.getpass, "getpass", side_effect=c.getpass.GetPassWarning("no tty")):
            with self.assertRaises(c.getpass.GetPassWarning):c.secure_key()
    def test_key_environment_skips_prompt(self):
        with patch.dict(c.os.environ, {c.KEY_ENV_VAR: "FAKE-PROJECT-KEY"}), patch.object(c.getpass, "getpass") as prompt:
            self.assertEqual(c.secure_key(), "FAKE-PROJECT-KEY")
            prompt.assert_not_called()
    def test_key_environment_rejects_invalid_value(self):
        with patch.dict(c.os.environ, {c.KEY_ENV_VAR: "FAKE KEY"}), patch.object(c.getpass, "getpass") as prompt:
            with self.assertRaisesRegex(c.ControllerError, "invalid_key"):
                c.secure_key()
            prompt.assert_not_called()
    def test_failure_not_retried(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d)/"control";root.mkdir();req=root/"001";req.mkdir()
            r={"protocol":"westworld-tool-file-v1","prompt":"p","responseSchema":SCHEMA,**{k:str(uuid.uuid4()) for k in c.IDS}}
            (req/"request.json").write_text(json.dumps(r))
            with patch.object(c,"infer",return_value=(None,{"error":"timeout"})) as call:
                c.file_mode(root,"unused",pathlib.Path(d)/"audit",**OPTIONS,max_requests=1,total_seconds=1)
                self.assertEqual(call.call_count,1)
            self.assertFalse((req/"response.json").exists())

    def test_unverified_model_no_fallback(self):
        with self.assertRaisesRegex(c.ControllerError,"unverified_model"):
            c.infer("p",SCHEMA,"unused",model="unknown-terra-name",reasoning_effort="none")
        with self.assertRaises(TypeError):
            c.infer("p",SCHEMA,"unused")
        with self.assertRaisesRegex(c.ControllerError,"unverified_model"):
            c.infer("p",SCHEMA,"unused",model="gpt-6.1-terra",reasoning_effort="unknown")
    def test_wait_for_host_creation(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d)/"control";clock=[0]
            def sleep(seconds):
                clock[0]+=seconds
                self.assertFalse(root.exists())
                root.mkdir()  # Simulated host, never controller.
            with patch.object(c.time,"monotonic",side_effect=lambda:clock[0]),patch.object(c.time,"sleep",side_effect=sleep):
                self.assertEqual(c.wait_for_control(root,60),root)
            self.assertEqual(clock[0],.25)
    def test_wait_timeout_no_mkdir(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d)/"control";clock=[0]
            with patch.object(c.time,"monotonic",side_effect=lambda:clock[0]),patch.object(c.time,"sleep",side_effect=lambda seconds:clock.__setitem__(0,clock[0]+seconds)):
                with self.assertRaisesRegex(c.ControllerError,"wait_timeout"):c.wait_for_control(root,1)
            self.assertFalse(root.exists());self.assertEqual(clock[0],1)
    def test_exit_before_control(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d)/"control";(root.parent/"exit.txt").write_text("0")
            self.assertIsNone(c.wait_for_control(root,c.time.monotonic()+60))
            self.assertFalse(root.exists())
    def test_wait_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            target=pathlib.Path(d)/"target";target.mkdir()
            root=pathlib.Path(d)/"control";root.symlink_to(target,target_is_directory=True)
            with self.assertRaisesRegex(c.ControllerError,"symlink"):c.wait_for_control(root,c.time.monotonic()+60)
    def test_wait_in_total_budget(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d)/"control";clock=[0]
            with patch.object(c.time,"monotonic",side_effect=lambda:clock[0]),patch.object(c.time,"sleep",side_effect=lambda seconds:clock.__setitem__(0,clock[0]+seconds)),patch.object(c,"infer") as infer:
                with self.assertRaisesRegex(c.ControllerError,"wait_timeout"):
                    c.file_mode(root,"unused",pathlib.Path(d)/"audit",**OPTIONS,total_seconds=1)
                infer.assert_not_called()
            self.assertEqual(clock[0],1);self.assertFalse(root.exists())
    def test_source_identity_frozen_at_import(self):
        import hashlib
        expected=hashlib.sha256(pathlib.Path(c.__file__).read_bytes()).hexdigest()
        self.assertEqual(c.DRIVER_SOURCE_SHA256,expected)
        with patch.object(pathlib.Path,"read_bytes",side_effect=AssertionError("infer must not reread mutable source")):
            _,audit=c.infer("p",SCHEMA,"unused",**OPTIONS,transport=lambda *a:body())
        self.assertEqual(audit["driverSourceSha256"],expected)
        self.assertEqual(audit["maxOutputTokens"],512)
    def test_wait_bound(self):
        with self.assertRaisesRegex(c.ControllerError,"wait_bound"):
            c.file_mode(pathlib.Path("/not-created"),"unused",pathlib.Path("/unused"),**OPTIONS,wait_seconds=61)




class WorkerTests(unittest.TestCase):
    def fake_process(self, raw=b'{"body":{"status":"completed"}}', code=0, timeout=False):
        import subprocess
        class CapturedInput(io.BytesIO):
            def write(self, packet):
                self.packet = packet
                return super().write(packet)

        class Fake:
            def __init__(self):
                self.stdin=CapturedInput();self.stdout=io.BytesIO(raw)
                self.returncode=None;self.killed=False;self.reaped=False;self.calls=[]
            def wait(self,timeout=None):
                self.calls.append(timeout)
                if timeout is not None and fail_timeout:
                    raise subprocess.TimeoutExpired("fixed-worker",timeout)
                if self.returncode is None:self.returncode=code
                self.reaped=True
                return self.returncode
            def kill(self):self.killed=True;self.returncode=-9
            def poll(self):return self.returncode
        fail_timeout=timeout
        return Fake()
    def test_fixed_argv_pipe_only(self):
        process=self.fake_process()
        with patch.dict(c.os.environ, {c.KEY_ENV_VAR: "FAKE-SECRET-ONLY"}), patch.object(
                c.subprocess, "Popen", return_value=process) as start:
            self.assertEqual(c.isolated_http_post({},"FAKE-SECRET-ONLY",1),{"status":"completed"})
        args,kwargs=start.call_args
        self.assertNotIn("FAKE-SECRET-ONLY",repr(args))
        self.assertFalse(c.KEY_ENV_VAR in kwargs["env"], "HTTP 工作进程继承了密钥环境变量")
        self.assertEqual(args[0][-1],"--http-worker");self.assertFalse(kwargs["shell"])
        self.assertEqual(kwargs["stderr"],c.subprocess.DEVNULL)
        self.assertIn(b"FAKE-SECRET-ONLY",process.stdin.packet);self.assertTrue(process.reaped)
    def test_timeout_kill_reap_unknown_usage(self):
        process=self.fake_process(timeout=True)
        with patch.object(c.subprocess,"Popen",return_value=process):
            decision,audit=c.infer("p",SCHEMA,"FAKE-SECRET-ONLY",**OPTIONS)
        self.assertIsNone(decision);self.assertEqual(audit["error"],"timeout")
        self.assertIsNone(audit["usage"]);self.assertTrue(process.killed);self.assertTrue(process.reaped)
        self.assertNotIn("FAKE-SECRET-ONLY",json.dumps(audit))
        self.assertEqual(len(process.calls),2)
    def test_output_bounded(self):
        process=self.fake_process(b'x'*(c.MAX_BYTES+10000))
        with patch.object(c.subprocess,"Popen",return_value=process):
            with self.assertRaisesRegex(c.ControllerError,"worker_output_bound"):
                c.isolated_http_post({},"fake",1)
        self.assertTrue(process.killed);self.assertTrue(process.reaped)
    def test_worker_exit(self):
        process=self.fake_process(code=3)
        with patch.object(c.subprocess,"Popen",return_value=process):
            with self.assertRaisesRegex(c.ControllerError,"worker_failed"):c.isolated_http_post({},"fake",1)
    def test_worker_error_cannot_log_key(self):
        process=self.fake_process(b'{"error":"FAKE-SECRET-ONLY"}')
        with patch.object(c.subprocess,"Popen",return_value=process):
            _,audit=c.infer("p",SCHEMA,"FAKE-SECRET-ONLY",**OPTIONS)
        self.assertNotIn("FAKE-SECRET-ONLY",json.dumps(audit));self.assertIsNone(audit["usage"])
    def test_credential_echo_rejected(self):
        reply=body();reply["model"]="FAKE-SECRET-ONLY"
        _,audit=c.infer("p",SCHEMA,"FAKE-SECRET-ONLY",**OPTIONS,transport=lambda *a:reply)
        self.assertEqual(audit["error"],"credential_echo_rejected")
        self.assertNotIn("FAKE-SECRET-ONLY",json.dumps(audit))
    def test_real_fixed_worker_invalid_packet_no_http(self):
        # Fixed entrypoint exits without touching HTTP for malformed stdin.
        result=c.subprocess.run([c.sys.executable,"-I",str(pathlib.Path(c.__file__).resolve()),"--http-worker"],
                                input=b'{}',capture_output=True,timeout=5,check=True)
        self.assertEqual(json.loads(result.stdout),{"error":"worker_protocol"});self.assertEqual(result.stderr,b'')


class RecoveryClaimsTests(unittest.TestCase):
    def test_claim_has_no_prompt_or_key_and_canonical_id(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d)
            envelope={k:str(uuid.uuid4()) for k in c.IDS}
            self.assertTrue(c.claim_request(root,envelope,"private game text",SCHEMA,**{"model":"gpt-6.1-terra","effort":"none"}))
            text=next((root/".request-claims").glob("*.json")).read_text()
            self.assertNotIn("private game text",text)
            envelope["requestId"]=envelope["requestId"].upper()
            self.assertFalse(c.claim_request(root,envelope,"changed",SCHEMA,"gpt-6.1-terra","none"))
    def test_partial_claim_blocks(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d);(root/".request-claims").mkdir()
            envelope={k:str(uuid.uuid4()) for k in c.IDS}
            (root/".request-claims"/(envelope["requestId"]+".json")).write_text("{")
            self.assertFalse(c.claim_request(root,envelope,"p",SCHEMA,"gpt-6.1-terra","none"))
    def test_claim_directory_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d);(root/"elsewhere").mkdir();(root/".request-claims").symlink_to(root/"elsewhere",target_is_directory=True)
            with self.assertRaises(c.ControllerError):
                c.claim_request(root,{k:str(uuid.uuid4()) for k in c.IDS},"p",SCHEMA,"gpt-6.1-terra","none")
    def test_restart_failure_never_reposts(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d)/"control";root.mkdir();req=root/"001";req.mkdir()
            r={"protocol":"westworld-tool-file-v1","prompt":"p","responseSchema":SCHEMA,**{k:str(uuid.uuid4()) for k in c.IDS}}
            (req/"request.json").write_text(json.dumps(r))
            with patch.object(c,"infer",return_value=(None,{"error":"timeout"})) as call:
                c.file_mode(root,"unused",pathlib.Path(d)/"audit1",**OPTIONS,max_requests=1,total_seconds=1)
                c.file_mode(root,"unused",pathlib.Path(d)/"audit2",**OPTIONS,max_requests=1,total_seconds=1)
                self.assertEqual(call.call_count,1)
            record=json.loads((pathlib.Path(d)/"audit2").read_text())
            self.assertEqual(record["error"],"prior_request_requires_reconciliation")
            self.assertIsNone(record["usage"])


class HostDeadlineTests(unittest.TestCase):
    def test_deadline_and_cancel(self):
        with tempfile.TemporaryDirectory() as d:
            p=pathlib.Path(d)
            future=(c.datetime.datetime.now(c.datetime.timezone.utc)+c.datetime.timedelta(seconds=10)).isoformat()
            self.assertGreater(c.host_remaining({"deadlineUtc":future},p),0)
            self.assertLessEqual(c.host_remaining({"deadlineUtc":future},p),10)
            self.assertEqual(c.host_remaining({"deadlineUtc":"2000-01-01T00:00:00Z"},p),0)
            for value in [12,"bad","2026-01-01T00:00:00"]:
                with self.assertRaises(c.ControllerError):c.host_remaining({"deadlineUtc":value},p)
            (p/"canceled.json").write_text("{}")
            self.assertEqual(c.host_remaining({"deadlineUtc":future},p),0)
    def test_expired_never_calls(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d)/"control";root.mkdir();p=root/"001";p.mkdir()
            request={"protocol":"westworld-tool-file-v1","prompt":"p","responseSchema":SCHEMA,"deadlineUtc":"2000-01-01T00:00:00Z",**{k:str(uuid.uuid4()) for k in c.IDS}}
            (p/"request.json").write_text(json.dumps(request))
            with patch.object(c,"infer") as call:
                c.file_mode(root,"unused",pathlib.Path(d)/"audit",**OPTIONS,max_requests=1,total_seconds=1)
                call.assert_not_called()
            self.assertFalse((p/"response.json").exists())
    def test_cancel_during_call_never_publishes(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d)/"control";root.mkdir();p=root/"001";p.mkdir()
            request={"protocol":"westworld-tool-file-v1","prompt":"p","responseSchema":SCHEMA,**{k:str(uuid.uuid4()) for k in c.IDS}}
            (p/"request.json").write_text(json.dumps(request))
            def result(*a,**k):
                (p/"canceled.json").write_text("{}")
                return DECISION,{"usage":{"total_tokens":17}}
            with patch.object(c,"infer",side_effect=result):
                c.file_mode(root,"unused",pathlib.Path(d)/"audit",**OPTIONS,max_requests=1,total_seconds=1)
            self.assertFalse((p/"response.json").exists())
            self.assertEqual(json.loads((pathlib.Path(d)/"audit").read_text())["usage"]["total_tokens"],17)


class InFlightCancellationTests(unittest.TestCase):
    def run_worker(self, delay, timeout, canceled):
        original=c.subprocess.Popen; children=[]
        def spawn(*args,**kwargs):
            # Test substitute: reads stdin and emits a fixture, never opens a network.
            script="import sys,time;sys.stdin.buffer.read();time.sleep("+repr(delay)+");sys.stdout.write("+repr(json.dumps({"body":body()}))+")"
            child=original([c.sys.executable,"-I","-c",script],**kwargs);children.append(child);return child
        with patch.object(c.subprocess,"Popen",side_effect=spawn):
            decision,audit=c.infer("p",SCHEMA,"FAKE-ONLY",**OPTIONS,timeout=timeout,canceled=canceled)
        self.assertTrue(all(p.poll() is not None for p in children))
        self.assertNotIn("FAKE-ONLY",json.dumps(audit))
        return decision,audit,children
    def test_slow_worker_cancel_reaped_unknown_usage(self):
        start=c.time.monotonic()
        d,a,kids=self.run_worker(10,2,lambda:c.time.monotonic()-start>.15)
        self.assertIsNone(d);self.assertEqual(a["error"],"host_canceled");self.assertIsNone(a["usage"])
        self.assertEqual(len(kids),1);self.assertLess(c.time.monotonic()-start,1.5)
    def test_worker_deadline_no_retry(self):
        d,a,kids=self.run_worker(10,.12,lambda:False)
        self.assertIsNone(d);self.assertEqual(a["error"],"timeout");self.assertIsNone(a["usage"]);self.assertEqual(len(kids),1)
    def test_completed_response_retains_usage(self):
        d,a,kids=self.run_worker(0,2,lambda:False)
        self.assertEqual(d,DECISION);self.assertEqual(a["usage"]["total_tokens"],17);self.assertEqual(len(kids),1)
    def test_cancel_while_stdin_is_backpressured_closes_all_pipes(self):
        original=c.subprocess.Popen; original_thread=c.threading.Thread
        children=[]; streams=[]; threads=[]
        def spawn(*args,**kwargs):
            # Deliberately never read stdin; a 200KB packet exceeds pipe capacity.
            child=original([c.sys.executable,"-I","-c","import time;time.sleep(10)"],**kwargs)
            children.append(child);streams.extend([child.stdin,child.stdout]);return child
        def thread(*args,**kwargs):
            t=original_thread(*args,**kwargs);threads.append(t);return t
        started=c.time.monotonic()
        with patch.object(c.subprocess,"Popen",side_effect=spawn),patch.object(c.threading,"Thread",side_effect=thread):
            with self.assertRaisesRegex(c.ControllerError,"host_canceled"):
                c.isolated_http_post({"test":"x"*200000},"fake",2,canceled=lambda:c.time.monotonic()-started>.15)
        self.assertEqual(len(children),1)
        self.assertIsNotNone(children[0].poll())
        self.assertTrue(all(stream.closed for stream in streams))
        self.assertTrue(all(not t.is_alive() for t in threads))
        self.assertLess(c.time.monotonic()-started,1.5)
    def test_precanceled_no_worker(self):
        with patch.object(c.subprocess,"Popen") as spawn:
            d,a=c.infer("p",SCHEMA,"fake",**OPTIONS,canceled=lambda:True)
            spawn.assert_not_called()
        self.assertEqual(a["error"],"host_canceled");self.assertIsNone(a["usage"])

class LifecycleAuditTests(unittest.TestCase):
    def setup_request(self, d):
        root=pathlib.Path(d)/"control";root.mkdir();req=root/"001";req.mkdir()
        request={"protocol":"westworld-tool-file-v1","prompt":"PRIVATE-PROMPT-NOT-IN-EVENTS","responseSchema":SCHEMA,**{k:str(uuid.uuid4()) for k in c.IDS}}
        (req/"request.json").write_text(json.dumps(request))
        return root,req,pathlib.Path(d)/"audit.jsonl"
    def events(self, audit):
        return [json.loads(line) for line in pathlib.Path(str(audit)+".events.jsonl").read_text().splitlines()]
    def test_durable_attempt_before_fake_transport_and_legacy_usage(self):
        with tempfile.TemporaryDirectory() as d:
            root,req,audit=self.setup_request(d);real_infer=c.infer;calls=[]
            def transport(payload,key,timeout,**kwargs):
                events=self.events(audit);self.assertEqual(events[-1]["event"],"request_attempt")
                self.assertEqual(events[-1]["state"],"attempt_not_proof_of_delivery_or_billing")
                self.assertIsNone(events[-1]["usage"]);calls.append(1);return body()
            with patch.object(c,"infer",side_effect=lambda *a,**kw:real_infer(*a,**kw,transport=transport)):
                c.file_mode(root,"FAKE-SECRET",audit,**OPTIONS,max_requests=1,total_seconds=1)
            self.assertEqual(calls,[1]);self.assertEqual(json.loads(audit.read_text())["usage"]["total_tokens"],17)
            events=self.events(audit)
            self.assertEqual([e["event"] for e in events],["startup","ready","host_control_ready","request_attempt","request_result","response_published","ended"])
            self.assertEqual([e["sequence"] for e in events],list(range(1,len(events)+1)))
            self.assertNotIn("PRIVATE-PROMPT-NOT-IN-EVENTS",json.dumps(events));self.assertNotIn("FAKE-SECRET",json.dumps(events))
            self.assertNotIn("rawDecision",json.dumps(events));self.assertNotIn('"steps"',json.dumps(events))
    def test_attempt_fsync_failure_prevents_call_and_claim_blocks_retry(self):
        with tempfile.TemporaryDirectory() as d:
            root,req,audit=self.setup_request(d);real_emit=c.LifecycleAudit.emit
            def emit(obj,event,**fields):
                if event=="request_attempt":
                    with patch.object(c.os,"fsync",side_effect=OSError("synthetic")):
                        return real_emit(obj,event,**fields)
                return real_emit(obj,event,**fields)
            with patch.object(c.LifecycleAudit,"emit",new=emit),patch.object(c,"infer") as call:
                with self.assertRaises(OSError):c.file_mode(root,"FAKE",audit,**OPTIONS,max_requests=1,total_seconds=1)
                call.assert_not_called()
            with patch.object(c,"infer") as call:
                c.file_mode(root,"FAKE",pathlib.Path(d)/"retry-audit",**OPTIONS,max_requests=1,total_seconds=1)
                call.assert_not_called()
            self.assertFalse((req/"response.json").exists())
    def test_startup_fsync_failure_prevents_key_read(self):
        with tempfile.TemporaryDirectory() as d:
            audit=pathlib.Path(d)/"audit"
            argv=["controller.py","file","--control",str(pathlib.Path(d)/"control"),"--audit",str(audit),"--model",OPTIONS["model"],"--reasoning-effort","none"]
            with patch.object(c.sys,"argv",argv),patch.object(c.os,"fsync",side_effect=OSError("synthetic")),patch.object(c,"secure_key") as key,patch.object(c,"infer") as infer:
                with self.assertRaises(OSError):c.main()
                key.assert_not_called();infer.assert_not_called()
    def test_key_interrupt_persists_start_and_end_without_secret(self):
        with tempfile.TemporaryDirectory() as d:
            audit=pathlib.Path(d)/"audit"
            argv=["controller.py","file","--control",str(pathlib.Path(d)/"control"),"--audit",str(audit),"--model",OPTIONS["model"],"--reasoning-effort","none"]
            with patch.object(c.sys,"argv",argv),patch.object(c,"secure_key",side_effect=KeyboardInterrupt("DO-NOT-LOG")):
                with self.assertRaises(KeyboardInterrupt):c.main()
            events=self.events(audit);self.assertEqual([e["event"] for e in events],["startup","ended"])
            self.assertEqual(events[-1]["status"],"interrupted_or_failed");self.assertNotIn("DO-NOT-LOG",json.dumps(events))
    def test_wait_timeout_persists_ready_no_attempt(self):
        with tempfile.TemporaryDirectory() as d:
            audit=pathlib.Path(d)/"audit";clock=[0]
            with patch.object(c.time,"monotonic",side_effect=lambda:clock[0]),patch.object(c.time,"sleep",side_effect=lambda s:clock.__setitem__(0,clock[0]+s)),patch.object(c,"infer") as call:
                with self.assertRaises(c.ControllerError):c.file_mode(pathlib.Path(d)/"control","FAKE",audit,**OPTIONS,total_seconds=1)
                call.assert_not_called()
            self.assertEqual([e["event"] for e in self.events(audit)],["startup","ready","ended"])
    def test_result_journal_failure_preserves_legacy_usage_no_publish(self):
        with tempfile.TemporaryDirectory() as d:
            root,req,audit=self.setup_request(d);real_emit=c.LifecycleAudit.emit
            def emit(obj,event,**fields):
                if event=="request_result":raise OSError("synthetic")
                return real_emit(obj,event,**fields)
            with patch.object(c.LifecycleAudit,"emit",new=emit),patch.object(c,"infer",return_value=(DECISION,{"usage":{"total_tokens":17}})) as call:
                with self.assertRaises(OSError):c.file_mode(root,"FAKE",audit,**OPTIONS,max_requests=1,total_seconds=1)
                self.assertEqual(call.call_count,1)
            self.assertEqual(json.loads(audit.read_text())["usage"]["total_tokens"],17)
            self.assertFalse((req/"response.json").exists())
    def test_replay_result_sidecar_failure_preserves_output_usage(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d);prompt=root/"prompt";schema=root/"schema";output=root/"result.json"
            prompt.write_text("PRIVATE-PROMPT");schema.write_text(json.dumps(SCHEMA))
            argv=["controller.py","replay","--prompt",str(prompt),"--schema",str(schema),"--output",str(output),"--model",OPTIONS["model"],"--reasoning-effort","none"]
            real_emit=c.LifecycleAudit.emit;real_infer=c.infer;calls=[]
            def emit(obj,event,**fields):
                if event=="request_result":raise OSError("synthetic sidecar failure")
                return real_emit(obj,event,**fields)
            def transport(*args,**kwargs):
                calls.append(1);return body()
            with patch.object(c.sys,"argv",argv),patch.object(c,"secure_key",return_value="FAKE-KEY"),patch.object(c.LifecycleAudit,"emit",new=emit),patch.object(c,"infer",side_effect=lambda *a,**kw:real_infer(*a,**kw,transport=transport)):
                with self.assertRaises(OSError):c.main()
            self.assertEqual(calls,[1])
            self.assertEqual(json.loads(output.read_text())["usage"]["total_tokens"],17)
            events=self.events(output)
            self.assertEqual(events[-1]["event"],"ended")
            self.assertEqual(events[-1]["status"],"interrupted_or_failed")
            self.assertNotIn("FAKE-KEY",output.read_text()+json.dumps(events))
    def test_interrupted_attempt_is_unknown_not_zero_usage(self):
        with tempfile.TemporaryDirectory() as d:
            root,req,audit=self.setup_request(d)
            with patch.object(c,"infer",side_effect=KeyboardInterrupt("PRIVATE-ERROR")) as call:
                with self.assertRaises(KeyboardInterrupt):c.file_mode(root,"FAKE",audit,**OPTIONS,max_requests=1,total_seconds=1)
                self.assertEqual(call.call_count,1)
            events=self.events(audit);names=[e["event"] for e in events]
            self.assertIn("request_attempt",names);self.assertNotIn("request_result",names)
            self.assertIsNone(next(e for e in events if e["event"]=="request_attempt")["usage"])
            self.assertNotIn("PRIVATE-ERROR",json.dumps(events));self.assertFalse((req/"response.json").exists())
    def test_ready_write_failure_never_waits_or_calls(self):
        with tempfile.TemporaryDirectory() as d:
            root,req,audit=self.setup_request(d);real_emit=c.LifecycleAudit.emit
            def emit(obj,event,**fields):
                if event=="ready":raise OSError("synthetic")
                return real_emit(obj,event,**fields)
            with patch.object(c.LifecycleAudit,"emit",new=emit),patch.object(c,"wait_for_control") as wait,patch.object(c,"infer") as call:
                with self.assertRaises(OSError):c.file_mode(root,"FAKE",audit,**OPTIONS,max_requests=1,total_seconds=1)
                wait.assert_not_called();call.assert_not_called()
    def test_existing_journal_never_overwritten(self):
        with tempfile.TemporaryDirectory() as d:
            root,req,audit=self.setup_request(d);p=pathlib.Path(str(audit)+".events.jsonl");p.write_text("old")
            with patch.object(c,"infer") as call:
                with self.assertRaises(FileExistsError):c.file_mode(root,"FAKE",audit,**OPTIONS,max_requests=1,total_seconds=1)
                call.assert_not_called()
            self.assertEqual(p.read_text(),"old")

class ParallelPlayTests(unittest.TestCase):
    def request(self, root, ordinal, prompt):
        directory = root / f"{ordinal:04d}"
        directory.mkdir(parents=True)
        envelope = {key: str(uuid.uuid4()) for key in c.IDS}
        (directory / "request.json").write_text(json.dumps(
            dict(envelope, protocol="westworld-tool-file-v1", prompt=prompt, responseSchema=SCHEMA)))
        return directory, envelope

    def test_fast_role_publishes_while_slow_role_is_waiting(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp) / "control"
            a, actor_a = self.request(root, 1, "slow")
            b, actor_b = self.request(root, 2, "fast")
            extra, _ = self.request(root, 3, "over-budget")
            audit = root.parent / "usage.jsonl"

            def infer(prompt, *args, **kwargs):
                if prompt == "slow":
                    end = time.monotonic() + 3
                    while not (b / "response.json").exists() and time.monotonic() < end:
                        time.sleep(0.01)
                    self.assertTrue((b / "response.json").exists())
                return DECISION, {"outcome": "received", "usage": {"total_tokens": 17}}

            with patch.object(c, "infer", side_effect=infer) as call:
                c.file_mode(root, "FAKE", audit, **OPTIONS, max_requests=2, parallelism=2, total_seconds=5)

            self.assertEqual(call.call_count, 2)
            self.assertEqual(c.read_json(a / "response.json")["actorId"], actor_a["actorId"])
            self.assertEqual(c.read_json(b / "response.json")["actorId"], actor_b["actorId"])
            self.assertFalse((extra / "response.json").exists())
            results = [json.loads(line) for line in audit.read_text().splitlines()]
            self.assertEqual([row["actorId"] for row in results], [actor_b["actorId"], actor_a["actorId"]])

    def test_host_exit_keeps_known_usage_and_withholds_both_replies(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp) / "control"
            a, _ = self.request(root, 1, "slow")
            b, _ = self.request(root, 2, "fast")
            audit = root.parent / "usage.jsonl"
            both_started = threading.Barrier(2)
            host_stopped = threading.Event()

            def infer(prompt, *args, canceled, **kwargs):
                both_started.wait(timeout=3)
                if prompt == "fast":
                    host_stopped.set()
                    return DECISION, {"outcome": "received", "usage": {"total_tokens": 17}}
                self.assertTrue(host_stopped.wait(timeout=3))
                self.assertTrue(canceled())
                return None, {"outcome": "failed", "error": "host_canceled", "usage": None}

            with patch.object(c, "infer", side_effect=infer):
                c.file_mode(root, "FAKE", audit, **OPTIONS, max_requests=2, parallelism=2, total_seconds=5,
                            host_stopped=host_stopped.is_set)

            self.assertFalse((a / "response.json").exists())
            self.assertFalse((b / "response.json").exists())
            results = [json.loads(line) for line in audit.read_text().splitlines()]
            self.assertEqual(len(results), 2)
            self.assertIn({"total_tokens": 17}, [row["usage"] for row in results])
            self.assertTrue(all(row["delivery"] == "withheld_or_no_decision" for row in results))

if __name__=="__main__":unittest.main()

