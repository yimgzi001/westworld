#nullable enable

using Engine;
using Game;
using Game.Westworld;
using Westworld.Scapi.Adapter;

internal static class PlaySurfacePath
{
    internal sealed record Node(Point3 Cell, bool Water)
    {
        public Vector3 Point => new(Cell.X + .5f, Cell.Y - (Water ? .75f : 0f), Cell.Z + .5f);
    }
    internal sealed record Plan(Node[] Nodes, float Cost)
    {
        public bool HasWater => Nodes.Any(node => node.Water);
    }
    private static readonly Point3[] Sides = [new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1)];

    // 水面和干地进入同一张局部图。水路、升降和转弯都计成本，只使用本次全向观察到的表面。
    public static Plan? Find(ActorPanorama? panorama, Vector3 origin, Vector3? target,
        Func<Vector3, bool> permitted, IReadOnlySet<Point3>? refused = null)
    {
        if (panorama is null) return null;
        var obstacles = panorama.Blocks.Where(block => BlocksManager.Blocks[Terrain.ExtractContents(block.Value)]
            .IsCollidable_(block.Value)).Select(block => block.Cell).ToHashSet();
        var nodes = panorama.Blocks.Where(block => block.BareTop
                && (BlocksManager.Blocks[Terrain.ExtractContents(block.Value)] is WaterBlock
                    || LocalRouteCandidates.SupportedGround(block.Value, RouteGroundPolicy.ObservedFullCube)))
            .Select(block => new Node(block.Cell + new Point3(0, 1, 0),
                BlocksManager.Blocks[Terrain.ExtractContents(block.Value)] is WaterBlock))
            .Where(node => !obstacles.Contains(node.Cell) && !obstacles.Contains(node.Cell + new Point3(0, 1, 0))
                && refused?.Contains(node.Cell) != true && permitted(node.Point))
            .DistinctBy(node => node.Cell).ToDictionary(node => node.Cell);
        var start = nodes.Values.Where(node => Terrain.ToCell(origin.X) == node.Cell.X && Terrain.ToCell(origin.Z) == node.Cell.Z
            && MathF.Abs(node.Point.Y - origin.Y) <= 1.4f).MinBy(node => MathF.Abs(node.Point.Y - origin.Y));
        if (start is null) return null;
        var costs = new Dictionary<Point3, float> { [start.Cell] = 0 };
        var previous = new Dictionary<Point3, Point3>();
        var queue = new PriorityQueue<(Point3 Cell, float Cost), float>();
        queue.Enqueue((start.Cell, 0), Estimate(start));
        while (queue.TryDequeue(out var entry, out _) && costs.Count <= 3072)
        {
            if (costs[entry.Cell] < entry.Cost) continue;
            var node = nodes[entry.Cell];
            bool reached = target is { } goal ? (node.Water
                    ? node.Cell.X == Terrain.ToCell(goal.X) && node.Cell.Z == Terrain.ToCell(goal.Z)
                    : (node.Point - goal).XZ.LengthSquared() <= .85f * .85f)
                && goal.Y >= node.Point.Y - .35f && goal.Y <= node.Point.Y + 1.3f : !node.Water;
            if (reached)
            {
                var path = new List<Node> { node };
                for (var at = entry.Cell; previous.TryGetValue(at, out var before); at = before) path.Add(nodes[before]);
                path.Reverse();
                return new(path.ToArray(), entry.Cost);
            }
            foreach (var side in Sides)
            for (int rise = -1; rise <= 1; rise++)
            {
                if (!nodes.TryGetValue(entry.Cell + side + new Point3(0, rise, 0), out var next)) continue;
                if (node.Water && next.Water && rise != 0) continue;
                // 上岸必须有跳跃空间，禁止从水面规划爬上两格高墙或钻入低顶。
                if (rise > 0 && obstacles.Contains(node.Cell + new Point3(0, 2, 0))
                    || rise < 0 && obstacles.Contains(next.Cell + new Point3(0, 2, 0))) continue;
                float turn = previous.TryGetValue(entry.Cell, out var prev)
                    && (entry.Cell.X - prev.X != side.X || entry.Cell.Z - prev.Z != side.Z) ? .2f : 0f;
                float cost = entry.Cost + (next.Water ? 2.4f : 1f) + Math.Abs(rise) * .6f + turn
                    + (node.Water != next.Water ? .8f : 0);
                if (cost > 65 || costs.TryGetValue(next.Cell, out var seen) && seen <= cost) continue;
                costs[next.Cell] = cost; previous[next.Cell] = entry.Cell;
                queue.Enqueue((next.Cell, cost), cost + Estimate(next));
            }
        }
        return null;

        float Estimate(Node node) => target is { } aim
            ? Math.Max(0, MathF.Abs(node.Point.X - aim.X) + MathF.Abs(node.Point.Z - aim.Z) - 1.2f) : 0;
    }
}
