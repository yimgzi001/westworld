# Proposed multi-tick fixture: OFFLINE ONLY

This new state machine addresses isolated-mirror-03. It has NOT been integrated into or run in the game. Original v0.1.2 source/package and failed evidence are unchanged.

Evidence: target ChangeCell made its chunk InvalidLight. Production Loaded therefore correctly rejected it; this was not a successful occlusion test. Attempted restoration wrote raw 15360 (Air+light) but ChangeCell returned 0 (Air with light reset). Old restoreOk=false remains valid as a raw comparison failure. It does not prove original block content/data remained changed.

Integration contract:
1. Only a new explicit isolated copy, one persisted claim. Retain all existing binding, sole-main-player, health/air/food/standing/awake/monotonic wall/rotation safeguards. Never load alongside old bridges.
2. Preserve original raw, extracted contents, extracted data, extracted light separately using the native Terrain methods, not guessed bit masks.
3. Preflight each selected naturally-Air target/ray with production Loaded. Plan all edits and originals before claim. Check session total <=180 seconds in adapter, leaving cleanup time under 240-second bridge ceiling.
4. For each layout instantiate FixtureSequencer. Snapshot comes from actual live game state and full production perception output. PathChunksValid means target + every intermediate supercover cell's chunk is Valid, not only endpoint chunks. Visibility must match that actual target, not any resource in frame.
5. Step emits a write exactly once. Journal intent BEFORE ChangeCell; read back contents/data and record raw/light/chunk state. Acknowledge only a confirmed write. Never retry ambiguous write. NeedsReconcile requires read-only inspection and explicit recovery handling; it is NOT proof of restored terrain.
6. Wait for TWO observations on distinct advancing game ticks with all path chunks Valid after every target/wall change. InvalidLight resets stability; do not loosen Loaded, busy-loop, or force chunk state.
7. If unsupported pumpkin breaks/drops during wait, TargetContentDataMatches fails. Record failure and cleanup. For a later supported fixture, choose naturally supported target cells or a separately approved isolated layout, not hidden injection.
8. Restore contents/data once, then wait for valid chunks. Ignore raw mismatch for deciding whether another ChangeCell is needed. Report ContentDataRestored, ChunksValid, RawMatches, LightMatches separately. Different raw/light is not described as identical restoration.
9. Run cleanup before generic termination short-circuits the update hook. On safety failure, no new fixtures, only bounded restoration of documented cells. Timeout or exception remains failed. Process exit/crash requires manual reconciliation; no auto replay.
10. After each layout restoration, continue next layout only when fully settled. At end compare position/health/inventory, check dropped entities if native callbacks created any, and discard experimental world copy.

The pure state machine does not fetch terrain, alter game state, launch processes or authorize actions. Adapter still owns journals, cross-layout budget, entity side effects, stop/failure reporting and native bridge lifecycle. Existing game bridge stays unmodified.
