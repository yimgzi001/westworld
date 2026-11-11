#nullable enable

using Engine;
using Game;
using Game.Westworld;

internal sealed class PlayEarthShelter
{
    internal sealed record Site(int X, int Surface, int Z)
    {
        public Vector3 Center(float y) => new(X + 0.5f, y, Z + 0.5f);
        public Point3 Cell(int y) => new(X, y, Z);
        public bool SameColumn(ComponentBody body) => Terrain.ToCell(body.Position.X) == X && Terrain.ToCell(body.Position.Z) == Z;
        public bool Contains(ComponentBody body) => SameColumn(body) && body.Position.Y < Surface - 0.5f;
    }

    private enum Phase { Center, SurveyFloor, DigFloor, Settle, SurveyWall, Seal, Resealed, SurveyRoof, Open, Peek, SurveyExitFloor, Jump, Raise }
    private static readonly Point3[] Walls = [new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0), new(0, 0, -1)];
    private readonly ComponentPlayer player;
    private readonly AtomicActions owner;
    private readonly Site site;
    private readonly bool leaving;
    private readonly double deadline;
    private readonly Action<object> log;
    private readonly Func<PlaySurvivalSenses.Threat, double, bool>? permitFarExit;
    private AtomicActionRun? action;
    private Phase phase = Phase.Center;
    private double settleUntil, nextPeek;
    private int wall, level, digRetries, jumpRetries, retreatAimRetries, lastExitLevel = int.MinValue;
    private float peakRise;
    private bool opened, retreating;

    public PlayEarthShelter(ComponentPlayer player, AtomicActions owner, Site site, bool leaving,
        double gameTime, Action<object> log, Func<PlaySurvivalSenses.Threat, double, bool>? permitFarExit = null)
    {
        this.player = player;
        this.owner = owner;
        this.site = site;
        this.leaving = leaving;
        this.log = log;
        this.permitFarExit = permitFarExit;
        deadline = gameTime + 45;
    }

    public string? Status { get; private set; }
    public string? Reason { get; private set; }
    public PlaySurvivalSenses.Threat? LastExitThreat { get; private set; }
    public PlaySurvivalSenses.Threat? FarExitThreat { get; private set; }
    public bool Leaving => leaving;
    public bool OwnsExitMotion(ComponentBody body) => leaving && site.SameColumn(body)
        && (body.Position.Y < site.Surface - 0.05f || !PlaySurvivalSenses.OnDryGround(body));

    public static int ExitRetryDelay(int retreats, float food) =>
        food <= 0.18f ? 36 : Math.Min(150, 48 + 24 * (Math.Clamp(retreats, 1, 6) - 1));

    public static Site? At(ComponentBody body)
    {
        var feet = Terrain.ToCell(body.Position + new Vector3(0, 0.05f, 0));
        if (body.ParentBody is not null || body.ImmersionDepth > 0.04f || body.StandingOnBody is not null
            || body.StandingOnValue is not int value || !SoftEarth(value) || MathF.Abs(body.Position.Y - feet.Y) > 0.05f)
            return null;
        var terrain = body.Project.FindSubsystem<SubsystemTerrain>(true).Terrain;
        // 接触面可能是相邻地块。挖坑前确认目标格中心下方本身也是软土。
        if (!(terrain.GetChunkAtCell(feet.X, feet.Z)?.State >= TerrainChunkState.InvalidVertices1)
            || !SoftEarth(terrain.GetCellValue(feet.X, feet.Y - 1, feet.Z))) return null;
        return new(feet.X, feet.Y, feet.Z);
    }

    public void Update(double gameTime)
    {
        if (Status is not null) return;
        var body = player.ComponentBody;
        if (gameTime >= deadline || body.ImmersionDepth > 0.04f || player.ComponentSleep.IsSleeping
            || Terrain.ToCell(body.Position.X) != site.X || Terrain.ToCell(body.Position.Z) != site.Z
            || body.Position.Y < site.Surface - 3.2f)
        {
            Finish("failed", "shelter_position_unsafe");
            return;
        }
        if (action is { } run)
        {
            if (run.Result.Status == AtomicActionStatus.Running) return;
            action = null;
            if (run.Result.Status != AtomicActionStatus.Succeeded)
            {
                if (retreating && run.Action is FaceTowards && retreatAimRetries++ < 3
                    && run.Result.Failure is AtomicActionFailure.PreconditionsNotMet or AtomicActionFailure.MovementInterrupted)
                {
                    phase = Phase.Settle;
                    settleUntil = gameTime + 0.12;
                    return;
                }
                // 自动拾取上一格土可能改变空手槽位。原生取消后重新观察，再尝试同一阶段。
                if (run.Action is Dig && run.Result.Failure == AtomicActionFailure.TargetChanged && digRetries++ < 2)
                {
                    phase = leaving && !retreating ? Phase.SurveyRoof : Phase.Settle;
                    settleUntil = gameTime + 0.25;
                    log(new { kind = "play_shelter_dig_retry", actor = player.PlayerData.Name, leaving, digRetries, gameTime });
                    return;
                }
                Finish("failed", $"{run.Action.GetType().Name}_{run.Result.Failure}");
                return;
            }
            if (run.Action is Dig) digRetries = 0;
            log(new { kind = "play_shelter_native", actor = player.PlayerData.Name,
                action = run.Action.GetType().Name, leaving, gameTime });
            if (phase == Phase.Settle && run.Action is Dig or Place) settleUntil = gameTime + 0.35;
            if (phase == Phase.Peek && run.Action is Dig) settleUntil = gameTime + 0.65;
            if (!leaving && phase == Phase.Open && run.Action is Place) settleUntil = gameTime + 3f;
        }

        switch (phase)
        {
            case Phase.Center:
                phase = Phase.Settle;
                if ((body.Position - site.Center(body.Position.Y)).XZ.Length() > 0.13f)
                    action = owner.Start(new MoveTo(site.Center(body.Position.Y), 0.12f, 2f));
                break;
            case Phase.Settle:
                if (gameTime < settleUntil || !body.StandingOnValue.HasValue || MathF.Abs(body.Velocity.Y) > 0.2f) return;
                level = (int)MathF.Round(body.Position.Y);
                if (leaving)
                {
                    if (MathF.Abs(body.Position.Y - level) > 0.05f) return;
                    if (level >= site.Surface)
                    {
                        Finish("succeeded", "surface_reached");
                        break;
                    }
                    if (level != lastExitLevel)
                    {
                        lastExitLevel = level;
                        jumpRetries = 0;
                    }
                    if (retreating && level == site.Surface - 3 && PlaySurvivalRest.HasSecureCover(player))
                    {
                        Finish("failed", "predator_at_exit_resealed");
                        break;
                    }
                    if (retreating)
                    {
                        // 身体还在原坑柱内就直接瞄准垫脚土或封顶面。退回时不为居中暴露在坑口。
                        phase = level > site.Surface - 3 ? Phase.SurveyFloor : Phase.SurveyWall;
                        break;
                    }
                    var center = site.Center(body.Position.Y);
                    if ((body.Position - center).XZ.Length() > 0.14f)
                    {
                        Begin(new MoveTo(center, 0.12f, 2f), Phase.Settle);
                        break;
                    }
                    phase = opened ? Phase.SurveyExitFloor : Phase.SurveyRoof;
                }
                else if (level == site.Surface - 3 && BlocksManager.Blocks[Terrain.ExtractContents(body.StandingOnValue.Value)].SleepSuitability > 0f)
                    phase = Phase.SurveyWall;
                else if (level > site.Surface - 3 && SoftEarth(body.StandingOnValue.Value))
                {
                    if (level == site.Surface && !CanKeepShelterEarth()) Finish("failed", "no_storage_for_shelter_earth");
                    else phase = Phase.SurveyFloor;
                }
                else Finish("failed", "no_soft_earth_or_sleep_floor");
                break;
            case Phase.SurveyFloor:
                Begin(new FaceTowards(site.Center(body.Position.Y), 2f, -81f), Phase.DigFloor);
                break;
            case Phase.DigFloor:
                if (Focus(site.Cell(level - 1), 4) is not { } floor || !SoftEarth(floor.ExpectedValue))
                {
                    Finish("failed", "floor_not_seen");
                    break;
                }
                Begin(new Dig(floor, 5), Phase.Settle);
                break;
            case Phase.SurveyWall:
                var delta = Walls[wall];
                var wallPoint = site.Center(site.Surface - 0.5f) + 0.66f * new Vector3(delta.X, 0, delta.Z);
                Begin(new FaceTowards(wallPoint, 2f), Phase.Seal);
                break;
            case Phase.Seal:
                if (Focus(site.Cell(site.Surface - 1) + Walls[wall], null) is { } wallTarget
                    && wallTarget.Face is int side && wallTarget.Cell + CellFace.FaceToPoint3(side) == site.Cell(site.Surface - 1))
                {
                    if (Dirt() is { } cover)
                    {
                        Begin(new Place(wallTarget, cover), retreating ? Phase.Resealed : Phase.Open);
                        break;
                    }
                    if (TryStageEarth(Phase.Seal)) break;
                }
                if (++wall < Walls.Length) phase = Phase.SurveyWall;
                else Finish("failed", "no_roof_anchor_or_owned_earth");
                break;
            case Phase.Resealed:
                Finish("failed", "predator_at_exit_resealed");
                break;
            case Phase.SurveyRoof:
                Begin(new FaceTowards(site.Center(body.Position.Y), 2f, 81f), Phase.Open);
                break;
            case Phase.Open:
                if (!leaving)
                {
                    // 放置会让所在区块重算光照。等原生遮蔽和实体围墙都确认后再交给休息动作。
                    if (PlaySurvivalRest.HasSecureCover(player)) Finish("succeeded", "earth_roof_placed");
                    else if (gameTime >= settleUntil) Finish("failed", "earth_cover_not_confirmed");
                    break;
                }
                opened = true;
                if (Focus(site.Cell(site.Surface - 1), 5) is { } roof && SoftEarth(roof.ExpectedValue))
                    Begin(new Dig(roof, 5f), Phase.Peek);
                else
                {
                    settleUntil = gameTime + 0.65;
                    phase = Phase.Peek;
                }
                break;
            case Phase.Peek:
                if (gameTime >= nextPeek)
                {
                    if (CheckExitThreat(gameTime)) break;
                    if (gameTime >= settleUntil) phase = Phase.SurveyExitFloor;
                }
                break;
            case Phase.SurveyExitFloor:
                if (!CheckExitThreat(gameTime))
                    Begin(new FaceTowards(site.Center(body.Position.Y), 2f, -81f), Phase.Jump);
                break;
            case Phase.Jump:
                if (Dirt() is null)
                {
                    if (!TryStageEarth(Phase.Jump)) Finish("failed", "no_earth_for_exit");
                    break;
                }
                if (Focus(site.Cell(level - 1), 4) is null)
                {
                    Finish("failed", "exit_floor_not_seen");
                    break;
                }
                peakRise = 0;
                Begin(new Jump(), Phase.Raise);
                break;
            case Phase.Raise:
                // 每一跳都可能从坑口看见靠近的猎食者。放垫脚土前再核对一次。
                bool canRaise = body.Position.Y >= level + 0.82f;
                if ((canRaise || gameTime >= nextPeek) && CheckExitThreat(gameTime)) break;
                // 原生放置用内缩 0.2 格的身体包围盒。接近一格时即可交给原生校验。
                peakRise = Math.Max(peakRise, body.Position.Y - level);
                if (canRaise && Focus(site.Cell(level - 1), 4) is { } baseBlock && Dirt() is { } earth)
                {
                    log(new { kind = "play_shelter_jump_place", actor = player.PlayerData.Name, peakRise, gameTime });
                    Begin(new Place(baseBlock, earth), Phase.Settle);
                }
                else if (body.Velocity.Y <= 0f)
                {
                    log(new { kind = "play_shelter_jump_blocked", actor = player.PlayerData.Name, peakRise, level,
                        position = body.Position.ToString(), jumpRetries, gameTime });
                    if (jumpRetries++ >= 2) Finish("failed", "native_jump_could_not_place_earth");
                    else
                    {
                        settleUntil = gameTime + 0.35;
                        phase = Phase.Settle;
                    }
                }
                break;
        }
    }

    private BlockTarget? Focus(Point3 cell, int? face)
    {
        var focus = owner.Queries.LookAhead(5);
        if (focus is { Kind: FocusKind.Block, Face: int hitFace, BlockValue: int value, Cell: Point3 point }
            && point == cell && (!face.HasValue || face == hitFace)) return new(point, value, hitFace);
        return null;
    }

    private OwnItemSlot? Dirt()
    {
        var inventory = player.ComponentMiner.Inventory;
        for (int slot = 0; slot < inventory.VisibleSlotsCount; slot++)
        {
            int value = inventory.GetSlotValue(slot);
            if (inventory.GetSlotCount(slot) > 0 && Terrain.ExtractContents(value) == DirtBlock.Index)
                return new(slot, value);
        }
        return null;
    }

    private bool CanKeepShelterEarth()
    {
        var inventory = player.ComponentMiner.Inventory;
        if (Dirt() is not null || EmptyHotbarSlot(inventory) >= 0) return true;
        int dirtSlot = BackpackDirtSlot(inventory);
        if (dirtSlot < 0)
            dirtSlot = BackpackSlots(inventory).FirstOrDefault(slot => inventory.GetSlotCount(slot) == 0, -1);
        // 快捷栏全满时，确认挖出的土能入包且能腾出一个放置用槽，再开始挖坑。
        return dirtSlot >= 0 && FindHotbarStorage(inventory, dirtSlot) is not null;
    }

    private bool TryStageEarth(Phase resume)
    {
        var inventory = player.ComponentMiner.Inventory;
        int source = BackpackDirtSlot(inventory);
        if (source < 0) return false;
        int hotbar = EmptyHotbarSlot(inventory);
        if (hotbar >= 0)
        {
            MoveInventory(inventory, source, hotbar, resume);
            return true;
        }
        if (FindHotbarStorage(inventory, source) is not { } storage) return false;
        // 先把原快捷栏物品整组移到背包，下一帧再搬土。两次都使用原生库存动作。
        MoveInventory(inventory, storage.Hotbar, storage.Backpack, resume);
        return true;
    }

    private static (int Hotbar, int Backpack)? FindHotbarStorage(IInventory inventory, int earthSlot)
    {
        for (int hotbar = 0; hotbar < inventory.VisibleSlotsCount; hotbar++)
        {
            int value = inventory.GetSlotValue(hotbar);
            int count = inventory.GetSlotCount(hotbar);
            int backpack = BackpackSlots(inventory).FirstOrDefault(slot => slot != earthSlot
                && (inventory.GetSlotCount(slot) == 0 || inventory.GetSlotValue(slot) == value)
                && inventory.GetSlotCapacity(slot, value) - inventory.GetSlotCount(slot) >= count, -1);
            if (count > 0 && backpack >= 0) return (hotbar, backpack);
        }
        return null;
    }

    private static IEnumerable<int> BackpackSlots(IInventory inventory) => Enumerable.Range(ComponentInventory.ShortInventorySlotsCount,
        Math.Max(0, inventory.SlotsCount - ComponentInventory.ShortInventorySlotsCount));

    private static int BackpackDirtSlot(IInventory inventory) => BackpackSlots(inventory).FirstOrDefault(slot =>
        inventory.GetSlotCount(slot) > 0 && Terrain.ExtractContents(inventory.GetSlotValue(slot)) == DirtBlock.Index, -1);

    private static int EmptyHotbarSlot(IInventory inventory) => Enumerable.Range(0, inventory.VisibleSlotsCount)
        .FirstOrDefault(slot => inventory.GetSlotCount(slot) == 0, -1);

    private void MoveInventory(IInventory inventory, int source, int destination, Phase resume) => Begin(new TransferItems(
        new InventorySlot(inventory, source, inventory.GetSlotValue(source)),
        new InventorySlot(inventory, destination, inventory.GetSlotValue(destination)), inventory.GetSlotCount(source)), resume);

    private static bool SoftEarth(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)] is GrassBlock or DirtBlock;

    private bool CheckExitThreat(double gameTime)
    {
        nextPeek = gameTime + 0.12;
        return TryRetreatFromPredators(PlaySurvivalSenses.Threats(player, owner.Queries), gameTime);
    }

    public bool TryRetreatFromPredators(IReadOnlyList<PlaySurvivalSenses.Threat> observed, double gameTime)
    {
        var body = player.ComponentBody;
        if (!opened || retreating || !OwnsExitMotion(body)) return false;
        bool BlocksExit(PlaySurvivalSenses.Threat animal) => animal.Y >= site.Surface - 1.05f
            && PlaySurvivalSenses.NeedsAvoidance(animal, inWater: false);
        var threat = observed.FirstOrDefault(BlocksExit);
        if (threat is null) return false;
        if (level == site.Surface - 1 && threat.Predator && !observed.Any(animal => animal.EntityId != threat.EntityId && BlocksExit(animal))
            && permitFarExit?.Invoke(threat, gameTime) == true)
        {
            if (FarExitThreat?.EntityId != threat.EntityId)
                log(new { kind = "play_shelter_far_exit_outlet", actor = player.PlayerData.Name, threat, level, gameTime });
            FarExitThreat = threat;
            return false;
        }
        bool duringJump = phase == Phase.Raise;
        Stop();
        LastExitThreat = threat;
        retreating = true;
        wall = 0;
        settleUntil = gameTime;
        phase = duringJump && level > site.Surface - 3 ? Phase.SurveyFloor : Phase.Settle;
        log(new { kind = "play_shelter_exit_threat", actor = player.PlayerData.Name, threat, level, duringJump, gameTime });
        return true;
    }

    private void Begin(AtomicAction next, Phase after)
    {
        action = owner.Start(next);
        phase = after;
    }

    public void Stop()
    {
        if (action?.Result.Status == AtomicActionStatus.Running) owner.Cancel(action);
        action = null;
    }

    private void Finish(string status, string reason)
    {
        Stop();
        Status = status;
        Reason = reason;
    }
}
