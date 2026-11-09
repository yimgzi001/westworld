# Actor-private local exploration

Run: `dotnet run --project tests/Westworld.Exploration.Tests/Exploration.Tests.csproj -c Release`

Public types live in `Westworld.Runtime.Core`:

```csharp
var explorer = new LocalExploration(sessionId, actorId, capacity: 128, failureBackoffSeconds: 10);
var observation = new ExplorationObservation(sessionId, actorId, observationId,
    source.Time, source.Position, candidates); // ExplorationCandidate[]
var selection = explorer.Select(observation, nowFromSameClock);
// Execute selection.Choice via main's sole native movement executor, then:
explorer.Report(choice.ChoiceId, nowFromSameClock, actualPosition, ExplorationOutcome.Arrived);
```

`ExplorationCandidate(Guid EvidenceId, Position Destination, bool KnownSafe)` must
come from a trusted projection of actual local observation. Evidence IDs are not
cryptographic authentication. The core verifies scope/format but cannot prove that a
caller truthfully marked a safe route. KnownSafe must mean both approach and target
were checked; unknown is never considered safe. Do not feed world scans or generated
model coordinates into this interface. Host retains proof cells for revalidation.

Select consumes at most 32 candidates within 4 meters and chooses a different cell
at least 1 meter away, excluding blocked directed edges. Among remaining safe
candidates it prefers a bounded observed frontier, lower visit counts, avoiding a
recent return, least recently visited cells, successful forward heading, distance, then coordinate
order for deterministic ties. Results carry original scope and evidence plus a new
choice identity and explanation. The candidate list is copied; its later mutation
cannot rewrite an issued choice. This is a bounded heuristic, not route planning.

Only one choice is pending per actor. Report accepts only that issued choice ID;
replayed/foreign receipts are rejected. Arrived requires actual 3D position within
0.4 meters of the target. Visits record the supplied actual position, never the
requested target by assertion. Movement failure retains actual partial movement;
Blocked/TimedOut back off the original directed edge, Interrupted does not. Once
accepted, every issued choice must be reported or explicitly interrupted; there is
no hidden auto-reissue that could overlap an existing native action.

All calls use the owner game thread, but timestamps are **the observation's own
monotonic clock**, not necessarily simulation game time. Use ActorObservation.Time's
clock for Select and Report; never mix it with SubsystemTime.GameTime. Observations
must be at most 2 seconds old and not from the future; submitted times cannot go
backwards. A new world/session needs a new instance. This memory is not checkpointed.

Memory cap counts visits and blocked edges together. Expired edges are removed on
calls. Capacity pressure evicts oldest visit history first, then the earliest
expiring edge if only edges remain. Therefore bounds are guaranteed, but indefinite
route avoidance is not. Polling without changing cells does not inflate visits.

Tests use deterministic times and positions only. They do not prove real physics,
collision safety, food discovery, native locomotion, or model decisions.


## Observed-frontier and anti-loop hints

Each retained visited cell records at most 32 directed neighboring cells that were
actually supplied as known-safe candidates. Hints expire for ranking after 30 seconds;
unknown terrain is never scanned or inserted. A bounded breadth-first search (at most
128 expanded visited cells per candidate) ranks routes toward a remembered unvisited
neighbor. These are fallible exploration hints, not a global path or a proof that an
old edge is still open. **Every selected first hop must be in the current fresh safe
candidate array**, even when memory points elsewhere. No historical edge is executed
automatically. Blocked directed edges are excluded from both ranking and selection.

When frontiers/counts tie, the successful movement heading and visit recency discourage
short reversals. Dead ends are still allowed to return; direction is a preference,
not a constraint. Failed or interrupted movement does not establish a new successful
heading. Choice.Reason exposes the remembered frontier hop count or `none`.

`MemoryCount` continues to count retained visits plus failed edges (<= configured
capacity). Neighbor hints are nested in visit records and bounded by 32 * capacity;
`NeighborHintCount` exposes this additional count. Evicting a visit also removes its
hints. Bounded eviction can make old visited places appear new again; no permanent
coverage or optimum routing is claimed. No checkpoint persistence is added.

Long deterministic tests cover a 60-step corridor, a 96-step cycle without two-cell
ping-pong, dead-end return, a remembered branch that beats the former coordinate tie,
failed first-hop fallback, expired hints and refusal to invent an absent first hop.
These tests do not prove improved distances in the real game; the host must replay
native scenarios and report actual coverage separately.
