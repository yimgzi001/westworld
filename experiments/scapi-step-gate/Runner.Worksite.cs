#nullable enable

using Engine;
using Game;
using Game.Westworld;
using Westworld.Scapi.Adapter;

sealed partial class Runner
{
    private Vector3 worksiteOrigin;
    private Point3 quarryOrigin;
    private bool sampleFixtureOffered, sampleFixturePrepared, sampleFixtureAcquired;
    private int sampleFixtureOperation;
    private float sampleFixtureFarthest;
    private void RunWorksite()
    {
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        var carry = p.ComponentMiner.Inventory;
        bool quarry = mode == "quarry-ground-stone";
        bool earth = mode == "quarry-ground-earth";
        bool sample = mode is "quarry-visible-sample" or "quarry-hidden-sample";
        bool hidden = mode == "quarry-hidden-sample";
        bool gathering = quarry || earth || sample;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(p.ComponentBody.Position);
            for (int x = cell.X - 4; x <= cell.X + 15; x++)
            for (int z = cell.Z - 4; z <= cell.Z + 4; z++)
            for (int y = cell.Y - 2; y <= cell.Y + 3; y++)
                terrain.ChangeCell(x, y, z, y < cell.Y ? quarry || sample && x >= cell.X + 7 ? GraniteBlock.Index : DirtBlock.Index : 0);
            p.ComponentBody.Position = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            p.ComponentBody.Velocity = Vector3.Zero;
            worksiteOrigin = p.ComponentBody.Position;
            quarryOrigin = cell - new Point3(0, 1, 0);
            var calendar = p.Project.FindSubsystem<SubsystemTimeOfDay>(true);
            calendar.TimeOfDayOffset += 0.3f - calendar.TimeOfDay;
            for (int slot = 0; slot < carry.SlotsCount; slot++) carry.RemoveSlotItems(slot, carry.GetSlotCount(slot));
            // 夹具只给起始工具或原料，移动、采挖、原生炉火和库存变化都由正式动作完成。
            if (sample)
            {
                // 首样夹具不提供石料或石斧；工作台与原木只供取样后的制斧步骤使用。
                carry.AddSlotItems(0, OakWoodBlock.Index, 2);
                carry.AddSlotItems(1, CraftingTableBlock.Index, 1);
            }
            else if (gathering) carry.AddSlotItems(0, earth ? CopperShovelBlock.Index : StoneAxeBlock.Index, 1);
            else
            {
                var furnace = cell + new Point3(10, 0, 0);
                terrain.ChangeCell(furnace.X, furnace.Y, furnace.Z, FurnaceBlock.Index);
                productionStations[FurnaceBlock.Index] = furnace;
                carry.AddSlotItems(0, RawMeatBlock.Index, 1);
                carry.AddSlotItems(1, OakWoodBlock.Index, 1);
            }
            forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            stage = 1;
            elapsed = 0;
            return;
        }
        if (elapsed < 1) return;
        if (sample) sampleFixtureFarthest = Math.Max(sampleFixtureFarthest, (p.ComponentBody.Position - worksiteOrigin).XZ.Length());
        if (sample && !sampleFixturePrepared)
        {
            var panorama = actions.Queries.ReadPanorama(32f);
            if (panorama is null) return;
            sampleFixtureOffered = PlaySurvivalProduction.PlanningTasks(p, panorama, productionStations, _ => true)
                .Contains(PlaySurvivalProduction.StoneSample);
            sampleFixturePrepared = true;
            if (hidden)
            {
                var cell = Terrain.ToCell(worksiteOrigin);
                for (int z = cell.Z - 4; z <= cell.Z + 4; z++)
                for (int y = cell.Y; y <= cell.Y + 4; y++) terrain.ChangeCell(cell.X + 4, y, z, PlanksBlock.Index);
                return;
            }
        }
        string task = sample ? sampleFixtureOperation == 0 ? PlaySurvivalProduction.StoneSample : PlaySurvivalProduction.Axe
            : gathering ? earth ? PlaySurvivalProduction.EarthSupply : PlaySurvivalProduction.StoneSupply
            : stage == 1 ? PlaySurvivalProduction.VisitFurnace : PlaySurvivalProduction.Cook;
        production ??= new(p, actions, forageTools!, task, now, Gate.Log, productionStations,
            () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)));
        production.Update(now);
        if (production.Status is null) return;
        Gate.Log(new { kind = "worksite_fixture_task", production.Task, production.Status, production.Reason, production.Added });
        if (sample)
        {
            if (hidden)
            {
                Passed = sampleFixtureOffered && production.Status == "failed" && production.Reason == "project_missing_visible_prerequisite"
                    && PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Stone) == 0
                    && (p.ComponentBody.Position - worksiteOrigin).XZ.Length() < 0.1f;
                Done = true;
            }
            else if (sampleFixtureOperation++ == 0 && production.Status == "succeeded")
            {
                sampleFixtureAcquired = production.Added == 1 && PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Stone) == 1
                    && Math.Abs(p.ComponentBody.Position.Y - worksiteOrigin.Y) < 0.1f
                    && PlaySurvivalSenses.OnDryGround(p.ComponentBody);
                production = null;
                return;
            }
            else
            {
                Passed = sampleFixtureOffered && sampleFixtureAcquired && production.Status == "succeeded"
                    && PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Axe) == 1
                    && PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Stone) == 0
                    && sampleFixtureFarthest > 4f;
                Done = true;
            }
            Passed &= p.ComponentHealth.Health >= 0.99f && PlaySurvivalSenses.OnDryGround(p.ComponentBody)
                && terrain.Terrain.GetCellContents(quarryOrigin.X, quarryOrigin.Y, quarryOrigin.Z) == DirtBlock.Index;
            Gate.Log(new { kind = "stone_sample_fixture_result", mode, Passed, sampleFixtureOffered,
                sampleFixtureAcquired, sampleFixtureFarthest, position = p.ComponentBody.Position.ToString(),
                noStoneOrAxeGift = true, modelCalls = 0 });
            return;
        }
        if (production.Status != "succeeded") { Done = true; return; }
        production = null;
        if (!gathering && stage++ == 1) return;
        float moved = (p.ComponentBody.Position - worksiteOrigin).XZ.Length();
        Passed = gathering ? PlaySurvivalProduction.Stock(p, earth ? PlaySurvivalProduction.Earth : PlaySurvivalProduction.Stone) >= (earth ? 40 : 8)
                && terrain.Terrain.GetCellContents(quarryOrigin.X, quarryOrigin.Y, quarryOrigin.Z) == (earth ? DirtBlock.Index : GraniteBlock.Index)
                && PlaySurvivalSenses.OnDryGround(p.ComponentBody)
            : NativeAcquisitionAudit.InventoryCount(carry, CookedMeatBlock.Index) == 1 && moved >= 5f;
        Done = true;
        Gate.Log(new { kind = "worksite_fixture_result", Passed, quarry, moved, modelCalls = 0,
            ownOriginalFootingIntact = terrain.Terrain.GetCellContents(quarryOrigin.X, quarryOrigin.Y, quarryOrigin.Z) == (quarry ? GraniteBlock.Index : DirtBlock.Index) });
    }
}
