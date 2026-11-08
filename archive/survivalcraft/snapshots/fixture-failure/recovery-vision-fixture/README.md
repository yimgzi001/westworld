# Explicit artificial vision fixture

NEW RECONSTRUCTION, not recovered original code and not autonomous discovery.
Use only a new isolated disposable copy. Do not load this with RecoveryVision.netmod or other agent bridges.

Set SC_RECOVERY_FIXTURE=1 and SC_RECOVERY_VISION_DIR to a new private IPC directory.
Single main player must be ready, awake, standing, healthy, fed, still, with near-horizontal real eye rotation. The fixture waits for suitable loaded natural AIR rays, up to the unchanged bridge session deadline. It does NOT clear existing terrain. Edits are at head-cell height, at least 1.5 coordinate units from player's XZ center; no floor/ground/player-occupied cells are edited. Maximum 8 unique cells. No movement, yaw or model calls.

Four layouts front/left/right/behind are selected from real eye geometry. They are counterpart classifications, NOT exact reflected grid layouts. Each gets one artificial dead mature pumpkin and is read synchronously with native terrain APIs. Front/left/right also get a temporary intermediate stone cell to verify obstruction. All touched values are restored at the end. Neighbor update effects or queued block behaviors are not proven absent; do not use or save the experimental copy as a natural-world result.

Before any ChangeCell, fixture-plan.json records original raw values and layout. fixture.claim is CreateNew and flushed; it is never deleted. All edit intents/results logged in fixture-edits.jsonl. Results in fixture-result.json, restoration in fixture-restoration.json. Failure or interruption must be inspected rather than replayed; never delete claim to retry. New experiment requires a new isolated copy and new IPC directory.

Success requires allPassed=true AND restoreOk=true, and gameplay safety/state independently inspected. The original visibility observer runs after restoration, so normal vision.json is not the fixture result. No food inventory is injected, no pickup/eat is performed. Same-tick native API checks do not prove persistent geometry, rendered visibility, perception latency or model discovery.

Compile:
../dependencies/dotnet/dotnet build RecoveryVisionFixture.csproj
Deliverable:
dist/RecoveryVisionFixture.netmod

## 0.1.2 saved-world binding correction
Initial isolated02 run loaded the save but produced no IPC state/hide effect/claim; parent exited. Suspected missing OnPlayerSpawned for saved-world load, not a proven unique root cause. This revision can bind from UpdateInput only when roster has exactly one PlayerData and ComponentPlayer, references match, and candidate is main. Multiple/replacement players fail closed. Stopwatch starts only once. bridge-initialized.json, player-bound.json and binding-deferred/fault provide stage evidence. Old source/DLL/package and reported failure retained under history/pre-load-bind-0.1.1. 0.1.2 compiles with zero warnings/errors; native loading still pending.
