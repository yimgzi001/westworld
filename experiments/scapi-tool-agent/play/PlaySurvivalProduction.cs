#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Decisions.Local;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

internal sealed partial class PlaySurvivalProduction
{
    public const string Tool = "prepare_survival_supplies";
    public const string Wood = "collect_wood", Stone = "collect_stone", Workbench = "craft_workbench", Fire = "craft_campfire";
    public const string Furnace = "craft_furnace", Cook = "cook_meat", Warm = "place_campfire";
    public const string Spear = "craft_wooden_spear", Hunt = "hunt_food_bird";
    public const string CookedFoodProject = "secure_cooked_food", SpearProject = "equip_hunting_gear";
    private static readonly JsonSerializerOptions RecipeJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private enum Phase { Start, Dig, PickUp, PlaceStation, AimStation, Craft, Cook, Hunt }
    private readonly ComponentPlayer player;
    private readonly AtomicActions owner;
    private readonly NativeToolSession tools;
    private readonly Dictionary<int, Point3> stations;
    private readonly Action<object> log;
    private readonly Func<LocalObservationWindow> readDryWindow;
    private readonly Func<Vector3, double, bool>? permitLandLeg;
    private readonly int? selectedBird;
    private readonly double deadline;
    private readonly int before;
    private Phase phase;
    private PanoramicBlock? material;
    private AtomicActionRun? action;
    private Guid? craftTask;
    private int craftValue, craftBefore, craftCount;
    private int digRetries;
    private PlaySurvivalTravel? materialPickupRoute;
    private bool approachedMaterialDrop;
    private readonly HashSet<Point3> missedMaterials = new();
    private double collectUntil, nextPoll;

    public PlaySurvivalProduction(ComponentPlayer player, AtomicActions owner, NativeToolSession tools, string task,
        double gameTime, Action<object> log, Dictionary<int, Point3> stations, Func<LocalObservationWindow> readDryWindow,
        Func<Vector3, double, bool>? permitLandLeg = null, int? selectedBird = null)
    {
        this.player = player;
        this.owner = owner;
        this.tools = tools;
        this.log = log;
        this.stations = stations;
        this.readDryWindow = readDryWindow;
        this.permitLandLeg = permitLandLeg;
        this.selectedBird = selectedBird;
        Task = task;
        before = Stock(player, task);
        deadline = gameTime + (IsProject(task) ? 180 : 36);
        log(new { kind = "play_production_started", actor = player.PlayerData.Name, task, before, gameTime });
    }

    public string Task { get; }
    public string? Status { get; private set; }
    public string? Reason { get; private set; }
    public int Added => Math.Max(0, Stock(player, Task) - before);
    public string? CurrentStage { get; private set; }

