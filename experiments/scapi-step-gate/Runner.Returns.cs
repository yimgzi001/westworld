#nullable enable

using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Scapi.Adapter;

sealed partial class Runner
{
    private const int ReturnFixtureTerrainKey = 34051;
    private Vector3 returnFixtureOrigin;
    private Point3 returnFixtureStation;
    private PlaySurvivalProduction.CampSite? returnFixtureCamp;
    private bool returnFixtureOffered, returnFixtureCooked;
    private int returnFixtureSegments, returnFixtureOperation, returnFixtureStepFinishes;
    private bool deadlineFixtureStepStarted;
    private double deadlineFixtureOffset;

    private void RunReturnStepDeadline()
    {
        bool noGrace = mode == "deadline-return-step-no-grace";
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            for (int x = cell.X - 3; x <= cell.X + 5; x++)
            for (int z = cell.Z - 3; z <= cell.Z + 3; z++)
            for (int y = cell.Y - 2; y <= cell.Y + 5; y++)
                terrain.ChangeCell(x, y, z, y < cell.Y + (x > cell.X ? 1 : 0) ? DirtBlock.Index : 0);
            body.Position = returnFixtureOrigin = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            body.Velocity = Vector3.Zero;
            p.ComponentVitalStats.Food = 0.8f;
            p.ComponentVitalStats.Temperature = 12f;
            stage = 1;
            elapsed = 0;
            return;
        }
        if (elapsed < 1) return;
        var destination = returnFixtureOrigin + new Vector3(1, 1, 0);
        if (travel is null)
        {
            float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, Vector2.UnitX));
            travel = new(p, actions, bearing, 1f, "work", false, now,
                () => (JsonSerializer.SerializeToElement(new { }), []),
                () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), record =>
                {
                    var e = JsonSerializer.SerializeToElement(record);
                    if (e.GetProperty("kind").GetString() == "play_scout_step"
                        && e.TryGetProperty("action", out var action) && action.GetString() == "StepTo") deadlineFixtureStepStarted = true;
                    if (e.GetProperty("kind").GetString() == "play_return_step_finishing") returnFixtureStepFinishes++;
                    Gate.Log(record);
                }, shelterTarget: destination,
                arrived: () => Math.Abs(body.Position.Y - destination.Y) < 0.1f
                    && (body.Position - destination).XZ.LengthSquared() < 0.45f * 0.45f,
                finishStepAtDeadline: !noGrace);
        }
        // 只推进技能看到的截止时间；游戏时钟、跳跃、重力与观察证据保持原生运行。
        if (deadlineFixtureStepStarted && body.Velocity.Y > 0.05f) deadlineFixtureOffset = 25;
        jumped |= p.ComponentLocomotion.LastJumpOrder > 0;
        travel.Update(now + deadlineFixtureOffset);
        if (travel.Status is null) return;
        Passed = noGrace ? travel.Status == "failed" && travel.Reason == "travel_interrupted"
                && !PlaySurvivalSenses.OnDryGround(body) && returnFixtureStepFinishes == 0
            : travel.Status == "succeeded" && returnFixtureStepFinishes == 1
                && PlaySurvivalSenses.OnDryGround(body) && Math.Abs(body.Velocity.Y) < 0.6f
                && Math.Abs(body.Position.Y - destination.Y) < 0.1f;
        Passed &= deadlineFixtureOffset == 25 && jumped && p.ComponentHealth.Health >= 0.99f;
        Done = true;
        Gate.Log(new { kind = "return_step_deadline_result", Passed, travel.Status, travel.Reason,
            noGrace, deadlineFixtureOffset, returnFixtureStepFinishes, jumped, position = body.Position.ToString(), modelCalls = 0 });
    }

    private void RunKnownReturnFixture()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        bool home = mode == "return-camp-long";
        bool changed = mode == "return-worksite-changed";
        bool blocked = mode == "return-worksite-blocked";
        var carry = p.ComponentMiner.Inventory;
        if (stage == 0)
        {
            returnFixtureOrigin = body.Position;
            terrain.TerrainUpdater.SetUpdateLocation(ReturnFixtureTerrainKey, body.Position.XZ + new Vector2(60, 0), 80, 96);
            stage = 1;
            return;
        }
        if (stage == 1)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(returnFixtureOrigin);
            for (int x = cell.X - 5; x <= cell.X + 120; x += 5)
            for (int z = cell.Z - 6; z <= cell.Z + 6; z += 6)
                if (!(terrain.Terrain.GetChunkAtCell(x, z)?.State >= TerrainChunkState.InvalidVertices1)) return;
            // 仅夹具预置过去的住处与工作站；正式返程仍只能读取当前局部观察。
            for (int x = cell.X - 5; x <= cell.X + 120; x++)
            for (int z = cell.Z - 6; z <= cell.Z + 6; z++)
            for (int y = cell.Y - 3; y <= cell.Y + 6; y++)
            {
                int rise = x >= cell.X + 28 && x <= cell.X + 49 ? 1 : 0;
                int value = y < cell.Y + rise ? DirtBlock.Index : 0;
                if (blocked && x == cell.X + 45 && y >= cell.Y && y <= cell.Y + 5) value = GraniteBlock.Index;
                terrain.ChangeCell(x, y, z, value);
            }
            body.Position = returnFixtureOrigin = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            body.Velocity = Vector3.Zero;
            p.ComponentVitalStats.Food = 0.8f;
            p.ComponentVitalStats.Sleep = 0.8f;
            p.ComponentVitalStats.Temperature = 12f;
            var calendar = p.Project.FindSubsystem<SubsystemTimeOfDay>(true);
            calendar.TimeOfDayOffset += 0.3f - calendar.TimeOfDay;
            for (int slot = 0; slot < carry.SlotsCount; slot++) carry.RemoveSlotItems(slot, carry.GetSlotCount(slot));
            if (home)
            {
                returnFixtureCamp = new(cell.X + 110, cell.Y, cell.Z - 1) { Built = true, DoorClosed = true };
                foreach (var shell in returnFixtureCamp.Shell.Concat(returnFixtureCamp.Door))
                    terrain.ChangeCell(shell.X, shell.Y, shell.Z, DirtBlock.Index);
                carry.AddSlotItems(0, DirtBlock.Index, 2);
            }
            else
            {
                returnFixtureStation = cell + new Point3(112, 0, 0);
                if (!changed) terrain.ChangeCell(returnFixtureStation.X, returnFixtureStation.Y, returnFixtureStation.Z, FurnaceBlock.Index);
                productionStations[FurnaceBlock.Index] = returnFixtureStation;
                carry.AddSlotItems(0, RawMeatBlock.Index, 1);
                carry.AddSlotItems(1, OakWoodBlock.Index, 1);
            }
            forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            stage = 2;
            elapsed = 0;
            return;
        }
        if (elapsed < 1) return;
        var at = Terrain.ToCell(body.Position);
        if (terrain.Terrain.GetChunkAtCell(at.X, at.Z)?.State != TerrainChunkState.Valid) return;
        if (!home && returnFixtureOperation == 0 && production is null)
        {
            var panorama = actions.Queries.ReadPanorama(40f);
            if (panorama is null) return;
            returnFixtureOffered = PlaySurvivalProduction.PlanningTasks(p, panorama,
                productionStations, _ => true).Contains(PlaySurvivalProduction.VisitFurnace);
        }
        string task = home ? PlaySurvivalProduction.ReturnCamp
            : returnFixtureOperation == 0 ? PlaySurvivalProduction.VisitFurnace : PlaySurvivalProduction.Cook;
        production ??= new(p, actions, forageTools!, task, now, record =>
        {
            var e = JsonSerializer.SerializeToElement(record);
            if (e.GetProperty("kind").GetString() == "play_known_return_progress") returnFixtureSegments++;
            if (e.GetProperty("kind").GetString() == "play_return_step_finishing") returnFixtureStepFinishes++;
            Gate.Log(record);
        }, productionStations, () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)),
            permitLandLeg: blocked ? (end, _) => Math.Abs(end.Z - returnFixtureOrigin.Z) < 4f : null,
            camp: returnFixtureCamp);
        production.Update(now);
        if (production.Status is null) return;
        Gate.Log(new { kind = "known_return_fixture_task", mode, production.Task, production.Status, production.Reason });
        if (!home && !changed && !blocked && returnFixtureOperation++ == 0 && production.Status == "succeeded")
        { production = null; return; }
        float moved = (body.Position - returnFixtureOrigin).XZ.Length();
        returnFixtureCooked = NativeAcquisitionAudit.InventoryCount(carry, CookedMeatBlock.Index) == 1;
        Passed = home ? production.Status == "succeeded" && returnFixtureCamp!.Contains(body)
                && returnFixtureCamp.DoorClosed && PlaySurvivalRest.HasSecureCover(p)
            : changed ? production.Status == "failed" && production.Reason == "own_worksite_observed_changed"
                && !productionStations.ContainsKey(FurnaceBlock.Index) && moved > 100f
            : blocked ? production.Status == "failed" && moved < 46f && !returnFixtureCooked
                && NativeAcquisitionAudit.InventoryCount(carry, RawMeatBlock.Index) == 1
            : production.Status == "succeeded" && returnFixtureCooked && moved > 100f;
        Passed &= p.ComponentHealth.Health >= 0.99f && PlaySurvivalSenses.OnDryGround(body)
            && (blocked || returnFixtureSegments > 0) && (home || returnFixtureOffered);
        Done = true;
        terrain.TerrainUpdater.RemoveUpdateLocation(ReturnFixtureTerrainKey);
        Gate.Log(new { kind = "known_return_fixture_result", mode, Passed, moved, returnFixtureOffered,
            returnFixtureSegments, returnFixtureStepFinishes, returnFixtureCooked, health = p.ComponentHealth.Health, modelCalls = 0 });
    }
}
