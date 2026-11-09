import json
import time
from threading import Lock
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class Handler(BaseHTTPRequestHandler):
    rate_lock = Lock()
    rate_calls = set()
    def do_POST(self):
        try:
            assert self.headers["Authorization"] == "Bearer local-test-key"
            payload = json.loads(self.read_body())
            assert payload["model"] == "gpt-6-luna"
            if self.path.endswith("/responses"):
                assert payload["store"] is False
                assert payload["max_output_tokens"] == 768
                assert payload["text"]["format"]["type"] == "json_object"
                message = payload["input"]
            else:
                assert self.path == "/v1/chat/completions"
                assert payload["max_completion_tokens"] == 768
                assert [item["role"] for item in payload["messages"]] == ["system", "user"]
                message = payload["messages"][1]["content"]
            context_json, schema_json = message.split("\nAvailable action schemas JSON:\n")
            context = json.loads(context_json.split("\n", 1)[1])
            schemas = {entry["name"] for entry in json.loads(schema_json)}
            if self.path.startswith("/global/"):
                self.global_reply(context)
                return
            assert "review" in context["reasons"]
            assert "act_Eat" in schemas and "submit_plan" in schemas and "read_social" not in schemas
            item = next(item for item in context["inventory"]["slots"] if item["count"] == 1)
            decision = json.dumps({
                "task": "start", "reason": "Eat the food already in my pack", "reviewAfter": 3,
                "steps": [{"tool": "act_Eat", "arguments": {"slot": item["slot"], "value": item["value"], "count": 1}}]
            })
            if self.path.startswith("/rate/"):
                self.send_json(429, {"error": {"message": "test rejection"}}, {"Retry-After": "2"})
            elif self.path.endswith("/responses"):
                if self.path.startswith("/bad/"):
                    decision = r'{"task":"start","steps":[{"arguments":{"text":"\uD800"}}]}'
                self.send_json(200, {"model": "gpt-6-luna", "status": "completed",
                    "output": [{"type": "message", "role": "assistant", "content": [{"type": "output_text", "text": decision}]}],
                    "usage": {"input_tokens": 800, "output_tokens": 60}})
            else:
                self.send_json(200, {"model": "gpt-6-luna",
                    "choices": [{"finish_reason": "stop", "message": {"role": "assistant", "content": decision}}],
                    "usage": {"prompt_tokens": 800, "completion_tokens": 60}})
        except (AssertionError, KeyError, StopIteration, ValueError):
            self.send_json(400, {"error": {"message": "mock request did not satisfy the contract"}})

    def global_reply(self, context):
        mode = self.path.split("/")[2]
        actor = context["profile"]
        if mode in ("scale", "scale-action"):
            index = int(actor.removeprefix("scale "))
            assert actor == f"scale {index}" and 0 <= index < 50
            initial = "initial" in context["reasons"]
            assert initial or (index < 5 and "review" in context["reasons"])
            time.sleep(.3 if index % 5 == 0 else .12)
            if mode == "scale-action":
                assert initial and "act_Jump" in context["actions"]
                self.send_global_decision({"reason": "Test footing", "task": "start", "reviewAfter": 120,
                    "steps": [{"tool": "act_Jump", "arguments": {}}]})
                return
            self.send_global_decision({"reason": "Wait for the next review", "task": "keep",
                "reviewAfter": 2 if initial and index < 5 else 120})
            return
        assert actor in ("cohort 0", "cohort 1")
        assert {"initial", "taskEnded"}.intersection(context["reasons"])
        if mode == "rate" and actor == "cohort 0":
            with self.rate_lock:
                first = context["id"] not in self.rate_calls
                self.rate_calls.add(context["id"])
            if first:
                self.send_json(429, {"error": {"message": "fixture rate limit"}}, {"Retry-After": "1"})
                return
        time.sleep(6 if mode == "ambiguous" else .45 if actor == "cohort 0" else .1)
        self.send_global_decision({"reason": "Wait briefly", "task": "start", "reviewAfter": 120,
            "steps": [{"tool": "act_Wait", "arguments": {"duration": .15}}]})

    def send_global_decision(self, decision):
        self.send_json(200, {"model": "gpt-6-luna", "status": "completed",
            "output": [{"type": "message", "role": "assistant", "content": [{"type": "output_text", "text": json.dumps(decision)}]}],
            "usage": {"input_tokens": 800, "output_tokens": 60}})

    def read_body(self):
        if self.headers.get("Transfer-Encoding") != "chunked":
            return self.rfile.read(int(self.headers["Content-Length"]))
        parts = []
        while True:
            size = int(self.rfile.readline().split(b";", 1)[0], 16)
            if not size:
                return b"".join(parts)
            parts.append(self.rfile.read(size))
            assert self.rfile.read(2) == b"\r\n"

    def send_json(self, status, body, extra=None):
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Connection", "close")
        self.send_header("x-request-id", "local-model-smoke")
        for name, value in (extra or {}).items():
            self.send_header(name, value)
        self.end_headers()
        self.wfile.write(data)


class MockServer(ThreadingHTTPServer):
    request_queue_size = 32


if __name__ == "__main__":
    MockServer(("127.0.0.1", 18759), Handler).serve_forever()
