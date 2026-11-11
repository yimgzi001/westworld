#nullable enable

using System.Text.Json;
using System.Diagnostics;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Runtime.Core;

internal sealed partial class PlaySurvivalProduction
{
    public const string Quarry = "probe_shallow_stone";
    public const string VisitQuarry = "return_to_known_shallow_quarry";
    public const int MaximumQuarryDepth = 6;
    private const int QuarryReturnLimit = 2 * (MaximumQuarryDepth + 2);
    internal sealed class QuarrySite(Point3 entry, Point3 direction)
    {
        public Point3 EntryCell { get; } = entry;
        public Point3 Direction { get; } = direction;
        public Vector3 Entry => new(EntryCell.X + 0.5f, EntryCell.Y, EntryCell.Z + 0.5f);
        public bool Worked { get; set; }
        public bool StoneSeen { get; set; }
        public float DepthReached { get; set; }
        public Point3 Column(int n) => EntryCell + n * Direction;
        public IEnumerable<Point3> Supports => Enumerable.Range(0, MaximumQuarryDepth + 1).Select(n => Column(n) - new Point3(0, n + 1, 0));
        public bool Protects(Point3 cell) => Enumerable.Range(0, MaximumQuarryDepth + 1).Any(n =>
            cell == Column(n) - new Point3(0, n + 1, 0));
        public object Snapshot(Vector2 facing) => new { entry = EntryCell.ToString(),
            bearing = Math.Round(MathUtils.RadToDeg(Vector2.Angle(facing, new Vector2(Direction.X, Direction.Z)))),
            maximumDepth = MaximumQuarryDepth, estimatedSeconds = "约四十至一百五十秒，未保证地下有石头", purpose = "浅挖阶梯、核对每级回跳净空、取得一块样本并回到地面" };
    }

    private enum QuarryPhase { Entry, Expose, Aim, Dig, Room, Sample, Return }
    private QuarrySite? quarrySite;
    private QuarryPhase quarryPhase, quarryDigResume;
    private PlaySurvivalTravel? quarryRoute;
    private PlaySurvivalProduction? quarryChild;
    private AtomicActionRun? quarryAction;
    private PlayDryRoutes.Leg? quarryDirectLeg;
    private long quarryDirectStarted;
    private Point3 quarryTarget;
    private readonly HashSet<Point3> quarryRoom = new();
    private int quarryDepth, quarryCarveY, quarryDigs, quarryPlantDigs, quarryReturnLegs;
    private bool quarryStarted, quarryDown, quarryClearingPlant;
    private string? quarryFailure;
    private readonly IReadOnlySet<Point3>? preserveGround;
    private bool quarryVisited;

    private void AdvanceQuarryVisit(double now)
    {
        CurrentStage = "return_to_known_shallow_quarry";
        if (quarrySite is not { Worked: true, StoneSeen: true } site)
        { Finish(false, "known_stone_quarry_unavailable"); return; }
        if (now >= deadline || player.ComponentSleep.IsSleeping)
        { Finish(false, "quarry_visit_interrupted"); return; }
        if (quarryRoute is { } route)
        {
            route.Update(now);
            if (route.Status is null) return;
            quarryRoute = null;
            if (route.Status != "succeeded") { Finish(false, route.Reason ?? "quarry_visit_route_blocked"); return; }
        }
        var body = player.ComponentBody;
        if (PlaySurvivalSenses.OnDryGround(body) && Math.Abs(body.Position.Y - site.EntryCell.Y) < 0.1f
            && (body.Position - site.Entry).XZ.LengthSquared() < 0.3f * 0.3f)
        {
            quarryVisited = true;
            Finish(true, "known_quarry_entry_reconfirmed");
            return;
        }
        if ((body.Position - site.Entry).XZ.LengthSquared() > 40f * 40f)
        { Finish(false, "known_quarry_beyond_navigation_range"); return; }
        StartQuarryRoute(site.Entry, now, true);
    }

