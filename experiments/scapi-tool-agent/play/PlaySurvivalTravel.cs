#nullable enable

using System.Diagnostics;
using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Runtime.Core;

internal sealed class PlaySurvivalTravel
{
    private const int LandLegLimit = 48;
    private const float EscapeTurnClearanceSquared = 14f * 14f;
    private enum SeatAim { None, Turning, Ready }
    private readonly ComponentPlayer player;
    private readonly AtomicActions owner;
    private readonly string seek;
    private readonly float distance, landDistanceLimit;
    private readonly float escapeClearance;
    private readonly bool allowEscapeTurn;
    private readonly float initialHealth;
    private readonly double deadline;
    private readonly Func<(JsonElement Observation, PlaySurvivalFood.Food[] Foods)> observe;
    private readonly Func<LocalObservationWindow> readDryWindow;
    private readonly Func<Position, int>? visits;
    private readonly PlaySurvivalFood.Food? foodTarget;
    private readonly int? initialFoodStock;
    private readonly Guid? foodSourceId, foodActorId;
    private bool tracksFood;
    private readonly Vector3? shelterTarget;
    private readonly Vector3? shoreTarget;
    private readonly Vector3 surveyTarget;
    private readonly Func<Vector2>? escapeFrom;
    private readonly Func<Vector3, double, bool>? permitLandLeg;
    private readonly Vector3[] waypoints;
    private readonly Func<Vector3?>? readMovingTarget;
    private readonly float movingTargetReach;
    private readonly Func<bool>? arrived;
    private readonly float goalRange;
    private Vector3? movingTarget;
    private double nextMovingTargetRead, lastMovingTargetSeen;
    private bool movingTargetVisible;
    private readonly Action<object> log;
    private Vector2 direction;
    private Vector3 legOrigin;
    private AtomicActionRun? leg;
    private PlayDryRoutes.Leg? dryProof;
    private Vector3? escapeOutlet;
    private long proofStarted;
    private int scans, legs;
    private readonly HashSet<(int X, int Y, int Z)> visitedGoalCells = new();
    private readonly HashSet<Vector3> refusedDryEndpoints = new();
    private int goalDetours;
    private int nextWaypoint;
    private float traveled;
    private bool detourUsed, jumpUsed, dryLeg, aligned, detouring;
    private bool approachingEscapeTurn;
    private SeatAim seatAim;
    private PlaySurvivalSenses.Shore? lastShore;
    private double shoreSeenAt;
    private string? status, reason;

    public PlaySurvivalTravel(ComponentPlayer player, AtomicActions owner, float bearing, float distance,
        string seek, bool evading, double gameTime, Func<(JsonElement Observation, PlaySurvivalFood.Food[] Foods)> observe,
        Func<LocalObservationWindow> readDryWindow, Action<object> log, Func<Position, int>? visits = null,
        PlaySurvivalFood.Food? foodTarget = null, Vector3? shelterTarget = null, Vector3? shoreTarget = null,
        Func<Vector2>? escapeFrom = null, Func<Vector3, double, bool>? permitLandLeg = null,
        float escapeClearance = 0f, bool allowEscapeTurn = false, PlayObservedPath.Plan? observedPath = null,
        Func<Vector3?>? readMovingTarget = null, float movingTargetReach = 0f, Func<bool>? arrived = null,
        float goalRange = 0.45f)
    {
        this.player = player;
        this.owner = owner;
        this.seek = seek;
        this.distance = distance;
        this.observe = observe;
        this.readDryWindow = readDryWindow;
        this.visits = visits;
        this.foodTarget = foodTarget;
        if (foodTarget is { SourceId: Guid exactSource })
        {
            foodSourceId = exactSource;
            foodActorId = NativeAcquisitionAudit.IdentityFor(player.Entity);
            NativeAcquisitionAudit.Recorded += TrackFoodPickup;
            tracksFood = true;
        }
        else if (foodTarget?.Source == "pickup")
            initialFoodStock = NativeAcquisitionAudit.InventoryCount(player.ComponentMiner.Inventory, foodTarget.Value);
        this.shelterTarget = shelterTarget;
        this.shoreTarget = shoreTarget;
        this.escapeFrom = escapeFrom;
        this.escapeClearance = escapeClearance;
        this.allowEscapeTurn = allowEscapeTurn;
        this.permitLandLeg = permitLandLeg;
        this.log = log;
        waypoints = observedPath?.Waypoints ?? [];
        this.readMovingTarget = readMovingTarget;
        this.movingTargetReach = movingTargetReach;
        this.arrived = arrived;
        this.goalRange = goalRange;
        Evading = evading;
        Origin = player.ComponentBody.Position;
        initialHealth = player.ComponentHealth.Health;
        deadline = gameTime + 25;
        direction = Rotate((BoatBody ?? player.ComponentBody).Matrix.Forward.XZ, bearing);
        var offset = distance * new Vector3(direction.X, 0, direction.Y);
        surveyTarget = Origin + offset;
        // 连续走廊可能沿两条轴绕行。以实际轴向距离留出余量，避免斜向目标还没到就用完步程。
        var destinationOffset = foodTarget is not null ? foodTarget.Point - Origin : shelterTarget is { } target ? target - Origin : offset;
        bool hasLandTarget = seek is "survey" or "camp" or "work" or "search" || foodTarget is not null || shelterTarget is not null;
        landDistanceLimit = escapeFrom is not null ? distance + (escapeClearance > 0f ? 12f : 6f)
            : hasLandTarget ? MathF.Abs(destinationOffset.X) + MathF.Abs(destinationOffset.Z) + Math.Max(1.5f, distance * 0.15f) : distance;
        if (observedPath is not null) landDistanceLimit = Math.Max(landDistanceLimit, observedPath.Cells + 3f);
        if (readMovingTarget is not null) landDistanceLimit = Math.Min(20f, distance + 6f);
    }

