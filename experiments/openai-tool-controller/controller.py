#!/usr/bin/env python3
"""Official Responses transport only; no game tools, shell tools or generated code."""
import subprocess, sys, threading
import argparse, datetime, getpass, hashlib, json, math, os, pathlib, socket, ssl, time, urllib.error, urllib.request, uuid, warnings
from concurrent.futures import ThreadPoolExecutor

URL = "https://api.openai.com/v1/responses"
KEY_ENV_VAR = "WESTWORLD_OPENAI_API_KEY"
VERIFIED_MODELS = {"gpt-6.1-terra": {"none"}}  # Explicit user-selected official model; no default/fallback.
IDS = ("sessionId", "actorId", "observationId", "requestId")
MAX_BYTES = 262144
DRIVER_SOURCE_SHA256 = hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest()

class ControllerError(Exception):
    pass

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ControllerError("redirect_rejected")

def configured_key():
    key = os.environ.get(KEY_ENV_VAR)
    if key is not None or os.name != "nt":
        return key

    # 已打开的 Windows 终端可能还没有继承新设置的用户环境变量。
    import winreg
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as user_environment:
            return winreg.QueryValueEx(user_environment, KEY_ENV_VAR)[0]
    except FileNotFoundError:
        return None


def secure_key():
    key = configured_key()
    if key is None:
        with warnings.catch_warnings():
            warnings.simplefilter("error", getpass.GetPassWarning)
            key = getpass.getpass("OpenAI API key (hidden, memory only): ")
    if not isinstance(key, str) or not key or any(c.isspace() for c in key):
        raise ControllerError("invalid_key")
    return key


def child_environment():
    environment = os.environ.copy()
    environment.pop(KEY_ENV_VAR, None)
    return environment

def decode(raw):
    def pairs(items):
        obj = {}
        for key, value in items:
            if key in obj:
                raise ControllerError("duplicate_json_field")
            obj[key] = value
        return obj
    return json.loads(raw, object_pairs_hook=pairs,
                      parse_constant=lambda _: (_ for _ in ()).throw(ControllerError("nonfinite_json")))

def compact(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False)

def validate_input(prompt, schema):
    if not isinstance(prompt, str) or not 0 < len(prompt) <= 32768:
        raise ControllerError("prompt_bound")
    if not isinstance(schema, dict) or schema.get("type") != "object" or len(compact(schema)) > 32768:
        raise ControllerError("schema_bound")

def validate_decision(value):
    # This validates transport structure only. The game validates every exact offered
    # tool, parameters, current refs and prerequisites before dispatch.
    if not isinstance(value, dict) or set(value) != {"goal", "steps"}:
        raise ControllerError("decision_shape")
    if not isinstance(value["goal"], str) or not 1 <= len(value["goal"]) <= 160:
        raise ControllerError("goal_bound")
    steps = value["steps"]
    if not isinstance(steps, list) or len(steps) > 4:
        raise ControllerError("steps_bound")
    for step in steps:
        if not isinstance(step, dict) or set(step) != {"tool", "arguments"} or not isinstance(step["tool"], str) or not isinstance(step["arguments"], dict):
            raise ControllerError("step_shape")
    return value

def http_post(payload, key, timeout):
    # Use the environment-supported HTTPS route, including its standard proxy
    # handling. Never inspect, print or override proxy configuration.
    opener = urllib.request.build_opener(NoRedirect(),
                 urllib.request.HTTPSHandler(context=ssl.create_default_context()))
    request = urllib.request.Request(URL, data=compact(payload).encode("utf-8"),
              headers={"Authorization": "Bearer " + key, "Content-Type": "application/json"}, method="POST")
    started = time.monotonic()
    try:
        with opener.open(request, timeout=timeout) as response:
            if response.geturl() != URL:
                raise ControllerError("destination_changed")
            if response.status != 200:
                raise ControllerError("http_status_" + str(response.status))
            raw = response.read(MAX_BYTES + 1)
            if time.monotonic() - started > timeout:
                raise ControllerError("timeout")
            if len(raw) > MAX_BYTES:
                raise ControllerError("response_bound")
            return decode(raw)
    except urllib.error.HTTPError as error:
        raise ControllerError("redirect_rejected" if 300 <= error.code < 400 else "http_status_" + str(error.code)) from None
    except (TimeoutError, socket.timeout):
        raise ControllerError("timeout") from None
    except (OSError, urllib.error.URLError):
        raise ControllerError("network_error") from None

