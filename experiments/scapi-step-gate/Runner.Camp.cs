#nullable enable

using Engine;
using Game;
using Game.Westworld;
using Westworld.Scapi.Adapter;

sealed partial class Runner
{
    private PlaySurvivalProduction.CampSite? fixtureCamp;
    private PlayThermalRecovery? campThermal;
    private int campOperation, fixtureCampPlacements;
    private bool campLeftSafely;
    private double campFixtureStarted;

    private void RunSimpleCamp()
    {
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        var carry = p.ComponentMiner.Inventory;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(p.ComponentBody.Position);
            for (int x = cell.X - 5; x <= cell.X + 5; x++)
            for (int z = cell.Z - 5; z <= cell.Z + 5; z++)
            for (int y = cell.Y - 2; y <= cell.Y + 4; y++)
                terrain.ChangeCell(x, y, z, y < cell.Y ? DirtBlock.Index : 0);
            p.ComponentBody.Position = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            p.ComponentBody.Velocity = Vector3.Zero;
            p.ComponentVitalStats.Food = 0.65f;
            p.ComponentVitalStats.Sleep = 0.5f;
            var calendar = p.Project.FindSubsystem<SubsystemTimeOfDay>(true);
            calendar.TimeOfDayOffset += 0.3f - calendar.TimeOfDay;
            for (int slot = 0; slot < carry.SlotsCount; slot++) carry.RemoveSlotItems(slot, carry.GetSlotCount(slot));
            // 验收夹具提供建屋材料，真实场次必须由模型自己安排采集。
            carry.AddSlotItems(0, DirtBlock.Index, mode == "camp-no-material" ? 39 : 40);
            if (mode != "camp-no-material") carry.AddSlotItems(1, DirtBlock.Index, 2);
            if (mode == "camp-warming") carry.AddSlotItems(2, CampfireBlock.Index, 1);
            forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            stage = 1;
            elapsed = 0;
            campFixtureStarted = now;
            return;
        }
        if (elapsed < 1) return;
        fixtureCamp ??= PlaySurvivalProduction.PlanCamp(memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)));
        if (fixtureCamp is null)
        {
            if (now - campFixtureStarted < 5) return;
            throw new InvalidOperationException("平整全向观察没有生成可核对的土屋地基。");
        }
        if (campThermal is { } heat)
        {
            var state = heat.Update(now);
            if (state == PlayThermalRecovery.State.Warming) return;
            Passed = state == PlayThermalRecovery.State.Warmed && PlaySurvivalRest.HasSecureCover(p)
                && p.ComponentHealth.Health >= 0.95f;
            Done = true;
            Gate.Log(new { kind = "camp_warming_result", Passed, state = state.ToString(),
                temperature = p.ComponentVitalStats.Temperature, before = heat.Before,
                health = p.ComponentHealth.Health, modelCalls = 0 });
            return;
        }
        string task = campOperation == 0 ? PlaySurvivalProduction.BuildCamp
            : mode == "camp-warming" ? PlaySurvivalProduction.Warm
            : campOperation == 1 ? PlaySurvivalProduction.LeaveCamp : PlaySurvivalProduction.ReturnCamp;
        production ??= new(p, actions, forageTools!, task, now, record =>
        {
            var entry = System.Text.Json.JsonSerializer.SerializeToElement(record);
            if (entry.GetProperty("kind").GetString() == "play_camp_block_placed") fixtureCampPlacements++;
            Gate.Log(record);
        }, productionStations, () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), camp: fixtureCamp);
        production.Update(now);
        if (production.Status is null) return;
        Gate.Log(new { kind = "camp_fixture_task", production.Task, production.Status, production.Reason, production.Added });
        if (mode == "camp-no-material")
        {
            Passed = production.Status == "failed" && production.Reason == "camp_materials_insufficient" && fixtureCampPlacements == 0;
            Done = true;
            Gate.Log(new { kind = "camp_material_guard_result", Passed, fixtureCampPlacements, modelCalls = 0 });
            return;
        }
        if (production.Status != "succeeded") { Done = true; return; }
        production = null;
        if (campOperation == 0 && !PlaySurvivalRest.HasSecureCover(p)) throw new InvalidOperationException("建屋结果未形成原生可睡掩护。");
        if (mode == "camp-warming" && campOperation++ == 0) { p.ComponentVitalStats.Temperature = 5.5f; return; }
        if (mode == "camp-warming")
        {
            campThermal = new(p, productionStations[CampfireBlock.Index], now);
            return;
        }
        if (campOperation == 1) campLeftSafely = !fixtureCamp.Contains(p.ComponentBody) && !PlaySurvivalRest.HasEnclosure(p);
        if (++campOperation < 3) return;
        Passed = campLeftSafely && fixtureCamp.Contains(p.ComponentBody) && fixtureCamp.Built && fixtureCamp.DoorClosed
            && PlaySurvivalRest.HasSecureCover(p) && fixtureCampPlacements == 42 && p.ComponentHealth.Health >= 0.95f;
        Done = true;
        Gate.Log(new { kind = "camp_fixture_result", Passed, campLeftSafely, fixtureCampPlacements,
            cover = PlaySurvivalRest.HasSecureCover(p), health = p.ComponentHealth.Health, modelCalls = 0 });
    }
}