    public bool Evading { get; }
    public bool UsedEscapeTurn { get; private set; }
    public bool HeadingForLand => seek == "dry_land";
    public bool HeadingForShelter => seek == "shelter";
    public Vector3 Origin { get; }
    public string? Status => status;
    public string? Reason => reason;
    public int Legs => legs;
    public Vector3? ActiveLandEndpoint => leg?.Result.Status == AtomicActionStatus.Running ? dryProof?.End : null;
    public int AcquiredTargetCount { get; private set; }
    private float TravelLimit => landDistanceLimit + 6f * goalDetours;
    private ComponentBody? BoatBody => player.Entity.FindComponent<ComponentRider>()?.Mount is { } mount
        && mount.Entity.FindComponent<ComponentBoat>() is not null ? mount.ComponentBody : null;

    public void Update(double gameTime)
    {
        if (status is not null) return;
        if (SettledOnLand() && arrived?.Invoke() == true)
        {
            Finish("succeeded", "native_goal_reconfirmed");
            return;
        }
        if (foodTarget is not null && SettledOnLand()
            && (AcquiredTargetCount > 0 || initialFoodStock is int before
            && NativeAcquisitionAudit.InventoryCount(player.ComponentMiner.Inventory, foodTarget.Value) > before))
        {
            Finish("succeeded", "food_collected_en_route");
            return;
        }
        var body = player.ComponentBody;
        if (dryLeg && body.ImmersionDepth > 0.08f)
        {
            Finish("failed", "land_route_became_wet");
            return;
        }
        if (gameTime >= deadline || player.ComponentSleep.IsSleeping
            || (!Evading && player.ComponentHealth.Health < initialHealth - 0.04f))
        {
            Finish("failed", "travel_interrupted");
            return;
        }
        if (readMovingTarget is { } read && RefreshMovingTarget(read, body, gameTime)) return;
        bool stepLeftEnvelope = false;
        if (leg?.Result.Status == AtomicActionStatus.Running)
        {
            bool threatClosing = approachingEscapeTurn && escapeFrom is { } readThreat
                && (body.Position.XZ - readThreat()).LengthSquared() < EscapeTurnClearanceSquared;
            string? rejection = threatClosing ? null : dryProof?.Rejection(body, Stopwatch.GetElapsedTime(proofStarted).TotalSeconds);
            if (!threatClosing && rejection is null) return;
            stepLeftEnvelope = leg.Action is StepTo && rejection is not (null or "stale_observed_envelope");
            owner.Cancel(leg);
            log(new { kind = threatClosing ? "play_predator_escape_turn_interrupted" : "play_scout_evidence_expired",
                actor = player.PlayerData.Name, gameTime, action = leg.Action.GetType().Name,
                rejection, step = dryProof?.Step?.Kind.ToString(), origin = legOrigin.ToString(),
                target = dryProof?.End.ToString(), position = body.Position.ToString() });
        }

        var previous = leg;
        if ((previous?.Result.Failure == AtomicActionFailure.InvalidArgument || stepLeftEnvelope) && dryProof is { } refused
            && (body.Position - legOrigin).XZ.LengthSquared() < 0.04f)
        {
            refusedDryEndpoints.Add(refused.End);
            log(new { kind = "play_scout_route_rejected", actor = player.PlayerData.Name, seek,
                target = refused.End.ToString(), gameTime });
        }
        leg = null;
        dryProof = null;
        approachingEscapeTurn = false;
        bool blocked = false;
        if (previous is not null)
            log(new { kind = "play_scout_leg", actor = player.PlayerData.Name, seek, gameTime,
                action = previous.Action.GetType().Name, nativeStatus = previous.Result.Status.ToString(),
                failure = previous.Result.Failure.ToString(), displacement = (body.Position - legOrigin).XZ.Length() });
        if (previous?.Action is MoveTo or MoveAlongObservedRoute or StepTo or Steer { Throttle: not 0f })
        {
            float progress = (body.Position - legOrigin).XZ.Length();
            traveled += progress;
            blocked = progress < 0.32f && previous.Result.Status != AtomicActionStatus.Succeeded;
        }
        else if (previous?.Action is Steer && previous.Result.Status != AtomicActionStatus.Succeeded)
            blocked = true;
        if (previous?.Action is Mount && previous.Result.Status != AtomicActionStatus.Succeeded)
        {
            Finish("failed", "boat_remount_failed");
            return;
        }
        if (seatAim == SeatAim.Turning && previous is not null)
        {
            if (previous.Result.Status != AtomicActionStatus.Succeeded)
            {
                Finish("failed", "boat_seat_aim_failed");
                return;
            }
            seatAim = SeatAim.Ready;
        }

        bool dry = PlaySurvivalSenses.OnDryGround(body);
        if (detouring)
        {
            bool moved = dry && previous?.Result.Status == AtomicActionStatus.Succeeded
                && (body.Position - legOrigin).XZ.Length() >= 0.5f;
            bool returned = moved && (body.Position - Origin).XZ.Length() < 0.5f;
            Finish(moved && !returned ? "succeeded" : "failed",
                returned ? "scout_returned_to_start" : moved ? "scout_detoured_to_open_ground" : "no_observed_dry_route");
            return;
        }
        if (seek == "dry_land" && dry)
        {
            Finish("succeeded", "dry_ground_reached");
            return;
        }
        if (dry)
        {
            var (reached, approach) = ReadLandGoal(body);
            if (reached is not null)
            {
                if (seek == "camp" && (shelterTarget is not { } camp || PlayEarthShelter.At(body)?.Surface != Terrain.ToCell(camp.Y)))
                    Finish("failed", "remembered_site_changed");
                else Finish("succeeded", reached);
                return;
            }
            if (legs >= LandLegLimit || traveled >= TravelLimit - 0.3f)
            {
                bool explored = seek == "food" && foodTarget is null && traveled >= landDistanceLimit - 0.3f
                    && (body.Position - Origin).XZ.Length() >= Math.Min(4f, distance * 0.5f);
                Finish(explored ? "succeeded" : "failed", explored ? "food_search_segment_complete" : "navigation_distance_limit");
                return;
            }
            AdvanceOnLand(blocked, RemainingOnLand(body, approach, traveled), TowardLowerDrop(body, approach), gameTime);
            return;
        }
        var boat = BoatBody;
        var deck = PlaySurvivalSenses.BoatDeck(body);
        // 失去地面接触可能只是一帧的下台阶。实际落地或接触水面之前保留原导航任务。
        if (boat is null && deck is null && body.ImmersionDepth < 0.04f) return;
        if (dryLeg)
        {
            Finish("failed", "land_route_became_wet");
            return;
        }
        if (legs >= 8 || traveled >= distance - 0.3f)
        {
            Finish(seek == "survey" && traveled >= 1f ? "succeeded" : "failed", "travel_distance_completed");
            return;
        }
        var (observation, _) = observe();
        if (seek == "dry_land" && PlaySurvivalSenses.VisibleShore(observation, body, boat ?? deck) is { } visible)
        {
            var offset = new Vector2(visible.X - body.Position.X, visible.Z - body.Position.Z);
            var aim = shoreTarget is { } plannedShore ? (plannedShore - body.Position).XZ : offset;
            if (Vector2.Dot(offset, aim) > 0f)
            {
                lastShore = visible;
                shoreSeenAt = gameTime;
            }
        }
        var shore = gameTime - shoreSeenAt <= 4 ? lastShore : null;
        Vector3? landing = shore is { } point ? new Vector3(point.X, point.Y, point.Z) : null;
        float shoreDistance = landing.HasValue ? (landing.Value - body.Position).XZ.Length() : float.MaxValue;
        var targetOnLand = landing ?? shoreTarget;
        var towardLand = targetOnLand is { } landAim ? (landAim - body.Position).XZ : Vector2.Zero;
        var heading = towardLand.LengthSquared() > 0.04f ? Vector2.Normalize(towardLand) : direction;

        if (boat is null && deck is not null)
        {
            // 近岸可以使用同帧全向观察，即便它此时位于角色背后。
            if (targetOnLand is { } land && (land - body.Position).XZ.LengthSquared() <= 3.4f * 3.4f && !blocked)
            {
                seatAim = SeatAim.None;
                legs++;
                Start(new MoveTo(land, 0.32f, 4f), gameTime);
                return;
            }
            if (seatAim == SeatAim.Ready)
            {
                seatAim = SeatAim.None;
                Start(new Mount(deck.Entity), gameTime);
                return;
            }
            if (deck.Entity.FindComponent<ComponentMount>() is not { } seat)
            {
                Finish("failed", "boat_seat_unavailable");
                return;
            }
            var seatPoint = deck.Position + Vector3.Transform(seat.MountOffset, deck.Rotation);
            var eye = player.ComponentCreatureModel.EyePosition;
            var toSeat = seatPoint - eye;
            float pitch = MathUtils.RadToDeg(MathF.Atan2(toSeat.Y, toSeat.XZ.Length()));
            seatAim = SeatAim.Turning;
            Start(new FaceTowards(seatPoint, 2f, Math.Clamp(pitch, -80f, 80f)), gameTime);
            return;
        }

        if (boat is not null)
        {
            if (shoreDistance <= 1.9f || boat.StandingOnValue.HasValue && boat.ImmersionDepth < 0.12f)
            {
                Start(new Dismount(), gameTime);
                return;
            }
            if (blocked && shoreDistance <= 2.8f)
            {
                Start(new Dismount(), gameTime);
                return;
            }
            if (blocked)
            {
                Finish("failed", "boat_cannot_advance");
                return;
            }
            float angle = Vector2.Angle(boat.Matrix.Forward.XZ, heading);
            if (MathF.Abs(angle) > 0.32f)
            {
                Start(new Steer(MathF.Sign(angle), 0f, Math.Clamp(MathF.Abs(angle), 0.3f, 2.6f)), gameTime);
                return;
            }
            if (seek == "dry_land" && targetOnLand is null && !Evading)
            {
                if (++scans > 4)
                {
                    Finish("failed", "no_shore_seen_from_boat");
                    return;
                }
                direction = Rotate(direction, 75);
                return;
            }
            legs++;
            Start(new Steer(0f, 0.9f, Math.Clamp(shoreDistance / 2f, 0.6f, 1.8f)), gameTime);
            return;
        }

        if (blocked)
        {
            if (!jumpUsed && (dry || body.ImmersionFactor > 0.5f) && landing.HasValue
                && landing.Value.Y > body.Position.Y + 0.3f)
            {
                jumpUsed = true;
                Start(new Jump(), gameTime);
                return;
            }
            if (detourUsed)
            {
                Finish("failed", "short_segments_blocked");
                return;
            }
            detourUsed = true;
            lastShore = null;
            direction = heading = Rotate(heading, 40);
            landing = null;
        }
        if (previous is null || blocked)
        {
            Start(new FaceTowards(body.Position + 2f * new Vector3(heading.X, 0f, heading.Y), 3f,
                seek == "food" ? -22f : -12f), gameTime);
            return;
        }

        float step = Math.Clamp(distance - traveled, 0.8f, 2.6f);
        var target = body.Position + step * new Vector3(heading.X, 0f, heading.Y);
        if (landing.HasValue)
            target = shoreDistance <= step ? landing.Value : new Vector3(target.X, landing.Value.Y, target.Z);
        legs++;
        Start(new MoveTo(target, 0.42f, 4f), gameTime);
    }

