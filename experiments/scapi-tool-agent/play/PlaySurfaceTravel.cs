#nullable enable

using Engine;
using Game;
using Game.Westworld;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

internal sealed class PlaySurfaceTravel
{
    private readonly ComponentPlayer player;
    private readonly AtomicActions owner;
    private readonly Func<LocalObservationWindow> readDryWindow;
    private readonly Func<Vector3, double, bool>? permitted;
    private readonly Func<Vector3?>? readTarget;
    private readonly Func<bool>? targetReached;
    private readonly Action<object> log;
    private readonly HashSet<Point3> refused = new();
    private readonly double deadline;
    private readonly float health;
    private PlaySurfacePath.Plan? plan;
    private int index, replans, fineApproaches;
    private AtomicActionRun? run;
    private PlayDryRoutes.Leg? dryProof;
    private long proofStarted;
    private Vector3? target;
    private Vector3? plannedTarget;
    private Vector3 legOrigin;
    private double nextCheck, missingSince = -1;

    public PlaySurfaceTravel(ComponentPlayer player, AtomicActions owner, double now, Func<LocalObservationWindow> readDryWindow,
        Action<object> log, Func<Vector3, double, bool>? permitted = null, Func<Vector3?>? readTarget = null,
        Func<bool>? targetReached = null)
    {
        this.player = player; this.owner = owner; this.readDryWindow = readDryWindow; this.log = log;
        this.permitted = permitted; this.readTarget = readTarget;
        this.targetReached = targetReached;
        deadline = now + 40; health = player.ComponentHealth.Health;
    }

    public string? Status { get; private set; }
    public string? Reason { get; private set; }

