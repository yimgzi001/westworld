#!/usr/bin/env python3
"""Official Responses transport only; no game tools, shell tools or generated code."""
import argparse, getpass, hashlib, json, os, pathlib, socket, ssl, time, urllib.error, urllib.request, uuid, warnings

URL = "https://api.openai.com/v1/responses"
VERIFIED_MODELS = {"gpt-6.1-terra": {"none"}}  # Explicit user-selected official model; no default/fallback.
IDS = ("sessionId", "actorId", "observationId", "requestId")
MAX_BYTES = 262144
DRIVER_SOURCE_SHA256 = hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest()

class ControllerError(Exception):
    pass

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ControllerError("redirect_rejected")

def secure_key():
    with warnings.catch_warnings():
        warnings.simplefilter("error", getpass.GetPassWarning)
        key = getpass.getpass("OpenAI API key (hidden, memory only): ")
    if not key or any(c.isspace() for c in key):
        raise ControllerError("invalid_key")
    return key

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

def safe_usage(value):
    if value is None:
        return None
    allowed = {"input_tokens", "output_tokens", "total_tokens",
               "input_tokens_details", "output_tokens_details", "cached_tokens", "reasoning_tokens"}
    def walk(v):
        if isinstance(v, dict):
            return {k: walk(x) for k, x in v.items() if k in allowed}
        if type(v) is int and v >= 0:
            return v
        return None
    return walk(value)

def infer(prompt, schema, key, *, model, reasoning_effort, timeout=45, transport=http_post):
    if model not in VERIFIED_MODELS or reasoning_effort not in VERIFIED_MODELS[model]:
        raise ControllerError("unverified_model_or_effort")
    validate_input(prompt, schema)
    if not 0 < timeout <= 45:
        raise ControllerError("timeout_bound")
    payload = {"model": model, "reasoning": {"effort": reasoning_effort}, "store": False,
               "max_output_tokens": 512, "input": prompt,
               "text": {"format": {"type": "json_schema", "name": "westworld_tool_decision",
                                  "strict": False, "schema": schema}}}
    started = time.monotonic()
    audit = {"driverSourceSha256": DRIVER_SOURCE_SHA256, "maxOutputTokens": 512, "requestedModel": model, "promptSha256": hashlib.sha256(prompt.encode()).hexdigest(),
             "schemaSha256": hashlib.sha256(compact(schema).encode()).hexdigest(),
             "schemaStrict": False, "effort": reasoning_effort, "usage": None}
    try:
        body = transport(payload, key, timeout)
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
        code = str(error) if isinstance(error, ControllerError) else ("timeout" if isinstance(error, TimeoutError) else "invalid_or_transport_response")
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

def wait_for_control(root, deadline):
    # Never mkdir: only the host may create its fresh control directory.
    root = checked_control_path(root)
    while True:
        checked_control_path(root)
        if (root.parent / "exit.txt").exists():
            return None
        if root.exists():
            if not root.is_dir():
                raise ControllerError("control_not_directory")
            return root
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise ControllerError("control_directory_wait_timeout")
        time.sleep(min(.25, remaining))

def file_mode(root, key, audit_file, *, model, reasoning_effort, max_requests=12, total_seconds=650, wait_seconds=60):
    if model not in VERIFIED_MODELS or reasoning_effort not in VERIFIED_MODELS[model]:
        raise ControllerError("unverified_model_or_effort")
    if not 1 <= max_requests <= 12 or not 0 < total_seconds <= 650:
        raise ControllerError("run_bound")
    if not 0 < wait_seconds <= 60:
        raise ControllerError("wait_bound")
    started = time.monotonic()
    print("Controller ready; waiting for host control directory (no directory creation).", flush=True)
    root = wait_for_control(root, started + min(wait_seconds, total_seconds))
    if root is None:
        return
    seen = set()
    with audit_file.open("x", encoding="utf-8") as audit:
        while len(seen) < max_requests and time.monotonic() - started < total_seconds:
            checked_control_path(root)
            if (root.parent / "exit.txt").exists():
                return
            for path in sorted(root.glob("*/request.json")):
                if path.parent.is_symlink() or path.parent.resolve().parent != root:
                    continue
                if (root.parent / "exit.txt").exists():
                    return
                request = read_json(path)
                envelope = request_envelope(request)
                identity = envelope["requestId"]
                if identity in seen or (path.parent / "response.json").exists():
                    continue
                remaining = total_seconds - (time.monotonic() - started)
                if remaining <= 0:
                    return
                seen.add(identity)  # Including failures: never retry a request automatically.
                decision, record = infer(request["prompt"], request["responseSchema"], key, model=model, reasoning_effort=reasoning_effort, timeout=min(45, remaining))
                record.update(envelope)
                audit.write(compact(record) + "\n"); audit.flush()
                if decision is not None and not (root.parent / "exit.txt").exists():
                    atomic_json(path.parent / "response.json", dict(envelope, decision=decision))
                if len(seen) >= max_requests:
                    return
            time.sleep(min(.25, max(0, total_seconds - (time.monotonic() - started))))

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
    for mode in (replay, files):
        mode.add_argument("--model", required=True, choices=sorted(VERIFIED_MODELS))
        mode.add_argument("--reasoning-effort", required=True)
    args = parser.parse_args()
    if args.reasoning_effort not in VERIFIED_MODELS[args.model]:
        raise ControllerError("unverified_model_or_effort")
    key = secure_key()
    try:
        if args.mode == "replay":
            if args.output.exists() or args.prompt.stat().st_size > MAX_BYTES:
                raise ControllerError("replay_file_bound")
            prompt = args.prompt.read_text()
            schema = read_json(args.schema)
            _, audit = infer(prompt, schema, key, model=args.model, reasoning_effort=args.reasoning_effort)
            atomic_json(args.output, audit)
        else:
            file_mode(args.control, key, args.audit, model=args.model, reasoning_effort=args.reasoning_effort)
    finally:
        key = None
    print("Finished; inspect local audit. No game actions executed by this driver.")

if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        # No exception repr/tracebacks: those can contain HTTP details.
        print("Controller stopped:", str(error) if isinstance(error, ControllerError) else type(error).__name__)
        raise SystemExit(1)
