using Engine;
using Game;
using Game.Westworld;

sealed partial class Runner
{
    private static readonly string[] KitOrder = [PlaySurvivalProduction.Wood, PlaySurvivalProduction.Workbench,
        PlaySurvivalProduction.Wood, PlaySurvivalProduction.Fire, PlaySurvivalProduction.Stone, PlaySurvivalProduction.Warm];
    private static readonly string[] KitchenOrder = [PlaySurvivalProduction.Wood, PlaySurvivalProduction.Workbench,
        PlaySurvivalProduction.Stone, PlaySurvivalProduction.Furnace, PlaySurvivalProduction.Cook];
    private static readonly string[] HuntOrder = [PlaySurvivalProduction.Wood, PlaySurvivalProduction.Workbench,
        PlaySurvivalProduction.Wood, PlaySurvivalProduction.Wood, PlaySurvivalProduction.Spear, PlaySurvivalProduction.Hunt,
        PlaySurvivalProduction.Stone, PlaySurvivalProduction.Furnace, PlaySurvivalProduction.Cook];
    private readonly Dictionary<int, Point3> productionStations = new();
    private PlaySurvivalProduction? production;
    private int productionIndex;
    private Point3[] resourceCells = [];
    private Vector3 productionOrigin;
    private bool projectInterrupted, projectApproachedMaterial;
    private int projectTablePlacements;