    public void Stop()
    {
        if (tracksFood)
        {
            NativeAcquisitionAudit.Recorded -= TrackFoodPickup;
            tracksFood = false;
        }
        if (leg?.Result.Status == AtomicActionStatus.Running) owner.Cancel(leg);
        leg = null;
        dryProof = null;
    }

    private bool RefreshMovingTarget(Func<Vector3?> read, ComponentBody body, double gameTime)
    {
        if (gameTime >= nextMovingTargetRead)
        {
            nextMovingTargetRead = gameTime + 0.2;
            var seen = read();
            movingTargetVisible = seen.HasValue;
            if (seen is { } point)
            {
                if (movingTarget is { } old && (point - old).XZ.LengthSquared() >= 0.25f * 0.25f)
                    log(new { kind = "play_scout_moving_target", actor = player.PlayerData.Name, seek, gameTime,
                        target = point.ToString() });
                movingTarget = point;
                lastMovingTargetSeen = gameTime;
            }
            else if (movingTarget is null || gameTime - lastMovingTargetSeen > 1.2)
            {
                Finish("failed", "moving_target_no_longer_visible");
                return true;
            }
        }
        if (movingTargetVisible && movingTarget is { } current && SettledOnLand()
            && MathF.Abs(body.Position.Y - current.Y) <= 1.05f
            && (body.Position - current).XZ.LengthSquared() <= movingTargetReach * movingTargetReach)
        {
            Finish("succeeded", "moving_target_in_range");
            return true;
        }
        return false;
    }

