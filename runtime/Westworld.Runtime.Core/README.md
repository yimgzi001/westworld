# Actor / view / simulation-interest contract

This pure .NET 10 library is the first shared runtime boundary from ADR-001, not another native game demonstration. It has no engine, network, model or graphics dependency. Tests use identity records, **not five live Survivalcraft bodies**.

## Contract
- WorldId and stable ActorId/native EntityKey are independent of the optional human ViewId.
- Add never allocates a view. Zero AI views and zero human views are valid; this phase allows at most one optional human view.
- Active, reconciled actors supply SimulationInterests, whether or not a camera exists. Moving/removing a camera never changes actor state or activity.
- These centers are requests for the future native adapter, not proof chunks/animals are updating. Terrain and spawn adapters must consume them separately from render views.
- Only the owning game thread may access registry state. Model/network workers must use immutable messages through a later queue, not call the registry.
- Update receives a native-observed position, current runtime session and expected actor revision. It does not move a body or affect health, inventory or world facts.
- Capture/Serialize produce actor metadata only. Retired identity tombstones are persisted to prevent remove/re-add identity reuse and stale updates hitting a replacement body. Views and runtime SessionId are not persisted. Snapshot arrays are detached from live state.
- Restore validates complete schema, world, required fields, duplicates, binding uniqueness, numeric envelope and revision. It creates a new session and no views. Native entity bindings start unreconciled, so no simulation interests are published until the host checks native saved entities and calls Rebind. Restoring metadata never spawns a replacement body. Restore must run on the owning game thread. Rebind is per actor; the host must keep its world-recovery barrier closed until all required bodies are reconciled before consuming interests.
- A native world checkpoint and this snapshot must still be coordinated by the future persistence adapter. This library does not implement atomic world saving or claim exactly-once game effects.

## Limits and ownership
Position is a privileged native-host cache, never a model-visible global map. Schema1 uses an explicit experimental X/Z ±10,000,000 and Y ±1,000,000 envelope, max1000 live identities and10,000 retired identity tombstones, JSON size max1,000,000 characters/depth8. These are validation bounds, not a measured capacity or a game-world limit. `Active=false` only affects interest publication; it does not freeze a real entity. The adapter must define death/dormancy semantics and validate full player-equivalent survival components.

Native EntityKey is opaque, nonempty, <=128 characters without control characters. The adapter must ensure it actually belongs to this world; this core cannot prove an entity exists or that a caller's observation is truthful. Capturing state is host-only, not an AI observation API.

## Validation
Run from repository root:
```sh
dotnet run --project tests/Westworld.Runtime.Core.Tests/Tests.csproj -c Release
```
Test coverage: five records with zero views, optional one-view lifecycle, independent actor activity and position, stale revision/session, detached snapshots, restart identity/activity continuity, required/duplicate/unknown fields, malformed positions, duplicate actor/native binding, native-rebind gate, thread ownership and removal.

The missing-coordinate regression initially failed because record-struct deserialization silently defaulted X to0 despite required-constructor settings. Explicit JsonRequired coordinate attributes fixed it. This is a real rejected bad-checkpoint case, not a native movement test.

Next integration gate: connect these identities and interests to real SCAPI entities and chunk/spawn activity; prove five real bodies with zero AI views and 0/1 human view, then save/reload native state. No MaxPlayers hack, dummy animals or hidden rendered AI views count as that gate.

Independent review found remove/re-add could reset revision and accept an old request in the same session. Persisted retired-ID tombstones now reject reuse both before and after restart; regression covers this. Identity-stage cloud result:49/49 pure contract checks, no native game integration tested.

Activity-center stage: InterestCoordinator and IInterestBackend now separate actor activity from camera state.70/70 pure core checks pass; native adapter details and remaining world-test gaps are in ../Westworld.Scapi.Adapter/README.md.