    private void RunProduction()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        bool approach = mode == "production-hunt-approach";
        bool project = mode == "production-project";
        bool ranged = approach || mode == "production-hunt-ranged";
        bool hunt = mode == "production-hunt" || ranged || project;
        bool kitchen = mode is "production-hunt" or "production-kitchen" || project;
        var order = project ? [PlaySurvivalProduction.CookedFoodProject] : hunt ? HuntOrder : kitchen ? KitchenOrder : KitOrder;
        int taskCount = ranged ? Array.IndexOf(order, PlaySurvivalProduction.Hunt) + 1 : order.Length;
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            for (int x = cell.X - 3; x <= cell.X + (approach || project ? 11 : ranged ? 6 : 3); x++)
            for (int z = cell.Z - 3; z <= cell.Z + 3; z++)
            for (int y = cell.Y - 1; y <= cell.Y + 3; y++)
                terrain.ChangeCell(x, y, z, y < cell.Y ? DirtBlock.Index : 0);
            resourceCells = project ? [cell + new Point3(5, 0, -1), cell + new Point3(6, 0, -1),
                    cell + new Point3(6, 0, -2), cell + new Point3(3, 0, 2)]
                : hunt ? [cell + new Point3(-1, 0, 0), cell + new Point3(0, 0, -1),
                    cell + new Point3(-1, 0, -1), cell + new Point3(1, 0, 1)]
                : kitchen ? [cell + new Point3(1, 0, 0), cell + new Point3(0, 0, 1)]
                : [cell + new Point3(1, 0, 0), cell + new Point3(-1, 0, 0), cell + new Point3(0, 0, 1)];
            for (int i = 0; i < resourceCells.Length; i++)
            {
                var item = resourceCells[i];
                terrain.ChangeCell(item.X, item.Y, item.Z, i < resourceCells.Length - 1 ? OakWoodBlock.Index : GraniteBlock.Index);
            }
            body.Position = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            productionOrigin = body.Position;
            body.Velocity = Vector3.Zero;
            if (project)
            {
                var initialInventory = p.ComponentMiner.Inventory;
                for (int slot = 0; slot < initialInventory.SlotsCount; slot++)
                {
                    int value = initialInventory.GetSlotValue(slot);
                    if (BlocksManager.Blocks[Terrain.ExtractContents(value)].GetNutritionalValue(value) > 0f)
                        initialInventory.RemoveSlotItems(slot, initialInventory.GetSlotCount(slot));
                }
            }
            if (order.Where(task => task != PlaySurvivalProduction.Cook).Any(task => PlaySurvivalProduction.Stock(p, task) > 0))
                throw new InvalidOperationException("生产夹具要求开局没有目标材料和成品。");
            if (kitchen)
            {
                var drops = p.Project.FindSubsystem<SubsystemPickables>(true);
                var at = body.Position + new Vector3(0, 0.2f, -0.2f);
                drops.AddPickable(CobblestoneBlock.Index, 7, at, Vector3.Zero, null);
                if (!hunt)
                {
                    drops.AddPickable(RawMeatBlock.Index, 2, at, Vector3.Zero, null);
                    drops.AddPickable(PlanksBlock.Index, 3, at, Vector3.Zero, null);
                }
            }
            if (hunt)
            {
                int wallX = cell.X + (approach || project ? 10 : ranged ? 5 : 2);
                for (int z = cell.Z - 1; z <= cell.Z + 1; z++)
                for (int y = cell.Y; y <= cell.Y + 2; y++) terrain.ChangeCell(wallX, y, z, GraniteBlock.Index);
                var duck = DatabaseManager.CreateEntity(p.Project, "Duck", true);
                duck.FindComponent<ComponentBody>(true).Position = body.Position + (approach || project ? 8.2f : ranged ? 3.65f : 1.1f) * Vector3.UnitX;
                var legs = duck.FindComponent<ComponentLocomotion>(true);
                legs.WalkSpeed = legs.FlySpeed = legs.JumpSpeed = 0f;
                p.Project.AddEntity(duck);
            }
            forageTools = new(Guid.NewGuid(), p, new ActorTools(actions, true));
            elapsed = 0;
            stage = 1;
            return;
        }
        if (elapsed < 1) return;
        if (production is null && approach && order[productionIndex] == PlaySurvivalProduction.Hunt)
        {
            var menu = PlaySurvivalProduction.Offer(p, actions.Queries.ReadPanorama(12f), productionStations, true);
            if (menu is null || !menu.Parameters.GetProperty("properties").GetProperty("task").GetProperty("enum")
                .EnumerateArray().Any(item => item.GetString() == PlaySurvivalProduction.Hunt))
                throw new InvalidOperationException("手持木矛时，射程外可见食用鸟应进入试玩菜单。");
        }
        production ??= new(p, actions, forageTools!, order[productionIndex], now, TrackProduction, productionStations,
            () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)));
        production.Update(now);
        if (project && !projectInterrupted && production.CurrentStage == PlaySurvivalProduction.Hunt
            && productionStations.ContainsKey(CraftingTableBlock.Index) && PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Spear) == 1)
        {
            // 已完成的工作台、木矛和原料保留。下一帧从实际库存重启同一任务。
            production.Stop();
            production = null;
            projectInterrupted = true;
            Gate.Log(new { kind = "production_fixture_interrupted", gameTime = now });
            return;
        }
        if (production.Status is null) return;
        Gate.Log(new { kind = "production_fixture_task", production.Task, production.Status, production.Reason, production.Added });
        if (production.Status != "succeeded" || production.Task != PlaySurvivalProduction.Warm && production.Added < 1)
        {
            Done = true;
            return;
        }
        production = null;
        if (++productionIndex < taskCount) return;

        bool materialConsumed = PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Wood) == 0;
        var inventory = p.ComponentMiner.Inventory;
        float moved = (body.Position - productionOrigin).XZ.Length();
        Passed = materialConsumed && (ranged
            ? NativeAcquisitionAudit.InventoryCount(inventory, RawBirdBlock.Index) == 1 && p.ComponentHealth.Health >= 0.99f
                && (!approach || moved >= 4f)
            : kitchen
            ? productionStations.ContainsKey(CraftingTableBlock.Index) && productionStations.ContainsKey(FurnaceBlock.Index)
                && PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Stone) == 0
                && NativeAcquisitionAudit.InventoryCount(inventory, hunt ? CookedBirdBlock.Index : CookedMeatBlock.Index) == (hunt ? 1 : 2)
                && NativeAcquisitionAudit.InventoryCount(inventory, hunt ? RawBirdBlock.Index : RawMeatBlock.Index) == 0
            : productionStations.ContainsKey(CampfireBlock.Index) && PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Stone) == 1
                && PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Workbench) == 1);
        if (project) Passed &= projectInterrupted && projectApproachedMaterial && projectTablePlacements == 1
            && p.ComponentHealth.Health >= 0.99f;
        Done = true;
        Gate.Log(new { kind = "production_fixture_result", Passed, kitchen, hunt, ranged, approach, project, projectInterrupted,
            projectApproachedMaterial, projectTablePlacements, moved, completed = productionIndex });
    }

    private void TrackProduction(object entry)
    {
        Gate.Log(entry);
        var record = System.Text.Json.JsonSerializer.SerializeToElement(entry);
        switch (record.GetProperty("kind").GetString())
        {
            case "play_production_stage" when record.GetProperty("stage").GetString() is "approach_wood" or "approach_stone":
                projectApproachedMaterial = true;
                break;
            case "play_station_placed" when record.GetProperty("stationBlock").GetInt32() == CraftingTableBlock.Index:
                projectTablePlacements++;
                break;
        }
    }

}
