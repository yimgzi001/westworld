# Async decision mailbox contract tests

Run with .NET 10:

```
dotnet run --project tests/Westworld.Decision.Tests/Decision.Tests.csproj -c Release
```

These are deterministic provider replacements only. They do not call a model, launch
Survivalcraft, or prove native-world survival. Thread synchronization uses explicit
signals; small wall-clock waits exercise actual deadline behavior.

`AsyncDecisionScheduler(provider, deadline, maxConcurrent: 2, capacity: 64)` is created
and used on the game thread. `Request(snapshot, out error)` clones immutable-record
arrays and dispatches provider work with Task.Run, including the provider's synchronous
prefix. `TryTake(actor, out decision, out error)` publishes one result or a bounded
error code. The host must poll; taking a decision never executes it. The host remains
responsible for current native availability, observation freshness, skill allowlists,
and mapping DecisionId to idempotent native action tickets.

Only one unconsumed request/result exists per actor. Timeout retires the result, not
the worker: an ignoring provider (or blocked cancellation callback) retains its
concurrency slot until it actually finishes. Retrying that actor cannot create a new
worker meanwhile. Results completed before their deadline remain available even if
the game thread polls late. Results completed late are rejected even if there was no
intervening poll. Slots are not forcibly terminated; an uncooperative in-process
provider can exhaust capacity, reported rather than hidden by unbounded replacement.
Disposal signals cancellation asynchronously and rejects subsequent operations.
It is not a sandbox and cannot stop arbitrary provider process-wide side effects.

Decision Goal/Skill are size-bounded strings; the scheduler intentionally does not
invent a skill catalog. Slot is -1 (not applicable) or 0..4095; ExpectedValue is a
native encoded integer whose semantic validation belongs to the execution adapter.
No observation, model output, or exception text is written to logs by the scheduler.