    public void Update(double now)
    {
        if (Status is not null) return;
        var body = player.ComponentBody;
        if (now >= deadline || player.ComponentSleep.IsSleeping || player.ComponentHealth.Health < health - .04f
            || player.ComponentVitalStats.Stamina < .12f)
        { Finish(false, "surface_route_interrupted"); return; }
        bool safe = PlaySurvivalSenses.Threats(player, owner.Queries, 24)
            .All(threat => !PlaySurvivalSenses.NeedsAvoidance(threat, true));
        if (!safe) { Finish(false, "surface_route_predator"); return; }
        if (readTarget is null && run?.Result.Status != AtomicActionStatus.Running
            && PlaySurvivalSenses.OnStableDryGround(body))
        { Finish(true, "dry_ground_reached"); return; }
        if (now >= nextCheck)
        {
            nextCheck = now + .25;
            if (readTarget is not null)
            {
                var seen = readTarget();
                if (seen is null)
                {
                    if (missingSince < 0) missingSince = now;
                    if (now - missingSince > 1.5) { Finish(false, "surface_target_no_longer_visible"); return; }
                }
                else
                {
                    missingSince = -1;
                    bool drifted = plannedTarget is { } old && (old - seen.Value).XZ.LengthSquared() > .65f * .65f;
                    target = seen;
                    if (drifted)
                    {
                        Stop(); plan = null; fineApproaches = 0;
                        log(new { kind = "play_surface_target_drift", actor = player.PlayerData.Name, target = target.ToString(), gameTime = now });
                    }
                }
            }
            if (targetReached?.Invoke() == true) { Finish(true, "surface_target_in_range"); return; }
            if (plan is not null && index < plan.Nodes.Length && permitted?.Invoke(plan.Nodes[index].Point, now) == false)
            { Finish(false, "surface_route_new_hazard"); return; }
        }
        if (run is { } active)
        {
            if (active.Result.Status == AtomicActionStatus.Running)
            {
                if (dryProof?.Rejection(body, System.Diagnostics.Stopwatch.GetElapsedTime(proofStarted).TotalSeconds) is not null)
                    owner.Cancel(active);
                else return;
            }
            if (active.Result.Status != AtomicActionStatus.Succeeded)
            {
                if (plan is not null && index < plan.Nodes.Length) refused.Add(plan.Nodes[index].Cell);
                log(new { kind = "play_surface_replan", actor = player.PlayerData.Name, failure = active.Result.Failure.ToString(),
                    displacement = (body.Position - legOrigin).XZ.Length(), gameTime = now });
                plan = null;
                if (++replans > 3) { Finish(false, "surface_route_blocked"); return; }
            }
            else if (plan is not null && index < plan.Nodes.Length
                && (body.Position - plan.Nodes[index].Point).XZ.LengthSquared() <= .5f * .5f
                && MathF.Abs(body.Position.Y - plan.Nodes[index].Point.Y) < .55f) index++;
            run = null;
            dryProof = null;
        }
        if (plan is null)
        {
            var panorama = owner.Queries.ReadPanorama(26);
            plan = PlaySurfacePath.Find(panorama, body.Position, target, end => permitted?.Invoke(end, now) != false, refused);
            if (plan is null) { Finish(false, "no_observed_surface_route"); return; }
            // 水面取物必须同时有已见干地出口；不为一块肉跳进只有高墙的水坑。
            if (target is not null && plan.HasWater && PlaySurfacePath.Find(panorama, plan.Nodes[^1].Point, null,
                end => permitted?.Invoke(end, now) != false) is null)
            { Finish(false, "surface_loot_has_no_exit"); return; }
            index = plan.Nodes.Length > 1 ? 1 : 0;
            plannedTarget = target;
            log(new { kind = "play_surface_path", actor = player.PlayerData.Name, nodes = plan.Nodes.Length, plan.Cost,
                plan.HasWater, target = target?.ToString(), gameTime = now });
        }
        if (index >= plan.Nodes.Length)
        {
            if (readTarget is not null && targetReached is not null && !targetReached())
            {
                var last = plan.Nodes[^1];
                if (fineApproaches++ < 2 && last.Water && target is { } exact
                    && Terrain.ToCell(exact.X) == last.Cell.X && Terrain.ToCell(exact.Z) == last.Cell.Z)
                {
                    var point = new Vector3(Math.Clamp(exact.X, last.Cell.X + .26f, last.Cell.X + .74f), last.Point.Y,
                        Math.Clamp(exact.Z, last.Cell.Z + .26f, last.Cell.Z + .74f));
                    legOrigin = body.Position;
                    run = owner.Start(new TraverseSurface(point, last.Cell.Y, false));
                    log(new { kind = "play_surface_pickup_alignment", actor = player.PlayerData.Name, gameTime = now });
                    return;
                }
                Finish(false, "surface_target_outside_pickup_range");
            }
            else if (readTarget is not null) Finish(true, "surface_target_in_range");
            else Finish(false, "surface_landing_not_settled");
            return;
        }
        var next = plan.Nodes[index];
        var previous = plan.Nodes[Math.Max(0, index - 1)];
        var fresh = owner.Queries.ReadPanorama(5);
        if (fresh is null || !fresh.Blocks.Any(block => block.BareTop && block.Cell + new Point3(0, 1, 0) == next.Cell))
        {
            refused.Add(next.Cell); plan = null;
            if (++replans > 3) Finish(false, "surface_route_evidence_lost");
            return;
        }
        AtomicAction action;
        if (next.Water || previous.Water || body.ImmersionDepth > .04f)
            action = new TraverseSurface(next.Point, next.Water ? next.Cell.Y : previous.Cell.Y, !next.Water)
                { Continue = ContinueWater };
        else
        {
            var offset = (next.Point - body.Position).XZ;
            var dry = PlayDryRoutes.Next(readDryWindow(), Vector2.Normalize(offset), offset.Length() + .1f, true,
                towardLowerDrop: next.Point.Y < body.Position.Y - .4f, permitted: end => permitted?.Invoke(end, now) != false);
            if (dry is null) { Finish(false, "surface_approach_no_dry_proof"); return; }
            action = dry.Action;
            dryProof = dry;
            proofStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        legOrigin = body.Position;
        run = owner.Start(action);
        log(new { kind = "play_surface_step", actor = player.PlayerData.Name, action = action.GetType().Name,
            target = next.Point.ToString(), landing = !next.Water, index, gameTime = now });
    }

    private (TraverseSurface Route, Action Accepted)? ContinueWater(double now)
    {
        if (Status is not null || now >= deadline || plan is null || index + 1 >= plan.Nodes.Length) return null;
        var current = plan.Nodes[index];
        var next = plan.Nodes[index + 1];
        var before = plan.Nodes[Math.Max(0, index - 1)];
        if (!current.Water || !next.Water || current.Cell.Y != next.Cell.Y
            || current.Cell - before.Cell != next.Cell - current.Cell || permitted?.Invoke(next.Point, now) == false)
            return null;
        var fresh = owner.Queries.ReadPanorama(5);
        if (fresh?.Blocks.Any(block => block.BareTop && block.Cell + new Point3(0, 1, 0) == next.Cell
            && BlocksManager.Blocks[Terrain.ExtractContents(block.Value)] is WaterBlock) != true) return null;
        var route = new TraverseSurface(next.Point, next.Cell.Y, false) { Continue = ContinueWater };
        return (route, () =>
        {
            index++; legOrigin = player.ComponentBody.Position;
            log(new { kind = "play_surface_continue", actor = player.PlayerData.Name, index, gameTime = now,
                speed = player.ComponentBody.Velocity.XZ.Length() });
        });
    }

    public void Stop()
    {
        if (run?.Result.Status == AtomicActionStatus.Running) owner.Cancel(run);
        run = null;
    }
    private void Finish(bool succeeded, string reason) { Stop(); Status = succeeded ? "succeeded" : "failed"; Reason = reason; }
}
