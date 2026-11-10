using System.Text.Json;
using Engine;
using Game;

sealed partial class Runner
{
    private Point3 navigationOrigin;
    private int continuations, stationaryLooks;
    private float slowestContinuation = float.PositiveInfinity;
    private float biggestContinuousTurn;

    private void RunRollingNavigation()
    {
        var body = p.ComponentBody;
        double now = p.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        bool cliff = mode == "navigate-drop";
        bool turn = mode == "navigate-turn";
        if (stage == 0)
        {
            if (elapsed < 3) return;
            var cell = Terrain.ToCell(body.Position);
            int endZ = cell.Z + (turn ? 9 : 4);
            for (int x = (cell.X - 3) & ~15; x <= cell.X + 22; x += 16)
            for (int z = (cell.Z - 4) & ~15; z <= endZ; z += 16)
                if (!(terrain.Terrain.GetChunkAtCell(x, z)?.State >= TerrainChunkState.InvalidVertices1)) return;
            navigationOrigin = cell;
            for (int x = cell.X - 3; x <= cell.X + 22; x++)
            for (int z = cell.Z - 4; z <= endZ; z++)
            {
                int floor = cliff && x >= cell.X + 8 ? cell.Y - 4 : !cliff && !turn && x >= cell.X + 9 ? cell.Y : cell.Y - 1;
                for (int y = cell.Y - 4; y <= cell.Y + 4; y++)
                    terrain.ChangeCell(x, y, z, y <= floor ? DirtBlock.Index : 0);
            }
            if (turn)
                for (int y = cell.Y; y < cell.Y + 2; y++) terrain.ChangeCell(cell.X + 6, y, cell.Z, GraniteBlock.Index);
            body.Position = new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            body.Velocity = Vector3.Zero;
            body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
            elapsed = 0;
            stage = 1;
            return;
        }
        if (elapsed < 1) return;
        travel ??= new(p, actions, turn ? 20 : 0, 18, "survey", false, now,
            () => (JsonSerializer.SerializeToElement(new { }), []),
            () => memory.Observe(senses.Observe(senses.SessionId, actor, allDirections: true)), TrackNavigation);
        jumped |= p.ComponentLocomotion.LastJumpOrder > 0;
        if (cliff && (body.BoundingBox.Max.X > navigationOrigin.X + 8.03f || body.Position.Y < navigationOrigin.Y - 0.05f))
            throw new InvalidOperationException("移动进入了无有效落脚证据的断崖。");
        travel.Update(now);
        if (travel.Status is null) return;

        float moved = body.Position.X - travel.Origin.X;
        bool continuousMovement = continuations >= (cliff ? 1 : 3) && slowestContinuation > 0.7f && stationaryLooks == 0;
        var destination = travel.Origin.XZ + 18 * PlaySurvivalTravel.Rotate(Vector2.UnitX, turn ? 20 : 0);
        float error = (body.Position.XZ - destination).Length();
        bool completed = cliff ? travel.Reason == "no_observed_dry_route" && moved > 6f && !jumped
            : travel.Status == "succeeded" && error <= 0.45f && (turn ? biggestContinuousTurn > 35f : jumped);
        Passed = continuousMovement && completed && PlaySurvivalSenses.OnDryGround(body)
            && (cliff || turn || body.Position.Y >= navigationOrigin.Y + 0.95f);
        Done = true;
        Gate.Log(new { kind = "rolling_navigation_result", Passed, cliff, turn, travel.Status, travel.Reason, travel.Legs,
            continuations, slowestContinuation = float.IsFinite(slowestContinuation) ? (float?)slowestContinuation : null,
            biggestContinuousTurn, stationaryLooks, jumped, moved, error, end = body.Position.ToString() });
    }

    private void TrackNavigation(object value)
    {
        Gate.Log(value);
        var entry = JsonSerializer.SerializeToElement(value);
        switch (entry.GetProperty("kind").GetString())
        {
            case "play_scout_continue":
                continuations++;
                slowestContinuation = MathF.Min(slowestContinuation, entry.GetProperty("speed").GetSingle());
                biggestContinuousTurn = MathF.Max(biggestContinuousTurn, MathF.Abs(entry.GetProperty("turn").GetSingle()));
                break;
            case "play_scout_step" when entry.GetProperty("action").GetString() == "FaceTowards":
                stationaryLooks++;
                break;
        }
    }
}
