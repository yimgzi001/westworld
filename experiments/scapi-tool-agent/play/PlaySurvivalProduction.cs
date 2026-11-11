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
    public const string WarmthProject = "secure_warmth";
    public const string Axe = "craft_stone_axe";
    public const string WoodSupply = "collect_three_wood", StoneSupply = "collect_eight_cobblestone";
    public const string StoneSample = "collect_one_cobblestone";
    private static readonly JsonSerializerOptions RecipeJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private enum Phase { Start, Dig, PickUp, PlaceStation, AimStation, Craft, Cook, Hunt, Farm, Camp, Worksite, ClearStationSight, TendFire }
    private readonly ComponentPlayer player;
    private readonly AtomicActions owner;
    private readonly NativeToolSession tools;
    private readonly Dictionary<int, Point3> stations;
    private readonly Action<object> log;
    private readonly Func<LocalObservationWindow> readDryWindow;
    private readonly Func<Vector3, double, bool>? permitLandLeg;
    private readonly int? selectedBird;
    private readonly bool resumeGear;
    private readonly double deadline;
    private readonly int before;
    private readonly bool startedAtNight;
    private readonly Point3 materialEntryFooting;
    private Phase phase;
    private PanoramicBlock? material;
    private AtomicActionRun? action;
    private Guid? craftTask;
    private int craftValue, craftBefore, craftCount;
    private int digRetries, staleMaterialCandidates;
    private PlaySurvivalTravel? materialPickupRoute;
    private bool approachedMaterialDrop;
    private readonly HashSet<Point3> missedMaterials = new();
    private HashSet<Guid> missedMaterialDrops = new();
    private Guid? lastMaterialDrop;
    private double collectUntil, nextPoll;
    private double? groundContactMissingSince;

    public PlaySurvivalProduction(ComponentPlayer player, AtomicActions owner, NativeToolSession tools, string task,
        double gameTime, Action<object> log, Dictionary<int, Point3> stations, Func<LocalObservationWindow> readDryWindow,
        Func<Vector3, double, bool>? permitLandLeg = null, int? selectedBird = null, bool resumeGear = false,
        Dictionary<Point3, Patch>? ownFarm = null, CampSite? camp = null, QuarrySite? quarrySite = null,
        IReadOnlySet<Point3>? preserveGround = null, NearbyPickableView? selectedPickup = null)
    {
        this.player = player;
        this.owner = owner;
        this.tools = tools;
        this.log = log;
        this.stations = stations;
        this.readDryWindow = readDryWindow;
        this.permitLandLeg = permitLandLeg;
        this.selectedBird = selectedBird;
        this.resumeGear = resumeGear;
        this.ownFarm = ownFarm;
        this.camp = camp;
        this.quarrySite = quarrySite;
        this.preserveGround = preserveGround;
        if (preserveGround is not null) missedMaterials.UnionWith(preserveGround);
        if (camp is not null)
            for (int x = camp.X; x <= camp.X + 3; x++)
            for (int z = camp.Z; z <= camp.Z + 3; z++)
            for (int y = camp.Y - 1; y <= camp.Y + 2; y++) missedMaterials.Add(new(x, y, z));
        triedMaterialApproaches.UnionWith(missedMaterials);
        Task = task;
        if (task == RecoverSpear && selectedPickup is not null) BindEquipment(selectedPickup);
        before = Stock(player, task);
        materialEntryFooting = Terrain.ToCell(player.ComponentBody.Position) - Up;
        startedAtNight = PlaySurvivalRest.NightCoverDue(player.Project.FindSubsystem<SubsystemTimeOfDay>(true));
        if (task is StoneSample or StoneSupply or EarthSupply)
            triedMaterialApproaches.Add(materialEntryFooting);
        deadline = gameTime + (task == RecoverSpear ? 90 : task is Quarry or VisitQuarry or BuildCamp or ReturnCamp or LeaveCamp or VisitTable or VisitFurnace or VisitFire
            or Furnace or Spear or Rake or Axe or Cook || IsProject(task) ? 180 : 36);
        log(new { kind = "play_production_started", actor = player.PlayerData.Name, task, before, gameTime });
    }

    public string Task { get; }
    public string? Status { get; private set; }
    public string? Reason { get; private set; }
    public int Added => Task is VisitTable or VisitFurnace or VisitFire ? worksiteReached ? 1 : 0
        : Task == VisitQuarry ? quarryVisited ? 1 : 0
        : Task == RecoverSpear ? equipmentRecovered
        : Task == TendFire ? fireFuelAdded
        : Task is BuildCamp or ReturnCamp or LeaveCamp ? campComplete ? 1 : 0
        : Task == PlantPumpkins ? plantedCount : Task == WarmthProject ? warmthReady ? 1 : 0 : Math.Max(0, Stock(player, Task) - before);
    public string? CurrentStage { get; private set; }
    public Vector3? ActiveLandEndpoint => projectRoute?.ActiveLandEndpoint ?? projectStep?.ActiveLandEndpoint
        ?? huntApproach?.ActiveLandEndpoint ?? materialPickupRoute?.ActiveLandEndpoint ?? campRoute?.ActiveLandEndpoint
        ?? quarryRoute?.ActiveLandEndpoint ?? quarryChild?.ActiveLandEndpoint ?? stationApproach?.ActiveLandEndpoint
        ?? equipmentRoute?.ActiveLandEndpoint ?? quarryDirectLeg?.End;

    public static ToolOffer? Offer(ComponentPlayer player, ActorPanorama? panorama, Dictionary<int, Point3> stations,
        bool routineWork, bool allowProjects = true, Func<Vector3, bool>? permitted = null)
    {
        var available = AvailableTasks(player, panorama, stations, routineWork, allowProjects, permitted);
        if (available.Length == 0) return null;
        return new(Tool, "Choose a survival production job. Each native stage checks local resources and actual inventory.",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { task = new { type = "string", @enum = available } },
                required = new[] { "task" },
                additionalProperties = false
            }));
    }

    public static string DescribeTask(string task) => task switch
    {
        CookedFoodProject => "备一份熟食。需要时先制矛猎取食用鸟，已有生肉就保留，备八块圆石和燃料再烧熟取出",
        SpearProject => "准备木矛，按需采木和做工作台，背包增加木矛才算完成",
        WarmthProject => "需要时先取木材，制作并点起营火，然后核对身体是否回暖",
        Fire => "做一个备用营火带在背包",
        Warm => "在已确认的地面生火并核对是否回暖",
        TendFire => "给仍有火苗的本人营火添加一份安全燃料，按实际消耗与火势增加核对",
        WarmAtFire => "在本人营火旁连续取暖晾干，达到安全体温与湿度后结束",
        Cook => "把现有生肉烧熟，并从熔炉取出",
        Hunt => "用木矛追猎一只看得见的食用鸟，取得生肉后保留",
        RecoverSpear => "沿已见地形回收这根可见投矛，核对本人实际入包，避免每次重新造矛",
        Wood => "取得可见的木材",
        Stone => "取得可见的圆石",
        WoodSupply => "连续采集三块木材，沿已见地形接近树干并逐块核对入包",
        StoneSupply => "连续取得八块圆石，材料够八块即完成",
        StoneSample => "走近已见石料并只取一块圆石，若拾取落进凹坑先带回出发地面，落稳后结束，便于先制石斧",
        Quarry => "从选定的已见土面浅挖阶梯找石，最多六格，每次下探先核对回跳净空，保留退路并带一块样本回地面；不保证地下有石头",
        VisitQuarry => "回本人已经发现石层的浅探入口，逐段核对地形，到达后重新观察石料",
        PumpkinSeeds => "用一只成熟南瓜按原生配方制三粒种子；消耗这只南瓜",
        PlantPumpkins => "在身边已见的向阳土面播种，最多三株；自带木耙时先耕地，苗不计作粮食",
        Rake => "在工作台制作木耙，用于改善南瓜种植土面",
        Axe => "在工作台制作石斧，用于更快采石和砍树",
        EarthSupply => "连续取得四十块土，避开脚下与已建营地的支撑；只采已经看到的土层",
        BuildCamp => "按选定地基建二乘二内部空间的简易土屋，封住入口并核对原生可安全睡眠",
        ReturnCamp => "沿已观察路线回自己的营地，进入后用土封闭入口",
        LeaveCamp => "挖开自己的营地入口，检查路线后回到屋外干地",
        VisitTable => "沿已见地形回到自己放置的工作台，靠近后重新确认工作台仍可用",
        VisitFurnace => "沿已见地形回到自己放置的熔炉，靠近后重新确认熔炉仍可用",
        VisitFire => "沿已见地形回到自己放置的营火，不假定它一直在燃烧",
        Workbench => "制作工作台",
        Furnace => "制作熔炉",
        _ => "在原生工作台制作木矛"
    };

    public static string Prerequisites(string task) => task switch
    {
        Workbench => "一块原木可制四块木板；随身合成工作台",
        Spear => "原生工作台、木板和木棒；自己已有的材料不必再采",
        Furnace => "已放置工作台和八块圆石",
        Cook => "熔炉、生肉和非爆炸燃料；熟肉必须取回背包",
        Hunt => "可见且掉肉的鸟、木矛、自己站在安全干地；命中伤害由原生结算",
        RecoverSpear => "本人已见的具体投矛掉落与安全路线；目标被遮住或消失则重新评估",
        Fire => "一块原木或两块木板或四根木棒",
        Warm => "自带营火和可见安全地面",
        TendFire => "先回到仍有火苗的营火；一份非爆炸燃料。已熄灭的灰烬不能靠添柴重新点燃",
        WarmAtFire => "先回自己的燃烧营火，必要时添柴；站在三格内安全距离，不踩火",
        StoneSample or StoneSupply or Stone => "本人已经见到的花岗岩及安全落脚位置",
        Quarry => "已见四格平整软土地面与净空；逐层观察，不读取地下地图；不可拆营地、农地或旧矿口退路",
        VisitQuarry => "本人已开挖且发现过石头的矿口，四十格内；记住矿口不等于地下资源仍然可采",
        PumpkinSeeds => "成熟南瓜一只；应先保留吃的南瓜，再考虑留种",
        PlantPumpkins => "背包南瓜种子、可见向阳土面；温暖季节生长较快，幼苗需等待自然成熟",
        Rake => "原生工作台、木板和木棒",
        Axe => "工作台、至少一块圆石或石块、两根木棒",
        EarthSupply or Earth => "已观察的土层和安全站位；不挖本人脚下及营地支撑",
        BuildCamp => "已确认四乘四平整地基、墙和顶共四十块土或木板",
        ReturnCamp => "已经亲自建成并记住的营地、当前可确认的返程路线、封门用土",
        LeaveCamp => "本人已建营地、安全屋外地面和可打开的入口",
        VisitTable or VisitFurnace or VisitFire => "本人记住且一百二十八格内的工作站；逐段重看地形与危险，路线未知时不能保证到达",
        _ => "本人已经见到的树干及安全落脚位置"
    };

    public static string[] PlanningTasks(ComponentPlayer player, ActorPanorama panorama, Dictionary<int, Point3> stations,
        Func<Vector3, bool> permitted)
    {
        var tasks = AvailableTasks(player, panorama, stations, true, false, permitted).ToList();
        bool wood = IngredientCount(player, "wood") + IngredientCount(player, "planks") > 0
            || CollectMaterial(player, panorama, Wood, permitted, null) is not null;
        bool stone = Stock(player, Stone) > 0 || CollectMaterial(player, panorama, Stone, permitted, null) is not null;
        if (wood)
        {
            tasks.Add(Workbench);
            tasks.Add(Spear);
            tasks.Add(Fire);
            tasks.Add(Warm);
            tasks.Add(Rake);
            if (CollectMaterial(player, panorama, Wood, permitted, null) is not null) tasks.Add(WoodSupply);
        }
        if (stone) { tasks.Add(StoneSupply); tasks.Add(Furnace); }
        if (wood || stone) tasks.Add(Axe);
        if (stations.TryGetValue(CampfireBlock.Index, out var fireSite)
            && (Center(fireSite) - player.ComponentBody.Position).XZ.Length() <= ProjectReach && !CampfireSeenOut(panorama, stations))
        {
            tasks.Add(WarmAtFire);
            if (wood || Count(player.ComponentMiner.Inventory, CampfireFuel) > 0) tasks.Add(TendFire);
        }
        if (CollectMaterial(player, panorama, Earth, permitted, null) is not null) tasks.Add(EarthSupply);
        if (FindFoodBird(player, panorama.Objects.Entities, ProjectBirdReach) is not null) tasks.Add(Hunt);
        if (Stock(player, Hunt) > 0 || tasks.Contains(Hunt)) tasks.Add(Cook);
        if (Count(player.ComponentMiner.Inventory, IsMaturePumpkin) > 0) { tasks.Add(PumpkinSeeds); tasks.Add(PlantPumpkins); }
        if (Count(player.ComponentMiner.Inventory, IsPumpkinSeed) > 0) tasks.Add(PlantPumpkins);
        foreach (string visit in new[] { VisitTable, VisitFurnace, VisitFire })
            if (stations.TryGetValue(WorksiteBlock(visit), out var known)
                && (Center(known) - player.ComponentBody.Position).XZ.Length() <= KnownReturnReach
                && (visit != VisitFire || !CampfireSeenOut(panorama, stations))) tasks.Add(visit);
        return tasks.Where(task => task != WarmthProject).Distinct().ToArray();
    }

    public static string[] AvailableTasks(ComponentPlayer player, ActorPanorama? panorama, Dictionary<int, Point3> stations,
        bool routineWork, bool allowProjects, Func<Vector3, bool>? permitted)
    {
        bool woodNearby = FindMaterial(panorama, player.ComponentBody.Position, Wood) is not null;
        var available = new List<string>();
        foreach (string visit in new[] { VisitTable, VisitFurnace, VisitFire })
            if (stations.TryGetValue(WorksiteBlock(visit), out var known)
                && (Center(known) - player.ComponentBody.Position).XZ.Length() <= KnownReturnReach
                && (visit != VisitFire || !CampfireSeenOut(panorama, stations))) available.Add(visit);
        if (CanAdvanceProject(player, WoodSupply, panorama, stations, permitted)) available.Add(WoodSupply);
        if (Stock(player, Stone) == 0 && CanAdvanceProject(player, StoneSample, panorama, stations, permitted)) available.Add(StoneSample);
        if (Stock(player, Stone) < 8 && CanAdvanceProject(player, StoneSupply, panorama, stations, permitted)) available.Add(StoneSupply);
        if (routineWork)
        {
            if (woodNearby && Stock(player, Wood) < 3) available.Add(Wood);
            if (FindMaterial(panorama, player.ComponentBody.Position, Stone) is not null && Stock(player, Stone) < 8) available.Add(Stone);
            bool hasBoards = HasIngredient(player, "wood", 1) || HasIngredient(player, "planks", 4);
            if (hasBoards && Stock(player, Workbench) == 0) available.Add(Workbench);
            bool hasTable = Stock(player, Workbench) > 0 || SeenStation(panorama, stations, CraftingTableBlock.Index) is not null;
            int boards = IngredientCount(player, "planks") + 4 * IngredientCount(player, "wood");
            int boardsNeeded = HasIngredient(player, "stick", 2) ? 3 : 5;
            if (Stock(player, Spear) == 0 && hasTable && boards >= boardsNeeded) available.Add(Spear);
            if (CanReadyHuntWeapon(player) && Stock(player, Hunt) < 2 && panorama is { } sight
                && FindFoodBird(player, sight.Objects.Entities, HuntApproachReach) is not null) available.Add(Hunt);
        }
        if (Stock(player, Fire) == 0 && player.ComponentVitalStats.Temperature < 9f && CanCraftFireFromStock(player))
            available.Add(Fire);
        if (player.ComponentVitalStats.Temperature < 7f
            && CanAdvanceProject(player, WarmthProject, panorama, stations, permitted)) available.Add(WarmthProject);
        AddStationOffers(player, panorama, stations, available);
        if (allowProjects && panorama is not null)
        {
            if (NextProjectStep(player, CookedFoodProject, panorama, stations, permitted) is not null) available.Add(CookedFoodProject);
            if (routineWork && Stock(player, Spear) == 0
                && NextProjectStep(player, SpearProject, panorama, stations, permitted) is not null) available.Add(SpearProject);
        }
        return available.ToArray();
    }

    public static bool CanCraftFireFromStock(ComponentPlayer player) => HasIngredient(player, "wood", 1)
        || HasIngredient(player, "planks", 2) || HasIngredient(player, "stick", 4);

    public static bool IsMaterial(int value, string task) => task == Wood
        ? BlocksManager.Blocks[Terrain.ExtractContents(value)] is WoodBlock
        : task == Earth ? BlocksManager.Blocks[Terrain.ExtractContents(value)] is GrassBlock or DirtBlock
        : Terrain.ExtractContents(value) == GraniteBlock.Index;

    private static PanoramicBlock? FindMaterial(ActorPanorama? panorama, Vector3 position, string task,
        IReadOnlySet<Point3>? missed = null, CampSite? protectedCamp = null) =>
        panorama?.Blocks.Where(block => IsMaterial(block.Value, task) && block.Distance <= 2.9f
                && protectedCamp?.Protects(block.Cell) != true
                && missed?.Contains(block.Cell) != true
                && block.Cell.Y >= Terrain.ToCell(position.Y) - (task is Earth or Stone ? 1 : 0) && block.Cell.Y <= Terrain.ToCell(position.Y) + 1
                && (task is not (Earth or Stone) || block.Cell.X + 1 <= position.X - 0.4f || block.Cell.X >= position.X + 0.4f
                    || block.Cell.Z + 1 <= position.Z - 0.4f || block.Cell.Z >= position.Z + 0.4f)
                && (new Vector2(block.Cell.X + 0.5f, block.Cell.Z + 0.5f) - position.XZ).LengthSquared() <= 1.7f * 1.7f)
            .MinBy(block => block.Distance);

    public static int Stock(ComponentPlayer player, string task) => Count(player.ComponentMiner.Inventory, value => task switch
    {
        Wood or WoodSupply => IsMaterial(value, Wood),
        Stone or StoneSample or StoneSupply or Quarry => Terrain.ExtractContents(value) == CobblestoneBlock.Index,
        Workbench => Terrain.ExtractContents(value) == CraftingTableBlock.Index,
        Fire => Terrain.ExtractContents(value) == CampfireBlock.Index,
        Furnace => Terrain.ExtractContents(value) == FurnaceBlock.Index,
        Cook or CookedFoodProject => IsCookedMeat(value),
        Spear or SpearProject or RecoverSpear => IsSpear(value),
        Hunt => PlaySurvivalFood.IsRawMeat(value),
        PumpkinSeeds => IsPumpkinSeed(value),
        Rake => Terrain.ExtractContents(value) == WoodenRakeBlock.Index,
        Axe => Terrain.ExtractContents(value) == StoneAxeBlock.Index,
        Earth or EarthSupply => Terrain.ExtractContents(value) == DirtBlock.Index,
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
        if (Task == VisitQuarry) { AdvanceQuarryVisit(gameTime); return; }
        if (Task == RecoverSpear) { AdvanceEquipment(gameTime); return; }
        if (stationApproach is { } approach)
        {
            if (gameTime >= deadline || player.ComponentSleep.IsSleeping)
            { Finish(false, "station_approach_interrupted"); return; }
            approach.Update(gameTime);
            if (approach.Status is null) return;
            stationApproach = null;
            if (approach.Status != "succeeded") { Finish(false, approach.Reason ?? "own_station_approach_failed"); return; }
            phase = Phase.Start;
            return;
        }
        if (Task == Quarry) { AdvanceQuarry(gameTime); return; }
        if (IsProject(Task))
        {
            AdvanceProject(gameTime);
            return;
        }
        var support = player.ComponentBody;
        bool noGround = !campJumping && huntApproach is null && materialPickupRoute is null && campRoute is null
            && !PlaySurvivalSenses.OnDryGround(support);
        if (noGround && support.ImmersionDepth < 0.04f && support.ParentBody is null && support.StandingOnBody is null
            && MathF.Abs(support.Position.Y - MathF.Round(support.Position.Y)) < 0.08f && MathF.Abs(support.Velocity.Y) < 0.6f)
        {
            groundContactMissingSince ??= gameTime;
            if (gameTime - groundContactMissingSince < 0.4) return;
        }
        else groundContactMissingSince = null;
        if (gameTime >= deadline || player.ComponentSleep.IsSleeping || noGround)
        {
            log(new { kind = "play_production_support_lost", actor = player.PlayerData.Name, task = Task, phase = phase.ToString(),
                noGround, position = support.Position.ToString(), velocity = support.Velocity.ToString(),
                support.StandingOnValue, support.ImmersionDepth, gameTime });
            Finish(false, "supply_task_interrupted");
            return;
        }
        if (action is { } native)
        {
            if (native.Result.Status == AtomicActionStatus.Running) return;
            action = null;
            if (native.Result.Status != AtomicActionStatus.Succeeded)
            {
                if (RetryCampPlacement(native, gameTime)) return;
                if (native.Action is Place && phase == Phase.AimStation && placementFloor is { } refused
                    && native.Result.Failure is AtomicActionFailure.Blocked or AtomicActionFailure.TargetChanged
                    && refusedStationFloors.Count < 6)
                {
                    // 原生拒绝尚未落地的工作站时，换一个已见地面，避免下一轮又挑到同一遮挡点。
                    refusedStationFloors.Add(refused.Cell);
                    log(new { kind = "play_station_native_placement_rejected", actor = player.PlayerData.Name,
                        cell = refused.Cell.ToString(), failure = native.Result.Failure.ToString(), gameTime });
                    phase = Phase.Start;
                    return;
                }
                if (Task == Hunt && native.Action is PickUp && native.Result.Failure == AtomicActionFailure.OutOfReach)
                {
                    if (!TryApproachPreyDrop(gameTime)) Finish(false, "prey_meat_out_of_reach");
                    return;
                }
                if (phase == Phase.ClearStationSight && native.Action is Dig && native.Result.Failure == AtomicActionFailure.TargetChanged)
                    return;
                if (native.Action is Dig && native.Result.Failure == AtomicActionFailure.TargetChanged && digRetries++ == 0)
                {
                    phase = Phase.Dig;
                    return;
                }
                Finish(false, $"{native.Action.GetType().Name}_{native.Result.Failure}");
                return;
            }
            if (native.Action is Dig) collectUntil = gameTime + 5;
            if (native.Action is Place && Task is not (PlantPumpkins or BuildCamp or ReturnCamp or LeaveCamp)
                && !ConfirmStationPlacement(gameTime)) return;
        }

        if ((phase is Phase.Craft or Phase.Cook || phase == Phase.TendFire && fireStage < 2)
            && stationAim is not null && ClearTrivialStationObstruction(phase)) return;
        switch (phase)
        {
            case Phase.Start:
                if (Task == TendFire) { phase = Phase.TendFire; break; }
                if (Task == WarmAtFire) { ConfirmRecoveryFire(); break; }
                if (Task is Workbench or Fire or PumpkinSeeds) { phase = Phase.Craft; break; }
                if (Task == PlantPumpkins) { phase = Phase.Farm; break; }
                if (Task is BuildCamp or ReturnCamp or LeaveCamp) { phase = Phase.Camp; break; }
                if (Task is VisitTable or VisitFurnace or VisitFire) { phase = Phase.Worksite; break; }
                if (Task is Furnace or Cook or Warm or Spear or Rake or Axe) { StartStation(); break; }
                if (Task == Hunt) { StartHunt(gameTime); break; }
                material = FindMaterial(owner.Queries.ReadPanorama(3.2f), player.ComponentBody.Position, Task, missedMaterials, camp);
                if (material is null) { Finish(false, "no_reachable_material"); break; }
                action = owner.Start(new FaceTowards(material.Point, 3f));
                phase = Phase.Dig;
                break;
            case Phase.Dig:
                if (!ReadyDigTool()) break;
                var focus = owner.Queries.LookAhead(4);
                if (focus is not { Kind: FocusKind.Block, Cell: Point3 cell, Face: int face, BlockValue: int value }
                    || cell != material!.Cell || !IsMaterial(value, Task))
                {
                    // 阶梯与耕地保护不计入失败候选数，仍允许换到真正可采的目标。
                    if (missedMaterials.Add(material!.Cell) && ++staleMaterialCandidates < 3) phase = Phase.Start;
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
            case Phase.Farm:
                AdvanceFarm(gameTime);
                break;
            case Phase.Camp:
                AdvanceCamp(gameTime);
                break;
            case Phase.Worksite:
                AdvanceWorksite(gameTime);
                break;
            case Phase.ClearStationSight:
                if (player.Project.FindSubsystem<SubsystemTerrain>(true).Terrain
                    .GetChunkAtCell(stationCell.X, stationCell.Z)?.State != TerrainChunkState.Valid) break;
                action = owner.Start(new FaceTowards(stationAim!.Value, 3f));
                phase = afterStationClear;
                break;
            case Phase.TendFire:
                AdvanceFireTending();
                break;
        }
    }

    private void AdvanceMaterialPickup(double gameTime)
    {
        if (materialPickupRoute is { } route)
        {
            route.Update(gameTime);
            if (route.Status is null) return;
            materialPickupRoute = null;
            log(new { kind = "play_production_drop_route_result", actor = player.PlayerData.Name, task = Task,
                route.Status, route.Reason, position = player.ComponentBody.Position.ToString(), gameTime });
            if (route.Status != "succeeded" && !PlaySurvivalSenses.OnDryGround(player.ComponentBody))
            {
                Finish(false, "material_drop_route_blocked");
                return;
            }
        }
        if (Added > 0)
        {
            // 入包可能发生在下台阶途中，先让这一段原生移动落稳再交接制作等下一步。
            if (PlaySurvivalSenses.OnDryGround(player.ComponentBody) && Math.Abs(player.ComponentBody.Velocity.Y) < 0.6f)
                Finish(true, "material_acquired");
            return;
        }
        if (gameTime >= collectUntil) { Finish(false, "material_drop_not_acquired"); return; }
        if (gameTime < nextPoll) return;
        nextPoll = gameTime + 0.25;
        var drop = owner.Queries.FindVisibleObjectsAround(6f).Pickables.FirstOrDefault(item =>
            !missedMaterialDrops.Contains(NativeAcquisitionAudit.IdentityFor(item.CreatePickup(1).Target)) && (Task == Wood
                ? IsMaterial(item.Value, Wood) : Terrain.ExtractContents(item.Value) == (Task == Earth ? DirtBlock.Index : CobblestoneBlock.Index)));
        if (drop is null) return;
        lastMaterialDrop = NativeAcquisitionAudit.IdentityFor(drop.CreatePickup(1).Target);
        var pickup = owner.Start(drop.CreatePickup(1));
        if (pickup.Result.Failure is not (AtomicActionFailure.OutOfReach or AtomicActionFailure.Blocked)) return;
        if (approachedMaterialDrop)
        {
            Finish(false, "material_drop_unreachable_after_approach");
            return;
        }

        var body = player.ComponentBody;
        // 采挖后的地形刷新尚未完成时，先等原生更新，避免把旧的实心证据当作拾取路线。
        if (player.Project.FindSubsystem<SubsystemTerrain>(true).Terrain
            .GetChunkAtCell(Terrain.ToCell(body.Position.X), Terrain.ToCell(body.Position.Z))?.State != TerrainChunkState.Valid) return;
        var offset = drop.Position - body.Position;
        float distance = offset.XZ.Length();
        if (distance < 0.15f || offset.Y is < -1.6f or > 1.9f || permitLandLeg?.Invoke(drop.Position, gameTime) == false) return;
        // 挖掘成功后掉落可能弹开。只试一次有落脚证据的短程接近，库存增加才完成采集。
        approachedMaterialDrop = true;
        collectUntil = gameTime + 12;
        float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset.XZ));
        var target = MaterialTarget(drop);
        var path = PlayObservedPath.Find(owner.Queries.ReadPanorama(8f), body.Position, drop.Position,
            (_, end) => permitLandLeg?.Invoke(end, gameTime) != false, goalReach: 0.7f, maximumAboveTarget: 0.2f);
        materialPickupRoute = new(player, owner, bearing, distance + 1f, "food", false, gameTime,
            () => (JsonSerializer.SerializeToElement(new { }), owner.Queries.FindVisibleObjectsAround(6f).Pickables.Select(MaterialTarget).ToArray()),
            readDryWindow, log, permitLandLeg: permitLandLeg, foodTarget: target, observedPath: path);
        log(new { kind = "play_production_drop_approach", actor = player.PlayerData.Name, task = Task, distance,
            origin = body.Position.ToString(), target = drop.Position.ToString(), gameTime });
    }

    private static PlaySurvivalFood.Food MaterialTarget(NearbyPickableView item) =>
        new(string.Empty, BlocksManager.Blocks[Terrain.ExtractContents(item.Value)].GetDisplayName(null, item.Value), "pickup",
            item.Distance, 0f, 0f, false, item.InReach, item.Value,
            JsonSerializer.SerializeToElement(new { x = item.Position.X, y = item.Position.Y, z = item.Position.Z }))
        { SourceId = NativeAcquisitionAudit.IdentityFor(item.CreatePickup(1).Target) };

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
            Spear => WoodenSpearBlock.Index, Rake => WoodenRakeBlock.Index, Axe => StoneAxeBlock.Index,
            PumpkinSeeds => PumpkinSeedValue, _ => FurnaceBlock.Index
        };
        var observed = tools.Call("observe", new { block_limit = 1, limit = 1 });
        var refs = observed.State == "returned" ? NativeProductionTools.WorkspaceReferences(observed.Data) : default;
        bool usesWorkbench = Task is Furnace or Spear or Rake or Axe;
        string? workspace = usesWorkbench ? refs.Visible : refs.Own;
        var grid = usesWorkbench ? ObservedStation<ComponentCraftingTable>() : player.Entity.FindComponent<ComponentCraftingTable>();
        if (workspace is null || grid is null)
        {
            var focus = owner.Queries.LookAhead(5f);
            log(new { kind = "play_crafting_station_unavailable", actor = player.PlayerData.Name, task = Task,
                expected = stationCell.ToString(), focus = focus.Cell?.ToString(), focus.Kind, focus.Distance,
                eye = player.ComponentCreatureModel.EyePosition.ToString(), position = player.ComponentBody.Position.ToString(),
                workspaceFound = workspace is not null, gridFound = grid is not null });
            Finish(false, "crafting_station_unavailable");
            return;
        }
        if (TryCraft(workspace, grid, target) || Task == Fire && TryCraft(workspace, grid, StickBlock.Index)
            || (Task is Spear or Rake or Axe) && !HasIngredient(player, "stick", 2) && TryCraft(workspace, grid, StickBlock.Index)
            || (Task is Workbench or Fire or Spear or Rake or Axe) && TryCraft(workspace, grid, PlanksBlock.Index)) return;
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

    private bool ReadyDigTool()
    {
        var carry = player.ComponentMiner.Inventory;
        float Power(int value)
        {
            var block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
            return Task == Stone ? block.GetQuarryPower(value) : Task == Earth ? block.GetShovelPower(value) : block.GetHackPower(value);
        }
        int best = Enumerable.Range(0, carry.SlotsCount).Where(slot => carry.GetSlotCount(slot) > 0
                && player.ComponentMiner.IsLevelSufficientForTool(carry.GetSlotValue(slot)))
            .OrderByDescending(slot => Power(carry.GetSlotValue(slot))).FirstOrDefault(-1);
        if (best < 0 || Power(carry.GetSlotValue(best)) <= Power(player.ComponentMiner.ActiveBlockValue)) return true;
        // 只选择本人已经拥有的工具；制作工具的安排仍来自模型计划。
        if (best >= carry.VisibleSlotsCount)
        {
            int empty = Enumerable.Range(0, carry.VisibleSlotsCount).FirstOrDefault(slot => carry.GetSlotCount(slot) == 0, -1);
            if (empty < 0) return true;
            Transfer(carry, best, carry, empty);
            return false;
        }
        action = owner.Start(new SelectItem(best, carry.GetSlotValue(best)));
        return false;
    }

    public void Stop()
    {
        StopQuarry();
        StopEquipment();
        stationApproach?.Stop();
        stationApproach = null;
        projectStep?.Stop();
        projectStep = null;
        projectRoute?.Stop();
        projectRoute = null;
        if (IsProject(Task)) return;
        materialPickupRoute?.Stop();
        materialPickupRoute = null;
        campRoute?.Stop();
        campRoute = null;
        StopHunt();
        if (action?.Result.Status == AtomicActionStatus.Running) owner.Cancel(action);
        action = null;
        if (craftTask.HasValue || tools.RequiresReconciliation.HasValue) tools.Call("cancel_task", new { });
        craftTask = null;
    }

    private void Finish(bool success, string reason)
    {
        if (!success && Task is Wood or Stone or Earth && lastMaterialDrop is Guid missed) missedMaterialDrops.Add(missed);
        Stop();
        Status = success ? "succeeded" : "failed";
        Reason = reason;
    }
}