    private void TrackFoodPickup(NativeAcquisitionFact fact)
    {
        // 同种同值的另一枚蛋入包也不能完成当前指定目标。
        if (fact.SourceId == foodSourceId && fact.ActorId == foodActorId && fact.Value == foodTarget?.Value
            && fact.InventoryBefore is int before && fact.InventoryAfter is int after
            && after > before && after - before == fact.SourceBefore - fact.SourceAfter)
            AcquiredTargetCount += after - before;
    }

    private bool SettledOnLand() => PlaySurvivalSenses.OnDryGround(player.ComponentBody)
        && Math.Abs(player.ComponentBody.Velocity.Y) < 0.6f
        // 上台阶过程中，原生支撑值可能短暂保留；先完成这一跳，不能在半空把制作交给下一步。
        && !(leg is { Action: StepTo, Result.Status: AtomicActionStatus.Running });

    private void Finish(string state, string outcome)
    {
        Stop();
        status = state;
        reason = outcome;
    }

    private void Start(AtomicAction action, double gameTime, PlayDryRoutes.Leg? proof = null)
    {
        legOrigin = player.ComponentBody.Position;
        dryProof = proof;
        proofStarted = Stopwatch.GetTimestamp();
        leg = owner.Start(action);
        log(new { kind = "play_scout_step", actor = player.PlayerData.Name, seek, Evading, gameTime,
            action = action.GetType().Name, nativeStatus = leg.Result.Status.ToString(), legs });
    }

