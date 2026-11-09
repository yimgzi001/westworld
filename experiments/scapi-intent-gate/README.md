# Native observation → intent → skill gate (2026-11-09)

This is the first reusable NativeActorSession seam, not an LLM integration. A fixed trusted caller observes and requests bounded movement; native simulation settles the outcome.

## Design contract
- Game-thread binding maps an external native Player to ActorRegistry identity. Human bindings are rejected. Model code receives DTOs only, never the session/native object.
- Observation: own position/health/food/inventory; fixed100local cells (5×5×4), <=5m from eyes,120degree cone. Only already Valid chunks are read.
- Occlusion uses conservative opaque-voxel DDA. Transparent blocks do not occlude; opaque voxel holes are conservatively hidden. It is not pixel-perfect rendering or collision-ray visibility.
- Unknown cells always carry the same label and null value, regardless of why they are unknown. No full world/other-actor inventory API is exposed in this observation.
- Native block Value includes light/data bits. Use Terrain.ExtractContents to identify air/content; value15360 can be air.
- MoveIntent binds session/actor/action/observation IDs and normalized actor-relative Right/Forward inputs. Duration0.05–1second, game-tick quantized (at most one additional allowed tick), dt<=0.25. Current facing at acceptance defines success projection.
- Observation must belong to the actor, be<=3seconds old and within2meters in3D. This is locomotion freshness, not proof that an old resource target remains visible.
- Tickets are recorded before input. Same ID plus identical payload returns its existing result even after observation expiry or entity retirement; changed payload is rejected. Global-per-session action IDs must be unique.256ticket cap rejects new work rather than silently evicting history.128observation cap; expired observations may retire.
- One active skill per actor; Accepted != Succeeded. Native directional progress plus horizontal settle produces Succeeded/Blocked. Cancel immediately clears pending and consumed movement; Canceled is reported after horizontal settle, not a freeze of gravity/collisions.
-2second settle limit and4second wall deadline terminate uncertain work. Missing/dead/asleep/unready actor fails the skill; removed entity retires binding/activity center. Terminals remain queryable. Session restart requires explicit new reconciliation; no durable cross-process ticket ledger yet.
- Host installs the session before native Input (order-15), an InterestCoordinator after registry refresh, and watches SubsystemUpdate failures. Host owns update registration/removal and must unregister before disposing a session.
- This is a **trusted in-process adapter**, not an authenticated public server. A later gateway must bind each caller to an actor and must not expose the host object, privileged registry or arbitrary native access.

## Real cloud result
See [structured evidence](evidence.jsonl).
- Known air target → temporary opaque wall inserted by test harness → unknown/null → wall restored → known again. This is an explicit fixture, not an AI building action.
-100cells:20known/80unknown at the recorded observation.
- Accepted initially had zero displacement; native finite move later completed0.8955m in the intended direction.
- Same-ID submission was stable; payload conflict, unknown/stale observation, and wrong session rejected.
- Terminal replay remained unchanged after observation expiry.
- Second move was canceled **while moving**. Pending/consumed input cleared; final travel0.1065m including pre-cancel movement and inertia. No instant physical-stop claim.
- Inventory unchanged and Health1.
- Entity removal cleared simulation interests; observation rejected; terminal replay still available.
- Exit0,10.651seconds, update failures0. No model/API calls.

First run failed in the fixture selector because it compared encoded native air Value to0. The test now extracts contents, and the successful evidence is from a fresh output. The first failed world remains untouched in ignored work storage.

## Reproduce
```sh
dotnet build experiments/scapi-intent-gate/IntentGate.csproj -c Release -m:1 -nodeReuse:false -o work/cloud-intent-run
cd work/cloud-intent-run
dotnet IntentGate.dll --new-disposable-world
```
Requires normal authorized graphical cloud context and fresh ignored output; refuses existing doc/Mods.90second per-frame timeout is not a hard process-kill timeout.

## Limits still open
No production host restart, token accounting, model planning, pathfinding, harvest/craft/eat chain, durable intent recovery, adversarial network authorization, exhaustive unloaded-chunk/FOV edge cases, ticket-cap stress or long-running survival. The next step is the host/checkpoint boundary and higher-level skills, **not one model call per small movement**.
