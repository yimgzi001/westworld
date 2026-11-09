# Local model decision mapping

This project references only Runtime.Core. LocalDecisionPrompt is a pure mapper.
LlamaProcessProvider is a separate, trusted local-process implementation of IDecisionProvider.
Neither executes generated code, reads a native world, calls an external API or holds credentials.

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

## Verified local runtime
LlamaProcessProvider starts the verified llama-completion binary without a shell,
uses Qwen3.5-4B Q4_K_M with 2048 context, 4 CPU threads, reasoning off, max96 generated
tokens and a JSON schema. Stdout may contain exactly the fixed trailing
[end of text] marker; after removing it the entire remaining text must pass the
strict mapper. Stderr never becomes an action. Failure is surfaced, not replaced
with a scripted decision.

Cancellation/60-second deadline kills the finite child process and waits for its
exit. Stdout/stderr are drained concurrently. Metrics include wall time with model
loading, 100ms-sampled process RSS/CPU and llama.cpp prompt/evaluation counters
when present; these are not API billing tokens or total game memory.

This first integration loads a process per request. The native gate uses a single
model slot; it is NOT a persistent shared pool or a multi-agent throughput result.
Use the provenance manifest and native evidence below, not estimates, to compare.
A slow response is never used to backdate an expired observation: native host
creates a fresh observation, rechecks the identical target, and records both IDs.

Final provider reads both streams with pre-append limits and cancellation through EOF;
cleanup waits are bounded. LastMetrics is per provider, only verified with concurrency=1.
BackendGenerationEvaluations reports llama.cpp eval runs, not exact final-text token
count and not billable API output. 54 mapper/stream tests cover limits and canceled EOF.
