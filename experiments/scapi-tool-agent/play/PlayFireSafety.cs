#nullable enable

using Engine;
using Game;
using Westworld.Runtime.Core;

internal static class PlayFireSafety
{
    internal sealed record Heat(Vector3 Center, BoundingBox Flame);

    public static Heat[] Observe(LocalObservationWindow window) => window.Cells
        .Where(cell => cell.Age <= 3 && IsFire(cell.Value))
        .Select(cell =>
        {
            var minimum = new Vector3(cell.X, cell.Y, cell.Z);
            bool campfire = Terrain.ExtractContents(cell.Value) == CampfireBlock.Index;
            return new Heat(minimum + new Vector3(0.5f), new(minimum + new Vector3(campfire ? 0.2f : 0f),
                minimum + new Vector3(campfire ? 0.8f : 1f)));
        }).ToArray();

    private static bool IsFire(int value) => Terrain.ExtractContents(value) == FireBlock.Index
        || Terrain.ExtractContents(value) == CampfireBlock.Index && Terrain.ExtractData(value) > 0;

    public static bool CampfireSpacingSafe(ComponentPlayer player, Point3 cell)
    {
        var body = player.ComponentBody.BoundingBox;
        var clearance = new Vector3(0.35f);
        var origin = new Vector3(cell);
        return !new BoundingBox(body.Min - clearance, body.Max + clearance)
            .Intersection(new BoundingBox(origin + new Vector3(0.2f), origin + new Vector3(0.8f)));
    }

    public static bool AllowsRoute(ComponentBody body, Vector3 end, IReadOnlyList<Heat> heat)
    {
        var origin = body.Position;
        var delta = end - origin;
        float length = delta.Length();
        var ray = new Ray3(origin, length > 0.001f ? delta / length : Vector3.UnitX);
        var bounds = body.BoundingBox;
        // 原生着火判定将身体向外扩 0.25 格。再留出移动余量，整段路都避开接触区。
        var lower = origin - bounds.Min + new Vector3(0.4f);
        var upper = bounds.Max - origin + new Vector3(0.4f);
        foreach (var source in heat)
        {
            var danger = new BoundingBox(source.Flame.Min - upper, source.Flame.Max + lower);
            if (danger.Contains(origin))
            {
                // 已经碰到火时允许向外撤，不能因自身位于禁区而拒绝所有退路。
                if (Vector2.Dot(delta.XZ, (origin - source.Center).XZ) < -0.01f
                    || (end - source.Center).XZ.LengthSquared() <= (origin - source.Center).XZ.LengthSquared() + 0.1f)
                    return false;
            }
            else if (ray.Intersection(danger) is { } hit && hit <= length) return false;
        }
        return true;
    }
}
