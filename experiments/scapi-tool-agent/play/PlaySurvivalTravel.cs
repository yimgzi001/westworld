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
    private enum SeatAim { None, Turning, Ready }
    private readonly ComponentPlayer player;
    private readonly AtomicActions owner;
    private readonly string seek;
    private readonly float distance, landDistanceLimit;
    private readonly float initialHealth;
    private readonly double deadline;
    private readonly Func<(JsonElement Observation, PlaySurvivalFood.Food[] Foods)> observe;
    private readonly Func<LocalObservationWindow> readDryWindow;
    private readonly Func<Position, int>? visits;
    private readonly PlaySurvivalFood.Food? foodTarget;
    private readonly int? initialFoodStock;
    private readonly Vector3? shelterTarget;
    private readonly Vector3? shoreTarget;
    private readonly Vector3 surveyTarget;
    private readonly Func<Vector2>? escapeFrom;
    private readonly Action<object> log;
    private Vector2 direction;
    private Vector3 legOrigin;
    private AtomicActionRun? leg;
    private PlayDryRoutes.Leg? dryProof;
    private long proofStarted;
    private int scans, legs;
    private float traveled;
    private bool detourUsed, jumpUsed, dryLeg, aligned, detouring;
    private SeatAim seatAim;
    private PlaySurvivalSenses.Shore? lastShore;
    private double shoreSeenAt;
    private string? status, reason;

    public PlaySurvivalTravel(ComponentPlayer player, AtomicActions owner, float bearing, float distance,
        string seek, bool evading, double gameTime, Func<(JsonElement Observation, PlaySurvivalFood.Food[] Foods)> observe,
        Func<LocalObservationWindow> readDryWindow, Action<object> log, Func<Position, int>? visits = null,
        PlaySurvivalFood.Food? foodTarget = null, Vector3? shelterTarget = null, Vector3? shoreTarget = null,
        Func<Vector2>? escapeFrom = null)
    {
        this.player = player;
        this.owner = owner;
        this.seek = seek;
        this.distance = distance;
        this.observe = observe;
        this.readDryWindow = readDryWindow;
        this.visits = visits;
        this.foodTarget = foodTarget;
        if (foodTarget is not null)
            initialFoodStock = NativeAcquisitionAudit.InventoryCount(player.ComponentMiner.Inventory, foodTarget.Value);
        this.shelterTarget = shelterTarget;
        this.shoreTarget = shoreTarget;
        this.escapeFrom = escapeFrom;
        this.log = log;
        Evading = evading;
        Origin = player.ComponentBody.Position;
        initialHealth = player.ComponentHealth.Health;
        deadline = gameTime + 25;
        direction = Rotate((BoatBody ?? player.ComponentBody).Matrix.Forward.XZ, bearing);
        var offset = distance * new Vector3(direction.X, 0, direction.Y);
        surveyTarget = Origin + offset;
        // 连续走廊可能沿两条轴绕行。以实际轴向距离留出余量，避免斜向目标还没到就用完步程。
        landDistanceLimit = escapeFrom is not null ? distance + 6f
            : seek is "survey" or "camp" ? MathF.Abs(offset.X) + MathF.Abs(offset.Z) + Math.Max(1.5f, distance * 0.15f) : distance;
    }

    public bool Evading { get; }
    public bool HeadingForLand => seek == "dry_land";
    public Vector3 Origin { get; }
    public string? Status => status;
    public string? Reason => reason;
    public int Legs => legs;
    private ComponentBody? BoatBody => player.Entity.FindComponent<ComponentRider>()?.Mount is { } mount
        && mount.Entity.FindComponent<ComponentBoat>() is not null ? mount.ComponentBody : null;

    public void Update(double gameTime)
    {
        if (status is not null) return;
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
        if (leg?.Result.Status == AtomicActionStatus.Running)
        {
            if (dryProof is null || dryProof.Contains(body, Stopwatch.GetElapsedTime(proofStarted).TotalSeconds)) return;
            owner.Cancel(leg);
            log(new { kind = "play_scout_evidence_expired", actor = player.PlayerData.Name, gameTime,
                action = leg.Action.GetType().Name });
        }

        var previous = leg;
        leg = null;
        dryProof = null;
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
            Finish(moved ? "succeeded" : "failed", moved ? "scout_detoured_to_open_ground" : "no_observed_dry_route");
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
            if (legs >= LandLegLimit || traveled >= landDistanceLimit - 0.3f)
            {
                Finish("failed", "navigation_distance_limit");
                return;
            }
            AdvanceOnLand(blocked, RemainingOnLand(body, approach, traveled), gameTime);
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
        if (leg?.Result.Status == AtomicActionStatus.Running) owner.Cancel(leg);
        leg = null;
        dryProof = null;
    }

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

    private void AdvanceOnLand(bool blocked, float remaining, double gameTime)
    {
        dryLeg = true;
        if (blocked) direction = Rotate(direction, 50);
        var window = readDryWindow();
        var route = DryRoute(window, remaining, !aligned);
        if (route is null && seek == "survey" && !Evading && remaining >= 1f)
        {
            // 原方向封住时走完一段已确认的旁路，再交给模型评估新位置。
            route = PlayDryRoutes.Detour(window, direction, remaining, visits);
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
            if (!detouring && action is MoveAlongObservedRoute flat)
            {
                var body = player.ComponentBody;
                var goalOffset = ((seek == "camp" ? shelterTarget ?? surveyTarget : surveyTarget) - body.Position).XZ;
                if ((seek is "survey" or "camp") && !Evading && goalOffset.LengthSquared() < 1 && flat.Points.Count == 1)
                {
                    // 最后一小段沿已验证的走廊收短，避免为一格内的目标再走过头。
                    var corridor = flat.Points[0] - body.Position;
                    float length = corridor.XZ.Length();
                    float along = Vector2.Dot(goalOffset, corridor.XZ) / length;
                    if (along >= 0.2f && along < length)
                        flat = flat with { Points = [body.Position + along / length * corridor], FinalRange = 0.1f };
                }
                action = flat with { Continue = ContinueFlat };
            }
            Start(action, gameTime, route);
            return;
        }
        // 同一位置已经取得全向落脚证据，缺少可走路段时直接反馈。
        Finish("failed", "no_observed_dry_route");
    }

    private PlayDryRoutes.Leg? DryRoute(LocalObservationWindow window, float remaining, bool canAlign) =>
        escapeFrom is { } readThreat ? PlayDryRoutes.Escape(window, readThreat(), remaining, canAlign)
            : PlayDryRoutes.Next(window, direction, remaining, canAlign, visits);

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
        var route = DryRoute(readDryWindow(), remaining, false);
        if (route?.Action is not MoveAlongObservedRoute next || !route.Contains(body, 0)) return null;

        return (next, () =>
        {
            var velocity = body.Velocity.XZ;
            float turn = velocity.LengthSquared() > 0.01f
                ? MathUtils.RadToDeg(Vector2.Angle(velocity, (next.Points[0] - body.Position).XZ)) : 0f;
            traveled += progress;
            legOrigin = body.Position;
            dryProof = route;
            proofStarted = Stopwatch.GetTimestamp();
            legs++;
            log(new { kind = "play_scout_continue", actor = player.PlayerData.Name, seek, gameTime,
                legs, traveled, speed = velocity.Length(), turn });
        });
    }

    private (string? Reached, Vector3? Approach) ReadLandGoal(ComponentBody body)
    {
        Vector3? approach = null;
        if (escapeFrom is not null)
        {
            if ((body.Position - Origin).XZ.Length() >= distance - 0.45f)
                return ("escaped_along_supported_ground", null);
        }
        else if (seek == "survey")
        {
            if ((surveyTarget - body.Position).XZ.LengthSquared() <= 0.45f * 0.45f)
                return ("navigation_goal_reached", null);
            approach = surveyTarget;
        }
        else if (seek == "food")
        {
            if (foodTarget is not null && initialFoodStock is int before
                && NativeAcquisitionAudit.InventoryCount(player.ComponentMiner.Inventory, foodTarget.Value) is int after && after > before)
                return ("food_collected_en_route", null);
            var (_, food) = observe();
            var edible = food.Where(item => foodTarget is not null ? item.Matches(foodTarget)
                    : PlaySurvivalFood.CanApproachOnLand(item, body.Position) && PlaySurvivalFood.CanSeek(player, item))
                .OrderBy(item => item.SicknessRisk).ThenBy(item => item.Distance).FirstOrDefault();
            if (edible?.InReach == true) return ("food_within_reach", null);
            approach = (edible ?? foodTarget)?.Point;
        }
        else if (seek == "shelter")
        {
            if (PlaySurvivalRest.HasSecureCover(player)) return ("enclosed_sleep_site_ready", null);
            if (PlayEarthShelter.At(body) is not null) return ("soft_earth_shelter_site", null);
            approach = shelterTarget;
        }
        else if (seek == "camp" && shelterTarget is { } camp)
        {
            if ((camp - body.Position).XZ.LengthSquared() <= 0.45f * 0.45f)
                return ("remembered_earth_shelter_reached", null);
            approach = camp;
        }
        if (approach is { } destination)
        {
            var offset = (destination - body.Position).XZ;
            if (offset.LengthSquared() > 0.1f) direction = Vector2.Normalize(offset);
        }
        return (null, approach);
    }

    private float RemainingOnLand(ComponentBody body, Vector3? approach, float used) => approach is { } destination
        ? Math.Min(landDistanceLimit - used, (destination - body.Position).XZ.Length() + 0.2f) : landDistanceLimit - used;

    internal static Vector2 Rotate(Vector2 source, float degrees)
    {
        float radians = MathUtils.DegToRad(degrees);
        float cosine = MathF.Cos(radians), sine = MathF.Sin(radians);
        var forward = Vector2.Normalize(source);
        return new(forward.X * cosine - forward.Y * sine, forward.X * sine + forward.Y * cosine);
    }
}