# The worker is fixed reviewed source, never model-generated code. Credentials
# travel solely in the anonymous stdin pipe, not argv/env/disk.
def isolated_http_post(payload, key, timeout, *, canceled=None):
    if not 0 < timeout <= 45:
        raise ControllerError("timeout_bound")
    if canceled is not None and canceled():
        raise ControllerError("host_canceled")
    packet = compact({"payload": payload, "key": key, "timeout": timeout}).encode()
    if len(packet) > MAX_BYTES or not isinstance(key, str) or not key or len(key) > 8192:
        raise ControllerError("worker_input_bound")
    deadline = time.monotonic() + timeout
    process = subprocess.Popen([sys.executable, "-I", str(pathlib.Path(__file__).resolve()), "--http-worker"],
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                               env=child_environment(), shell=False)
    stream = process.stdout
    collected = bytearray()
    overflow = threading.Event()
    read_failed = threading.Event()
    write_failed = threading.Event()
    def drain():
        try:
            while True:
                chunk = stream.read(4096)
                if not chunk:
                    break
                room = MAX_BYTES - len(collected)
                collected.extend(chunk[:room])
                if len(chunk) > room:
                    overflow.set()
                    process.kill()
                    break
        except Exception:
            read_failed.set()
        finally:
            stream.close()

    def supply_input():
        # Windows 管道写满时可能阻塞。主线程仍须能按截止时间终止进程。
        try:
            with process.stdin as input_pipe:
                input_pipe.write(packet)
        except BrokenPipeError:
            pass
        except OSError:
            write_failed.set()

    reader = threading.Thread(target=drain, daemon=True)
    writer = threading.Thread(target=supply_input, daemon=True)
    reader.start()
    writer.start()
    try:
        while process.poll() is None:
            # 进程先完成时保留真实用量，能否发布回复由上层再核对。
            if canceled is not None and canceled():
                raise ControllerError("host_canceled")
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise ControllerError("timeout")
            try:
                process.wait(timeout=remaining if canceled is None else min(.05, remaining))
            except subprocess.TimeoutExpired:
                if canceled is None:
                    raise ControllerError("timeout") from None
        writer.join()
        reader.join()
        if overflow.is_set():
            raise ControllerError("worker_output_bound")
        if read_failed.is_set() or write_failed.is_set() or process.returncode != 0:
            raise ControllerError("worker_failed")
        reply = decode(collected)
        if not isinstance(reply, dict) or set(reply) not in ({"body"}, {"error"}):
            raise ControllerError("worker_protocol")
        if "error" in reply:
            raise ControllerError(safe_error(reply["error"]))
        return reply["body"]
    finally:
        if process.poll() is None:
            process.kill()
        process.wait()
        writer.join()
        reader.join()
        packet = None

SAFE_ERRORS = {"host_canceled", "credential_echo_rejected", "redirect_rejected", "destination_changed", "response_bound", "timeout",
               "network_error", "worker_failed", "worker_protocol", "worker_output_bound",
               "worker_input_bound", "response_shape", "response_not_completed", "model_refusal",
               "decision_bound", "decision_shape", "goal_bound", "steps_bound", "step_shape",
               "duplicate_json_field", "nonfinite_json"}
def safe_error(code):
    if isinstance(code, str) and (code in SAFE_ERRORS or
            (code.startswith("http_status_") and len(code) == 15 and code[12:].isdigit())):
        return code
    return "invalid_or_transport_response"

def http_worker():
    # No CLI-selected URL, module, executable or credential storage.
    try:
        raw = sys.stdin.buffer.read(MAX_BYTES + 1)
        if len(raw) > MAX_BYTES:
            raise ControllerError("worker_input_bound")
        packet = decode(raw)
        if not isinstance(packet, dict) or set(packet) != {"payload", "key", "timeout"}:
            raise ControllerError("worker_protocol")
        key, timeout = packet["key"], packet["timeout"]
        if not isinstance(key, str) or not key or len(key) > 8192 or any(c.isspace() for c in key):
            raise ControllerError("worker_input_bound")
        if type(timeout) not in (int, float) or not 0 < timeout <= 45:
            raise ControllerError("worker_input_bound")
        result = {"body": http_post(packet["payload"], key, timeout)}
        encoded = compact(result).encode()
        if len(encoded) > MAX_BYTES:
            raise ControllerError("response_bound")
    except Exception as error:
        encoded = compact({"error": safe_error(str(error)) if isinstance(error, ControllerError)
                           else "invalid_or_transport_response"}).encode()
    sys.stdout.buffer.write(encoded)
    sys.stdout.buffer.flush()


