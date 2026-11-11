#nullable enable

using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Scapi.Adapter;

sealed partial class Runner
{
    private PlaySurvivalProduction.QuarrySite? fixtureQuarry;
    private Point3 fixtureQuarryEntry;
    private int quarryFixtureStep;
    private bool quarryReturnedWithSample;
    private float quarryLowest;

    private void RunShallowQuarryFixture()
    {
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        var carry = p.ComponentMiner.Inventory;
        bool kitchen = mode == "quarry-kitchen";
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(p.ComponentBody.Position);
            for (int x = cell.X - 6; x <= cell.X + 14; x++)
            for (int z = cell.Z - 6; z <= cell.Z + 6; z++)
            for (int y = cell.Y - 7; y <= cell.Y + 3; y++)
            {
                int value = y >= cell.Y ? 0 : y >= cell.Y - 2 || mode == "quarry-depth-limit" ? DirtBlock.Index : GraniteBlock.Index;
                if (mode == "quarry-water" && y == cell.Y - 2) value = WaterBlock.Index;
                terrain.ChangeCell(x, y, z, value);
            }
            p.ComponentBody.Position = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            p.ComponentBody.Velocity = Vector3.Zero;
            p.ComponentVitalStats.Food = 0.8f;
            p.ComponentVitalStats.Sleep = 0.8f;
            p.ComponentVitalStats.Temperature = 12f;
            fixtureQuarryEntry = cell;
            quarryLowest = cell.Y;
            var calendar = p.Project.FindSubsystem<SubsystemTimeOfDay>(true);
            calendar.TimeOfDayOffset += 0.3f - calendar.TimeOfDay;
            for (int slot = 0; slot < carry.SlotsCount; slot++) carry.RemoveSlotItems(slot, carry.GetSlotCount(slot));
            if (kitchen)
            {
                fixtureCookingTrial = new(p.Project, [p], now, record =>
                {
                    var e = JsonSerializer.SerializeToElement(record);
                    if (e.GetProperty("kind").GetString() == "play_survival_meal" && e.GetProperty("FromPreparation").GetBoolean())
                        fixturePreparedMeal = true;
                    Gate.Log(record);
                });
                // 夹具给木材、工作台和生肉，不给石料或石斧；石料必须从遮住的地层逐步露出并实际采得。
                carry.AddSlotItems(0, OakWoodBlock.Index, 6);
                carry.AddSlotItems(1, CraftingTableBlock.Index, 1);
                carry.AddSlotItems(2, RawMeatBlock.Index, 1);
            }
            forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            stage = 1;
            elapsed = 0;
            return;
        }
        if (elapsed < 1) return;
        quarryLowest = Math.Min(quarryLowest, p.ComponentBody.Position.Y);
        fixtureQuarry ??= PlaySurvivalProduction.QuarrySites(memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)))
            .FirstOrDefault(site => site.Direction.X == 1);
        if (fixtureQuarry is null)
        {
            if (elapsed < 4) return;
            throw new InvalidOperationException("夹具的已见平坦土面未形成浅探候选。");
        }
        string[] tasks = [PlaySurvivalProduction.Quarry, PlaySurvivalProduction.Axe, PlaySurvivalProduction.StoneSupply,
            PlaySurvivalProduction.VisitTable, PlaySurvivalProduction.Furnace, PlaySurvivalProduction.Cook];
        if (quarryFixtureStep == tasks.Length)
        {
            int slot = Enumerable.Range(0, carry.SlotsCount).FirstOrDefault(i => carry.GetSlotCount(i) > 0
                && Terrain.ExtractContents(carry.GetSlotValue(i)) == CookedMeatBlock.Index, -1);
            var eat = slot < 0 ? null : actions.Start(new Eat(slot, carry.GetSlotValue(slot), 1));
            fixtureCookingTrial!.Sample(now, true);
            Passed = eat?.Result.Status == AtomicActionStatus.Succeeded && fixturePreparedMeal && quarryReturnedWithSample;
            Done = true;
            Gate.Log(new { kind = "quarry_kitchen_result", Passed, fixturePreparedMeal, quarryReturnedWithSample,
                maximumDepth = fixtureQuarryEntry.Y - quarryLowest, noStoneGift = true, modelCalls = 0 });
            return;
        }
        production ??= new(p, actions, forageTools!, tasks[quarryFixtureStep], now, Gate.Log, productionStations,
            () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), quarrySite: fixtureQuarry,
            preserveGround: fixtureQuarry.Supports.ToHashSet());
        production.Update(now);
        if (production.Status is null) return;
        Gate.Log(new { kind = "quarry_fixture_task", mode, production.Task, production.Status, production.Reason,
            production.Added, position = p.ComponentBody.Position.ToString() });
        bool surface = Math.Abs(p.ComponentBody.Position.Y - fixtureQuarryEntry.Y) < 0.1f
            && (p.ComponentBody.Position - fixtureQuarry.Entry).XZ.LengthSquared() < 0.1f;
        bool footing = terrain.Terrain.GetCellContents(fixtureQuarryEntry.X, fixtureQuarryEntry.Y - 1, fixtureQuarryEntry.Z) == DirtBlock.Index;
        if (quarryFixtureStep == 0) quarryReturnedWithSample = surface && production.Added > 0 && fixtureQuarry.StoneSeen;
        if (mode is "quarry-depth-limit" or "quarry-water")
        {
            Passed = production.Status == "failed" && surface && footing && !fixtureQuarry.StoneSeen
                && p.ComponentHealth.Health >= 0.95f && fixtureQuarry.DepthReached <= 3.1f
                && (mode == "quarry-water" ? production.Reason == "quarry_liquid_or_missing_support"
                    : production.Reason == "no_stone_within_shallow_limit" && fixtureQuarry.DepthReached >= 2.9f);
            Done = true;
        }
        else if (production.Status != "succeeded") Done = true;
        else if (!kitchen)
        {
            Passed = quarryReturnedWithSample && footing && fixtureQuarry.DepthReached <= 4.1f
                && p.ComponentHealth.Health >= 0.95f;
            Done = true;
        }
        if (Done)
        {
            Gate.Log(new { kind = "quarry_fixture_result", mode, Passed, surface, footing, quarryReturnedWithSample,
                fixtureQuarry.DepthReached, modelCalls = 0 });
            return;
        }
        quarryFixtureStep++;
        production = null;
    }
}