    public static ToolOffer? Offer(ComponentPlayer player, ActorPanorama? panorama, Dictionary<int, Point3> stations,
        bool routineWork, bool allowProjects = true, Func<Vector3, bool>? permitted = null)
    {
        bool woodNearby = FindMaterial(panorama, player.ComponentBody.Position, Wood) is not null;
        var available = new List<string>();
        if (routineWork)
        {
            if (woodNearby && Stock(player, Wood) < 3) available.Add(Wood);
            if (FindMaterial(panorama, player.ComponentBody.Position, Stone) is not null && Stock(player, Stone) < 8) available.Add(Stone);
            bool hasBoards = HasIngredient(player, "wood", 1) || HasIngredient(player, "planks", 4);
            bool hasSticks = hasBoards || HasIngredient(player, "planks", 2) || HasIngredient(player, "stick", 4);
            if ((woodNearby || hasBoards) && Stock(player, Workbench) == 0
                && !stations.ContainsKey(CraftingTableBlock.Index)) available.Add(Workbench);
            if ((woodNearby || hasSticks) && Stock(player, Fire) == 0 && player.ComponentVitalStats.Temperature < 8f) available.Add(Fire);
            bool hasTable = Stock(player, Workbench) > 0 || SeenStation(panorama, stations, CraftingTableBlock.Index) is not null;
            int boards = IngredientCount(player, "planks") + 4 * IngredientCount(player, "wood");
            int boardsNeeded = HasIngredient(player, "stick", 2) ? 3 : 5;
            if (Stock(player, Spear) == 0 && hasTable && boards >= boardsNeeded) available.Add(Spear);
            if (CanReadyHuntWeapon(player) && Stock(player, Hunt) < 2 && panorama is { } sight
                && FindFoodBird(player, sight.Objects.Entities, HuntApproachReach) is not null) available.Add(Hunt);
        }
        AddStationOffers(player, panorama, stations, available);
        if (allowProjects && panorama is not null)
        {
            if (NextProjectStep(player, CookedFoodProject, panorama, stations, permitted) is not null) available.Add(CookedFoodProject);
            if (routineWork && Stock(player, Spear) == 0
                && NextProjectStep(player, SpearProject, panorama, stations, permitted) is not null) available.Add(SpearProject);
        }
        if (available.Count == 0) return null;
        return new(Tool, "Collect a reachable log or stone, craft survival gear, or cook carried raw meat at a furnace. "
            + "Hunt a visible nonaggressive food bird with a spear. Walk on verified dry ground to reach it, then throw or strike in range. "
            + "Choose secure_cooked_food for a continuous job: obtain meat, gather stone and fuel, build or reuse a furnace, and retrieve cooked food. "
            + "Choose equip_hunting_gear to gather wood and make a spear. Native inventory and current sight are checked before each stage.",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { task = new { type = "string", @enum = available.ToArray() } },
                required = new[] { "task" },
                additionalProperties = false
            }));
    }

    public static bool IsMaterial(int value, string task) => task == Wood
        ? BlocksManager.Blocks[Terrain.ExtractContents(value)] is WoodBlock
        : Terrain.ExtractContents(value) == GraniteBlock.Index;

    private static PanoramicBlock? FindMaterial(ActorPanorama? panorama, Vector3 position, string task,
        IReadOnlySet<Point3>? missed = null) =>
        panorama?.Blocks.Where(block => IsMaterial(block.Value, task) && block.Distance <= 2.9f
                && missed?.Contains(block.Cell) != true
                && block.Cell.Y >= Terrain.ToCell(position.Y) && block.Cell.Y <= Terrain.ToCell(position.Y) + 1
                && (new Vector2(block.Cell.X + 0.5f, block.Cell.Z + 0.5f) - position.XZ).LengthSquared() <= 1.7f * 1.7f)
            .MinBy(block => block.Distance);

    public static int Stock(ComponentPlayer player, string task) => Count(player.ComponentMiner.Inventory, value => task switch
    {
        Wood => IsMaterial(value, Wood),
        Stone => Terrain.ExtractContents(value) == CobblestoneBlock.Index,
        Workbench => Terrain.ExtractContents(value) == CraftingTableBlock.Index,
        Fire => Terrain.ExtractContents(value) == CampfireBlock.Index,
        Furnace => Terrain.ExtractContents(value) == FurnaceBlock.Index,
        Cook or CookedFoodProject => IsCookedMeat(value),
        Spear or SpearProject => IsSpear(value),
        Hunt => PlaySurvivalFood.IsRawMeat(value),
        _ => false
    });

    private static bool HasIngredient(ComponentPlayer player, string craftingId, int required) =>
        IngredientCount(player, craftingId) >= required;

    private static int IngredientCount(ComponentPlayer player, string craftingId)
    {
        bool Matches(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)]?.GetCraftingId(value) == craftingId;
        var grid = player.Entity.FindComponent<ComponentCraftingTable>();
        int carried = Count(player.ComponentMiner.Inventory, Matches);
        return carried + (grid is null ? 0 : Count(grid, Matches, grid.ResultSlotIndex));
    }

    private static int Count(IInventory inventory, Func<int, bool> matches, int? slotLimit = null) =>
        Enumerable.Range(0, slotLimit ?? inventory.SlotsCount).Where(slot => inventory.GetSlotCount(slot) > 0
            && matches(inventory.GetSlotValue(slot))).Sum(inventory.GetSlotCount);

    public void Update(double gameTime)
    {
        if (Status is not null) return;
        if (IsProject(Task))
        {
            AdvanceProject(gameTime);
            return;
        }
        if (gameTime >= deadline || player.ComponentSleep.IsSleeping
            || huntApproach is null && materialPickupRoute is null && !PlaySurvivalSenses.OnDryGround(player.ComponentBody))
        {
            Finish(false, "supply_task_interrupted");
            return;
        }
        if (action is { } native)
        {
            if (native.Result.Status == AtomicActionStatus.Running) return;
            action = null;
            if (native.Result.Status != AtomicActionStatus.Succeeded)
            {
                if (Task == Hunt && native.Action is PickUp && native.Result.Failure == AtomicActionFailure.OutOfReach)
                {
                    if (!TryApproachPreyDrop(gameTime)) Finish(false, "prey_meat_out_of_reach");
                    return;
                }
                if (native.Action is Dig && native.Result.Failure == AtomicActionFailure.TargetChanged && digRetries++ == 0)
                {
                    phase = Phase.Dig;
                    return;
                }
                Finish(false, $"{native.Action.GetType().Name}_{native.Result.Failure}");
                return;
            }
            if (native.Action is Dig) collectUntil = gameTime + 5;
            if (native.Action is Place && !ConfirmStationPlacement(gameTime)) return;
        }

        switch (phase)
        {
            case Phase.Start:
                if (Task is Workbench or Fire) { phase = Phase.Craft; break; }
                if (Task is Furnace or Cook or Warm or Spear) { StartStation(); break; }
                if (Task == Hunt) { StartHunt(gameTime); break; }
                material = FindMaterial(owner.Queries.ReadPanorama(3.2f), player.ComponentBody.Position, Task, missedMaterials);
                if (material is null) { Finish(false, "no_reachable_material"); break; }
                action = owner.Start(new FaceTowards(material.Point, 3f));
                phase = Phase.Dig;
                break;
            case Phase.Dig:
                var focus = owner.Queries.LookAhead(4);
                if (focus is not { Kind: FocusKind.Block, Cell: Point3 cell, Face: int face, BlockValue: int value }
                    || cell != material!.Cell || !IsMaterial(value, Task))
                {
                    if (missedMaterials.Add(material!.Cell) && missedMaterials.Count < 3) phase = Phase.Start;
                    else Finish(false, "material_no_longer_visible");
                    break;
                }
                float seconds = player.ComponentMiner.CalculateDigTime(value, player.ComponentMiner.ActiveBlockValue);
                action = owner.Start(new Dig(new BlockTarget(cell, value, face), Math.Clamp(seconds + 2, 3, 25)));
                phase = Phase.PickUp;
                break;
            case Phase.PickUp:
                AdvanceMaterialPickup(gameTime);
                break;
            case Phase.Craft:
                AdvanceCraft(gameTime);
                break;
            case Phase.PlaceStation:
                PlaceStation();
                break;
            case Phase.AimStation:
                AimStation(Center(stationCell));
                break;
            case Phase.Cook:
                AdvanceCook(gameTime);
                break;
            case Phase.Hunt:
                AdvanceHunt(gameTime);
                break;
        }
    }

    private void AdvanceMaterialPickup(double gameTime)
    {
        if (Added > 0) { Finish(true, "material_acquired"); return; }
        if (materialPickupRoute is { } route)
        {
            route.Update(gameTime);
            if (route.Status is null) return;
            materialPickupRoute = null;
            if (route.Status != "succeeded" && !PlaySurvivalSenses.OnDryGround(player.ComponentBody))
            {
                Finish(false, "material_drop_route_blocked");
                return;
            }
        }
        if (gameTime >= collectUntil) { Finish(false, "material_drop_not_acquired"); return; }
        if (gameTime < nextPoll) return;
        nextPoll = gameTime + 0.25;
        var drop = owner.Queries.FindVisibleObjectsAround(3.2f).Pickables.FirstOrDefault(item => Task == Wood
            ? IsMaterial(item.Value, Wood) : Terrain.ExtractContents(item.Value) == CobblestoneBlock.Index);
        if (drop is null) return;
        var pickup = owner.Start(drop.CreatePickup(1));
        if (pickup.Result.Failure is not (AtomicActionFailure.OutOfReach or AtomicActionFailure.Blocked)) return;
        if (approachedMaterialDrop)
        {
            Finish(false, "material_drop_unreachable_after_approach");
            return;
        }

        var body = player.ComponentBody;
        var offset = drop.Position - body.Position;
        float distance = offset.XZ.Length();
        if (distance < 1f || offset.Y is < -1.05f or > 1.9f || permitLandLeg?.Invoke(drop.Position, gameTime) == false) return;
        // 挖掘成功后掉落可能弹开。只试一次有落脚证据的短程接近，库存增加才完成采集。
        approachedMaterialDrop = true;
        collectUntil = gameTime + 7;
        float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset.XZ));
        materialPickupRoute = new(player, owner, bearing, distance - 0.55f, "work", false, gameTime,
            () => (JsonSerializer.SerializeToElement(new { }), []), readDryWindow, log, permitLandLeg: permitLandLeg);
        log(new { kind = "play_production_drop_approach", actor = player.PlayerData.Name, task = Task, distance, gameTime });
    }

    private void AdvanceCraft(double gameTime)
    {
        if (craftTask is Guid pending)
        {
            if (gameTime < nextPoll) return;
            nextPoll = gameTime + 0.12;
            var receipt = tools.Call("task_status", new { });
            if (receipt.State != "returned" || !receipt.Data.TryGetProperty("taskId", out var id) || id.GetGuid() != pending)
            {
                Finish(false, "native_craft_task_unavailable");
                return;
            }
            string? status = receipt.Data.GetProperty("status").GetString();
            if (status is "waiting" or "running" or "blocked") return;
            craftTask = null;
            if (status != "succeeded" || CountExact(craftValue) < craftBefore + craftCount)
            {
                Finish(false, "native_craft_effect_not_confirmed");
                return;
            }
            log(new { kind = "play_production_crafted", actor = player.PlayerData.Name, task = Task, value = craftValue, count = craftCount, gameTime });
        }
        if (Added > 0) { Finish(true, "native_recipe_completed"); return; }

        int target = Task switch
        {
            Workbench => CraftingTableBlock.Index, Fire => CampfireBlock.Index,
            Spear => WoodenSpearBlock.Index, _ => FurnaceBlock.Index
        };
        var observed = tools.Call("observe", new { block_limit = 1, limit = 1 });
        var refs = observed.State == "returned" ? NativeProductionTools.WorkspaceReferences(observed.Data) : default;
        bool usesWorkbench = Task is Furnace or Spear;
        string? workspace = usesWorkbench ? refs.Visible : refs.Own;
        var grid = usesWorkbench ? ObservedStation<ComponentCraftingTable>() : player.Entity.FindComponent<ComponentCraftingTable>();
        if (workspace is null || grid is null)
        {
            Finish(false, "crafting_station_unavailable");
            return;
        }
        if (TryCraft(workspace, grid, target) || Task == Fire && TryCraft(workspace, grid, StickBlock.Index)
            || Task == Spear && !HasIngredient(player, "stick", 2) && TryCraft(workspace, grid, StickBlock.Index)
            || (Task is Workbench or Fire or Spear) && TryCraft(workspace, grid, PlanksBlock.Index)) return;
        Finish(false, "recipe_materials_unavailable");
    }

    private bool TryCraft(string workspace, ComponentCraftingTable grid, int value)
    {
        var recipes = tools.Call("find_recipes", new { result_value = value, limit = 8 });
        if (recipes.State != "returned") return false;
        foreach (var entry in recipes.Data.EnumerateArray())
        {
            var recipe = entry.GetProperty("recipe").Deserialize<RecipeGuideEntry>(RecipeJson);
            if (recipe is null || value == PlanksBlock.Index && !recipe.Ingredients.Any(ingredient => ingredient.CraftingId == "wood")) continue;
            int count = recipe.ResultCount;
            // 原生任务拒绝后需要核对状态。先无副作用地验证材料，避免把预料内的缺料当作写失败。
            if (RecipeTaskComposer.Compose(owner, player.ComponentMiner.Inventory, grid, recipe, count) is null) continue;
            int stock = CountExact(value);
            var plan = tools.Call("plan_craft", new { workspace, recipe = entry.GetProperty("reference").GetString(), count });
            if (plan.State != "returned" || !plan.Data.TryGetProperty("taskId", out var id) || !id.TryGetGuid(out var taskId))
            {
                Finish(false, plan.Error ?? "native_craft_admission_unknown");
                return true;
            }
            craftTask = taskId;
            craftValue = value;
            craftCount = count;
            craftBefore = stock;
            return true;
        }
        return false;
    }

    private int CountExact(int value) => NativeAcquisitionAudit.InventoryCount(player.ComponentMiner.Inventory, value) ?? 0;

    public void Stop()
    {
        projectStep?.Stop();
        projectStep = null;
        projectRoute?.Stop();
        projectRoute = null;
        if (IsProject(Task)) return;
        materialPickupRoute?.Stop();
        materialPickupRoute = null;
        StopHunt();
        if (action?.Result.Status == AtomicActionStatus.Running) owner.Cancel(action);
        action = null;
        if (craftTask.HasValue || tools.RequiresReconciliation.HasValue) tools.Call("cancel_task", new { });
        craftTask = null;
    }

    private void Finish(bool success, string reason)
    {
        Stop();
        Status = success ? "succeeded" : "failed";
        Reason = reason;
    }
}
