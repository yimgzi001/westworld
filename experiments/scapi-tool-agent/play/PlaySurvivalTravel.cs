#nullable enable

using System.Diagnostics;
using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

internal sealed class PlaySurvivalTravel
{
    private readonly ComponentPlayer player;
    private readonly AtomicActions owner;
    private readonly string seek;
    private readonly float distance;
    private readonly float initialHealth;
    private readonly double deadline;
    private readonly Func<(JsonElement Observation, PlaySurvivalFood.Food[] Foods)> observe;
    private readonly Func<LocalObservationWindow> readDryWindow;
    private readonly Func<Position, int>? visits;
    private readonly PlaySurvivalFood.Food? foodTarget;
    private readonly Action<object> log;
    private Vector2 direction;
    private Vector3 legOrigin;
    private AtomicActionRun? leg;
    private PlayDryRoutes.Leg? dryProof;
    private NavigationLookPlan lookPlan = new(6);
    private long proofStarted;
    private int scans, legs, dryLooks;
    private float traveled;
    private bool detourUsed, jumpUsed, dryLeg, aligned;
    private PlaySurvivalSenses.Shore? lastShore;
    private double shoreSeenAt;
    private string? status, reason;

    public PlaySurvivalTravel(ComponentPlayer player, AtomicActions owner, float bearing, float distance,
        string seek, bool evading, double gameTime, Func<(JsonElement Observation, PlaySurvivalFood.Food[] Foods)> observe,
        Func<LocalObservationWindow> readDryWindow, Action<object> log, Func<Position, int>? visits = null,
        PlaySurvivalFood.Food? foodTarget = null)
    {
        this.player = player;
        this.owner = owner;
        this.seek = seek;
        this.distance = distance;
        this.observe = observe;
        this.readDryWindow = readDryWindow;
        this.visits = visits;
        this.foodTarget = foodTarget;
        this.log = log;
        Evading = evading;
        Origin = player.ComponentBody.Position;
        initialHealth = player.ComponentHealth.Health;
        deadline = gameTime + 25;
        direction = Rotate((BoatBody ?? player.ComponentBody).Matrix.Forward.XZ, bearing);
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
            if (progress >= 0.32f)
            {
                dryLooks = 0;
                lookPlan = new(6);
            }
        }
        else if (previous?.Action is Steer && previous.Result.Status != AtomicActionStatus.Succeeded)
            blocked = true;

        bool dry = body.StandingOnValue.HasValue && body.ImmersionDepth < 0.04f;
        if (seek == "dry_land" && dry)
        {
            Finish("succeeded", "dry_ground_reached");
            return;
        }
        var (observation, food) = observe();
        Vector3? approach = null;
        if (seek == "food" && dry)
        {
            var edible = food.Where(item => foodTarget is not null ? item.Matches(foodTarget)
                    : PlaySurvivalFood.CanApproachOnLand(item, body.Position) && !PlaySurvivalFood.IsRawMeat(item.Value)
                        && item.SicknessRisk < 0.5f).MinBy(item => item.Distance);
            if (edible?.InReach == true)
            {
                Finish("succeeded", "food_within_reach");
                return;
            }
            approach = (edible ?? foodTarget)?.Point;
            if (approach is { } foodPoint)
            {
                var offset = (foodPoint - body.Position).XZ;
                if (offset.LengthSquared() > 0.1f) direction = Vector2.Normalize(offset);
            }
        }
        if (seek == "shelter" && dry && player.ComponentSleep.CanSleep(out _))
        {
            Finish("succeeded", "native_sleep_site_ready");
            return;
        }
        if (legs >= 8 || traveled >= distance - 0.3f)
        {
            Finish(seek == "survey" && traveled >= 1f ? "succeeded" : "failed", "travel_distance_completed");
            return;
        }
        if (dry)
        {
            float remaining = approach is { } destination ? Math.Min(distance - traveled, (destination - body.Position).XZ.Length() + 0.2f)
                : distance - traveled;
            AdvanceOnLand(body, blocked, remaining, gameTime);
            return;
        }
        dryLeg = false;
        var boat = BoatBody;
        if (!dry && seek == "dry_land" && PlaySurvivalSenses.VisibleShore(observation, body, boat) is { } visible)
        {
            lastShore = visible;
            shoreSeenAt = gameTime;
        }
        var shore = gameTime - shoreSeenAt <= 4 ? lastShore : null;
        Vector3? landing = shore is { } point ? new Vector3(point.X, point.Y, point.Z) : null;
        float shoreDistance = landing.HasValue ? (landing.Value - body.Position).XZ.Length() : float.MaxValue;
        var heading = shoreDistance > 0.18f && landing.HasValue
            ? Vector2.Normalize((landing.Value - body.Position).XZ) : direction;

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
            if (seek == "dry_land" && !landing.HasValue && !Evading)
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

    private void AdvanceOnLand(ComponentBody body, bool blocked, float remaining, double gameTime)
    {
        dryLeg = true;
        if (blocked) direction = Rotate(direction, 50);
        var window = readDryWindow();
        var route = PlayDryRoutes.Next(window, direction, remaining, !aligned, visits);
        if (route is not null)
        {
            aligned = route.Action is MoveTo;
            legs++;
            Start(route.Action, gameTime, route);
            return;
        }
        var eye = player.ComponentCreatureModel.EyePosition;
        if (dryLooks == 0 && Vector2.Dot(Vector2.Normalize(body.Matrix.Forward.XZ), direction) < 0.85f)
        {
            dryLooks++;
            Start(new FaceTowards(body.Position + 3f * new Vector3(direction.X, 0f, direction.Y), 3f, -12f), gameTime);
            return;
        }
        var missing = LocalNavigationSkillGeometry.MissingCells(window, allowObservedSandAndGravel: true);
        var observation = lookPlan.Next(window, new(eye.X, eye.Y, eye.Z), missing, maximumKnownAge: 1.5);
        if (observation.Look is { } turn)
        {
            Start(new FaceTowards(new Vector3((float)turn.Target.X, (float)turn.Target.Y, (float)turn.Target.Z), 3f,
                (float)Math.Clamp(turn.PitchRadians * 180 / Math.PI, -75, 75)), gameTime);
            return;
        }
        if (dryLooks >= 4)
        {
            Finish("failed", "no_observed_dry_route");
            return;
        }
        float offset = dryLooks switch { 0 => 0f, 1 => 55f, 2 => -55f, _ => 100f };
        dryLooks++;
        var look = Rotate(direction, offset);
        // 只有连续走廊与常规观察都缺少落脚证据时，才专门低头补看。
        Start(new FaceTowards(body.Position + 2f * new Vector3(look.X, 0f, look.Y), 3f, -28f), gameTime);
    }

    internal static Vector2 Rotate(Vector2 source, float degrees)
    {
        float radians = MathUtils.DegToRad(degrees);
        float cosine = MathF.Cos(radians), sine = MathF.Sin(radians);
        var forward = Vector2.Normalize(source);
        return new(forward.X * cosine - forward.Y * sine, forward.X * sine + forward.Y * cosine);
    }
}
