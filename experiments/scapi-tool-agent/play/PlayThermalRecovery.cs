#nullable enable

using Engine;
using Game;

internal sealed class PlayThermalRecovery(ComponentPlayer player, Point3 fire, double start, bool fully = false)
{
    public enum State { Warming, Warmed, NoImprovement, FireUnavailable, UnsafeHeat }
    public float Before { get; } = player.ComponentVitalStats.Temperature;
    private readonly float wetBefore = player.ComponentVitalStats.Wetness;
    private double nextCheck;

    public static bool FireWithinReach(ComponentPlayer player, Point3 cell)
    {
        var distance = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f) - player.ComponentBody.Position;
        if (distance.LengthSquared() > 9f || !PlayFireSafety.CampfireSpacingSafe(player, cell)) return false;
        var terrain = player.Project.FindSubsystem<SubsystemTerrain>(true).Terrain;
        if (!(terrain.GetChunkAtCell(cell.X, cell.Z)?.State >= TerrainChunkState.InvalidLight)) return false;
        int value = terrain.GetCellValue(cell.X, cell.Y, cell.Z);
        return Terrain.ExtractContents(value) == CampfireBlock.Index && BlocksManager.Blocks[CampfireBlock.Index].GetHeat(value) > 0f;
    }

    public State Update(double now)
    {
        if (now < nextCheck) return State.Warming;
        nextCheck = now + 0.5;
        var burning = player.Entity.FindComponent<ComponentOnFire>();
        if (burning?.IsOnFire == true || burning?.CheckIfBodyTouchesFire() == true) return State.UnsafeHeat;
        if (!FireWithinReach(player, fire)) return State.FireUnavailable;
        var vitals = player.ComponentVitalStats;
        bool warmed = vitals.Temperature >= Before + 0.45f
            && (vitals.Temperature >= 7.5f || vitals.m_targetTemperature >= 8f && now - start >= 18);
        bool dried = wetBefore >= 0.4f && vitals.Wetness < 0.3f && vitals.Temperature >= 6f;
        if (fully ? vitals.Temperature >= 8f && vitals.Wetness < 0.3f : warmed || dried) return State.Warmed;
        return now - start >= (fully ? 50 : 26) || now - start >= 7 && vitals.m_targetTemperature < Before + 0.4f
            && vitals.Wetness >= wetBefore - 0.05f
            ? State.NoImprovement : State.Warming;
    }
}