    public static QuarrySite[] QuarrySites(LocalObservationWindow window, Func<Point3, bool>? protect = null)
    {
        int y = (int)Math.Round(window.Position.Y);
        if (Math.Abs(window.Position.Y - y) > 0.08) return [];
        var cells = window.Cells.Where(cell => cell.IsCurrent).ToDictionary(cell => (cell.X, cell.Y, cell.Z));
        var entry = new Point3((int)Math.Floor(window.Position.X), y, (int)Math.Floor(window.Position.Z));
        return new[] { new Point3(1, 0, 0), new Point3(-1, 0, 0), new Point3(0, 0, 1), new Point3(0, 0, -1) }
            .Select(axis => new QuarrySite(entry, axis)).Where(site => Enumerable.Range(0, 5).All(n =>
            {
                var at = site.Column(n);
                if (n > 0 && protect?.Invoke(at - Up) == true) return false;
                if (!cells.TryGetValue((at.X, y - 1, at.Z), out var ground) || !QuarrySoil(ground.Value)) return false;
                return Enumerable.Range(0, 3).All(dy => cells.TryGetValue((at.X, y + dy, at.Z), out var air)
                    && (Terrain.ExtractContents(air.Value) == 0 || TrivialPlant(air.Value)));
            })).ToArray();
    }