    private void AdvanceOnLand(bool blocked, float remaining, bool towardLowerDrop, double gameTime)
    {
        dryLeg = true;
        if (blocked) direction = Rotate(direction, 50);
        var window = readDryWindow();
        if (KeepsTarget) visitedGoalCells.Add(CellXYZ(player.ComponentBody.Position));
        bool preciseCenter = goalRange < 0.2f && CellXZ(surveyTarget) == CellXZ(player.ComponentBody.Position)
            && (surveyTarget - player.ComponentBody.Position).XZ.LengthSquared() < 0.5f * 0.5f;
        var route = preciseCenter ? PlayDryRoutes.Alignment(window) : DryRoute(window, remaining, !aligned, towardLowerDrop, gameTime);
        if (route is null && nextWaypoint < waypoints.Length)
        {
            // 局部净空与先前路标冲突时放弃路标，保留原来的食物目标。
            nextWaypoint = waypoints.Length;
            var body = player.ComponentBody;
            var (_, direct) = ReadLandGoal(body);
            remaining = RemainingOnLand(body, direct, traveled);
            route = DryRoute(window, remaining, !aligned, TowardLowerDrop(body, direct), gameTime);
            log(new { kind = "play_scout_path_hint_dropped", actor = player.PlayerData.Name, seek, gameTime });
        }
        if (route is null && !aligned)
        {
            route = PlayDryRoutes.RecoverBesidePumpkin(window, player.Project.FindSubsystem<SubsystemTerrain>(true));
            if (route is not null)
                log(new { kind = "play_scout_pumpkin_alignment", actor = player.PlayerData.Name, gameTime,
                    target = route.End.ToString() });
        }
        if (route is null && allowEscapeTurn && !UsedEscapeTurn && remaining >= 4f && escapeFrom is { } readThreat
            && PlayDryRoutes.EscapeTurn(window, readThreat()) is { } turn
            && (!DepartingShelter || CanUseLandLeg(turn.Approach.End, gameTime) && CanUseLandLeg(turn.Exit, gameTime)))
        {
            route = turn.Approach;
            escapeOutlet = turn.Exit;
            UsedEscapeTurn = approachingEscapeTurn = true;
            log(new { kind = "play_predator_escape_turn", actor = player.PlayerData.Name, gameTime,
                corner = route.End.ToString(), outlet = turn.Exit.ToString() });
        }
        bool exploring = !DepartingShelter
            && (seek == "survey" || seek == "food" && foodTarget is null || seek == "shelter" && shelterTarget is null);
        bool targetedDetour = false;
        if (route is null && KeepsTarget && remaining >= 1f && goalDetours < 3)
        {
            route = PlayDryRoutes.Detour(window, direction, remaining, visits, end => CanUseLandLeg(end, gameTime));
            if (route is not null)
            {
                goalDetours++;
                targetedDetour = true;
                log(new { kind = "play_scout_target_detour", actor = player.PlayerData.Name, seek, gameTime,
                    target = (foodTarget?.Point ?? movingTarget ?? surveyTarget).ToString(), via = route.End.ToString(), attempts = goalDetours });
            }
        }
        if (route is null && exploring && !Evading && remaining >= 1f)
        {
            // 原方向封住时走完一段已确认的旁路，再交给模型评估新位置。
            route = PlayDryRoutes.Detour(window, direction, remaining, visits, end => CanUseLandLeg(end, gameTime));
            if (route is not null)
            {
                detouring = true;
                log(new { kind = "play_scout_detour", actor = player.PlayerData.Name, gameTime,
                    target = route.End.ToString() });
            }
        }
        if (route is not null)
        {
            aligned = route.Action is MoveTo;
            legs++;
            AtomicAction action = route.Action;
            if (!detouring && !targetedDetour && !approachingEscapeTurn && action is MoveAlongObservedRoute flat)
            {
                flat = LimitFinalFlat(flat);
                action = flat with { Continue = ContinueFlat };
            }
            Start(action, gameTime, route);
            return;
        }
        // 同一位置已经取得全向落脚证据，缺少可走路段时直接反馈。
        Finish("failed", "no_observed_dry_route");
    }