def safe_usage(value):
    if value is None:
        return None
    allowed = {"input_tokens", "output_tokens", "total_tokens",
               "input_tokens_details", "output_tokens_details", "cached_tokens", "reasoning_tokens",
               "cache_write_tokens", "cache_write_tokens_details", "ttl_30m_tokens", "ttl_12h_tokens"}
    def walk(v):
        if isinstance(v, dict):
            return {k: walk(x) for k, x in v.items() if k in allowed}
        if type(v) is int and v >= 0:
            return v
        return None
    return walk(value)

def infer(prompt, schema, key, *, model, reasoning_effort, timeout=45, transport=isolated_http_post, canceled=None,
          implicit_cache=True):
    if model not in VERIFIED_MODELS or reasoning_effort not in VERIFIED_MODELS[model]:
        raise ControllerError("unverified_model_or_effort")
    validate_input(prompt, schema)
    if not 0 < timeout <= 45:
        raise ControllerError("timeout_bound")
    payload = {"model": model, "reasoning": {"effort": reasoning_effort}, "store": False,
               "max_output_tokens": 512, "input": prompt,
               "text": {"format": {"type": "json_schema", "name": "westworld_tool_decision",
                                  "strict": False, "schema": schema}}}
    if not implicit_cache:
        # 生存每轮的动态菜单和地形不同。没有复用前缀时，避免写入整轮快照。
        payload["prompt_cache_options"] = {"mode": "explicit"}
    started = time.monotonic()
    audit = {"driverSourceSha256": DRIVER_SOURCE_SHA256, "maxOutputTokens": 512, "requestedModel": model, "promptSha256": hashlib.sha256(prompt.encode()).hexdigest(),
             "schemaSha256": hashlib.sha256(compact(schema).encode()).hexdigest(),
             "schemaStrict": False, "effort": reasoning_effort, "usage": None,
             "cacheMode": "implicit" if implicit_cache else "explicit"}
    try:
        body = transport(payload, key, timeout) if canceled is None else transport(payload, key, timeout, canceled=canceled)
        if key and key in compact(body):
            raise ControllerError("credential_echo_rejected")
        if not isinstance(body, dict):
            raise ControllerError("response_shape")
        model = body.get("model")
        audit["actualModel"] = model if isinstance(model, str) and len(model) <= 128 else None
        audit["usage"] = safe_usage(body.get("usage"))
        if body.get("status") != "completed":
            raise ControllerError("response_not_completed")
        texts = []
        for message in body.get("output", []):
            if message.get("type") == "message" and message.get("role") == "assistant":
                for part in message.get("content", []):
                    if part.get("type") == "refusal":
                        raise ControllerError("model_refusal")
                    if part.get("type") == "output_text":
                        texts.append(part.get("text", ""))
        raw = "".join(texts)
        if not raw or len(raw.encode()) > 16384:
            raise ControllerError("decision_bound")
        decision = validate_decision(decode(raw))
        audit.update(outcome="received", decision=decision, rawDecision=raw)
        return decision, audit
    except Exception as error:
        code = safe_error(str(error)) if isinstance(error, ControllerError) else ("timeout" if isinstance(error, TimeoutError) else "invalid_or_transport_response")
        audit.update(outcome="failed", error=code)
        return None, audit
    finally:
        audit["wallSeconds"] = time.monotonic() - started

def request_envelope(request):
    if request.get("protocol") != "westworld-tool-file-v1":
        raise ControllerError("protocol_mismatch")
    result = {}
    for field in IDS:
        raw = request.get(field)
        if not isinstance(raw, str):
            raise ControllerError("invalid_identity")
        try:
            if uuid.UUID(raw).int == 0:
                raise ValueError()
        except ValueError:
            raise ControllerError("invalid_identity") from None
        result[field] = raw
    validate_input(request.get("prompt"), request.get("responseSchema"))
    return result

