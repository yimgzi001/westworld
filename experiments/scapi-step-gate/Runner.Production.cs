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
    private bool projectRejectedOverhead;
    private Point3 overheadWood;
    private int projectTablePlacements;
    private ComponentBody? movingFixtureBird;
    private float fixtureBirdStartZ;
    private double fixtureBirdMoveUntil;
    private bool fixtureBirdMotionEnded, trackedFixtureBird;
    private bool movedFirstWoodDrop, approachedFirstWoodDrop;
    private ComponentBody? projectFixtureBird;
    private bool screenedProjectBird, preparedWhileBirdHidden;

    private void RunProduction()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        bool tracking = mode == "production-hunt-tracking";
        bool approach = mode == "production-hunt-approach" || tracking;
        bool hidePrey = mode == "production-gear-while-prey-hidden";
        bool project = mode == "production-project" || hidePrey;
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
            if (project)
            {
                overheadWood = cell + new Point3(3, 3, 0);
                terrain.ChangeCell(overheadWood.X, overheadWood.Y, overheadWood.Z, OakWoodBlock.Index);
            }
            if (hidePrey)
            {
                for (int z = cell.Z - 3; z <= cell.Z; z++)
                for (int y = cell.Y; y <= cell.Y + 2; y++)
                    terrain.ChangeCell(cell.X + 7, y, z, GraniteBlock.Index);
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
                if (project) drops.PickableAdded += MoveFirstWoodDrop;
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
                var duckBody = duck.FindComponent<ComponentBody>(true);
                // 制作链验证接续库存和工作站，把静止鸭子放在木堆另一侧的开阔地。
                duckBody.Position = body.Position + (project ? new Vector3(7.6f, 0f, 1.65f)
                    : (approach ? 8.2f : ranged ? 3.65f : 1.1f) * Vector3.UnitX);
                if (hidePrey) projectFixtureBird = duckBody;
                if (tracking)
                {
                    movingFixtureBird = duckBody;
                    fixtureBirdStartZ = duckBody.Position.Z;
                }
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
        if (movingFixtureBird is { } bird && fixtureBirdMoveUntil > 0 && !fixtureBirdMotionEnded)
        {
            // 用原生速度让猎物在接近途中横移，角色仍必须通过观察更新目标。
            var velocity = bird.Velocity;
            fixtureBirdMotionEnded = now >= fixtureBirdMoveUntil || bird.Position.Z - fixtureBirdStartZ >= 2.3f;
            velocity.Z = fixtureBirdMotionEnded ? 0f : 2.2f;
            bird.Velocity = velocity;
        }
        if (project && !projectRejectedOverhead)
        {
            var visible = actions.Queries.ReadPanorama(10f) ?? throw new InvalidOperationException("生产夹具缺少全向观察。");
            var aboveOnly = visible.Blocks.Where(block => !PlaySurvivalProduction.IsMaterial(block.Value, PlaySurvivalProduction.Wood)
                || block.Cell == overheadWood).ToArray();
            projectRejectedOverhead = aboveOnly.Any(block => block.Cell == overheadWood)
                && !PlaySurvivalProduction.CanAdvanceProject(p, PlaySurvivalProduction.SpearProject,
                    visible with { Blocks = aboveOnly }, productionStations, null);
            if (!projectRejectedOverhead) throw new InvalidOperationException("缺乏合适落脚高度的树干仍进入了木料路线。");
        }
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
        if (hidePrey)
        {
            int spears = PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Spear);
            int rawMeat = PlaySurvivalProduction.Stock(p, PlaySurvivalProduction.Hunt);
            Passed = preparedWhileBirdHidden && projectApproachedMaterial && movedFirstWoodDrop && approachedFirstWoodDrop
                && projectTablePlacements == 1 && spears == 1 && rawMeat == 0
                && production.Reason == "project_missing_visible_prerequisite" && p.ComponentHealth.Health >= 0.99f;
            Done = true;
            Gate.Log(new { kind = "production_fixture_hidden_prey_result", Passed, preparedWhileBirdHidden, spears, rawMeat,
                projectTablePlacements, movedFirstWoodDrop, approachedFirstWoodDrop, health = p.ComponentHealth.Health });
            return;
        }
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
        if (project) Passed &= projectInterrupted && projectApproachedMaterial && projectRejectedOverhead && projectTablePlacements == 1
            && movedFirstWoodDrop && approachedFirstWoodDrop && p.ComponentHealth.Health >= 0.99f;
        float preyMoved = movingFixtureBird is { } fixtureBird ? fixtureBird.Position.Z - fixtureBirdStartZ : 0f;
        if (tracking) Passed &= trackedFixtureBird && preyMoved >= 1.4f;
        Done = true;
        Gate.Log(new { kind = "production_fixture_result", Passed, kitchen, hunt, ranged, approach, project, projectInterrupted,
            projectApproachedMaterial, projectRejectedOverhead, projectTablePlacements, moved, tracking, preyMoved,
            trackedFixtureBird, movedFirstWoodDrop, approachedFirstWoodDrop, completed = productionIndex });
    }

    private void TrackProduction(object entry)
    {
        Gate.Log(entry);
        var record = System.Text.Json.JsonSerializer.SerializeToElement(entry);
        switch (record.GetProperty("kind").GetString())
        {
            case "play_production_stage" when record.GetProperty("stage").GetString() is "approach_wood" or "approach_stone":
                if ((mode is "production-project" or "production-gear-while-prey-hidden")
                    && record.GetProperty("material").GetString() == overheadWood.ToString())
                    throw new InvalidOperationException("实际任务选择了高处无可用落脚点的树干。");
                projectApproachedMaterial = true;
                if (projectFixtureBird is { } duck && !screenedProjectBird)
                {
                    // 在夹具中把同一只鸭放到已经建好的遮挡后面，木材和路线保持原样。
                    duck.Position -= 3.4f * Vector3.UnitZ;
                    duck.Velocity = Vector3.Zero;
                    screenedProjectBird = true;
                }
                break;
            case "play_production_stage" when screenedProjectBird
                && record.GetProperty("stage").GetString() == PlaySurvivalProduction.Spear:
                preparedWhileBirdHidden = actions.Queries.FindVisibleObjectsAround(20f).Entities
                    .All(animal => animal.EntityId != projectFixtureBird!.Entity.Id);
                Gate.Log(new { kind = "production_fixture_prey_screened", preparedWhileBirdHidden });
                break;
            case "play_station_placed" when record.GetProperty("stationBlock").GetInt32() == CraftingTableBlock.Index:
                projectTablePlacements++;
                break;
            case "play_hunt_stalk" when movingFixtureBird is not null:
                fixtureBirdMoveUntil = record.GetProperty("gameTime").GetDouble() + 1.15;
                break;
            case "play_scout_moving_target":
                trackedFixtureBird = true;
                break;
            case "play_production_drop_approach" when movedFirstWoodDrop:
                approachedFirstWoodDrop = true;
                break;
        }
    }

    private void MoveFirstWoodDrop(Pickable drop)
    {
        if (movedFirstWoodDrop || !PlaySurvivalProduction.IsMaterial(drop.Value, PlaySurvivalProduction.Wood)) return;
        // 模拟刚挖掉的木材弹到手够不到的干地。保留原生掉落物和它的来源。
        drop.Position = p.ComponentBody.Position + new Vector3(2.55f, 0.2f, 0f);
        drop.Velocity = Vector3.Zero;
        movedFirstWoodDrop = true;
        Gate.Log(new { kind = "production_fixture_drop_moved", target = drop.Position.ToString() });
    }

}
