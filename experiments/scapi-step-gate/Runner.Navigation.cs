using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;

sealed partial class Runner
{
    private Point3 navigationOrigin;
    private int continuations, stationaryLooks;
    private float slowestContinuation = float.PositiveInfinity;
    private float biggestContinuousTurn;
    private bool insetAtStart, passedThroughPlant;
    private ComponentBody? pursuingBear;
    private Vector3 bearOrigin;
    private bool recoveredGround;
    private float escapeStartDistance, farthestForward;

    private void RunNaturalStall()
    {
        const int fixtureTerrainKey = 33001;
        var body = p.ComponentBody;
        var stalled = new Vector3(-101.541466f, 67f, 119.49001f);
        var cell = Terrain.ToCell(stalled);
        if (stage == 0)
        {
            terrain.TerrainUpdater.SetUpdateLocation(fixtureTerrainKey, stalled.XZ, 16, 48);
            stage = 1;
            return;
        }
        if (stage == 1)
        {
            for (int x = cell.X - 5; x <= cell.X + 5; x += 5)
            for (int z = cell.Z - 5; z <= cell.Z + 5; z += 5)
                if (!(terrain.Terrain.GetChunkAtCell(x, z)?.State >= TerrainChunkState.InvalidVertices1)) return;
            body.Position = stalled;
            body.Velocity = Vector3.Zero;
            elapsed = 0;
            stage = 2;
            return;
        }
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        if (stage == 2)
        {
            if (elapsed < 0.9) return;
            terrain.TerrainUpdater.RemoveUpdateLocation(fixtureTerrainKey);
            var window = memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true));
            var blockedHeading = -Vector2.UnitY;
            var starts = PlayDryRoutes.WalkableBearings(window, body.Matrix.Forward.XZ);
            bool blocked = PlayDryRoutes.Next(window, blockedHeading, 6f, false) is null;
            Gate.Log(new { kind = "natural_stall_geometry", window.Position, blocked, starts,
                grounded = PlaySurvivalSenses.OnDryGround(body) });
            if (!blocked || starts.Length == 0) throw new InvalidOperationException("夹具需要原方向封住且有其他观察到的起步方向。");
            float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, blockedHeading));
            travel = new(p, actions, bearing, 6f, "survey", false, now,
                () => (JsonSerializer.SerializeToElement(new { }), []),
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), TrackNavigation);
            stage = 3;
        }
        travel!.Update(now);
        if (travel.Status is null) return;
        float moved = (body.Position - travel.Origin).XZ.Length();
        Passed = travel.Reason == "scout_detoured_to_open_ground" && moved >= 1f && PlaySurvivalSenses.OnDryGround(body)
            && stationaryLooks == 0 && p.ComponentHealth.Health >= 0.99f;
        Done = true;
        Gate.Log(new { kind = "natural_stall_result", Passed, travel.Status, travel.Reason, moved, stationaryLooks });
    }

    private void RunRollingNavigation()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        bool cliff = mode == "navigate-drop";
        bool turn = mode == "navigate-turn";
        bool edge = mode == "navigate-edge";
        bool inset = mode == "navigate-inset";
        bool vegetation = mode == "navigate-vegetation";
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            int endZ = cell.Z + (turn ? 9 : 4);
            for (int x = (cell.X - 3) & ~15; x <= cell.X + 22; x += 16)
            for (int z = (cell.Z - 4) & ~15; z <= endZ; z += 16)
                if (!(terrain.Terrain.GetChunkAtCell(x, z)?.State >= TerrainChunkState.InvalidVertices1)) return;
            navigationOrigin = cell;
            for (int x = cell.X - 3; x <= cell.X + 22; x++)
            for (int z = cell.Z - 4; z <= endZ; z++)
            {
                int floor = cliff && x >= cell.X + 8 ? cell.Y - 4 : edge && x < cell.X ? cell.Y - 2
                    : !cliff && !turn && !edge && x >= cell.X + 9 ? cell.Y : cell.Y - 1;
                for (int y = cell.Y - 4; y <= cell.Y + 4; y++)
                    terrain.ChangeCell(x, y, z, y <= floor ? DirtBlock.Index : 0);
            }
            if (turn)
                for (int y = cell.Y; y < cell.Y + 2; y++) terrain.ChangeCell(cell.X + 6, y, cell.Z, GraniteBlock.Index);
            if (vegetation)
                for (int x = 2; x <= 15; x++) terrain.ChangeCell(cell.X + x, cell.Y + (x >= 9 ? 1 : 0), cell.Z,
                    x % 2 == 0 ? TallGrassBlock.Index : RedFlowerBlock.Index);
            body.Position = new(cell.X + (edge ? 0.231f : inset ? 0.474f : 0.5f), cell.Y, cell.Z + (inset ? 0.451f : 0.5f));
            body.Velocity = Vector3.Zero;
            body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
            elapsed = 0;
            stage = 1;
            return;
        }
        if (elapsed < 1) return;
        if (inset)
        {
            RunShortAlignment(body);
            return;
        }
        if (edge && travel is null && (!PlaySurvivalSenses.OnDryGround(body) || body.BoundingBox.Min.X >= navigationOrigin.X))
            throw new InvalidOperationException("夹具需要已站稳且向低一格侧稍微悬出。");
        travel ??= new(p, actions, turn ? 20 : 0, 18, "survey", false, now,
            () => (JsonSerializer.SerializeToElement(new { }), []),
            () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), TrackNavigation);
        jumped |= p.ComponentLocomotion.LastJumpOrder > 0;
        farthestForward = Math.Max(farthestForward, body.Position.X - travel.Origin.X);
        if (vegetation)
        {
            var feet = Terrain.ToCell(body.Position + new Vector3(0, 0.05f, 0));
            int contents = terrain.Terrain.GetCellContents(feet.X, feet.Y, feet.Z);
            passedThroughPlant |= contents == TallGrassBlock.Index || contents == RedFlowerBlock.Index;
        }
        if (cliff && (body.BoundingBox.Max.X > navigationOrigin.X + 8.03f || body.Position.Y < navigationOrigin.Y - 0.05f))
            throw new InvalidOperationException("移动进入了无有效落脚证据的断崖。");
        travel.Update(now);
        if (travel.Status is null) return;

        float moved = body.Position.X - travel.Origin.X;
        bool continuousMovement = continuations >= (cliff ? 1 : 3) && slowestContinuation > 0.7f && stationaryLooks == 0;
        var destination = travel.Origin.XZ + 18 * PlaySurvivalTravel.Rotate(Vector2.UnitX, turn ? 20 : 0);
        float error = (body.Position.XZ - destination).Length();
        bool completed = cliff ? (travel.Reason is "no_observed_dry_route" or "scout_detoured_to_open_ground")
                && farthestForward > 6f && !jumped
            : travel.Status == "succeeded" && error <= 0.45f && (turn ? biggestContinuousTurn > 35f : edge ? insetAtStart : jumped);
        Passed = continuousMovement && completed && PlaySurvivalSenses.OnDryGround(body)
            && (cliff || turn || edge || body.Position.Y >= navigationOrigin.Y + 0.95f) && (!vegetation || passedThroughPlant);
        Done = true;
        Gate.Log(new { kind = "rolling_navigation_result", Passed, cliff, turn, edge, insetAtStart, travel.Status, travel.Reason, travel.Legs,
            continuations, slowestContinuation = float.IsFinite(slowestContinuation) ? (float?)slowestContinuation : null,
            biggestContinuousTurn, stationaryLooks, jumped, moved, error, vegetation, passedThroughPlant, end = body.Position.ToString() });
    }

    private void RunShortAlignment(ComponentBody body)
    {
        const float range = 0.04f;
        var center = new Vector3(navigationOrigin.X + 0.5f, navigationOrigin.Y, navigationOrigin.Z + 0.5f);
        float distance = Vector3.Distance(body.Position, center);
        if (run is null)
        {
            if (!PlaySurvivalSenses.OnDryGround(body) || distance is <= 0.05f or >= 0.1f)
                throw new InvalidOperationException("夹具需要站在离支撑格中心 0.05 到 0.10 格的位置。");
            run = actions.Start(new MoveTo(center, range, 3f));
            Gate.Log(new { kind = "short_alignment_started", distance, range });
            return;
        }
        if (run.Result.Status == AtomicActionStatus.Running) return;

        Passed = run.Result.Status == AtomicActionStatus.Succeeded && distance <= range && PlaySurvivalSenses.OnDryGround(body);
        Done = true;
        Gate.Log(new { kind = "short_alignment_result", Passed, distance, range,
            status = run.Result.Status.ToString(), failure = run.Result.Failure.ToString() });
    }

    private void RunPredatorCliff()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        bool verifyClearance = mode == "predator-clearance";
        bool extendedOverhang = mode == "predator-edge-tip";
        bool overhang = mode == "predator-edge-return" || extendedOverhang;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            for (int x = (cell.X - 17) & ~15; x <= cell.X + 7; x += 16)
            for (int z = (cell.Z - 17) & ~15; z <= cell.Z + 17; z += 16)
                if (!(terrain.Terrain.GetChunkAtCell(x, z)?.State >= TerrainChunkState.InvalidVertices1)) return;
            navigationOrigin = cell;
            for (int x = cell.X - 17; x <= cell.X + 7; x++)
            for (int z = cell.Z - 17; z <= cell.Z + 17; z++)
            {
                int floor = x < cell.X + 2 ? cell.Y - 1 : cell.Y - 8;
                for (int y = cell.Y - 8; y <= cell.Y + 4; y++)
                    terrain.ChangeCell(x, y, z, y <= floor ? DirtBlock.Index : 0);
            }
            body.Position = extendedOverhang ? new(cell.X + 2.197f, cell.Y, cell.Z + 0.113f)
                : overhang ? new(cell.X + 2.026f, cell.Y, cell.Z + 0.233f)
                : new(cell.X + 0.474f, cell.Y, cell.Z + 0.451f);
            body.Velocity = Vector3.Zero;
            body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
            var bear = DatabaseManager.CreateEntity(p.Project, overhang ? "Wolf_Coyote" : "Bear_Brown", true);
            pursuingBear = bear.FindComponent<ComponentBody>(true);
            bearOrigin = body.Position - (verifyClearance ? 7f : 15f) * Vector3.UnitX;
            pursuingBear.Position = bearOrigin;
            if (verifyClearance)
            {
                var locomotion = bear.FindComponent<ComponentLocomotion>(true);
                locomotion.WalkSpeed = 0f;
                locomotion.JumpSpeed = 0f;
            }
            p.Project.AddEntity(bear);
            elapsed = 0;
            stage = 1;
            return;
        }
        if (elapsed < 0.75) return;
        float cliff = navigationOrigin.X + 2;
        recoveredGround |= body.BoundingBox.Max.X <= cliff + 0.03f;
        float allowedEdge = overhang && !recoveredGround ? cliff + (extendedOverhang ? 0.46f : 0.30f) : cliff + 0.03f;
        if (body.BoundingBox.Max.X > allowedEdge || body.Position.Y < navigationOrigin.Y - 0.05f)
            throw new InvalidOperationException($"追击时跨进了断崖。位置 {body.Position}，边界 {allowedEdge}，脚底高度 {navigationOrigin.Y}。");
        if (travel is null)
        {
            var initial = PlaySurvivalSenses.Threats(p, actions.Queries).FirstOrDefault(threat => threat.Predator && !threat.Aquatic);
            if (initial is null || !PlaySurvivalSenses.OnDryGround(body)) return;
            var window = memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true));
            var towardLedge = PlayDryRoutes.Next(window, Vector2.UnitX, 12f, false);
            var threat = new Vector2(initial.X, initial.Z);
            if (overhang && (body.Position.X <= cliff || towardLedge is not null
                || PlayDryRoutes.Escape(window, threat, 12f, true)?.Action is not MoveTo))
                throw new InvalidOperationException("夹具需要脚底搭在崖边且首段退路收向支撑格。");
            if (!overhang && (towardLedge is null || towardLedge.End.X < navigationOrigin.X + 1f))
                throw new InvalidOperationException("夹具需要复现正向退路将停在崖边。");
            escapeStartDistance = initial.Distance;
            var lastSeen = initial;
            pursuingBear!.Entity.FindComponent<ComponentChaseBehavior>(true).Attack(p, 24f, 20f, true);
            travel = new(p, actions, 0, verifyClearance ? 4f : 9f, "survey", true, now,
                () => (JsonSerializer.SerializeToElement(new { }), []),
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), TrackNavigation,
                escapeFrom: () =>
                {
                    lastSeen = PlaySurvivalSenses.Threats(p, actions.Queries).FirstOrDefault(threat => threat.Predator && !threat.Aquatic) ?? lastSeen;
                    return new(lastSeen.X, lastSeen.Z);
                }, escapeClearance: verifyClearance ? 10f : 0f);
            Gate.Log(new { kind = "predator_cliff_started", overhang, extendedOverhang, distance = escapeStartDistance,
                origin = body.Position.ToString(), priorStopX = towardLedge?.End.X, cliffX = cliff });
        }
        travel.Update(now);
        if (travel.Status is null) return;

        float sideDistance = MathF.Abs(body.Position.Z - travel.Origin.Z);
        float pursuitDistance = (pursuingBear!.Position - bearOrigin).XZ.Length();
        float clearance = (body.Position - pursuingBear.Position).XZ.Length();
        bool achieved = verifyClearance ? clearance >= 10f && (body.Position - travel.Origin).XZ.Length() > 4.5f
            : sideDistance > 6f && pursuitDistance > 2f;
        Passed = travel.Status == "succeeded" && recoveredGround && achieved && stationaryLooks == 0
            && continuations > 0 && p.ComponentHealth.Health >= 0.99f && PlaySurvivalSenses.OnDryGround(body);
        Done = true;
        Gate.Log(new { kind = "predator_cliff_result", Passed, overhang, extendedOverhang, recoveredGround, travel.Status, travel.Reason, escapeStartDistance,
            sideDistance, pursuitDistance, clearance, verifyClearance, continuations, stationaryLooks, health = p.ComponentHealth.Health });
    }

    private void TrackNavigation(object value)
    {
        Gate.Log(value);
        var entry = JsonSerializer.SerializeToElement(value);
        switch (entry.GetProperty("kind").GetString())
        {
            case "play_scout_continue":
                continuations++;
                slowestContinuation = MathF.Min(slowestContinuation, entry.GetProperty("speed").GetSingle());
                biggestContinuousTurn = MathF.Max(biggestContinuousTurn, MathF.Abs(entry.GetProperty("turn").GetSingle()));
                break;
            case "play_scout_step" when entry.GetProperty("action").GetString() == "FaceTowards":
                stationaryLooks++;
                break;
            case "play_scout_step" when entry.GetProperty("action").GetString() == "MoveTo" && entry.GetProperty("legs").GetInt32() == 1:
                insetAtStart = true;
                break;
        }
    }
}
