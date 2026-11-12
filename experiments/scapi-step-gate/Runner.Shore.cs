using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;

sealed partial class Runner
{
    private int shoreActions, shoreFailures, shoreJumps;
    private bool shoreEnteredWater;
    private Vector3 shoreFixtureGoal;
    private Pickable? shoreNativeMeat;
    private double shoreDriftUntil;
    private bool shoreDrifted, shoreHidden;
    private bool shoreDriftStopped;
    private int shoreDriftUpdates;
    private int shoreContinuations;
    private bool shoreThreatAdded;

    private void RunShoreFixture()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        bool hunt = mode.StartsWith("shore-hunt", StringComparison.Ordinal);
        bool high = mode == "shore-high-bank";
        bool blocked = mode == "shore-blocked-bank";
        bool noExit = mode == "shore-no-exit";
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            for (int x = cell.X - 6; x <= cell.X + 13; x++)
            for (int z = cell.Z - 6; z <= cell.Z + 6; z++)
            {
                bool pool = hunt ? x >= cell.X + 2 && x <= cell.X + 11 : x <= cell.X + 1;
                int top = cell.Y - 1 + (!pool && high ? 1 : 0);
                for (int y = cell.Y - 4; y <= cell.Y + 4; y++)
                    terrain.ChangeCell(x, y, z, y < cell.Y - 3 ? DirtBlock.Index
                        : y <= top ? pool ? WaterBlock.Index : DirtBlock.Index : 0);
                if (pool) terrain.ChangeCell(x, cell.Y - 1, z,
                    Terrain.MakeBlockValue(WaterBlock.Index, 0, FluidBlock.SetIsTop(0, true)));
            }
            if (blocked)
                for (int x = cell.X + 2; x <= cell.X + 3; x++)
                for (int z = cell.Z - 2; z <= cell.Z + 1; z++)
                for (int y = cell.Y; y <= cell.Y + 2; y++) terrain.ChangeCell(x, y, z, GraniteBlock.Index);
            if (noExit)
                for (int x = cell.X - 6; x <= cell.X + 2; x++)
                for (int z = cell.Z - 6; z <= cell.Z + 6; z++)
                if (x == cell.X - 6 || x == cell.X + 2 || Math.Abs(z - cell.Z) == 6)
                    for (int y = cell.Y - 1; y <= cell.Y + 2; y++) terrain.ChangeCell(x, y, z, GraniteBlock.Index);
            body.Position = new(cell.X + 0.5f, cell.Y - (hunt ? 0 : 0.8f), cell.Z + 0.5f);
            body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
            body.Velocity = Vector3.Zero;
            productionOrigin = body.Position;
            shoreFixtureGoal = new(cell.X + 3.5f, cell.Y + (high ? 1 : 0), cell.Z + (blocked ? 3.5f : 0.5f));
            forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            if (hunt)
            {
                p.Project.FindSubsystem<SubsystemPickables>(true).PickableAdded += item =>
                {
                    if (Terrain.ExtractContents(item.Value) == RawBirdBlock.Index) shoreNativeMeat = item;
                };
                p.ComponentMiner.Inventory.AddSlotItems(0, WoodenSpearBlock.Index, 1);
                var duck = DatabaseManager.CreateEntity(p.Project, "Duck", true);
                var bird = duck.FindComponent<ComponentBody>(true);
                bird.Position = new(cell.X + 8.5f, cell.Y - 0.3f, cell.Z + 0.5f);
                var locomotion = duck.FindComponent<ComponentLocomotion>(true);
                locomotion.WalkSpeed = locomotion.FlySpeed = locomotion.JumpSpeed = 0;
                p.Project.AddEntity(duck);
            }
            stage = 1;
            elapsed = 0;
            return;
        }
        if (elapsed < 1) return;
        shoreEnteredWater |= body.ImmersionDepth > 0.12f;
        shoreJumps += p.ComponentLocomotion.LastJumpOrder > 0 ? 1 : 0;
        if (mode == "shore-hunt-threat" && shoreEnteredWater && !shoreThreatAdded)
        {
            var wolf = DatabaseManager.CreateEntity(p.Project, "Wolf_Gray", true);
            wolf.FindComponent<ComponentBody>(true).Position = productionOrigin + 12 * Vector3.UnitX;
            var locomotion = wolf.FindComponent<ComponentLocomotion>(true);
            locomotion.WalkSpeed = locomotion.JumpSpeed = 0;
            p.Project.AddEntity(wolf);
            shoreThreatAdded = true;
        }
        if (mode == "shore-hunt-drift" && shoreNativeMeat is { ToRemove: false } meat && shoreDriftUntil > now)
        {
            // 测试给原生猎物掉落施加横向水漂速度，不新增物品、不修改入包结果。
            meat.Velocity = new(0, meat.Velocity.Y, 1.6f);
            shoreDrifted = true;
        }
        else if (mode == "shore-hunt-drift" && shoreNativeMeat is { ToRemove: false } drifting
            && shoreDrifted && !shoreDriftStopped)
        {
            drifting.Velocity = new(0, drifting.Velocity.Y, 0);
            shoreDriftStopped = true;
        }
        if (mode == "shore-hunt-hidden" && shoreNativeMeat is not null && !shoreHidden)
        {
            int wallX = Terrain.ToCell(productionOrigin.X) + 3;
            for (int z = Terrain.ToCell(productionOrigin.Z) - 6; z <= Terrain.ToCell(productionOrigin.Z) + 6; z++)
            for (int y = Terrain.ToCell(productionOrigin.Y) - 1; y <= Terrain.ToCell(productionOrigin.Y) + 5; y++)
                terrain.ChangeCell(wallX, y, z, GraniteBlock.Index);
            shoreHidden = true;
        }
        if (hunt)
        {
            production ??= new(p, actions, forageTools!, PlaySurvivalProduction.Hunt, now,
                TrackShore, productionStations, () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)));
            production.Update(now);
            if (production.Status is null && elapsed < 42) return;
            Passed = production.Status == "succeeded" && NativeAcquisitionAudit.InventoryCount(p.ComponentMiner.Inventory, RawBirdBlock.Index) >= 1
                && p.ComponentHealth.Health >= 0.99f && PlaySurvivalSenses.OnStableDryGround(body);
            if (mode == "shore-hunt-drift") Passed &= shoreDrifted && shoreDriftUpdates > 0 && shoreEnteredWater
                && shoreContinuations > 0;
            if (mode == "shore-hunt-hidden") Passed = shoreHidden && production.Status == "failed"
                && NativeAcquisitionAudit.InventoryCount(p.ComponentMiner.Inventory, RawBirdBlock.Index) == 0
                && !shoreEnteredWater && shoreActions == 0;
            if (mode == "shore-hunt-threat") Passed = shoreThreatAdded && production.Status == "failed"
                && production.Reason == "surface_route_predator" && p.ComponentHealth.Health >= .99f && !actions.IsActionRunning;
        }
        else
        {
            travel ??= new(p, actions, 0, 12, "dry_land", false, now, ReadForage,
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), TrackShore,
                shoreTarget: shoreFixtureGoal);
            travel.Update(now);
            if (travel.Status is null && elapsed < 32) return;
            Passed = travel.Status == "succeeded" && PlaySurvivalSenses.OnStableDryGround(body)
                && p.ComponentHealth.Health >= 0.99f && shoreActions < 20 && shoreEnteredWater;
            if (noExit) Passed = travel.Status == "failed" && travel.Reason == "no_observed_surface_route"
                && shoreActions == 0 && p.ComponentHealth.Health >= .99f;
        }
        Done = true;
        Gate.Log(new { kind = "shore_fixture_result", mode, Passed, shoreActions, shoreFailures, shoreJumps, shoreEnteredWater,
            status = hunt ? production?.Status : travel?.Status, reason = hunt ? production?.Reason : travel?.Reason,
            position = body.Position.ToString(), health = p.ComponentHealth.Health,
            rawMeat = NativeAcquisitionAudit.InventoryCount(p.ComponentMiner.Inventory, RawBirdBlock.Index), shoreDrifted, shoreDriftUpdates,
            shoreHidden, shoreContinuations, shoreThreatAdded, shoreDriftStopped });
    }

    private void TrackShore(object value)
    {
        Gate.Log(value);
        var row = JsonSerializer.SerializeToElement(value);
        string? kind = row.GetProperty("kind").GetString();
        if (kind is "play_scout_step" or "play_surface_step") shoreActions++;
        if (kind == "play_scout_leg" && row.GetProperty("nativeStatus").GetString() != "Succeeded") shoreFailures++;
        if (kind == "play_surface_target_drift") shoreDriftUpdates++;
        if (kind == "play_surface_continue") shoreContinuations++;
        if (kind == "play_hunt_water_loot" && mode == "shore-hunt-drift")
            shoreDriftUntil = row.GetProperty("gameTime").GetDouble() + 1.2;
    }
}
