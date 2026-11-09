# Structured file controller (not player chat)

Launch a fresh staged directory: `bash work/<label>-craft/run.sh file`. Default remains `llama`. File mode does not construct or start llama; dot internal token usage is not exposed and must not be reported as zero.

The game thread publishes `control/0001-<request UUID>/request.json`. It contains sessionId, actorId, observationId, requestId, the same bounded public context/offers/schema/prompt used by the local model, and timestamps. It contains no fixture audit, hidden prey positions or privileged world query. The game continues asynchronously while waiting.

Read the request; write a JSON response to `response.tmp`, then atomically rename to `response.json` in that request directory:
```json
{"sessionId":"copy actual request","actorId":"copy actual request","observationId":"copy actual request","requestId":"copy actual request","decision":{"goal":"your short goal","steps":[]}}
```
Only an offered tool with its exact current argument schema is valid. No arbitrary scripts or new tool names. Native execution remains behind NativeToolSession and per-step fresh observation revalidation. The file adapter never executes actions. A decision is not a successful effect.

Each response is atomically claimed into an immutable numbered received file. Strict envelope fields/identity, 64KiB limit and the existing ToolDecisionPrompt parser apply. Wrong/old identifiers, duplicate fields and unoffered actions are rejected. Identical rejected content hashes log once per request. Valid response is consumed once; later duplicate files cannot replay it. Invalid submissions do not extend the 120-second request deadline. Fresh control directory is mandatory, so old sessions cannot resume implicitly.

Native wall budget595 seconds and12 decision rounds remain unchanged. Protocol tests create temporary data files only; no game/model/native action.

## Timing semantics

Logs separate goal_accepted, decision_request_started (file additionally controller_request_published), response_received and host model_response, tool_step_queued, tool_dispatch_started/returned and asynchronous tool_terminal_observed. Dispatch logs report request-to-dispatch INCLUDING decision wait, reply-observed-to-dispatch (scheduling only), and goal-to-first-dispatch. Terminal timestamps include native game time and wall time. Read-only tool dispatch is not character movement; filter tool names and require native result/effect evidence. Dot response time includes coordination latency, not just internal model inference. None of these measurements is player game-chat latency.

File protocol tests: tests/Westworld.FileController.Tests;26/26 passing. A same-runtime dot/4B artificial crafting comparison is archived in evidence; dot passed and 4B exhausted its deadline. The post-trial attempt-cap/monotonic timing fixes are pure-tested and compiled, not rerun in that native comparison.
