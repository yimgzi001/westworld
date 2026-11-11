#nullable enable

using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Scapi.Adapter;

sealed partial class Runner
{
    private Point3 stationFixtureFire;
    private Vector3 stationFixtureOrigin;
    private int stationFixtureCleared, stationFixtureProgress, stationFixtureStage;

    private void RunStationFixture()
    {
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        bool returning = mode == "worksite-hidden";
        bool fuel = mode.StartsWith("station-fire-", StringComparison.Ordinal);
        var carry = p.ComponentMiner.Inventory;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(p.ComponentBody.Position);
            for (int x = cell.X - 7; x <= cell.X + 26; x++)
            for (int z = cell.Z - 7; z <= cell.Z + 7; z++)
            for (int y = cell.Y - 3; y <= cell.Y + 5; y++)
            {
                int rise = returning && x >= cell.X + 6 ? 1 : 0;
                if (returning && x == cell.X + 8 && Math.Abs(z - cell.Z) <= 3) rise = 2;
                terrain.ChangeCell(x, y, z, y < cell.Y + rise ? DirtBlock.Index : 0);
            }
            p.ComponentBody.Position = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            p.ComponentBody.Velocity = Vector3.Zero;
            stationFixtureOrigin = p.ComponentBody.Position;
            p.ComponentVitalStats.Food = 0.7f;
            p.ComponentVitalStats.Temperature = 8f;
            var calendar = p.Project.FindSubsystem<SubsystemTimeOfDay>(true);
            calendar.TimeOfDayOffset += 0.3f - calendar.TimeOfDay;
            for (int slot = 0; slot < carry.SlotsCount; slot++) carry.RemoveSlotItems(slot, carry.GetSlotCount(slot));
            // 初态只在夹具中布置；正式过程只用角色观察、原生移动、挖掘、放置、交互和库存动作。
            if (returning)
            {
                var furnace = cell + new Point3(18, 1, 0);
                terrain.ChangeCell(furnace.X, furnace.Y, furnace.Z, FurnaceBlock.Index);
                productionStations[FurnaceBlock.Index] = furnace;
                carry.AddSlotItems(0, RawMeatBlock.Index, 1);
                carry.AddSlotItems(1, OakWoodBlock.Index, 1);
            }
            else if (fuel)
            {
                stationFixtureFire = cell + new Point3(2, 0, 0);
                int size = mode == "station-fire-out" ? 0 : mode == "station-fire-full" ? 15 : 2;
                terrain.ChangeCell(stationFixtureFire.X, stationFixtureFire.Y, stationFixtureFire.Z,
                    Terrain.MakeBlockValue(CampfireBlock.Index, 0, size));
                productionStations[CampfireBlock.Index] = stationFixtureFire;
                carry.AddSlotItems(0, WoodenSpearBlock.Index, 1);
                carry.AddSlotItems(carry.VisibleSlotsCount + 1, OakWoodBlock.Index, 2);
            }
            else
            {
                carry.AddSlotItems(0, CampfireBlock.Index, 1);
                int plant = mode == "station-flower" ? RedFlowerBlock.Index : mode == "station-crop" ? RyeBlock.Index : TallGrassBlock.Index;
                for (int x = cell.X - 4; x <= cell.X + 4; x++)
                for (int z = cell.Z - 4; z <= cell.Z + 4; z++) terrain.ChangeCell(x, cell.Y, z, plant);
            }
            forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            stage = 1;
            elapsed = 0;
            return;
        }
        if (elapsed < 1) return;
        string task = returning ? stationFixtureStage == 0 ? PlaySurvivalProduction.VisitFurnace : PlaySurvivalProduction.Cook
            : fuel ? PlaySurvivalProduction.TendFire : PlaySurvivalProduction.Warm;
        production ??= new(p, actions, forageTools!, task, now, record =>
        {
            var e = JsonSerializer.SerializeToElement(record);
            if (e.GetProperty("kind").GetString() == "play_station_plant_clearing") stationFixtureCleared++;
            if (e.GetProperty("kind").GetString() == "play_worksite_progressive_route") stationFixtureProgress++;
            Gate.Log(record);
        }, productionStations, () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)));
        production.Update(now);
        if (production.Status is null) return;
        Gate.Log(new { kind = "station_fixture_task", mode, production.Task, production.Status, production.Reason, production.Added });
        if (returning && stationFixtureStage++ == 0 && production.Status == "succeeded") { production = null; return; }
        float moved = (p.ComponentBody.Position - stationFixtureOrigin).XZ.Length();
        var fireCell = fuel ? stationFixtureFire : productionStations.GetValueOrDefault(CampfireBlock.Index);
        int fireSize = returning || !fuel && !productionStations.ContainsKey(CampfireBlock.Index) ? 0
            : Terrain.ExtractData(terrain.Terrain.GetCellValue(fireCell.X, fireCell.Y, fireCell.Z));
        int fuelStock = NativeAcquisitionAudit.InventoryCount(carry, OakWoodBlock.Index) ?? 0;
        Passed = returning ? production.Status == "succeeded" && stationFixtureProgress > 0 && moved > 10
                && NativeAcquisitionAudit.InventoryCount(carry, CookedMeatBlock.Index) == 1
            : mode == "station-crop" ? production.Status == "failed" && stationFixtureCleared == 0
                && NativeAcquisitionAudit.InventoryCount(carry, CampfireBlock.Index) == 1
            : mode == "station-fire-out" ? production.Status == "failed" && fuelStock == 2 && fireSize == 0
            : mode == "station-fire-full" ? production.Status == "succeeded" && production.Added == 0 && fuelStock == 2 && fireSize == 15
            : fuel ? production.Status == "succeeded" && production.Added == 1 && fuelStock == 1 && fireSize > 2
            : production.Status == "succeeded" && stationFixtureCleared > 0 && productionStations.ContainsKey(CampfireBlock.Index);
        Passed &= p.ComponentHealth.Health >= 0.95f && !p.Entity.FindComponent<ComponentOnFire>(true).IsOnFire;
        Done = true;
        Gate.Log(new { kind = "station_fixture_result", mode, Passed, moved, stationFixtureCleared, stationFixtureProgress,
            fuelStock, fireSize, health = p.ComponentHealth.Health, modelCalls = 0 });
    }
}
