# Bounded multi-segment native navigation

Explicit local skill, not a replacement for original MoveTo. Model integration is pending. The deterministic fixture requests bearing90 (+X), distance3m, maximum12s, at most4 fresh local segments. Endpoint distance<=.4m is required for overall Arrived. Every segment still observes and settles; this is NOT uninterrupted walking. Host must forbid every other write while BoundedLocalNavigation.Busy, including the interval between segment leases. No second AtomicActions owner, hidden terrain route query, model, inventory injection or fixed production script.

`BoundedLocalNavigation` API: Start(requestId,bearing,distance,maxSeconds), Update, Cancel, Busy, Terminal, Snapshot. Bearing[0,360), +Z=0/+X=90, distance[1,4], seconds[1,12]. Total navigation budget triggers cancel/drain; local settling can take another bounded5s and may quarantine. A cancelled/failed segment cannot cause another segment. Lower-level LocalNavigationSkill's TTL3/observation/body guards remain unchanged.

## Native01 results

| case | outcome | total host wall | segments | actual path | remaining |
|---|---|---:|---:|---:|---:|
| flat | Arrived |11.342s|3|2.81394m|.186m|
| cancel | Cancelled and settled |7.088s|1|.23064m|2.769m|
| obstacle | Failed and settled |9.012s|1|.25m|2.75m|

All health1, GameWidgetnull, update failures0, cleanupVerifiedtrue, no quarantine. Wall injection's exact failure was `segment_evidence_expired`, not native Blocked. Raw per-segment evidence is in evidence/*-01.jsonl, hashes in manifest01.json. Existing skill logs preserve source observation ages and segment terminal paths, but these records do not include explicit timestamps on every segment event; precise per-segment idle/dwell duration is not measured by this batch. No true model/navigation overlap has been validated here.

Pure tests: tests/Westworld.BoundedNavigation.Tests,21 budget checks. They do not prove native lifecycle or interception of arbitrary external writes. A future host must enforce ownership at all dispatch paths and use the skill's own terminal result, not ActorTools.action_status, before asynchronous handoff.

Build with cloud SDK using serial `dotnet build .../BoundedNavigationGate.csproj -c Release --no-restore -m:1`. Stage binaries plus Content.zip into a new work directory without doc/Mods; launch normal desktop `dotnet BoundedNavigationGate.dll flat|cancel|obstacle`. Never reuse a world. No generated code or external model call runs in this gate.

## Ownership02 regression

Constructor now requires exact player/skill/lease binding. Start rejects an already-active supplied skill before mutating the wrapper; exception cleanup may cancel only its own newly assigned segment request. Update/Cancel reject ownership loss. Native ownership02 passed5.627s: same binding accepted; a distinct ComponentPlayer object and wrong lease rejected; an existing independent skill request ID/status preserved after rejected wrapper Start; only the original owner subsequently cancelled/drained it. Health1, speed0, zeroGameWidget/update failures, cleanuptrue. Evidence and exact deployed DLL hashes are separate from01. This used a distinct unbound ComponentPlayer object for the wrong-player constructor test, not a second live-world actor.

The later read-only Matches(player,AtomicActions) overload is for unified gateway registration and awaits the integration build; no behavior claim is added to ownership02 from that later overload.
