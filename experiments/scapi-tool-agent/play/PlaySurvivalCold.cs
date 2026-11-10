#nullable enable

using Engine;
using Game;
using Game.Westworld;

internal static class PlaySurvivalCold
{
    private const float Buffer = 2f;

    public static bool Frozen(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)] is SnowBlock or IceBlock;

    public static Vector3[] Observe(ActorPanorama? panorama) => panorama?.Blocks.Where(block => Frozen(block.Value))
        .Select(block => new Vector3(block.Cell.X + 0.5f, block.Cell.Y + 1f, block.Cell.Z + 0.5f)).ToArray() ?? [];

    public static float Insulation(ComponentPlayer player) => player.ComponentClothing.Insulation
        * MathUtils.Lerp(1f, 0.05f, MathUtils.Saturate(4f * player.ComponentVitalStats.Wetness));

    // 沿用游戏的湿衣衰减。阈值是角色主动进入雪区的保守准入，伤害和体温仍由原生系统结算。
    public static bool CanEnterSnow(ComponentPlayer player) => player.ComponentVitalStats.Temperature >= 8f
        && Insulation(player) >= 1.8f && !player.ComponentFlu.HasFlu;

    public static bool NearSnow(Vector3 at, IReadOnlyList<Vector3> frozen) => Exposure(at, frozen) > 0f;

    public static bool AllowsRoute(Vector3 origin, Vector3 end, IReadOnlyList<Vector3> frozen)
    {
        if (frozen.Count == 0) return true;
        float start = Exposure(origin, frozen);
        // 已经靠近雪地时保留能走向更少暴露的路线，避免把角色困在边界。
        if (start > 0f) return Exposure(end, frozen) <= start + 0.01f;

        var delta = end - origin;
        float lengthSquared = delta.XZ.LengthSquared();
        if (lengthSquared < 0.01f) return true;
        foreach (var point in frozen)
        {
            float fraction = Math.Clamp(Vector2.Dot((point - origin).XZ, delta.XZ) / lengthSquared, 0f, 1f);
            var closest = origin + fraction * delta;
            if (MathF.Abs(point.Y - closest.Y) <= 2.5f && (point - closest).XZ.LengthSquared() < Buffer * Buffer)
                return false;
        }
        return true;
    }

    private static float Exposure(Vector3 at, IReadOnlyList<Vector3> frozen)
    {
        float exposure = 0f;
        foreach (var point in frozen)
            if (MathF.Abs(point.Y - at.Y) <= 2.5f)
                exposure += Math.Max(0f, Buffer * Buffer - (point - at).XZ.LengthSquared());
        return exposure;
    }
}
