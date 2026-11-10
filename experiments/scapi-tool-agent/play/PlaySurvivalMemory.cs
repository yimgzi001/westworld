#nullable enable

using Engine;
using Westworld.Runtime.Core;

internal sealed class PlaySurvivalMemory
{
    private sealed record Visit(int X, int Z, int Visits, bool Dry, double LastSeen);
    private sealed record Sighting(string Name, float X, float Y, float Z, double LastSeen);
    private const double PredatorMemorySeconds = 48;
    private const float PredatorKeepOutRadius = 18f;
    private readonly Dictionary<(int X, int Z), Visit> visits = new();
    private readonly List<Sighting> food = new();
    private readonly Dictionary<int, Sighting> predators = new();
    private (int X, int Z)? previous;

    public void VisitPosition(Vector3 point, bool dry, double gameTime)
    {
        var sector = Sector(point.X, point.Z);
        if (sector == previous) return;
        previous = sector;
        int count = visits.TryGetValue(sector, out var old) ? old.Visits : 0;
        visits[sector] = new(sector.X, sector.Z, count + 1, dry, gameTime);
        if (visits.Count > 32) visits.Remove(visits.MinBy(pair => pair.Value.LastSeen).Key);
    }

    public int VisitsTo(Position point) => visits.TryGetValue(Sector(point.X, point.Z), out var visit) ? visit.Visits : 0;

    public void SeeFood(IEnumerable<PlaySurvivalFood.Food> foods, double gameTime)
    {
        foreach (var item in foods.Where(item => !item.Underwater))
        {
            var position = item.Position;
            float x = position.GetProperty("x").GetSingle();
            float y = position.GetProperty("y").GetSingle();
            float z = position.GetProperty("z").GetSingle();
            food.RemoveAll(old => old.Name == item.Name && Sector(old.X, old.Z) == Sector(x, z));
            food.Add(new(item.Name, x, y, z, gameTime));
        }
        if (food.Count > 6) food.RemoveRange(0, food.Count - 6);
    }

    public void SeePredators(IEnumerable<PlaySurvivalSenses.Threat> visible, double gameTime)
    {
        foreach (var entry in predators.Where(entry => gameTime - entry.Value.LastSeen > PredatorMemorySeconds).ToArray())
            predators.Remove(entry.Key);
        foreach (var predator in visible.Where(item => item.Predator && !item.Aquatic))
            predators[predator.EntityId] = new(predator.Name, predator.X, predator.Y, predator.Z, gameTime);
        while (predators.Count > 8) predators.Remove(predators.MinBy(entry => entry.Value.LastSeen).Key);
    }

    public bool AvoidsPredatorAreas(Vector3 origin, Vector3 end, double gameTime)
    {
        var leg = (end - origin).XZ;
        float lengthSquared = leg.LengthSquared();
        if (lengthSquared < 0.01f) return true;
        foreach (var threat in predators.Values)
        {
            if (gameTime - threat.LastSeen > PredatorMemorySeconds) continue;
            var offset = new Vector2(threat.X - origin.X, threat.Z - origin.Z);
            float closest = Math.Clamp(Vector2.Dot(offset, leg) / lengthSquared, 0f, 1f);
            float required = Math.Min(offset.LengthSquared(), PredatorKeepOutRadius * PredatorKeepOutRadius);
            // 已经在附近时仍可侧向走开。候选线段不能进一步靠近刚看到猎食者的位置。
            if ((offset - closest * leg).LengthSquared() < required - 0.01f) return false;
        }
        return true;
    }

    public object Snapshot(double gameTime) => new
    {
        visited = visits.Values.OrderByDescending(visit => visit.LastSeen).Take(8).Select(visit => new object[]
        {
            visit.X * 4 + 2, visit.Z * 4 + 2, visit.Visits, visit.Dry, Math.Round(gameTime - visit.LastSeen)
        }).ToArray(),
        pastFoodSightings = food.Where(item => gameTime - item.LastSeen <= 180).Reverse().Select(item => new object[]
        {
            item.Name, Math.Round(item.X, 1), Math.Round(item.Y, 1), Math.Round(item.Z, 1), Math.Round(gameTime - item.LastSeen)
        }).ToArray(),
        pastPredators = predators.Values.Where(item => gameTime - item.LastSeen is > 0.6 and <= PredatorMemorySeconds)
            .OrderByDescending(item => item.LastSeen).Take(3).Select(item => new object[]
            {
                item.Name, Math.Round(item.X, 1), Math.Round(item.Z, 1), Math.Round(gameTime - item.LastSeen)
            }).ToArray()
    };

    private static (int X, int Z) Sector(double x, double z) =>
        ((int)Math.Floor(x / 4), (int)Math.Floor(z / 4));
}
