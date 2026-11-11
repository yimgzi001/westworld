import argparse
import json
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        try:
            assert self.headers["Authorization"] == "Bearer local-test-key"
            payload = json.loads(self.read_body())
            assert payload["model"] == "gpt-6-luna"
            if self.path.endswith("/responses"):
                assert payload["store"] is False
                assert payload["max_output_tokens"] == 1536
                assert payload["text"]["format"]["type"] == "json_object"
                message = payload["input"]
            else:
                assert self.path == "/v1/chat/completions"
                assert payload["max_completion_tokens"] == 1536
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
        except (AssertionError, KeyError, StopIteration, ValueError) as error:
            if self.path.startswith("/global/solo-production/"):
                self.log_error("solo production fixture: %s %s", type(error).__name__, error)
            self.send_json(400, {"error": {"message": "mock request did not satisfy the contract"}})

    def global_reply(self, context):
        mode = self.path.split("/")[2]
        actor = context["profile"]
        if mode == "solo-production":
            self.solo_production(context)
            return
        if mode == "founding-quota":
            marker = Path("quota-test-unknown")
            unknown = actor.startswith("你叫杉1，") and "initial" in context["reasons"] and not marker.exists()
            if unknown:
                marker.touch()
            self.send_global_decision({"reason": "Review on the next turn", "task": "keep", "reviewAfter": 1}, not unknown)
            return
        if mode == "founding-resume":
            assert actor.startswith("你叫") and "部落成员" in actor
            decision = {"reason": "Keep the existing task", "task": "keep", "reviewAfter": 120}
            if "initial" in context["reasons"] and not Path("founding-resume").is_file():
                food = next(item for item in context["inventory"]["slots"] if item["slot"] == 0 and item["count"] == 3)
                eat = {"tool": "act_Eat", "arguments": {"slot": 0, "value": food["value"], "count": 1}}
                middle = {"tool": "act_Wait", "arguments": {"duration": 12}}
                if actor.startswith("你叫杉1，"):
                    middle = {"tool": "act_Speak", "arguments": {"mode": "shout",
                        "text": "These words begin before saving. The rest of the sentence is spoken after reopening the same world."}}
                elif actor.startswith("你叫禾1，"):
                    middle = {"tool": "act_FaceTowards", "arguments": {"bearing": 90, "timeout": 12}}
                decision.update(task="start", steps=[eat, middle, eat])
            self.send_global_decision(decision)
            return
        if mode == "founding":
            assert actor.startswith("你叫") and "部落成员" in actor
            decision = {"reason": "Wait for the next review", "task": "keep", "reviewAfter": 4}
            food = next((item for item in context["inventory"]["slots"] if item["slot"] == 0 and item["count"] == 1), None)
            if Path("founding-player-away").is_file():
                decision["reviewAfter"] = 120
                if food and "act_Eat" in context["actions"] and "act_MoveTo" in context["actions"]:
                    decision.update(task="start", steps=[
                        {"tool": "act_Eat", "arguments": {"slot": 0, "value": food["value"], "count": 1}},
                        {"tool": "act_MoveTo", "arguments": {"bearing": 0, "distance": 1, "range": .25}},
                    ])
            self.send_global_decision(decision)
            return
        if mode == "observe-ahead":
            self.observation_reply(context)
            return
        if mode == "observe-world":
            index = int(actor.split()[1].removesuffix("."))
            decision = {"reason": "Rest", "task": "keep", "reviewAfter": 120}
            if index == 0 and ("initial" in context["reasons"] or "nextTask" in context["reasons"]):
                item = next(item for item in context["inventory"]["slots"] if item["slot"] == 0)
                eat = {"tool": "act_Eat", "arguments": {"slot": 0, "value": item["value"], "count": 1}}
                if "initial" in context["reasons"]:
                    decision.update(reason="Eat once, then wait", task="start", steps=[eat,
                        {"tool": "act_Wait", "arguments": {"duration": 4}},
                        {"tool": "act_Wait", "arguments": {"duration": 1}}])
                else:
                    decision.update(reason="Eat the remaining portion after waiting", task="start", steps=[eat])
            if index == 1:
                time.sleep(7)
            self.send_global_decision(decision)
            return
        if mode in ("scale", "scale-action"):
            index = int(actor.removeprefix("scale "))
            assert actor == f"scale {index}" and 0 <= index < 50
            initial = "initial" in context["reasons"]
            assert initial or (mode == "scale-action" and "taskEnded" in context["reasons"]) or (index < 5 and "review" in context["reasons"])
            time.sleep(.3 if index % 5 == 0 else .12)
            if mode == "scale-action" and initial:
                assert "act_Jump" in context["actions"]
                self.send_global_decision({"reason": "Test footing", "task": "start", "reviewAfter": 120,
                    "steps": [{"tool": "act_Jump", "arguments": {}}]})
                return
            self.send_global_decision({"reason": "Wait for the next review", "task": "keep",
                "reviewAfter": 2 if initial and index < 5 else 120})
            return
        raise ValueError("Unknown global fixture mode")

    def observation_reply(self, context):
        index = int(context["profile"].split()[1].removesuffix("."))
        decision = {"reason": "Use the test plan", "task": "keep", "reviewAfter": 120}
        move = {"tool": "act_MoveTo", "arguments": {"bearing": 0, "distance": 3, "range": .25}}
        wait = {"tool": "act_Wait", "arguments": {"duration": 5}}
        if "initial" in context["reasons"]:
            last_step = dict(wait, minimum_affected=1) if index % 5 == 1 else wait
            decision.update(task="start", steps=[move, last_step])
        elif "nextTask" in context["reasons"]:
            assert context["task"]["status"] in ("waiting", "running", "blocked")
            time.sleep(.45)
            if context["task"]["totalSteps"] == 2:
                decision.update(task="start", steps=[move if index % 5 == 0 else wait])
            else:
                assert context["lastOutcome"]["status"] == "succeeded"
        self.send_global_decision(decision)

    def solo_production(self, context):
        production = context["observation"]["production"]
        carried = production["carried"]
        grid = {slot["slot"]: slot for slot in production["personalGrid"]}
        decision = {"reason": "Review the real inventory and surroundings", "task": "keep", "reviewAfter": .3}
        if "nextTask" in context["reasons"]:
            self.send_global_decision(decision)
            return

        def step(tool, **arguments):
            return {"tool": f"act_{tool}", "arguments": arguments}

        steps = []
        bread = next((item for item in carried if item["item"] == "BreadBlock"), None)
        axe = next(item for item in carried if item["item"] == "StoneAxeBlock")
        has_table = any(item["item"] == "CraftingTableBlock" for item in carried)
        if bread:
            steps = [step("Eat", slot=bread["slot"], value=bread["value"], count=1)]
        elif not has_table:
            for name, ingredient_id in (("CraftingTableBlock", "planks"), ("PlanksBlock", "wood")):
                ingredient = next((item for item in carried if item["craftingId"] == ingredient_id), None)
                if not ingredient:
                    continue
                recipe = next(item for item in production["recipes"] if item["name"] == name)
                steps = [step("TransferItems", source="carried", source_slot=ingredient["slot"], source_value=ingredient["value"],
                              destination="personal", destination_slot=part["slot"], destination_value=0, count=1)
                         for part in recipe["ingredients"] if part["slot"] not in grid]
                steps.append(step("Craft", workspace="personal", result_value=recipe["resultValue"]))
                break
            if not steps and production["materials"]:
                material = production["materials"][0]
                pickup = step("PickUp", material_value=material["value"], count=1)
                if material["inReach"]:
                    steps = [pickup]
                else:
                    steps = [step("MoveTo", bearing=material["bearing"],
                                  distance=max(1, min(4, material["horizontalDistance"] - .45)), range=.25)]
            if not steps and production["logs"]:
                log = production["logs"][0]
                steps = [step("SelectItem", slot=axe["slot"], value=axe["value"]), step("Dig", target=log["target"])] if log["inReach"] else [
                    step("MoveTo", bearing=log["bearing"], distance=2, range=.35)]
        if steps:
            decision.update(task="start", steps=steps)
        self.send_global_decision(decision)

    def send_global_decision(self, decision, known_usage=True):
        reply = {"model": "gpt-6-luna", "status": "completed",
            "output": [{"type": "message", "role": "assistant", "content": [{"type": "output_text", "text": json.dumps(decision)}]}]}
        if known_usage:
            reply["usage"] = {"input_tokens": 800, "output_tokens": 60}
        self.send_json(200, reply)

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
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=18759)
    MockServer(("127.0.0.1", parser.parse_args().port), Handler).serve_forever()
