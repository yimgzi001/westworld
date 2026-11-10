using Engine;
using Game;
using Game.Westworld;

sealed partial class Runner
{
    private static readonly string[] KitOrder = [PlaySurvivalProduction.Wood, PlaySurvivalProduction.Workbench,
        PlaySurvivalProduction.Wood, PlaySurvivalProduction.Fire, PlaySurvivalProduction.Stone, PlaySurvivalProduction.Warm];
    private static readonly string[] KitchenOrder = [PlaySurvivalProduction.Wood, PlaySurvivalProduction.Workbench,
        PlaySurvivalProduction.Stone, PlaySurvivalProduction.Furnace, PlaySurvivalProduction.Cook];
    private readonly Dictionary<int, Point3> productionStations = new();
    private PlaySurvivalProduction? production;
    private int productionIndex;
    private Point3[] resourceCells = [];

    private void RunProduction()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        bool kitchen = mode == "production-kitchen";
        var order = kitchen ? KitchenOrder : KitOrder;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            for (int x = cell.X - 3; x <= cell.X + 3; x++)
            for (int z = cell.Z - 3; z <= cell.Z + 3; z++)
            for (int y = cell.Y - 1; y <= cell.Y + 3; y++)
                terrain.ChangeCell(x, y, z, y < cell.Y ? DirtBlock.Index : 0);
            resourceCells = kitchen ? [cell + new Point3(1, 0, 0), cell + new Point3(0, 0, 1)]
                : [cell + new Point3(1, 0, 0), cell + new Point3(-1, 0, 0), cell + new Point3(0, 0, 1)];
            for (int i = 0; i < resourceCells.Length; i++)
            {
                var item = resourceCells[i];
                terrain.ChangeCell(item.X, item.Y, item.Z, i < resourceCells.Length - 1 ? OakWoodBlock.Index : GraniteBlock.Index);
            }
            body.Position = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            body.Velocity = Vector3.Zero;
            if (order.Where(task => task != PlaySurvivalProduction.Cook).Any(task => PlaySurvivalProduction.Stock(p, task) > 0))
                throw new InvalidOperationException("生产夹具要求开局没有目标材料和成品。");
            if (kitchen)
            {
                var drops = p.Project.FindSubsystem<SubsystemPickables>(true);
                var at = body.Position + new Vector3(0, 0.2f, -0.2f);
                drops.AddPickable(CobblestoneBlock.Index, 7, at, Vector3.Zero, null);
                drops.AddPickable(RawMeatBlock.Index, 2, at, Vector3.Zero, null);
                drops.AddPickable(PlanksBlock.Index, 3, at, Vector3.Zero, null);
            }
            forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            elapsed = 0;
            stage = 1;
            return;
        }
        if (elapsed < 1) return;
        production ??= new(p, actions, forageTools!, order[productionIndex], now, Gate.Log, productionStations);
        production.Update(now);
        if (production.Status is null) return;
        Gate.Log(new { kind = "production_fixture_task", production.Task, production.Status, production.Reason, production.Added });
        if (production.Status != "succeeded" || production.Task != PlaySurvivalProduction.Warm && production.Added < 1)
        {
            Done = true;
            return;
        }
        production = null;
        if (++productionIndex < order.Length) return;

        bool materialConsumed = PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Wood) == 0;
        var inventory = p.ComponentMiner.Inventory;
        Passed = materialConsumed && (kitchen
            ? productionStations.ContainsKey(CraftingTableBlock.Index) && productionStations.ContainsKey(FurnaceBlock.Index)
                && PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Stone) == 0
                && NativeAcquisitionAudit.InventoryCount(inventory, CookedMeatBlock.Index) == 2
                && NativeAcquisitionAudit.InventoryCount(inventory, RawMeatBlock.Index) == 0
            : productionStations.ContainsKey(CampfireBlock.Index) && PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Stone) == 1
                && PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Workbench) == 1);
        Done = true;
        Gate.Log(new { kind = "production_fixture_result", Passed, kitchen, completed = productionIndex });
    }
}
