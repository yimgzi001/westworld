#nullable enable

using Engine;
using Game;
using Game.Westworld;

internal sealed class PlayPredatorHearing(ComponentPlayer player)
{
    private const double MemorySeconds = 6;
    private readonly List<Cue> recent = new();
    private sealed record Cue(NativePredatorAcoustics.Kind Sound, int Sector, string Range, bool Muffled,
        Vector3 HeardFrom, double Time)
    {
        public Vector2 Direction => PlaySurvivalTravel.Rotate(Vector2.UnitX, Sector * 45);
    }

    public object? Hear(NativePredatorAcoustics.Signal signal)
    {
        if (!ReferenceEquals(signal.World, player.Project) || player.ComponentHealth.Health <= 0
            || player.ComponentSleep.IsSleeping) return null;
        var body = player.ComponentBody;
        var ear = new Vector3(body.Position.X, body.BoundingBox.Max.Y - 0.15f, body.Position.Z);
        var source = signal.Origin + 0.55f * Vector3.UnitY;
        var offset = source - ear;
        float distance = offset.Length();
        if (distance < 0.01f || distance > signal.MaxDistance || offset.XZ.LengthSquared() < 0.04f) return null;
        var audio = player.Project.FindSubsystem<SubsystemAudio>(true);
        float volume = signal.Gain * audio.CalculateVolume(distance, signal.ReferenceDistance);
        var terrain = player.Project.FindSubsystem<SubsystemTerrain>(true);
        bool muffled = terrain.Raycast(ear, source, false, true, (value, _) =>
            !BlocksManager.Blocks[Terrain.ExtractContents(value)].IsTransparent_(value)) is not null;
        if (muffled) volume *= 0.5f;
        if (volume < 0.1f || muffled && distance > 12f) return null;

        int sector = (int)MathF.Round(MathUtils.RadToDeg(Vector2.Angle(Vector2.UnitX, offset.XZ)) / 45);
        if (sector == -4) sector = 4;
        string range = distance <= 6f ? "near" : distance <= 12f ? "middle" : "far";
        recent.RemoveAll(cue => !Fresh(cue, signal.GameTime, body.Position));
        int prior = recent.FindIndex(cue => cue.Sector == sector && cue.Sound == signal.Sound && cue.Range == range);
        var heard = new Cue(signal.Sound, sector, range, muffled, body.Position, signal.GameTime);
        if (prior >= 0)
        {
            recent[prior] = heard;
            return null;
        }
        if (recent.Count >= 6) recent.RemoveAt(0);
        recent.Add(heard);
        return Describe(heard, signal.GameTime, body.Matrix.Forward.XZ);
    }

    public object[]? Snapshot(double now)
    {
        var body = player.ComponentBody;
        var cues = recent.Where(cue => Fresh(cue, now, body.Position))
            .Select(cue => Describe(cue, now, body.Matrix.Forward.XZ)).ToArray();
        return cues.Length == 0 ? null : cues;
    }

    public bool Nearby(double now) => recent.Any(cue => cue.Range == "near" && Fresh(cue, now, player.ComponentBody.Position));

    public bool AllowsRoute(Vector3 start, Vector3 end, double now)
    {
        var offset = (end - start).XZ;
        if (offset.LengthSquared() < 0.04f) return true;
        return !recent.Any(cue => cue.Range != "far" && Fresh(cue, now, start)
            && Vector2.Dot(Vector2.Normalize(offset), cue.Direction) >= 0.72f);
    }

    private static bool Fresh(Cue cue, double now, Vector3 at) =>
        now - cue.Time is >= 0 and <= MemorySeconds && (at - cue.HeardFrom).LengthSquared() <= 16f;

    private static object Describe(Cue cue, double now, Vector2 facing) => new
    {
        sound = cue.Sound.ToString().ToLowerInvariant(),
        bearing = (int)MathF.Round(MathUtils.RadToDeg(Vector2.Angle(facing, cue.Direction)) / 45) * 45,
        range = cue.Range, muffled = cue.Muffled, secondsAgo = Math.Round(now - cue.Time, 1)
    };
}
