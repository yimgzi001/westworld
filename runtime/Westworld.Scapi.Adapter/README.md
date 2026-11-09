# SCAPI simulation-interest adapter boundary

This is the next shared architecture seam after ActorRegistry, not a full actor host. It compiles against the real Linux SCAPI game project. It does not create players, views or worlds, and does not bypass GLFW/environment restrictions.

## Implemented
- Core InterestCoordinator publishes only active, native-reconciled actor centers on its owning game thread. It never consumes RenderViews. Explicit radii require16<=FullReady<=Content<=256.
- ScapiInterestBackend connects those requests to real TerrainUpdater.SetUpdateLocation/RemoveUpdateLocation. Each actor gets a negative location key, checked against active and pending native keys; standard camera/player keys are nonnegative. Keys are not reused within an adapter lifetime.
- It stages updates and installs/publishes a simulation-center provider only after a complete successful Sync/Release. Construction does not install an empty provider. The provider reads the last published snapshot, not partially edited leases. IsPublished exposes this boundary. It registers that provider on SubsystemSpawn. A minimal upstream extension lets SpawnChunks, DespawnChunks and UpdateLastVisitedTime consume actor centers rather than camera positions. Null provider preserves original behavior; a non-null empty provider means zero active centers, not camera fallback.
- One snapshot is used through a DespawnChunks pass. A caller receives a copy of provider centers, and provider exceptions propagate rather than masquerading as an empty world.
- Dispose queues removal of owned terrain keys and detaches only its own provider. It does not remove camera keys. Existing providers and unloaded/cross-world subsystems are rejected.

## Host integration responsibilities
1. Construct only for a loaded, matching SCAPI Project, on the game thread. Reconcile all required native bodies from the same world checkpoint before releasing the host recovery barrier.
2. Register every simulated body, including a participating human body; an observer camera alone is presentation, not an actor activity center.
3. Feed actual body positions into ActorRegistry and Sync before terrain/spawn update consumption. Model workers never call these APIs or supply authoritative positions.
4. Radii32/64 in tests are experimental configuration, not measured defaults. FullReady maps to native visibility processing so current adapters requiring Valid can eventually work. This may generate meshes even without rendering; it is not zero-GL yet. ContentDistance alone only advances some chunks to InvalidVertices1.
5. SetUpdateLocation is deferred and has native movement hysteresis. Upsert acceptance is not a loaded chunk or successful simulation. The future host must inspect actual chunk states before collision-sensitive actions and record loading stalls.
6. Backend operations are not a world-wide transaction. If Sync fails after partial submissions, stop the host's advancement, retain state and reconcile/retry; do not turn exceptions into empty-center success. Release retries retain failed removals in the coordinator. Attempted upserts are tracked before calling the backend/native setter, so partial-write exceptions can still be released; Remove must be idempotent for unknown actors. Other mods must not overwrite leased keys; namespace ownership is a host contract, not a network security boundary.
7. Keep the host paused while installing/publishing or disposing the provider. An empty provider can permit normal despawn behavior. Shutdown must drain native pending removals or dispose the world; it must not resume with a half-installed actor set.

## Verification on dot cloud
```sh
dotnet run --project tests/Westworld.Runtime.Core.Tests/Tests.csproj -c Release
dotnet build tests/Westworld.Scapi.Adapter.Tests/Tests.csproj -c Release -m:1 -nodeReuse:false
dotnet tests/Westworld.Scapi.Adapter.Tests/bin/Release/net10.0/Tests.dll
```
Core checks cover five centers/zero views, camera isolation, actor movement/activity/removal, release failure/retry, radius bounds, thread ownership and restore gates. Native hook tests instantiate the real compiled SubsystemSpawn without loading a Project and check its provider semantics; they are not terrain/animal/world tests.

Still unverified: live chunk allocation, distant animal spawning/despawning, real body physics/survival, five full actors, native save/reload and0/1human viewport behavior. Current cloud native startup remains blocked by display/socket capabilities. No MaxPlayers constant, hidden AI view or simplified substitute body was introduced.

## Local attack readiness

`LocalAttackReadiness` is a pure bounded timing hint for successive already-requested attacks, not a hunting policy or attack authorization. Use the same native game clock, keep the original wait start and deadline, and wait at most4 game seconds with a separate host wall-clock limit. After Ready, re-observe the same target and invoke the existing native tool; native hooks may still reject. Invalid clocks, cancellation and expiry discard pending work. The timing probe is separately documented as compiled but not native-run.
