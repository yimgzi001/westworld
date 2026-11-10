#nullable enable

using Engine;
using Westworld.Runtime.Core;

internal sealed class PlaySurvivalMemory
{
    private sealed record Visit(int X, int Z, int Visits, bool Dry, double LastSeen);
    private sealed record Sighting(string Name, float X, float Y, float Z, double LastSeen);
    private readonly Dictionary<(int X, int Z), Visit> visits = new();
    private readonly List<Sighting> food = new();
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

    public object Snapshot(double gameTime) => new
    {
        visited = visits.Values.OrderByDescending(visit => visit.LastSeen).Take(8).Select(visit => new
        {
            centerX = visit.X * 4 + 2, centerZ = visit.Z * 4 + 2,
            visit.Visits, visit.Dry, secondsAgo = Math.Round(gameTime - visit.LastSeen)
        }).ToArray(),
        pastFoodSightings = food.Where(item => gameTime - item.LastSeen <= 180).Reverse().Select(item => new
        {
            item.Name, item.X, item.Y, item.Z, secondsAgo = Math.Round(gameTime - item.LastSeen)
        }).ToArray()
    };

    private static (int X, int Z) Sector(double x, double z) =>
        ((int)Math.Floor(x / 4), (int)Math.Floor(z / 4));
}