def atomic_json(path, value):
    temporary = path.with_suffix(".tmp")
    with temporary.open("x", encoding="utf-8") as output:
        output.write(compact(value))
        output.flush()
        os.fsync(output.fileno())
    if path.exists():
        temporary.unlink()
        raise ControllerError("response_exists")
    os.replace(temporary, path)

def read_json(path):
    if path.is_symlink() or path.stat().st_size > MAX_BYTES:
        raise ControllerError("input_file_bound")
    return decode(path.read_bytes())

def checked_control_path(root):
    root = pathlib.Path(os.path.abspath(root))
    if any(part.is_symlink() for part in (root, *root.parents)):
        raise ControllerError("control_symlink_rejected")
    return root

def wait_for_control(root, deadline, host_stopped=lambda: False):
    # Never mkdir: only the host may create its fresh control directory.
    root = checked_control_path(root)
    while True:
        checked_control_path(root)
        if host_stopped() or (root.parent / "exit.txt").exists():
            return None
        if root.exists():
            if not root.is_dir():
                raise ControllerError("control_not_directory")
            return root
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise ControllerError("control_directory_wait_timeout")
        time.sleep(min(.25, remaining))

def sync_directory(directory):
    # Windows 上的 Python 不支持用 os.open 打开目录，文件本身仍单独刷新。
    if os.name != "nt":
        fd = os.open(directory, os.O_RDONLY)
        try:
            os.fsync(fd)
        finally:
            os.close(fd)

def claim_request(root, envelope, prompt, schema, model, effort):
    # Reserve before calling the provider. A crash between reservation and POST
    # sacrifices availability rather than risking duplicate billing on restart.
    # This is local crash protection, not a distributed/provider transaction.
    directory = root / ".request-claims"
    directory.mkdir(exist_ok=True)
    checked_control_path(directory)
    identity = str(uuid.UUID(envelope["requestId"]))
    claim = directory / (identity + ".json")
    value = {"state": "reserved_or_submitted_unknown", **envelope,
             "promptSha256": hashlib.sha256(prompt.encode()).hexdigest(),
             "schemaSha256": hashlib.sha256(compact(schema).encode()).hexdigest(),
             "model": model, "effort": effort}
    try:
        # O_EXCL also refuses an existing symlink; partial claims remain blocking.
        with claim.open("x", encoding="utf-8") as output:
            output.write(compact(value)); output.flush(); os.fsync(output.fileno())
        sync_directory(directory)
        return True
    except FileExistsError:
        return False

def host_remaining(request, directory):
    if (directory / "canceled.json").exists() or (directory / "canceled.json").is_symlink():
        return 0.0
    deadline = request.get("deadlineUtc")
    if deadline is None:  # Legacy serial requests; total and HTTP bounds still apply.
        return 120.0
    if not isinstance(deadline, str) or len(deadline) > 80:
        raise ControllerError("host_deadline_invalid")
    try:
        parsed = datetime.datetime.fromisoformat(deadline.replace("Z", "+00:00"))
        if parsed.tzinfo is None:
            raise ValueError()
        left = (parsed - datetime.datetime.now(datetime.timezone.utc)).total_seconds()
    except (ValueError, OverflowError):
        raise ControllerError("host_deadline_invalid") from None
    # UTC is only an advisory transport bound; game host owns monotonic expiry.
    return max(0.0, min(120.0, left))

class LifecycleAudit:
    """Separate fsynced journal: never changes legacy per-request usage records."""
    def __init__(self, audit_path, *, mode, model, reasoning_effort):
        if mode not in ("file", "replay") or model not in VERIFIED_MODELS or reasoning_effort not in VERIFIED_MODELS[model]:
            raise ControllerError("unverified_model_or_effort")
        self.path = checked_control_path(str(audit_path) + ".events.jsonl")
        self.metadata = dict(mode=mode, requestedModel=model, effort=reasoning_effort,
                             driverSourceSha256=DRIVER_SOURCE_SHA256)
        self.output = None
        self.sequence = 0
    def __enter__(self):
        self.output = self.path.open("x", encoding="utf-8")
        try:
            self.emit("startup")
            sync_directory(self.path.parent)
        except BaseException:
            self.output.close()
            raise
        return self
    def emit(self, event, **fields):
        self.sequence += 1
        self.output.write(compact(dict(schema="westworld-controller-lifecycle-v1",
            event=event, sequence=self.sequence, utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
            monotonicSeconds=time.monotonic(), **self.metadata, **fields)) + "\n")
        self.output.flush()
        os.fsync(self.output.fileno())
    def attempt(self, prompt, schema, envelope=None):
        self.emit("request_attempt", **(envelope or {}),
            promptSha256=hashlib.sha256(prompt.encode()).hexdigest(),
            schemaSha256=hashlib.sha256(compact(schema).encode()).hexdigest(),
            state="attempt_not_proof_of_delivery_or_billing", usage=None)
    def result(self, record, envelope=None):
        self.emit("request_result", **(envelope or {}), usage=safe_usage(record.get("usage")),
            outcome="received" if record.get("outcome")=="received" else "failed_or_unknown",
            error=safe_error(record["error"]) if "error" in record else None)
    def __exit__(self, kind, value, traceback):
        try:
            self.emit("ended", status="completed" if kind is None else "interrupted_or_failed")
        finally:
            self.output.close()
        return False

