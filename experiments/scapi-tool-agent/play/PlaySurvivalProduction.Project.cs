#nullable enable

using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;

internal sealed partial class PlaySurvivalProduction
{
    private const float ProjectReach = 40f;
    private const int ProjectStageLimit = 24;
    private sealed record ProjectStep(string Task, Vector3? Approach = null);
    private PlaySurvivalProduction? projectStep;
    private PlaySurvivalTravel? projectRoute;
    private int projectStages;

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
                Finish(false, route.Reason ?? "project_route_failed");
                return;
            }
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
        var next = NextProjectStep(player, Task, owner.Queries.ReadPanorama(ProjectReach), stations, Permitted);
        if (next is null)
        {
            Finish(false, "project_missing_visible_prerequisite");
            return;
        }
        projectStages++;
        CurrentStage = next.Task;
        log(new { kind = "play_production_stage", actor = player.PlayerData.Name, project = Task, stage = next.Task,
            number = projectStages, destination = next.Approach?.ToString(), gameTime });
        if (next.Approach is { } destination)
        {
            var body = player.ComponentBody;
            var delta = (destination - body.Position).XZ;
            float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, delta));
            projectRoute = new(player, owner, bearing, delta.Length(), "work", false, gameTime,
                () => (JsonSerializer.SerializeToElement(new { }), []), readDryWindow, log, permitLandLeg: permitLandLeg);
            return;
        }
        projectStep = new(player, owner, tools, next.Task, gameTime, log, stations, readDryWindow, permitLandLeg);
    }

    private static ProjectStep? NextProjectStep(ComponentPlayer player, string task, ActorPanorama? panorama,
        Dictionary<int, Point3> stations, Func<Vector3, bool>? permitted)
    {
        if (panorama is null) return null;
        if (task == SpearProject) return NextSpearStep(player, panorama, stations, permitted);

        var body = player.ComponentBody.Position;
        var entities = player.Project.FindSubsystem<SubsystemBlockEntities>(true);
        var visibleOven = SeenStation(panorama, stations, FurnaceBlock.Index, ProjectReach);
        var oven = visibleOven is { Distance: <= 4.5f }
            ? entities.GetBlockEntity(visibleOven.Cell)?.Entity.FindComponent<ComponentFurnace>() : null;
        bool rawInOven = oven is not null && oven.GetSlotCount(0) > 0 && PlaySurvivalFood.IsRawMeat(oven.GetSlotValue(0));
        bool cookedInOven = oven is not null && oven.GetSlotCount(oven.ResultSlotIndex) > 0
            && IsCookedMeat(oven.GetSlotValue(oven.ResultSlotIndex));
        if (cookedInOven) return new(Cook);
        if (Stock(player, Hunt) == 0 && !rawInOven)
        {
            var prey = FindFoodBird(player, panorama.Objects.Entities.Where(animal => permitted?.Invoke(animal.Position) != false),
                ProjectReach);
            if (prey is null)
                return ApproachStation(visibleOven, body, "return_to_furnace", permitted);
            if (!CanReadyHuntWeapon(player))
                return Stock(player, Spear) == 0 ? NextSpearStep(player, panorama, stations, permitted) : null;
            var sight = panorama.Objects.Entities.First(animal => animal.EntityId == prey.Id);
            return sight.Distance <= HuntApproachReach ? new(Hunt)
                : Approach(body, sight.Position, HuntApproachReach - 1f, "approach_food_bird", permitted);
        }

        if (oven is not null || Stock(player, Furnace) > 0)
        {
            bool fuel = Count(player.ComponentMiner.Inventory, value => FuelSuitable(value, 1f, 6.67f)) > 0
                || oven is not null && (oven.HeatLevel >= 1f && oven.FireTimeRemaining > 0f
                    || oven.GetSlotCount(oven.FuelSlotIndex) > 0 && FuelSuitable(oven.GetSlotValue(oven.FuelSlotIndex), 1f, 6.67f));
            return fuel ? new(Cook) : CollectMaterial(player, panorama, Wood, permitted);
        }
        if (ApproachStation(visibleOven, body, "return_to_furnace", permitted) is { } returnOven) return returnOven;
        var visibleTable = SeenStation(panorama, stations, CraftingTableBlock.Index, ProjectReach);
        var table = visibleTable is { Distance: <= 4.5f }
            ? entities.GetBlockEntity(visibleTable.Cell)?.Entity.FindComponent<ComponentCraftingTable>() : null;
        int stagedStone = table is null ? 0 : Count(table, value => Terrain.ExtractContents(value) == CobblestoneBlock.Index,
            table.ResultSlotIndex);
        if (Stock(player, Stone) + stagedStone < 8) return CollectMaterial(player, panorama, Stone, permitted);
        if (table is not null || Stock(player, Workbench) > 0) return new(Furnace);
        return ApproachStation(visibleTable, body, "return_to_workbench", permitted)
            ?? (HasBoardsForTable(player) ? new(Workbench) : CollectMaterial(player, panorama, Wood, permitted));
    }

    private static ProjectStep? NextSpearStep(ComponentPlayer player, ActorPanorama panorama, Dictionary<int, Point3> stations,
        Func<Vector3, bool>? permitted)
    {
        var body = player.ComponentBody.Position;
        var table = SeenStation(panorama, stations, CraftingTableBlock.Index, ProjectReach);
        int boards = IngredientCount(player, "planks") + 4 * IngredientCount(player, "wood");
        int needed = HasIngredient(player, "stick", 2) ? 3 : 5;
        if (table is { Distance: <= 4.5f } || Stock(player, Workbench) > 0)
            return boards >= needed ? new(Spear) : CollectMaterial(player, panorama, Wood, permitted);
        return ApproachStation(table, body, "return_to_workbench", permitted)
            ?? (HasBoardsForTable(player) ? new(Workbench) : CollectMaterial(player, panorama, Wood, permitted));
    }

    private static bool HasBoardsForTable(ComponentPlayer player) =>
        HasIngredient(player, "planks", 4) || HasIngredient(player, "wood", 1);

    private static ProjectStep? CollectMaterial(ComponentPlayer player, ActorPanorama panorama, string task,
        Func<Vector3, bool>? permitted)
    {
        var origin = player.ComponentBody.Position;
        if (FindMaterial(panorama, origin, task) is not null) return new(task);
        foreach (var block in panorama.Blocks.Where(block => IsMaterial(block.Value, task)
            && MathF.Abs(block.Cell.Y - origin.Y) <= 4f).OrderBy(block => block.Distance))
        {
            var point = Center(block.Cell);
            if (Approach(origin, point, 1.25f, task == Wood ? "approach_wood" : "approach_stone", permitted) is { } step)
                return step;
        }
        return null;
    }

    private static ProjectStep? ApproachStation(PanoramicBlock? station, Vector3 origin, string task, Func<Vector3, bool>? permitted) =>
        station is { Distance: > 4.5f } ? Approach(origin, Center(station.Cell), 2.5f, task, permitted) : null;

    private static ProjectStep? Approach(Vector3 origin, Vector3 target, float reach, string task, Func<Vector3, bool>? permitted)
    {
        var offset = (target - origin).XZ;
        float distance = offset.Length();
        if (distance < reach + 0.45f || distance > ProjectReach) return null;
        var end = origin + (1f - reach / distance) * new Vector3(offset.X, 0f, offset.Y);
        return permitted?.Invoke(end) == false ? null : new(task, end);
    }
}