    private PlayDryRoutes.Leg? DryRoute(LocalObservationWindow window, float remaining, bool canAlign, bool towardLowerDrop,
        double gameTime)
    {
        if (escapeFrom is not { } readThreat)
            return PlayDryRoutes.Next(window, direction, remaining, canAlign, visits, towardLowerDrop,
                end => CanUseLandLeg(end, gameTime));

        var threat = readThreat();
        var origin = player.ComponentBody.Position;
        if (escapeOutlet is { } exit)
        {
            var offset = (exit - origin).XZ;
            if ((origin.XZ - threat).LengthSquared() >= EscapeTurnClearanceSquared && offset.LengthSquared() > 0.4f * 0.4f
                && PlayDryRoutes.Next(window, Vector2.Normalize(offset), Math.Min(remaining, offset.Length() + 0.2f),
                    canAlign, permitted: end => (end.XZ - threat).LengthSquared() >= EscapeTurnClearanceSquared
                        && (!DepartingShelter || CanUseLandLeg(end, gameTime))
                        && (end - exit).XZ.LengthSquared() < offset.LengthSquared()) is { } side)
                return side;
            escapeOutlet = null;
        }
        return PlayDryRoutes.Escape(window, threat, remaining, canAlign, end => Evading
            ? !refusedDryEndpoints.Contains(end) : CanUseLandLeg(end, gameTime));
    }