    private static bool QuarrySoil(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)] is GrassBlock or DirtBlock;
    private bool ProtectedMaterial(Point3 cell) => preserveGround?.Contains(cell) == true || camp?.Protects(cell) == true;

    private void AdvanceQuarry(double now)
    {
        var body = player.ComponentBody;
        if (!quarryStarted)
        {
            quarrySite ??= QuarrySites(readDryWindow(), ProtectedMaterial).FirstOrDefault();
            if (quarrySite is null) { Finish(false, "no_observed_shallow_quarry_site"); return; }
            quarryStarted = true;
            quarryCarveY = quarrySite.EntryCell.Y - 1;
            CurrentStage = "approach_quarry_entry";
        }
        var site = quarrySite!;
        site.DepthReached = Math.Max(site.DepthReached, site.EntryCell.Y - body.Position.Y);
        if (player.ComponentSleep.IsSleeping || now >= deadline)
        { Finish(false, "shallow_quarry_work_limit"); return; }
        if (quarryPhase != QuarryPhase.Return && (now > deadline - 30 || player.ComponentHealth.Health < 0.2f
            || body.ImmersionDepth > 0.04f)) ReturnFromQuarry("shallow_quarry_safety_return");

        if (quarryRoute is { } route)
        {
            route.Update(now);
            if (route.Status is null) return;
            quarryRoute = null;
            if (quarryPhase == QuarryPhase.Return)
            {
                bool returned = PlaySurvivalSenses.OnDryGround(body) && Math.Abs(body.Position.Y - site.EntryCell.Y) < 0.1f
                    && (body.Position - site.Entry).XZ.LengthSquared() < 0.3f * 0.3f;
                log(new { kind = "play_shallow_quarry_return_leg", actor = player.PlayerData.Name,
                    route.Status, route.Reason, returned, position = body.Position.ToString(), gameTime = now });
                if (!returned && route.Status != "succeeded")
                {
                    var observed = readDryWindow();
                    log(new { kind = "play_shallow_quarry_return_observation", actor = player.PlayerData.Name,
                        site = site.EntryCell.ToString(), site.Direction,
                        cells = observed.Cells.Where(cell => Math.Abs(cell.X + 0.5f - body.Position.X) < 2.1f
                            && Math.Abs(cell.Z + 0.5f - body.Position.Z) < 2.1f
                            && cell.Y >= body.Position.Y - 1.1f && cell.Y <= body.Position.Y + 3.1f)
                            .Select(cell => new { cell.X, cell.Y, cell.Z, cell.Value, cell.Age }).ToArray() });
                }
                if (!returned && route.Status == "succeeded" && quarryReturnLegs < QuarryReturnLimit)
                { quarryPhase = QuarryPhase.Return; return; }
                bool sample = Added > 0 && site.StoneSeen;
                string reason = returned ? sample ? "shallow_stone_sample_returned" : quarryFailure ?? "shallow_probe_no_stone"
                    : "shallow_quarry_return_blocked";
                log(new { kind = "play_shallow_quarry_result", actor = player.PlayerData.Name, returned, sample,
                    site.DepthReached, quarryDigs, reason, position = body.Position.ToString(), gameTime = now });
                Finish(returned && sample, reason);
                return;
            }
            if (route.Status != "succeeded") { ReturnFromQuarry(route.Reason ?? "quarry_step_blocked"); return; }
            if (quarryDown)
            {
                quarryDown = false;
                if (Math.Abs(body.Position.Y - (site.EntryCell.Y - quarryDepth - 1)) > 0.1f)
                { ReturnFromQuarry("quarry_step_height_not_confirmed"); return; }
                quarryDepth++;
            }
            quarryPhase = QuarryPhase.Expose;
            quarryCarveY = site.EntryCell.Y - 1;
        }
        if (quarryChild is { } child)
        {
            child.Update(now);
            if (child.Status is null) return;
            quarryChild = null;
            ReturnFromQuarry(child.Status == "succeeded" ? null : child.Reason ?? "quarry_sample_not_acquired");
            return;
        }
        if (quarryAction is { } native)
        {
            if (native.Result.Status == AtomicActionStatus.Running)
            {
                if (quarryDirectLeg?.Rejection(body, Stopwatch.GetElapsedTime(quarryDirectStarted).TotalSeconds) is { } rejection)
                    Finish(false, $"quarry_return_{rejection}");
                return;
            }
            quarryAction = null;
            quarryDirectLeg = null;
            if (native.Result.Status != AtomicActionStatus.Succeeded)
            {
                log(new { kind = "play_shallow_quarry_action_failed", actor = player.PlayerData.Name,
                    phase = quarryPhase.ToString(), target = quarryTarget.ToString(), position = body.Position.ToString(),
                    failure = native.Result.Failure.ToString(), gameTime = now });
                ReturnFromQuarry($"quarry_{native.Action.GetType().Name}_{native.Result.Failure}");
                return;
            }
            if (native.Action is Dig)
            {
                if (!quarryClearingPlant) site.Worked = true;
                quarryDigs++;
                log(new { kind = "play_shallow_quarry_dug", actor = player.PlayerData.Name,
                    cell = quarryTarget.ToString(), quarryDigs, plant = quarryClearingPlant, gameTime = now });
                if (!quarryClearingPlant && quarryDigResume == QuarryPhase.Expose) quarryCarveY--;
                if (!quarryClearingPlant && quarryDigResume == QuarryPhase.Room) quarryRoom.Remove(quarryTarget);
                quarryPhase = quarryDigResume;
            }
        }
        if (!PlaySurvivalSenses.OnDryGround(body)) return;
        if (player.Project.FindSubsystem<SubsystemTerrain>(true).Terrain
            .GetChunkAtCell(Terrain.ToCell(body.Position.X), Terrain.ToCell(body.Position.Z))?.State != TerrainChunkState.Valid) return;
        if (quarryDigs >= 48 && quarryPhase != QuarryPhase.Return) ReturnFromQuarry("shallow_quarry_excavation_limit");

        switch (quarryPhase)
        {
            case QuarryPhase.Entry:
                StartQuarryRoute(site.Entry, now, false);
                break;
            case QuarryPhase.Expose:
            {
                CurrentStage = "expose_observed_soil";
                if (quarryCarveY < site.EntryCell.Y - quarryDepth - 1)
                {
                    if (quarryDepth >= MaximumQuarryDepth - 1) { ReturnFromQuarry("no_stone_within_shallow_limit"); break; }
                    var column = site.Column(quarryDepth + 1);
                    var landing = new Vector3(column.X + 0.5f, site.EntryCell.Y - quarryDepth - 1, column.Z + 0.5f);
                    var below = readDryWindow().Cells.FirstOrDefault(cell => cell.IsCurrent && cell.X == column.X
                        && cell.Y == (int)landing.Y - 1 && cell.Z == column.Z);
                    if (below is not null && !PlaySurvivalSenses.IsDryTerrain(below.Value))
                    { ReturnFromQuarry("quarry_liquid_or_missing_support"); break; }
                    var previous = site.Column(quarryDepth);
                    var upper = new Vector3(previous.X + 0.5f, site.EntryCell.Y - quarryDepth, previous.Z + 0.5f);
                    // 能下去不等于能跳回来。使用同一份实际观察推演反向台阶，未知净空也不下探。
                    if (!PlayDryRoutes.HasObservedReturnStep(readDryWindow(), landing, upper))
                    { ReturnFromQuarry("quarry_return_step_not_observed"); break; }
                    quarryDown = true;
                    StartQuarryRoute(landing, now, false);
                    break;
                }
                quarryTarget = site.Column(quarryDepth + 1);
                quarryTarget.Y = quarryCarveY;
                var panorama = owner.Queries.ReadPanorama(7f);
                var target = panorama?.Blocks.FirstOrDefault(block => block.Cell == quarryTarget);
                if (target is null)
                {
                    var air = readDryWindow().Cells.FirstOrDefault(cell => cell.IsCurrent && cell.X == quarryTarget.X
                        && cell.Y == quarryTarget.Y && cell.Z == quarryTarget.Z);
                    if (air is not null && Terrain.ExtractContents(air.Value) == 0) quarryCarveY--;
                    else ReturnFromQuarry("quarry_layer_not_observed");
                    break;
                }
                if (ProtectedMaterial(target.Cell)) { ReturnFromQuarry("quarry_protected_support"); break; }
                if (Terrain.ExtractContents(target.Value) == GraniteBlock.Index)
                {
                    site.StoneSeen = true;
                    var side = new Point3(-site.Direction.Z, 0, site.Direction.X);
                    for (int forward = -1; forward <= 1; forward++)
                    for (int lateral = -1; lateral <= 1; lateral++)
                    for (int height = 1; height <= 3; height++)
                        quarryRoom.Add(target.Cell + height * Up + forward * site.Direction + lateral * side);
                    quarryPhase = QuarryPhase.Room;
                    log(new { kind = "play_shallow_stone_seen", actor = player.PlayerData.Name, cell = target.Cell.ToString(), gameTime = now });
                    break;
                }
                if (!QuarrySoil(target.Value)) { ReturnFromQuarry("quarry_non_soil_or_fluid_layer"); break; }
                AimQuarrySoil(target, QuarryPhase.Expose);
                break;
            }
            case QuarryPhase.Room:
            {
                CurrentStage = "clear_observed_working_space";
                var panorama = owner.Queries.ReadPanorama(6f);
                var target = panorama?.Blocks.Where(block => quarryRoom.Contains(block.Cell) && QuarrySoil(block.Value)
                    && !ProtectedMaterial(block.Cell)).MinBy(block => block.Distance);
                if (target is not null) { quarryTarget = target.Cell; AimQuarrySoil(target, QuarryPhase.Room); break; }
                quarryPhase = QuarryPhase.Sample;
                break;
            }
            case QuarryPhase.Aim:
            {
                var target = owner.Queries.ReadPanorama(6f)?.Blocks.FirstOrDefault(block => block.Cell == quarryTarget);
                if (target is null || !QuarrySoil(target.Value)) { quarryPhase = quarryDigResume; break; }
                var focus = owner.Queries.LookAhead(5f);
                quarryClearingPlant = focus is { Cell: Point3 plant, BlockValue: int plantValue } && TrivialPlant(plantValue)
                    && quarryPlantDigs < 4;
                int value = target.Value;
                if (quarryClearingPlant)
                {
                    quarryPlantDigs++;
                    quarryTarget = focus!.Cell!.Value;
                    value = focus.BlockValue!.Value;
                }
                float seconds = player.ComponentMiner.CalculateDigTime(value, player.ComponentMiner.ActiveBlockValue);
                if (!float.IsFinite(seconds) || seconds > 25f) { ReturnFromQuarry("quarry_tool_cannot_dig_soil"); break; }
                // 保留全景实际看见的方块面。窄阶梯中能看到侧面，不代表能射到方块中心。
                var offset = target.Point - Center(target.Cell);
                int? face = !quarryClearingPlant && offset.LengthSquared() > 0.16f ? CellFace.Vector3ToFace(offset) : null;
                quarryAction = owner.Start(new Dig(new BlockTarget(quarryTarget, value, face), Math.Clamp(seconds + 3f, 4f, 30f)));
                quarryPhase = QuarryPhase.Dig;
                break;
            }
            case QuarryPhase.Sample:
                CurrentStage = "take_native_stone_sample";
                quarryChild = new(player, owner, tools, Stone, now, log, stations, readDryWindow, permitLandLeg,
                    ownFarm: ownFarm, camp: camp, preserveGround: preserveGround);
                break;
            case QuarryPhase.Return:
                CurrentStage = "return_to_quarry_surface";
                if (Math.Abs(body.Position.Y - site.EntryCell.Y) < 0.1f
                    && (body.Position - site.Entry).XZ.LengthSquared() < 0.3f * 0.3f)
                {
                    bool sample = Added > 0 && site.StoneSeen;
                    string reason = sample ? "shallow_stone_sample_returned" : quarryFailure ?? "shallow_probe_no_stone";
                    log(new { kind = "play_shallow_quarry_result", actor = player.PlayerData.Name, returned = true, sample,
                        site.DepthReached, quarryDigs, reason, position = body.Position.ToString(), gameTime = now });
                    Finish(sample, reason);
                    break;
                }
                if (quarryReturnLegs >= QuarryReturnLimit) { Finish(false, "quarry_return_step_limit"); break; }
                var returnWindow = readDryWindow();
                var up = PlayDryRoutes.ObservedUpSteps(returnWindow)
                    .Where(leg => leg.End.Y <= site.EntryCell.Y + 0.1f && permitLandLeg?.Invoke(leg.End, now) != false)
                    .MinBy(leg =>
                    {
                        int n = Math.Clamp((int)Math.Round(site.EntryCell.Y - leg.End.Y), 0, quarryDepth);
                        var column = site.Column(n);
                        return (leg.End - new Vector3(column.X + 0.5f, site.EntryCell.Y - n, column.Z + 0.5f)).LengthSquared();
                    });
                var direct = up ?? PlayDryRoutes.Alignment(returnWindow);
                if (direct is not null && body.Position.Y < site.EntryCell.Y - 0.1f)
                {
                    // 返回已挖阶梯时优先使用本帧证实的上阶，不让通用寻路又跳回采样坑。
                    quarryReturnLegs++;
                    quarryDirectLeg = direct;
                    quarryDirectStarted = Stopwatch.GetTimestamp();
                    quarryAction = owner.Start(direct.Action);
                    log(new { kind = "play_shallow_quarry_return_step", actor = player.PlayerData.Name,
                        action = direct.Action.GetType().Name, from = body.Position.ToString(), target = direct.End.ToString(), gameTime = now });
                    break;
                }
                // 取样坑可能比阶梯低一格，只并入真正挖过的台阶，不把未挖的矿层当作落脚点。
                int currentLevel = Math.Clamp((int)Math.Round(site.EntryCell.Y - body.Position.Y), 0, quarryDepth);
                var currentColumn = site.Column(currentLevel);
                var currentStep = new Vector3(currentColumn.X + 0.5f, site.EntryCell.Y - currentLevel, currentColumn.Z + 0.5f);
                // 拾取会走到阶梯侧边，先并入同高度台阶，再上一级，避免斜着跨过拐角。
                bool onStair = (currentStep - body.Position).XZ.LengthSquared() < 0.35f * 0.35f;
                int level = onStair ? Math.Max(0, currentLevel - 1) : currentLevel;
                var checkpoint = site.Column(level);
                quarryReturnLegs++;
                StartQuarryRoute(new(checkpoint.X + 0.5f, site.EntryCell.Y - level, checkpoint.Z + 0.5f), now, true);
                break;
        }
    }

    private void AimQuarrySoil(PanoramicBlock target, QuarryPhase resume)
    {
        quarryDigResume = resume;
        quarryClearingPlant = false;
        quarryAction = owner.Start(new FaceTowards(target.Point, 3f));
        quarryPhase = QuarryPhase.Aim;
    }

    private void StartQuarryRoute(Vector3 target, double now, bool returning)
    {
        var body = player.ComponentBody;
        var offset = (target - body.Position).XZ;
        float bearing = offset.LengthSquared() > 0.0001f ? MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset)) : 0f;
        float range = Task == VisitQuarry || returning ? 0.3f : 0.08f;
        bool Permit(Vector3 end, double at) => permitLandLeg?.Invoke(end, at) != false
            && (Task != Quarry || !returning || end.Y >= player.ComponentBody.Position.Y - 0.1f);
        var path = returning ? PlayObservedPath.Find(owner.Queries.ReadPanorama(14f), body.Position, target,
            (_, end) => Permit(end, now), goalReach: 0.2f, maximumAboveTarget: 0.1f) : null;
        quarryRoute = new(player, owner, bearing, Math.Max(0.1f, offset.Length()), "work", false, now,
            () => (JsonSerializer.SerializeToElement(new { }), []), readDryWindow, log, shelterTarget: target,
            observedPath: path,
            permitLandLeg: Permit, goalRange: range,
            arrived: () => Math.Abs(body.Position.Y - target.Y) < 0.1f && (body.Position - target).XZ.LengthSquared()
                < range * range);
    }

    private void ReturnFromQuarry(string? reason)
    {
        quarryFailure ??= reason;
        quarryChild?.Stop();
        quarryChild = null;
        quarryRoute?.Stop();
        quarryRoute = null;
        if (quarryAction?.Result.Status == AtomicActionStatus.Running) owner.Cancel(quarryAction);
        quarryAction = null;
        quarryDirectLeg = null;
        quarryPhase = QuarryPhase.Return;
    }

    private void StopQuarry()
    {
        quarryChild?.Stop(); quarryChild = null;
        quarryRoute?.Stop(); quarryRoute = null;
        if (quarryAction?.Result.Status == AtomicActionStatus.Running) owner.Cancel(quarryAction);
        quarryAction = null;
        quarryDirectLeg = null;
    }
}