def file_mode(root, key, audit_file, *, model, reasoning_effort, max_requests=12,
              total_seconds=650, wait_seconds=60, parallelism=1, host_stopped=lambda: False, lifecycle=None,
              survival_run=False):
    valid_requests = max_requests is None or type(max_requests) is int and max_requests > 0
    valid_time = total_seconds is None or math.isfinite(total_seconds) and total_seconds > 0
    if not valid_requests or not valid_time or (not survival_run and
            (max_requests is None or max_requests > 12 or total_seconds is None or total_seconds > 650)):
        raise ControllerError("run_bound")
    if not 0 < wait_seconds <= 60:
        raise ControllerError("wait_bound")
    if parallelism not in (1, 2):
        raise ControllerError("parallelism_bound")
    if lifecycle is None:
        with LifecycleAudit(audit_file, mode="file", model=model, reasoning_effort=reasoning_effort) as events:
            return _file_mode(root, key, audit_file, model=model, reasoning_effort=reasoning_effort,
                              max_requests=max_requests, total_seconds=total_seconds, wait_seconds=wait_seconds,
                              parallelism=parallelism, host_stopped=host_stopped, lifecycle=events,
                              implicit_cache=not survival_run)
    return _file_mode(root, key, audit_file, model=model, reasoning_effort=reasoning_effort,
                      max_requests=max_requests, total_seconds=total_seconds, wait_seconds=wait_seconds,
                      parallelism=parallelism, host_stopped=host_stopped, lifecycle=lifecycle,
                      implicit_cache=not survival_run)

