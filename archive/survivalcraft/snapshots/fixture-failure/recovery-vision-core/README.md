# Reconstructed vision math, 2026-11-08

This is newly written pure C# based on research notes, NOT recovered historical VisionBridge code and NOT a game-tested implementation.

`ContainsLocal` uses camera coordinates (X right, Y up, Z forward), radius 8, rectangular horizontal 90° and vertical 70° FOV. Boundaries are inclusive, no FOV epsilon; zero direction, behind-camera and nonfinite inputs fail closed. `ContainsWorld` accepts a finite orthonormal eye basis. Its 1e-5 tolerance only validates the supplied basis. Derive basis from actual eye rotation; confirm the game engine conventions independently.

This only answers geometric membership. It does NOT read terrain, test loaded chunks, perform line-of-sight/occlusion, filter memory, or authorize motion. Integrator must do those separately on the game thread. Center-point visibility is not renderer-equivalent, and non-air occlusion may hide grass/glass.

`ValidYaw(float)` compares to `0.1f` without promotion to `double 0.1` and without epsilon. It rejects the next representable float. A valid magnitude is NOT execution permission: require fresh same-tick state, Ready, standing, awake, health/air/food limits, leases, budgets and cancel/stop acknowledgments outside this math library.

Run from this directory:
`../dependencies/dotnet/dotnet run --project Tests.csproj`
No game or model launched. No modification of v10 or original dependencies.

## Conservative voxel visibility
`VoxelVisibility.HasClearLine(Vec eye, int targetX, int targetY, int targetZ, Func<int,int,int,bool> isClear)` traverses to target cell center within radius 8. Callback must return true only for loaded Air, false for unknown/unloaded/non-Air. Callback exceptions fail closed. Target cell itself is not queried, intermediate and source cells are. Supercover DDA checks all side/edge neighbors at boundary ties, deliberately conservative; near-tie tolerance only adds obstruction checks. No material transparency assumptions. 64-step cap fails closed.
