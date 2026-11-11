#nullable enable

using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;

internal sealed partial class PlaySurvivalProduction
{
    private const float ProjectReach = 40f;
    private const float ProjectBirdReach = 20f;
    private const int ProjectStageLimit = 24;
    private sealed record ProjectStep(string Task, Vector3? Approach = null, Point3? Material = null, int? Bird = null);
    private PlaySurvivalProduction? projectStep;
    private PlaySurvivalTravel? projectRoute;
    private Point3? approachingMaterial;
    private readonly HashSet<Point3> triedMaterialApproaches = new();
    private int projectStages;
    private int? projectBird;

    public static bool IsProject(string task) => task is CookedFoodProject or SpearProject;

    public static bool CanAdvanceProject(ComponentPlayer player, string task, ActorPanorama? panorama,
        Dictionary<int, Point3> stations, Func<Vector3, bool>? permitted) => IsProject(task)
        && NextProjectStep(player, task, panorama, stations, permitted) is not null;

    private void AdvanceProject(double gameTime)
    {
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        if (PlaySurvivalRest.NightCoverDue(calendar) || player.ComponentSleep.IsSleeping)
        {
            Finish(false, "project_paused_for_night");
            return;
        }
        if (gameTime >= deadline || player.ComponentHealth.Health < 0.45f)
        {
            Finish(false, "project_safety_or_work_limit");
            return;
        }
        if (projectRoute is { } route)
        {
            route.Update(gameTime);
            if (route.Status is null) return;
            projectRoute = null;
            if (route.Status != "succeeded")
            {
                if (approachingMaterial is not { } blocked)
                {
                    Finish(false, route.Reason ?? "project_route_failed");
                    return;
                }
                log(new { kind = "play_production_material_route_released", actor = player.PlayerData.Name,
                    project = Task, material = blocked.ToString(), route.Reason, gameTime });
            }
            approachingMaterial = null;
        }
        if (projectStep is { } step)
        {
            step.Update(gameTime);
            if (step.Status is null) return;
            projectStep = null;
            if (step.Status != "succeeded")
            {
                Finish(false, step.Reason ?? "project_step_failed");
                return;
            }
        }
        if (Added > 0)
        {
            Finish(true, "project_inventory_goal_confirmed");
            return;
        }
        if (!PlaySurvivalSenses.OnDryGround(player.ComponentBody))
        {
            Finish(false, "project_needs_dry_ground");
            return;
        }
        if (projectStages >= ProjectStageLimit)
        {
            Finish(false, "project_stage_limit");
            return;
        }

        // 每个阶段完成后读取实际库存、工作站和可见资源。中断后从这些事实重新选择前置步骤。
        bool Permitted(Vector3 end) => permitLandLeg?.Invoke(end, gameTime) != false;
        var next = NextProjectStep(player, Task, owner.Queries.ReadPanorama(ProjectReach), stations, Permitted,
            triedMaterialApproaches, projectBird, keepPreparingGear: projectStages > 0 && projectBird is null);
        if (next is null)
        {
            Finish(false, "project_missing_visible_prerequisite");
            return;
        }
        projectStages++;
        CurrentStage = next.Task;
        if (next.Bird is int bird) projectBird = bird;
        log(new { kind = "play_production_stage", actor = player.PlayerData.Name, project = Task, stage = next.Task,
            number = projectStages, destination = next.Approach?.ToString(), material = next.Material?.ToString(), next.Bird, gameTime });
        if (next.Approach is { } destination)
        {
            approachingMaterial = next.Material;
            if (next.Material is { } material) triedMaterialApproaches.Add(material);
            var body = player.ComponentBody;
            var delta = (destination - body.Position).XZ;
            float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, delta));
            projectRoute = new(player, owner, bearing, delta.Length(), next.Bird.HasValue ? "prey" : "work", false, gameTime,
                () => (JsonSerializer.SerializeToElement(new { }), []), readDryWindow, log, permitLandLeg: permitLandLeg,
                readMovingTarget: next.Bird is int target ? () => ReadFoodBirdPoint(target, ProjectBirdReach) : null,
                movingTargetReach: HuntApproachReach - 1f);
            return;
        }
        projectStep = new(player, owner, tools, next.Task, gameTime, log, stations, readDryWindow, permitLandLeg, next.Bird);
    }

    private static ProjectStep? NextProjectStep(ComponentPlayer player, string task, ActorPanorama? panorama,
        Dictionary<int, Point3> stations, Func<Vector3, bool>? permitted, IReadOnlySet<Point3>? triedMaterials = null,
        int? selectedBird = null, bool keepPreparingGear = false)
    {
        if (panorama is null) return null;
        if (task == SpearProject) return NextSpearStep(player, panorama, stations, permitted, triedMaterials);

        var body = player.ComponentBody.Position;
        var entities = player.Project.FindSubsystem<SubsystemBlockEntities>(true);
        var visibleOven = SeenStation(panorama, stations, FurnaceBlock.Index, ProjectReach);
        var oven = visibleOven is { Distance: <= StationReach }
            ? entities.GetBlockEntity(visibleOven.Cell)?.Entity.FindComponent<ComponentFurnace>() : null;
        bool rawInOven = oven is not null && oven.GetSlotCount(0) > 0 && PlaySurvivalFood.IsRawMeat(oven.GetSlotValue(0));
        bool cookedInOven = oven is not null && oven.GetSlotCount(oven.ResultSlotIndex) > 0
            && IsCookedMeat(oven.GetSlotValue(oven.ResultSlotIndex));
        if (cookedInOven) return new(Cook);
        if (Stock(player, Hunt) == 0 && !rawInOven)
        {
            var prey = FindFoodBird(player, panorama.Objects.Entities.Where(animal => (selectedBird is null || animal.EntityId == selectedBird)
                && permitted?.Invoke(animal.Position) != false), ProjectBirdReach);
            if (prey is null)
                // 已开始备木矛后可以做完已知材料的准备。发起追猎时仍要重新看到食用鸟。
                return (keepPreparingGear && Stock(player, Spear) == 0
                    ? NextSpearStep(player, panorama, stations, permitted, triedMaterials) : null)
                    ?? ApproachStation(visibleOven, body, "return_to_furnace", permitted);
            if (!CanReadyHuntWeapon(player))
                return Stock(player, Spear) == 0 ? NextSpearStep(player, panorama, stations, permitted, triedMaterials) : null;
            var sight = panorama.Objects.Entities.First(animal => animal.EntityId == prey.Id);
            if (sight.Distance <= HuntApproachReach) return new(Hunt, Bird: prey.Id);
            var approach = Approach(body, sight.Position, HuntApproachReach - 1f, "approach_food_bird", permitted);
            return approach is not null ? approach with { Bird = prey.Id } : null;
        }

        if (oven is not null || Stock(player, Furnace) > 0)
        {
            bool fuel = Count(player.ComponentMiner.Inventory, value => FuelSuitable(value, 1f, 6.67f)) > 0
                || oven is not null && (oven.HeatLevel >= 1f && oven.FireTimeRemaining > 0f
                    || oven.GetSlotCount(oven.FuelSlotIndex) > 0 && FuelSuitable(oven.GetSlotValue(oven.FuelSlotIndex), 1f, 6.67f));
            return fuel ? new(Cook) : CollectMaterial(player, panorama, Wood, permitted, triedMaterials);
        }
        if (ApproachStation(visibleOven, body, "return_to_furnace", permitted) is { } returnOven) return returnOven;
        var visibleTable = SeenStation(panorama, stations, CraftingTableBlock.Index, ProjectReach);
        var table = visibleTable is { Distance: <= StationReach }
            ? entities.GetBlockEntity(visibleTable.Cell)?.Entity.FindComponent<ComponentCraftingTable>() : null;
        int stagedStone = table is null ? 0 : Count(table, value => Terrain.ExtractContents(value) == CobblestoneBlock.Index,
            table.ResultSlotIndex);
        if (Stock(player, Stone) + stagedStone < 8) return CollectMaterial(player, panorama, Stone, permitted, triedMaterials);
        if (table is not null || Stock(player, Workbench) > 0) return new(Furnace);
        return ApproachStation(visibleTable, body, "return_to_workbench", permitted)
            ?? (HasBoardsForTable(player) ? new(Workbench) : CollectMaterial(player, panorama, Wood, permitted, triedMaterials));
    }

    private static ProjectStep? NextSpearStep(ComponentPlayer player, ActorPanorama panorama, Dictionary<int, Point3> stations,
        Func<Vector3, bool>? permitted, IReadOnlySet<Point3>? triedMaterials)
    {
        var body = player.ComponentBody.Position;
        var table = SeenStation(panorama, stations, CraftingTableBlock.Index, ProjectReach);
        int boards = IngredientCount(player, "planks") + 4 * IngredientCount(player, "wood");
        int needed = HasIngredient(player, "stick", 2) ? 3 : 5;
        if (table is { Distance: <= StationReach } || Stock(player, Workbench) > 0)
            return boards >= needed ? new(Spear) : CollectMaterial(player, panorama, Wood, permitted, triedMaterials);
        return ApproachStation(table, body, "return_to_workbench", permitted)
            ?? (HasBoardsForTable(player) ? new(Workbench) : CollectMaterial(player, panorama, Wood, permitted, triedMaterials));
    }

    private static bool HasBoardsForTable(ComponentPlayer player) =>
        HasIngredient(player, "planks", 4) || HasIngredient(player, "wood", 1);

    private static ProjectStep? CollectMaterial(ComponentPlayer player, ActorPanorama panorama, string task,
        Func<Vector3, bool>? permitted, IReadOnlySet<Point3>? triedMaterials)
    {
        var origin = player.ComponentBody.Position;
        if (FindMaterial(panorama, origin, task) is not null) return new(task);
        var ground = panorama.Blocks.Where(block => block.BareTop && PlaySurvivalSenses.IsDryTerrain(block.Value))
            .ToLookup(block => (block.Cell.X, block.Cell.Z));
        foreach (var material in panorama.Blocks.Where(block => IsMaterial(block.Value, task)
            && MathF.Abs(block.Cell.Y - origin.Y) <= 4f && triedMaterials?.Contains(block.Cell) != true).OrderBy(block => block.Distance))
        {
            var cell = material.Cell;
            // 先证明材料旁有可以站着挖的位置。悬在头顶的树干不会反复生成一格内的靠近任务。
            var stances = from dx in NeighborOffsets
                          from dz in NeighborOffsets
                          where dx != 0 || dz != 0
                          from support in ground[(cell.X + dx, cell.Z + dz)]
                          let feet = support.Cell.Y + 1
                          where feet >= cell.Y - 1 && feet <= cell.Y && MathF.Abs(feet - origin.Y) <= 3f
                          let stand = new Vector3(support.Cell.X + 0.5f, feet, support.Cell.Z + 0.5f)
                          let distance = (stand - origin).XZ.LengthSquared()
                          where distance is >= 0.25f and <= ProjectReach * ProjectReach
                          orderby (stand - origin).LengthSquared()
                          select stand;
            foreach (var stand in stances)
                if (permitted?.Invoke(stand) != false)
                    return new(task == Wood ? "approach_wood" : "approach_stone", stand, cell);
        }
        return null;
    }

    private static readonly int[] NeighborOffsets = [-1, 0, 1];

    private static ProjectStep? ApproachStation(PanoramicBlock? station, Vector3 origin, string task, Func<Vector3, bool>? permitted) =>
        station is { Distance: > StationReach } ? Approach(origin, Center(station.Cell), 2.5f, task, permitted) : null;

    private static ProjectStep? Approach(Vector3 origin, Vector3 target, float reach, string task, Func<Vector3, bool>? permitted)
    {
        var offset = (target - origin).XZ;
        float distance = offset.Length();
        if (distance < reach + 0.45f || distance > ProjectReach) return null;
        var end = origin + (1f - reach / distance) * new Vector3(offset.X, 0f, offset.Y);
        return permitted?.Invoke(end) == false ? null : new(task, end);
    }
}
