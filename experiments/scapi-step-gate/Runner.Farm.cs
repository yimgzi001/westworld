#nullable enable

using Engine;
using Game;
using Game.Westworld;
using Westworld.Scapi.Adapter;

sealed partial class Runner
{
    private readonly Dictionary<Point3, PlaySurvivalProduction.Patch> fixtureFarm = new();
    private int farmOperation;
    private int farmInitialPumpkins;

    private void RunPumpkinFarm()
    {
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        var carry = p.ComponentMiner.Inventory;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(p.ComponentBody.Position);
            for (int x = cell.X - 4; x <= cell.X + 4; x++)
            for (int z = cell.Z - 4; z <= cell.Z + 4; z++)
            for (int y = cell.Y - 2; y <= cell.Y + 3; y++)
                terrain.ChangeCell(x, y, z, y < cell.Y ? DirtBlock.Index : 0);
            p.ComponentBody.Position = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            p.ComponentBody.Velocity = Vector3.Zero;
            p.ComponentVitalStats.Food = 0.65f;
            var calendar = p.Project.FindSubsystem<SubsystemTimeOfDay>(true);
            calendar.TimeOfDayOffset += 0.3f - calendar.TimeOfDay;
            for (int slot = 0; slot < carry.SlotsCount; slot++) carry.RemoveSlotItems(slot, carry.GetSlotCount(slot));
            int pumpkin = Terrain.MakeBlockValue(PumpkinBlock.Index, 0, BasePumpkinBlock.SetIsDead(BasePumpkinBlock.SetSize(0, 7), true));
            // 仅布置夹具初态。制种、耕土与播种随后全部走原生动作。
            carry.AddSlotItems(0, pumpkin, 2);
            carry.AddSlotItems(1, WoodenRakeBlock.Index, 1);
            farmInitialPumpkins = 2;
            forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            stage = 1;
            elapsed = 0;
            return;
        }
        if (elapsed < 1) return;
        production ??= new(p, actions, forageTools!, farmOperation == 0 ? PlaySurvivalProduction.PumpkinSeeds
            : PlaySurvivalProduction.PlantPumpkins, now, Gate.Log, productionStations,
            () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), ownFarm: fixtureFarm);
        production.Update(now);
        if (production.Status is null) return;
        Gate.Log(new { kind = "farm_fixture_task", production.Task, production.Status, production.Reason, production.Added });
        if (production.Status != "succeeded") { Done = true; return; }
        if (farmOperation++ == 0)
        {
            int remaining = Enumerable.Range(0, carry.SlotsCount).Where(slot => carry.GetSlotCount(slot) > 0
                && Terrain.ExtractContents(carry.GetSlotValue(slot)) == PumpkinBlock.Index).Sum(carry.GetSlotCount);
            if (remaining != farmInitialPumpkins - 1) throw new InvalidOperationException("原生制种应仅消耗一只成熟南瓜。");
            production = null;
            return;
        }
        int seeds = PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.PumpkinSeeds);
        bool nativeSeedlings = fixtureFarm.Count == 3 && fixtureFarm.Values.All(patch =>
        {
            int value = terrain.Terrain.GetCellValue(patch.Cell);
            int ground = terrain.Terrain.GetCellValue(patch.Cell - new Point3(0, 1, 0));
            return Terrain.ExtractContents(value) == PumpkinBlock.Index && BasePumpkinBlock.GetSize(Terrain.ExtractData(value)) == 0
                && Terrain.ExtractContents(ground) == SoilBlock.Index && !BasePumpkinBlock.GetIsDead(Terrain.ExtractData(value));
        });
        var budget = PlayFoodBudget.Count(p);
        Passed = seeds == 0 && nativeSeedlings && budget.Safe > 0f && budget.Safe < 0.3f;
        Done = true;
        Gate.Log(new { kind = "farm_fixture_result", Passed, seeds, seedlings = fixtureFarm.Count, nativeSeedlings,
            budget, modelCalls = 0, naturalGrowthNotAccelerated = true });
    }
}
