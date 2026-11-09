# Local model decision mapping

This project references only Runtime.Core. Its prompt/parse classes are pure protocol code; `LlamaProcessProvider` runs the explicitly configured local llama CLI. It does not execute model-generated code, read a world, call a cloud API, or hold credentials.

```csharp
var request = LocalDecisionPrompt.Create(observation, isSupportedHarvestValue);
string prompt = request.Prompt;
// The separate provider obtains a bounded final response from the chosen model.
HighLevelDecision decision = request.ParseResponse(finalResponse);
```

The trusted native caller supplies `Func<int,bool> isSupportedHarvestValue` to
identify supported mature ordinary pumpkins from complete block values. Unknown
cells are excluded before calling this predicate. Edible candidates require a
positive count, `CanEat=true`, and finite positive Nutrition supplied by the host.

The prompt exposes fullness, health and at most 24 compact candidates (configurable
1..64), not the full local cell grid, session IDs, inventory encoding, or arbitrary
text from game entities. Inventory candidates sort first by slot; harvesting
candidates sort by proximity then coordinates. Capping is deliberate and is not a
claim that omitted candidates do not exist. It is a capability-selection interface,
not a general natural-language world observation.

The only accepted output is one strict JSON object with `skill` and
`candidate_index`. Wait requires -1; other skills must match the selected candidate.
Unknown fields, duplicate keys (including escaped aliases), fences, comments,
trailing text, fractional/exponent indices, hallucinated coordinates and code are
rejected. Parse errors are `FormatException` or `JsonException`; a conflicting
second valid response to the same prompt throws `InvalidOperationException`.

The mapper stores private candidate records, binds all scope IDs itself, and assigns
one decision ID per prompt. Repeating the same choice returns the same decision;
changing it after acceptance fails. Source-array changes cannot change a candidate.
Freshness, current native conditions and side-effect idempotency remain the native
adapter's responsibilities. Do not silently turn parse failures into a fabricated
successful decision. The model's final text, not CLI event/status JSON, is the parser
input. No response is interpreted as a command or script.

Tests:
`dotnet run --project tests/Westworld.LocalDecision.Tests/LocalDecision.Tests.csproj -c Release`


## Composable tool plans

`ToolDecisionPrompt` accepts host-provided native tool schemas and actor-private observed reference bindings. The model chooses its own stage goal and zero to four calls, rather than selecting a prescribed food workflow. The host executes each step on the game thread, revalidates current native references, waits for real asynchronous action/task completion, and provides failures and actual effects for replanning. JSON plans are not generated scripts.

The local provider defaults remain context2048/output96 for existing single-choice calls. The tool-agent experiment explicitly requests context8192/output512 and records real CPU/RSS/prompt tokens. Bounds are 2048..8192 context and64..512 output; larger configured capacity is not a claim of actual tokens used. Calls on one provider instance are serialized; a second overlapping call is rejected. A persistent multi-actor model pool is not implemented.

## Failure audit

`LastAudit` complements (does not replace) `LastMetrics`: it records whether the process started, terminal outcome, actual elapsed wall time, known sampled CPU/RSS, exit code, and only timing counters parsed from completely read stderr. Unknown canceled-call tokens are null, not zero. The host must persist the audit after normal completion, failure and shutdown cancellation. No stderr text or environment is retained by this record.