    private bool DepartingShelter => !Evading && escapeFrom is not null;

    private bool KeepsTarget => foodTarget is not null || readMovingTarget is not null || DepartingShelter
        || seek is "work" or "search" || seek == "shelter" && shelterTarget is not null;

    private static (int X, int Z) CellXZ(Vector3 point) => (Terrain.ToCell(point.X), Terrain.ToCell(point.Z));
    private static (int X, int Y, int Z) CellXYZ(Vector3 point) =>
        (Terrain.ToCell(point.X), Terrain.ToCell(point.Y + 0.05f), Terrain.ToCell(point.Z));

    private bool CanUseLandLeg(Vector3 end, double gameTime) => !refusedDryEndpoints.Contains(end)
        // 不同高度的同一列是不同落脚点，不能因走过下层就禁止跳回上层。
        && (!KeepsTarget || !visitedGoalCells.Contains(CellXYZ(end)))
        && permitLandLeg?.Invoke(end, gameTime) != false;

    private (MoveAlongObservedRoute Route, Action Accepted)? ContinueFlat(double gameTime)
    {
        var body = player.ComponentBody;
        if (gameTime >= deadline || legs >= LandLegLimit || !PlaySurvivalSenses.OnDryGround(body)
            || (!Evading && player.ComponentHealth.Health < initialHealth - 0.04f)
            || dryProof?.Contains(body, Stopwatch.GetElapsedTime(proofStarted).TotalSeconds) != true)
            return null;
        var (reached, approach) = ReadLandGoal(body);
        if (reached is not null) return null;
        float progress = (body.Position - legOrigin).XZ.Length();
        float remaining = RemainingOnLand(body, approach, traveled + progress);
        if (remaining < 1.1f) return null;
        bool towardLowerDrop = TowardLowerDrop(body, approach);
        var route = DryRoute(readDryWindow(), remaining, false, towardLowerDrop, gameTime);
        if (route?.Action is not MoveAlongObservedRoute next || !route.Contains(body, 0)) return null;
        // 低处拾取物需要对齐下台阶时，先结束当前平地段，避免连续改走侧向短步。
        if (towardLowerDrop && Vector2.Dot(Vector2.Normalize((route.End - body.Position).XZ), direction) < 0.9f)
            return null;

        next = LimitFinalFlat(next);
        return (next, () =>
        {
            var velocity = body.Velocity.XZ;
            float turn = velocity.LengthSquared() > 0.01f
                ? MathUtils.RadToDeg(Vector2.Angle(velocity, (next.Points[0] - body.Position).XZ)) : 0f;
            traveled += progress;
            if (KeepsTarget) visitedGoalCells.Add(CellXYZ(body.Position));
            legOrigin = body.Position;
            dryProof = route;
            proofStarted = Stopwatch.GetTimestamp();
            legs++;
            log(new { kind = "play_scout_continue", actor = player.PlayerData.Name, seek, gameTime,
                legs, traveled, speed = velocity.Length(), turn });
        });
    }

    private MoveAlongObservedRoute LimitFinalFlat(MoveAlongObservedRoute route)
    {
        Vector3 goal;
        if (Evading) return route;
        if (nextWaypoint < waypoints.Length) goal = waypoints[nextWaypoint];
        else if (seek is "survey" or "work" or "search") goal = surveyTarget;
        else if (seek is "camp" or "shelter" && shelterTarget is { } site) goal = site;
        else return route;
        var body = player.ComponentBody;
        var goalOffset = (goal - body.Position).XZ;
        var corridor = route.Points[^1] - body.Position;
        float length = corridor.XZ.Length();
        float along = Vector2.Dot(goalOffset, corridor.XZ) / length;
        if (along < 0.2f || along >= length) return route;
        // 保留末段之前的原生走廊节点，末端投影仍在已验证的直线内。
        var end = body.Position + along / length * corridor;
        var points = route.Points.TakeWhile(point => (point - body.Position).XZ.Length() < along - 0.11f).Append(end).ToArray();
        return route with { Points = points, FinalRange = 0.1f };
    }

