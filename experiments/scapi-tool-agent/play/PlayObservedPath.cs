#nullable enable

using Engine;
using Game;
using Game.Westworld;
using Westworld.Scapi.Adapter;

internal static class PlayObservedPath
{
    internal sealed record Plan(Vector3[] Waypoints, int Cells);
    private sealed record Link(Point3 Previous, float Cost);
    private static readonly Point3 Up = new(0, 1, 0);
    private static readonly Point3[] Sides = [new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1)];
    private const int VisitLimit = 4096;
    private const int RouteLimit = 52;

    public static Plan? Find(ActorPanorama? panorama, Vector3 origin, Vector3 target,
        Func<Vector3, Vector3, bool> allowed, float goalReach = 1.2f)
    {
        if (panorama is null || (target - origin).XZ.LengthSquared() < 0.04f) return null;
        var obstacles = panorama.Blocks.Where(block => BlocksManager.Blocks[Terrain.ExtractContents(block.Value)]
            .IsCollidable_(block.Value)).Select(block => block.Cell).ToHashSet();
        var floors = panorama.Blocks.Where(block => block.BareTop
                && LocalRouteCandidates.SupportedGround(block.Value, RouteGroundPolicy.ObservedFullCube))
            .Select(block => block.Cell + Up).Where(cell => !obstacles.Contains(cell) && !obstacles.Contains(cell + Up)).ToHashSet();
        var start = Terrain.ToCell(origin + 0.05f * Vector3.UnitY);
        if (!floors.Contains(start)) return null;

        // 只在已观察到的表面间选方向。得到的路径是路标，每一步仍要核对当前身体净空和实际落脚。
        var links = new Dictionary<Point3, Link> { [start] = new(start, 0) };
        var frontier = new PriorityQueue<(Point3 Cell, float Cost), float>();
        frontier.Enqueue((start, 0), Estimate(start));
        while (frontier.TryDequeue(out var node, out _) && links.Count <= VisitLimit)
        {
            var (cell, cost) = node;
            if (cost > links[cell].Cost) continue;
            var at = Position(cell);
            if ((at - target).XZ.LengthSquared() <= goalReach * goalReach && MathF.Abs(at.Y - target.Y) <= 1.25f)
                return Build(start, cell, links);
            foreach (var side in Sides)
            for (int height = -1; height <= 1; height++)
            {
                var next = cell + side + new Point3(0, height, 0);
                var previous = links[cell].Previous;
                bool turns = cell != start && (cell.X - previous.X != side.X || cell.Z - previous.Z != side.Z);
                float newCost = cost + 1f + 0.3f * Math.Abs(height) + (turns ? 0.25f : 0f);
                if (newCost > RouteLimit || !floors.Contains(next)
                    || links.TryGetValue(next, out var seen) && seen.Cost <= newCost
                    || !allowed(at, Position(next))) continue;
                links[next] = new(cell, newCost);
                frontier.Enqueue((next, newCost), newCost + Estimate(next));
            }
        }
        return null;

        float Estimate(Point3 cell)
        {
            var offset = Position(cell) - target;
            return Math.Max(0f, MathF.Abs(offset.X) + MathF.Abs(offset.Z) - 1.7f);
        }
    }

    private static Plan Build(Point3 start, Point3 end, Dictionary<Point3, Link> links)
    {
        var cells = new List<Point3>();
        for (var cell = end; cell != start; cell = links[cell].Previous) cells.Add(cell);
        cells.Reverse();
        var waypoints = new List<Vector3>();
        var previous = start;
        for (int index = 0; index < cells.Count; index++)
        {
            var cell = cells[index];
            if (index + 1 == cells.Count || cells[index + 1] - cell != cell - previous)
                waypoints.Add(Position(cell));
            previous = cell;
        }
        return new(waypoints.ToArray(), cells.Count);
    }

    private static Vector3 Position(Point3 cell) => new(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
}