def _file_mode(root, key, audit_file, *, model, reasoning_effort, max_requests, total_seconds,
               wait_seconds, parallelism, host_stopped, lifecycle, implicit_cache):
    if model not in VERIFIED_MODELS or reasoning_effort not in VERIFIED_MODELS[model]:
        raise ControllerError("unverified_model_or_effort")
    deadline = math.inf if total_seconds is None else time.monotonic() + total_seconds
    lifecycle.emit("ready", phase="waiting_for_host")
    print("Controller ready; waiting for host control directory (no directory creation).", flush=True)
    root = wait_for_control(root, min(deadline, time.monotonic() + wait_seconds), host_stopped)
    if root is None:
        return
    lifecycle.emit("host_control_ready")
    seen = set()
    claimed_paths = set()
    stopping = threading.Event()

    def stopped():
        return (stopping.is_set() or host_stopped() or (root.parent / "exit.txt").exists()
                or time.monotonic() >= deadline)

    def can_claim():
        return (max_requests is None or len(seen) < max_requests) and not stopped()

    def request_model(request, directory, remaining):
        return infer(request["prompt"], request["responseSchema"], key, model=model,
                     reasoning_effort=reasoning_effort, timeout=min(45, remaining),
                     canceled=lambda: stopped() or host_remaining(request, directory) <= 0,
                     implicit_cache=implicit_cache)

    def record_result(audit, record):
        audit.write(compact(record) + "\n")
        audit.flush()
        os.fsync(audit.fileno())

    # 工作线程只访问模型，领用、审计和回复发布由一个线程顺序完成。
    with audit_file.open("x", encoding="utf-8") as audit, ThreadPoolExecutor(max_workers=parallelism) as executor:
        pending = {}
        try:
            while pending or can_claim():
                for job, (request, envelope, directory) in list(pending.items()):
                    if not job.done():
                        continue
                    del pending[job]
                    decision, record = job.result()
                    record.update(envelope)
                    publish = decision is not None and not stopped() and host_remaining(request, directory) > 0
                    record["delivery"] = "ready_to_publish" if publish else "withheld_or_no_decision"
                    record_result(audit, record)
                    lifecycle.result(record, envelope)
                    if publish and not stopped() and host_remaining(request, directory) > 0:
                        atomic_json(directory / "response.json", dict(envelope, decision=decision))
                        lifecycle.emit("response_published", **envelope)

                if len(pending) < parallelism and can_claim():
                    checked_control_path(root)
                    for path in sorted(root.glob("*/request.json")):
                        if len(pending) >= parallelism or not can_claim():
                            break
                        directory = path.parent
                        if path in claimed_paths:
                            continue
                        if directory.is_symlink() or directory.resolve().parent != root:
                            continue
                        request = read_json(path)
                        envelope = request_envelope(request)
                        identity = envelope["requestId"]
                        if identity in seen or (directory / "response.json").exists():
                            continue
                        seen.add(identity)
                        claimed_paths.add(path)
                        claimed = claim_request(root, envelope, request["prompt"], request["responseSchema"], model, reasoning_effort)
                        remaining = min(deadline - time.monotonic(), host_remaining(request, directory))
                        if not claimed or remaining <= 0:
                            reason = "host_request_expired_or_canceled" if claimed else "prior_request_requires_reconciliation"
                            record_result(audit, dict(envelope, error=reason, usage=None))
                            lifecycle.emit("request_skipped", **envelope, reason=reason)
                            continue
                        lifecycle.attempt(request["prompt"], request["responseSchema"], envelope)
                        job = executor.submit(request_model, request, directory, remaining)
                        pending[job] = request, envelope, directory
                if pending or can_claim():
                    time.sleep(0.05)
        finally:
            stopping.set()

def main():
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="mode", required=True)
    replay = sub.add_parser("replay")
    replay.add_argument("--prompt", type=pathlib.Path, required=True)
    replay.add_argument("--schema", type=pathlib.Path, required=True)
    replay.add_argument("--output", type=pathlib.Path, required=True)
    files = sub.add_parser("file")
    files.add_argument("--control", type=pathlib.Path, required=True)
    files.add_argument("--audit", type=pathlib.Path, required=True)
    files.add_argument("--max-requests", type=int, default=12)
    files.add_argument("--total-seconds", type=float, default=650)
    files.add_argument("--wait-seconds", type=float, default=60)
    for mode in (replay, files):
        mode.add_argument("--model", required=True, choices=sorted(VERIFIED_MODELS))
        mode.add_argument("--reasoning-effort", required=True)
    args = parser.parse_args()
    if args.reasoning_effort not in VERIFIED_MODELS[args.model]:
        raise ControllerError("unverified_model_or_effort")
    audit_path = args.output if args.mode == "replay" else args.audit
    with LifecycleAudit(audit_path, mode=args.mode, model=args.model, reasoning_effort=args.reasoning_effort) as events:
        key = None
        try:
            key = secure_key()
            if args.mode == "replay":
                if args.output.exists() or args.prompt.stat().st_size > MAX_BYTES:
                    raise ControllerError("replay_file_bound")
                prompt = args.prompt.read_text()
                schema = read_json(args.schema)
                validate_input(prompt, schema)
                events.emit("ready", phase="replay_input_validated")
                events.attempt(prompt, schema)
                _, audit = infer(prompt, schema, key, model=args.model, reasoning_effort=args.reasoning_effort)
                atomic_json(args.output, audit)
                events.result(audit)
            else:
                file_mode(args.control, key, args.audit, model=args.model, reasoning_effort=args.reasoning_effort, max_requests=args.max_requests, total_seconds=args.total_seconds, wait_seconds=args.wait_seconds, lifecycle=events)
        finally:
            key = None
    print("Finished; inspect local audit. No game actions executed by this driver.")

if __name__ == "__main__":
    try:
        if sys.argv[1:] == ["--http-worker"]:
            http_worker()
        else:
            main()
    except Exception as error:
        # No exception repr/tracebacks: those can contain HTTP details.
        print("Controller stopped:", str(error) if isinstance(error, ControllerError) else type(error).__name__)
        raise SystemExit(1)
