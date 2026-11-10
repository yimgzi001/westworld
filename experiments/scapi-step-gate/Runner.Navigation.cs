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
    private Vector3[]? coldFixtureGround;

    private void RunNaturalStall()
    {
        const int fixtureTerrainKey = 33001;
        var body = p.ComponentBody;
        bool cotton = mode == "navigate-natural-cotton";
        bool pumpkin = mode == "predator-pumpkin-corner";
        bool lengthBoundary = mode == "predator-length-boundary";
        var stalled = lengthBoundary ? new Vector3(7.484409f, 67f, 152.49844f) : pumpkin ? new Vector3(-131.16777f, 66f, 257.81088f)
            : cotton ? new Vector3(-26.385578f, 65f, 156.83945f) : new Vector3(-101.541466f, 67f, 119.49001f);
        // 使用死亡场第一次撤离时记录的威胁位置。夹具只复现路线计算和原生移动。
        var threat = lengthBoundary ? new Vector2(3.843923f, 148.61324f) : new Vector2(-149.43459f, 247.28473f);
        var cell = Terrain.ToCell(stalled);
        if (stage == 0)
        {
            terrain.TerrainUpdater.SetUpdateLocation(fixtureTerrainKey, stalled.XZ, 16, 48);
            stage = 1;
            return;
        }
        if (stage == 1)
        {
            if (!DismountedForFixture(body)) return;
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
            if (lengthBoundary)
            {
                var legacyPoints = Enumerable.Range(1, 4).Select(index =>
                    new Vector3((float)(window.Position.X + index), (float)window.Position.Y, (float)window.Position.Z)).ToArray();
                var legacy = actions.Start(new MoveAlongObservedRoute(legacyPoints, LevelGaze: true));
                var safe = PlayDryRoutes.Escape(window, threat, 3.75f, true);
                float originalLength = (legacyPoints[^1] - body.Position).XZ.Length();
                float? correctedLength = safe is not null ? (safe.End - body.Position).XZ.Length() : null;
                Gate.Log(new { kind = "predator_length_geometry", window.Position, originalLength, correctedLength,
                    legacyFailure = legacy.Result.Failure.ToString(), chosen = safe?.End.ToString() });
                if (originalLength <= 4f || legacy.Result.Failure != AtomicActionFailure.InvalidArgument
                    || correctedLength is not (>= 3.5f and <= 4f) || !PlaySurvivalSenses.OnDryGround(body))
                    throw new InvalidOperationException("未复现老虎撤离场的四格长度边界。");
                float bearingToSafety = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, body.Position.XZ - threat));
                travel = new(p, actions, bearingToSafety, 3.75f, "survey", true, now,
                    () => (JsonSerializer.SerializeToElement(new { }), []),
                    () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), TrackNavigation,
                    escapeFrom: () => threat);
                stage = 3;
                return;
            }
            if (pumpkin)
            {
                bool ordinaryBlocked = PlayDryRoutes.Escape(window, threat, 12f, true) is null;
                bool seen = window.Cells.Any(evidence => evidence.X == -131 && evidence.Y == 66 && evidence.Z == 258
                    && Terrain.ExtractContents(evidence.Value) == PumpkinBlock.Index);
                Gate.Log(new { kind = "pumpkin_corner_geometry", window.Position, ordinaryBlocked, seen,
                    bounds = body.BoundingBox.ToString(), grounded = PlaySurvivalSenses.OnDryGround(body) });
                if (!ordinaryBlocked || !seen || !PlaySurvivalSenses.OnDryGround(body))
                    throw new InvalidOperationException("同种子南瓜卡点与自然场记录不符。");
                float away = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, body.Position.XZ - threat));
                travel = new(p, actions, away, 12f, "survey", true, now,
                    () => (JsonSerializer.SerializeToElement(new { }), []),
                    () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), TrackNavigation,
                    escapeFrom: () => threat, escapeClearance: 10f);
                stage = 3;
                return;
            }
            var blockedHeading = -Vector2.UnitY;
            var starts = PlayDryRoutes.WalkableBearings(window, body.Matrix.Forward.XZ);
            var first = PlayDryRoutes.Next(window, blockedHeading, 6f, false);
            bool cottonInProof = first?.Evidence.Any(evidence => Terrain.ExtractContents(evidence.Value) == CottonBlock.Index) == true;
            bool blocked = first is null;
            Gate.Log(new { kind = "natural_stall_geometry", window.Position, cotton, blocked, starts, cottonInProof,
                grounded = PlaySurvivalSenses.OnDryGround(body) });
            if (cotton ? !cottonInProof : !blocked || starts.Length == 0)
                throw new InvalidOperationException("同种子卡点的已观察地形与夹具不符。");
            float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, blockedHeading));
            travel = new(p, actions, bearing, cotton ? 4f : 6f, "survey", false, now,
                () => (JsonSerializer.SerializeToElement(new { }), []),
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), TrackNavigation);
            stage = 3;
        }
        jumped |= p.ComponentLocomotion.LastJumpOrder > 0;
        travel!.Update(now);
        if (travel.Status is null) return;
        float moved = (body.Position - travel.Origin).XZ.Length();
        bool reached = lengthBoundary ? travel.Reason == "escaped_along_supported_ground" && moved >= 3.5f
            : pumpkin ? travel.Reason == "escaped_along_supported_ground" && moved >= 11.5f && travel.Legs >= 2
            : cotton ? travel.Status == "succeeded" && jumped && moved >= 2.5f
            : travel.Reason == "scout_detoured_to_open_ground" && moved >= 1f;
        Passed = reached && PlaySurvivalSenses.OnDryGround(body)
            && stationaryLooks == 0 && p.ComponentHealth.Health >= 0.99f;
        Done = true;
        Gate.Log(new { kind = "natural_stall_result", Passed, cotton, pumpkin, lengthBoundary, travel.Status, travel.Reason, moved, jumped, stationaryLooks });
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
        bool cold = mode == "navigate-cold";
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
                    : !cliff && !turn && !edge && !cold && x >= cell.X + 9 ? cell.Y : cell.Y - 1;
                for (int y = cell.Y - 4; y <= cell.Y + 4; y++)
                    terrain.ChangeCell(x, y, z, y <= floor ? DirtBlock.Index : 0);
            }
            if (turn)
                for (int y = cell.Y; y < cell.Y + 2; y++) terrain.ChangeCell(cell.X + 6, y, cell.Z, GraniteBlock.Index);
            if (vegetation)
                for (int x = 2; x <= 15; x++) terrain.ChangeCell(cell.X + x, cell.Y + (x >= 9 ? 1 : 0), cell.Z,
                    x % 2 == 0 ? TallGrassBlock.Index : RedFlowerBlock.Index);
            if (cold)
            {
                for (int x = 5; x <= 12; x++) terrain.ChangeCell(cell.X + x, cell.Y, cell.Z + 1, SnowBlock.Index);
                p.ComponentClothing.Insulation = 0.6f;
            }
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
        if (cold && coldFixtureGround is null)
        {
            coldFixtureGround = PlaySurvivalCold.Observe(actions.Queries.ReadPanorama(24f));
            if (PlaySurvivalCold.CanEnterSnow(p) || coldFixtureGround.Length < 3
                || PlaySurvivalCold.AllowsRoute(body.Position, body.Position + 16f * Vector3.UnitX, coldFixtureGround)
                || !PlaySurvivalCold.AllowsRoute(body.Position, body.Position - 3f * Vector3.UnitZ, coldFixtureGround))
                throw new InvalidOperationException("原生雪地没有被看到，或防寒路线条件不符。");
            var nearSnow = new Vector3(navigationOrigin.X + 6.5f, navigationOrigin.Y, navigationOrigin.Z + 0.5f);
            if (!PlaySurvivalCold.NearSnow(nearSnow, coldFixtureGround)
                || !PlaySurvivalCold.AllowsRoute(nearSnow, nearSnow - 3f * Vector3.UnitZ, coldFixtureGround))
                throw new InvalidOperationException("缺少从雪边退回的路径。");
        }
        travel ??= new(p, actions, turn ? 20 : 0, 18, cold ? "work" : "survey", false, now,
            () => (JsonSerializer.SerializeToElement(new { }), []),
            () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), TrackNavigation,
            permitLandLeg: cold ? (end, _) => PlaySurvivalCold.AllowsRoute(body.Position, end, coldFixtureGround!) : null);
        if (cold && PlaySurvivalCold.NearSnow(body.Position, coldFixtureGround!))
            throw new InvalidOperationException("保温不足时踏进了已知雪地边缘。");
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
        if (cold) Passed = travel.Status == "succeeded" && error <= 0.45f && stationaryLooks == 0
            && PlaySurvivalSenses.OnDryGround(body) && p.ComponentHealth.Health >= 0.99f;
        Done = true;
        Gate.Log(new { kind = "rolling_navigation_result", Passed, cliff, turn, edge, insetAtStart, travel.Status, travel.Reason, travel.Legs,
            continuations, slowestContinuation = float.IsFinite(slowestContinuation) ? (float?)slowestContinuation : null,
            biggestContinuousTurn, stationaryLooks, jumped, moved, error, vegetation, passedThroughPlant, cold,
            frozen = coldFixtureGround?.Length, end = body.Position.ToString() });
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
        bool shelterClearance = mode == "predator-shelter-clearance";
        bool verifyClearance = mode == "predator-clearance" || shelterClearance;
        float requiredClearance = shelterClearance ? 18f : verifyClearance ? 10f : 0f;
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
            if (!shelterClearance) pursuingBear!.Entity.FindComponent<ComponentChaseBehavior>(true).Attack(p, 24f, 20f, true);
            travel = new(p, actions, 0, shelterClearance ? 8f : verifyClearance ? 4f : 9f, "survey", !shelterClearance, now,
                () => (JsonSerializer.SerializeToElement(new { }), []),
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), TrackNavigation,
                escapeFrom: () =>
                {
                    if (!shelterClearance)
                        lastSeen = PlaySurvivalSenses.Threats(p, actions.Queries).FirstOrDefault(threat => threat.Predator && !threat.Aquatic) ?? lastSeen;
                    return new(lastSeen.X, lastSeen.Z);
                }, permitLandLeg: shelterClearance ? (end, _) => end.Z >= navigationOrigin.Z : null, escapeClearance: requiredClearance);
            Gate.Log(new { kind = "predator_cliff_started", overhang, extendedOverhang, distance = escapeStartDistance,
                origin = body.Position.ToString(), priorStopX = towardLedge?.End.X, cliffX = cliff });
        }
        travel.Update(now);
        if (travel.Status is null) return;

        float sideDistance = MathF.Abs(body.Position.Z - travel.Origin.Z);
        float pursuitDistance = (pursuingBear!.Position - bearOrigin).XZ.Length();
        float clearance = (body.Position - pursuingBear.Position).XZ.Length();
        bool achieved = verifyClearance ? clearance >= requiredClearance
            && (body.Position - travel.Origin).XZ.Length() > (shelterClearance ? 8f : 4.5f)
            : sideDistance > 6f && pursuitDistance > 2f;
        Passed = travel.Status == "succeeded" && recoveredGround && achieved && stationaryLooks == 0
            && continuations > 0 && p.ComponentHealth.Health >= 0.99f && PlaySurvivalSenses.OnDryGround(body);
        Done = true;
        Gate.Log(new { kind = "predator_cliff_result", Passed, overhang, extendedOverhang, recoveredGround, travel.Status, travel.Reason, escapeStartDistance,
            sideDistance, pursuitDistance, clearance, verifyClearance, continuations, stationaryLooks, health = p.ComponentHealth.Health });
    }

    private void RunPredatorCoastTurn()
    {
        const int terrainKey = 33002;
        var body = p.ComponentBody;
        var shore = new Vector3(-100.5172f, 65f, 195.5003f);
        var cell = Terrain.ToCell(shore);
        if (stage == 0)
        {
            terrain.TerrainUpdater.SetUpdateLocation(terrainKey, shore.XZ, 24, 48);
            stage = 1;
            return;
        }
        if (stage == 1)
        {
            if (!DismountedForFixture(body)) return;
            for (int x = cell.X - 6; x <= cell.X + 24; x += 6)
            for (int z = cell.Z - 8; z <= cell.Z + 8; z += 8)
                if (!(terrain.Terrain.GetChunkAtCell(x, z)?.State >= TerrainChunkState.InvalidVertices1)) return;
            body.Position = shore;
            body.Velocity = Vector3.Zero;
            var wolf = DatabaseManager.CreateEntity(p.Project, "Wolf_Gray", true);
            pursuingBear = wolf.FindComponent<ComponentBody>(true);
            pursuingBear.Position = new(-80.31f, 66f, 194.095f);
            var locomotion = wolf.FindComponent<ComponentLocomotion>(true);
            locomotion.WalkSpeed = 0f;
            locomotion.JumpSpeed = 0f;
            p.Project.AddEntity(wolf);
            elapsed = 0;
            stage = 2;
            return;
        }
        if (elapsed < 1) return;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        var threat = pursuingBear!.Position.XZ;
        if (travel is null)
        {
            var seen = PlaySurvivalSenses.Threats(p, actions.Queries).FirstOrDefault(animal => animal.EntityId == pursuingBear.Entity.Id);
            var window = memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true));
            bool direct = PlayDryRoutes.Escape(window, threat, 12, false) is not null;
            var turn = PlayDryRoutes.EscapeTurn(window, threat);
            bool rejectsClose = PlayDryRoutes.EscapeTurn(window, body.Position.XZ + 12f * Vector2.UnitX) is null;
            Gate.Log(new { kind = "predator_coast_geometry", direct, seen = seen?.Distance, rejectsClose, window.Position,
                corner = turn?.Approach.End.ToString(), exit = turn?.Exit.ToString() });
            if (direct || seen is null || !rejectsClose || turn is null)
                throw new InvalidOperationException("同种子岸边应有可见远处灰狼和经过转弯的干地出口。");
            escapeStartDistance = seen.Distance;
            travel = new(p, actions, 0, 6f, "survey", true, now,
                () => (JsonSerializer.SerializeToElement(new { }), []),
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), TrackNavigation,
                escapeFrom: () => PlaySurvivalSenses.Threats(p, actions.Queries)
                    .FirstOrDefault(animal => animal.EntityId == pursuingBear.Entity.Id) is { } visible
                    ? new(visible.X, visible.Z) : threat,
                escapeClearance: 10f, allowEscapeTurn: true);
        }
        jumped |= p.ComponentLocomotion.LastJumpOrder > 0;
        if (body.ImmersionDepth > 0.08f || body.Position.Y < shore.Y - 0.05f)
            throw new InvalidOperationException("撤离路线进入了水中或落差。");
        travel.Update(now);
        if (travel.Status is null) return;
        terrain.TerrainUpdater.RemoveUpdateLocation(terrainKey);
        float moved = (body.Position - travel.Origin).XZ.Length();
        float clearance = (body.Position.XZ - threat).Length();
        Passed = travel.UsedEscapeTurn && travel.Status == "succeeded" && jumped && body.Position.Y >= shore.Y + 0.95f
            && moved >= 5f && clearance >= 14f && stationaryLooks == 0 && p.ComponentHealth.Health >= 0.99f;
        Done = true;
        Gate.Log(new { kind = "predator_coast_turn_result", Passed, escapeStartDistance, travel.Status, travel.Reason,
            moved, clearance, jumped, stationaryLooks, end = body.Position.ToString(), health = p.ComponentHealth.Health });
    }

    private bool DismountedForFixture(ComponentBody body)
    {
        if (body.ParentBody is null) return true;
        if (elapsed < 1) return false;
        run ??= actions.Start(new Dismount());
        if (run.Result.Status is AtomicActionStatus.Failed or AtomicActionStatus.Cancelled)
            throw new InvalidOperationException("夹具角色无法离开开局木船。");
        return false;
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
