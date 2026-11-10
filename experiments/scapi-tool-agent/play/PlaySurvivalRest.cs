#nullable enable

using Engine;
using Game;

internal sealed class PlaySurvivalRest(ComponentPlayer player, double started)
{
    internal enum State { Drying, Ready, Interrupted, TimedOut }
    private static readonly Point3[] Walls = [new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1)];
    private static readonly Point3 Above = new(0, 1, 0);
    private readonly double deadline = started + 45;
    private double nextCheck;

    public static bool HasUsableCover(ComponentPlayer player) => PlaySurvivalSenses.OnDryGround(player.ComponentBody)
        && !Blocked(player, "BlockIsComfortable") && !Blocked(player, "Ceiling");

    public static bool HasSecureCover(ComponentPlayer player)
    {
        var body = player.ComponentBody;
        if (!PlaySurvivalSenses.OnDryGround(body)) return false;
        var terrain = player.Project.FindSubsystem<SubsystemTerrain>(true).Terrain;
        var feet = Terrain.ToCell(body.Position + new Vector3(0, 0.05f, 0));
        bool Solid(Point3 cell)
        {
            if (!(terrain.GetChunkAtCell(cell.X, cell.Z)?.State >= TerrainChunkState.InvalidVertices1)) return false;
            int value = terrain.GetCellValue(cell.X, cell.Y, cell.Z);
            return BlocksManager.Blocks[Terrain.ExtractContents(value)] is CubeBlock block && block.IsCollidable_(value);
        }

        // 检查贴身的原生实体围墙和屋顶。仅能挡雨的树冠不能挡住地面猎食者。
        return Solid(feet + Above + Above) && Walls.All(side => Solid(feet + side) && Solid(feet + side + Above))
            && HasUsableCover(player);
    }

    public static bool WetnessBlocksSleep(ComponentPlayer player) => Blocked(player, "NotTooWet");

    public static bool NeedsRecovery(ComponentPlayer player) => player.ComponentHealth.Health < 0.55f
        && (player.ComponentSickness.IsSick || player.ComponentFlu.HasFlu);

    public State Update(double gameTime)
    {
        if (gameTime < nextCheck) return State.Drying;
        nextCheck = gameTime + 0.4;
        if (!HasSecureCover(player)) return State.Interrupted;
        if (player.ComponentSleep.CanSleep(out _)) return State.Ready;
        return gameTime >= deadline ? State.TimedOut : State.Drying;
    }

    private static bool Blocked(ComponentPlayer player, string condition) =>
        !player.ComponentSleep.m_conditionsToSleep.TryGetValue(condition, out var check) || !string.IsNullOrEmpty(check());
}