    private (string? Reached, Vector3? Approach) ReadLandGoal(ComponentBody body)
    {
        Vector3? approach = null;
        if (seek == "search" && observe().Foods.Any(food => food.InReach && PlaySurvivalFood.CanSeek(player, food)))
            return ("food_within_reach", null);
        if (escapeFrom is { } currentThreat)
        {
            if ((body.Position - Origin).XZ.Length() >= (DepartingShelter ? distance : distance - 0.45f)
                && (escapeClearance <= 0f || (body.Position.XZ - currentThreat()).Length() >= escapeClearance))
                return ("escaped_along_supported_ground", null);
        }
        else if (seek is "survey" or "work" or "search")
        {
            // 带原生到达条件的任务还须核对楼层或交互状态，不能只靠平面距离提前完成。
            if (arrived is null && (surveyTarget - body.Position).XZ.LengthSquared() <= goalRange * goalRange)
                return ("navigation_goal_reached", null);
            approach = shelterTarget ?? surveyTarget;
        }
        else if (movingTarget is { } observed)
        {
            var delta = (observed - body.Position).XZ;
            float distanceToTarget = delta.Length();
            approach = distanceToTarget > movingTargetReach
                ? observed - (movingTargetReach - 0.2f) * new Vector3(delta.X, 0f, delta.Y) / distanceToTarget : observed;
        }
        else if (seek == "food")
        {
            var edible = foodTarget is not null && (foodTarget.Point - body.Position).XZ.LengthSquared() > 16f * 16f ? null
                : observe().Foods.Where(item => foodTarget is not null ? item.Matches(foodTarget)
                        : PlaySurvivalFood.CanApproachOnLand(item, body.Position) && PlaySurvivalFood.CanSeek(player, item))
                    .OrderBy(item => item.SicknessRisk).ThenBy(item => item.Distance).FirstOrDefault();
            if (edible?.InReach == true) return ("food_within_reach", null);
            approach = (edible ?? foodTarget)?.Point;
        }
        else if (seek == "shelter")
        {
            if (PlaySurvivalRest.HasSecureCover(player)) return ("enclosed_sleep_site_ready", null);
            if (PlayEarthShelter.At(body) is { } earth && (shelterTarget is not { } targetPoint
                || earth.X == Terrain.ToCell(targetPoint.X) && earth.Z == Terrain.ToCell(targetPoint.Z)
                    && earth.Surface == Terrain.ToCell(targetPoint.Y))) return ("soft_earth_shelter_site", null);
            approach = shelterTarget;
        }
        else if (seek == "camp" && shelterTarget is { } camp)
        {
            if ((camp - body.Position).XZ.LengthSquared() <= 0.45f * 0.45f)
                return ("remembered_earth_shelter_reached", null);
            approach = camp;
        }
        while (nextWaypoint < waypoints.Length && (waypoints[nextWaypoint] - body.Position).XZ.LengthSquared() <= 0.65f * 0.65f
            && MathF.Abs(waypoints[nextWaypoint].Y - body.Position.Y) <= 0.35f)
            nextWaypoint++;
        if (nextWaypoint < waypoints.Length) approach = waypoints[nextWaypoint];
        if (approach is { } destination)
        {
            var offset = (destination - body.Position).XZ;
            if (offset.LengthSquared() > 0.1f) direction = Vector2.Normalize(offset);
        }
        return (null, approach);
    }

    private float RemainingOnLand(ComponentBody body, Vector3? approach, float used) => approach is { } destination
        ? Math.Min(TravelLimit - used, (destination - body.Position).XZ.Length() + 0.2f) : TravelLimit - used;

    private bool TowardLowerDrop(ComponentBody body, Vector3? approach) =>
        foodTarget?.Source == "pickup" && approach is { } point && point.Y < body.Position.Y - 0.45f;

    internal static Vector2 Rotate(Vector2 source, float degrees)
    {
        float radians = MathUtils.DegToRad(degrees);
        float cosine = MathF.Cos(radians), sine = MathF.Sin(radians);
        var forward = Vector2.Normalize(source);
        return new(forward.X * cosine - forward.Y * sine, forward.X * sine + forward.Y * cosine);
    }
}
