using Engine;
using Game;
using Game.Westworld;

sealed partial class Runner
{
    private bool contactInjected;
    private Vector3 contactOrigin;
    private double contactStarted;
    private int contactGaps;
    private int? contactOriginalSupport;
    private bool contactRestored;
    private void RunContactGap()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        bool persistent = mode.EndsWith("persistent", StringComparison.Ordinal);
        bool stepUp = mode.Contains("step", StringComparison.Ordinal);
        bool corner = mode.EndsWith("corner", StringComparison.Ordinal);
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            for (int x = cell.X - 3; x <= cell.X + 7; x++)
            for (int z = cell.Z - 3; z <= cell.Z + 3; z++)
            for (int y = cell.Y - 2; y <= cell.Y + 3; y++) terrain.ChangeCell(x, y, z, y < cell.Y + (stepUp && x > cell.X ? 1 : 0) ? DirtBlock.Index : 0);
            body.Position = new(cell.X + .5f, cell.Y, cell.Z + .5f);
            body.Velocity = Vector3.Zero;
            body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
            stage = 1; elapsed = 0; return;
        }
        if (elapsed < .8) return;
        if (run is null)
        {
            if (!PlaySurvivalSenses.OnStableDryGround(body)) return;
            contactOrigin = body.Position; contactStarted = now;
            if (corner)
            {
                body.Rotation = Quaternion.Identity;
                body.Velocity = new(0, 0, 1.2f);
            }
            run = stepUp ? actions.Start(new StepTo(body.Position + new Vector3(1, 1, 0), LevelGaze: true))
                : actions.Start(new MoveAlongObservedRoute(Enumerable.Range(1, 3)
                .Select(step => body.Position + step * Vector3.UnitX).ToArray(), LevelGaze: true));
        }
        if (run.Result.Status == AtomicActionStatus.Running)
        {
            if (!corner && (!contactInjected || persistent))
            {
                // 精确复现原生瞬时接触标记缺失：地面、高度和速度不变，不赠送移动或修改物理规则。
                contactOriginalSupport ??= body.StandingOnValue;
                body.StandingOnValue = null;
                contactInjected = true; contactGaps++;
            }
            else if (!corner && !persistent && !contactRestored)
            {
                // 注入严格只持续一帧。地面未改，恢复原标记，避免把单帧场误变成多帧持续缺失。
                if (MathF.Abs(body.Position.Y - contactOrigin.Y) > .02f)
                    throw new InvalidOperationException("接触注入恢复前身体已离开原地面。");
                body.StandingOnValue ??= contactOriginalSupport;
                contactRestored = true;
            }
            return;
        }
        float moved = (body.Position - contactOrigin).XZ.Length();
        Passed = persistent ? contactInjected && run.Result.Failure == AtomicActionFailure.MovementInterrupted
                && now - contactStarted < .4 && moved < .1f
            : (corner || contactInjected) && run.Result.Status == AtomicActionStatus.Succeeded
                && (stepUp ? moved > .75f && body.Position.Y > contactOrigin.Y + .95f : moved > 2.7f);
        Done = true;
        Gate.Log(new { kind = "contact_gap_result", mode, Passed, contactInjected, contactGaps,
            status = run.Result.Status.ToString(), failure = run.Result.Failure.ToString(), moved, duration = now - contactStarted,
            contactRestored, corner });
    }
}
