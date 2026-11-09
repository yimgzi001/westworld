import importlib.util, pathlib, tempfile, unittest, uuid, json, io
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
        with patch.object(c.getpass,"getpass",side_effect=c.getpass.GetPassWarning("no tty")):
            with self.assertRaises(c.getpass.GetPassWarning):c.secure_key()
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
        class Fake:
            def __init__(self):
                self.stdout=io.BytesIO(raw);self.returncode=None;self.killed=False;self.reaped=False;self.calls=[]
            def communicate(self,input=None,timeout=None):
                self.calls.append((input,timeout))
                if timeout is not None and fail_timeout:
                    raise subprocess.TimeoutExpired("fixed-worker",timeout)
                self.returncode=code;self.reaped=True
                return None,None
            def kill(self):self.killed=True;self.returncode=-9
            def wait(self):self.reaped=True;return self.returncode
            def poll(self):return self.returncode
        fail_timeout=timeout
        return Fake()
    def test_fixed_argv_pipe_only(self):
        process=self.fake_process()
        with patch.object(c.subprocess,"Popen",return_value=process) as start:
            self.assertEqual(c.isolated_http_post({},"FAKE-SECRET-ONLY",1),{"status":"completed"})
        args,kwargs=start.call_args
        self.assertNotIn("FAKE-SECRET-ONLY",repr(args)+repr(kwargs))
        self.assertEqual(args[0][-1],"--http-worker");self.assertFalse(kwargs["shell"])
        self.assertEqual(kwargs["stderr"],c.subprocess.DEVNULL)
        self.assertIn(b"FAKE-SECRET-ONLY",process.calls[0][0]);self.assertTrue(process.reaped)
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

if __name__=="__main__":unittest.main()
