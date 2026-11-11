#nullable enable

using Engine;
using Game;
using Game.Westworld;

internal static class PlayShelterSites
{
    internal sealed record Choice(PlayEarthShelter.Site Site, int Outlets);
    private static readonly (int X, int Z)[] Sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int X, int Z)[] Rim = [.. Sides, (1, 1), (1, -1), (-1, 1), (-1, -1)];

    public static Choice? Choose(ActorPanorama? panorama, Vector3 origin, PlayEarthShelter.Site? standing,
        Func<Vector3, bool> permitted)
    {
        if (panorama is null) return standing is not null ? new(standing, 0) : null;
        var floors = panorama.Blocks.Where(block => block.BareTop && PlaySurvivalSenses.IsDryTerrain(block.Value))
            .ToLookup(block => (block.Cell.X, block.Cell.Z));
        int CountOutlets(PlayEarthShelter.Site site) => Sides.Count(side =>
            floors[(site.X + side.X, site.Z + side.Z)].Any(first => Math.Abs(first.Cell.Y + 1 - site.Surface) <= 1
                && floors[(site.X + 2 * side.X, site.Z + 2 * side.Z)].Any(next => Math.Abs(next.Cell.Y - first.Cell.Y) <= 1)));
        // 原生睡眠会查坑周围九列的顶。四向能走仍不足，坑边的对角不能有深落差。
        bool HasRim(PlayEarthShelter.Site site) => Rim.All(side => floors[(site.X + side.X, site.Z + side.Z)]
            .Any(block => block.Cell.Y + 1 >= site.Surface - 1))
            && Sides.All(side => floors[(site.X + 2 * side.X, site.Z + 2 * side.Z)]
                .Any(block => block.Cell.Y + 1 >= site.Surface - 1))
            && !panorama.Blocks.Any(block => BlocksManager.Blocks[Terrain.ExtractContents(block.Value)] is WaterBlock
                && Math.Abs(block.Cell.Y + 1 - site.Surface) <= 2
                && (new Vector2(block.Cell.X + 0.5f, block.Cell.Z + 0.5f) - site.Center(site.Surface).XZ).LengthSquared() < 9f);

        var local = standing is not null ? new Choice(standing, CountOutlets(standing)) : null;
        if (local is { Outlets: >= 2 } && HasRim(local.Site) && permitted(local.Site.Center(local.Site.Surface))) return local;
        // 候选位置只来自实际看到的软土表面。路线和坑内状况仍分别由原生动作核对。
        return floors.SelectMany(column => column)
            .Where(block => BlocksManager.Blocks[Terrain.ExtractContents(block.Value)] is DirtBlock or GrassBlock)
            .Select(block => new PlayEarthShelter.Site(block.Cell.X, block.Cell.Y + 1, block.Cell.Z))
            .Where(site => site != standing && MathF.Abs(site.Surface - origin.Y) <= 1.05f
                && (site.Center(site.Surface) - origin).XZ.LengthSquared() is >= 0.64f and <= 100f && HasRim(site))
            .Select(site => new Choice(site, CountOutlets(site)))
            .Where(choice => choice.Outlets >= (local is null ? 1 : 2))
            .OrderByDescending(choice => choice.Outlets).ThenBy(choice => (choice.Site.Center(choice.Site.Surface) - origin).XZ.LengthSquared())
            .FirstOrDefault(choice => permitted(choice.Site.Center(choice.Site.Surface)))
            ?? (local is { Outlets: >= 1 } && HasRim(local.Site) && permitted(local.Site.Center(local.Site.Surface)) ? local : null);
    }

    public static Choice[] Candidates(ActorPanorama panorama, Vector3 origin, PlayEarthShelter.Site? standing, Func<Vector3, bool> permitted)
    {
        var result = new List<Choice>();
        for (int i = 0; i < 3; i++)
        {
            var next = Choose(panorama, origin, standing, end => permitted(end)
                && result.All(old => old.Site.X != Terrain.ToCell(end.X) || old.Site.Z != Terrain.ToCell(end.Z)));
            if (next is null) break;
            result.Add(next);
        }
        return result.ToArray();
    }
}
