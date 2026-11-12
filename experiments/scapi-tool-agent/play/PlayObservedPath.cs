#nullable enable

using Engine;
using Game;
using Game.Westworld;
using Westworld.Scapi.Adapter;

internal static class PlayObservedPath
{
    internal sealed record Plan(Vector3[] Waypoints, int Cells);
    internal sealed record Frontier(Vector3 Destination, Plan Path, float ForwardGain);
    private sealed record Link(Point3 Previous, float Cost);
    private static readonly Point3 Up = new(0, 1, 0);
    private static readonly Point3[] Sides = [new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1)];
    private const int VisitLimit = 4096;
    private const int RouteLimit = 52;

    public static ActorPanorama? WithLocalGround(ActorPanorama? panorama, Westworld.Runtime.Core.LocalObservationWindow window)
    {
        if (panorama is null) return null;
        var cells = window.Cells.Where(cell => cell.Age <= .5).ToDictionary(cell => (cell.X, cell.Y, cell.Z));
        var blocks = panorama.Blocks.ToDictionary(block => block.Cell);
        foreach (var cell in cells.Values.Where(cell => LocalRouteCandidates.SupportedGround(cell.Value, RouteGroundPolicy.ObservedFullCube)))
        {
            if (!Open(cell.X, cell.Y + 1, cell.Z) || !Open(cell.X, cell.Y + 2, cell.Z)) continue;
            var key = new Point3(cell.X, cell.Y, cell.Z);
            var point = new Vector3(cell.X + .5f, cell.Y + 1, cell.Z + .5f);
            var origin = new Vector3((float)window.Position.X, (float)window.Position.Y, (float)window.Position.Z);
            // 全景射线未必看见脚下方块顶面；合并本人当前局部观察中已确认的地面和净空，不扫描新地形。
            blocks[key] = new(key, point, cell.Value, (point - origin).Length(), true);
        }
        return panorama with { Blocks = blocks.Values.ToArray() };

        bool Open(int x, int y, int z) => cells.TryGetValue((x, y, z), out var cell)
            && (Terrain.ExtractContents(cell.Value) == 0 || BlocksManager.Blocks[Terrain.ExtractContents(cell.Value)] is CrossBlock plant
                && !plant.IsCollidable_(cell.Value) && !plant.ShouldAvoid(cell.Value));
    }

    public static Frontier? FindFrontier(ActorPanorama? panorama, Vector3 origin, Vector2 heading, float distance,
        Func<Vector3, bool> allowed, Func<Westworld.Runtime.Core.Position, int>? visits = null)
    {
        if (panorama is null) return null;
        var obstacles = panorama.Blocks.Where(block => BlocksManager.Blocks[Terrain.ExtractContents(block.Value)]
            .IsCollidable_(block.Value)).Select(block => block.Cell).ToHashSet();
        var floors = panorama.Blocks.Where(block => block.BareTop
            && LocalRouteCandidates.SupportedGround(block.Value, RouteGroundPolicy.ObservedFullCube))
            .Select(block => block.Cell + Up).Where(cell => !obstacles.Contains(cell) && !obstacles.Contains(cell + Up)).ToHashSet();
        var start = Terrain.ToCell(origin + .05f * Vector3.UnitY);
        if (!floors.Contains(start)) return null;
        var links = new Dictionary<Point3, Link> { [start] = new(start, 0) };
        var queue = new PriorityQueue<(Point3 Cell, float Cost), float>();
        queue.Enqueue((start, 0), 0);
        Point3? best = null;
        float bestScore = float.NegativeInfinity, bestGain = 0;
        while (queue.TryDequeue(out var entry, out _) && links.Count <= VisitLimit)
        {
            if (entry.Cost > links[entry.Cell].Cost) continue;
            var at = Position(entry.Cell);
            var offset = (at - origin).XZ;
            float span = offset.Length(), gain = Vector2.Dot(offset, heading);
            if (span >= 3 && gain >= 2 && span <= distance + .5f && gain >= span * .25f)
            {
                int visited = visits?.Invoke(new(at.X, at.Y, at.Z)) ?? 0;
                float score = gain + .1f * span - .35f * Math.Max(0, entry.Cost - span)
                    - .9f * Math.Min(6, visited) - .15f * MathF.Abs(at.Y - origin.Y);
                if (score > bestScore) { best = entry.Cell; bestScore = score; bestGain = gain; }
            }
            foreach (var side in Sides)
            for (int rise = -1; rise <= 1; rise++)
            {
                var next = entry.Cell + side + new Point3(0, rise, 0);
                var previous = links[entry.Cell].Previous;
                bool turns = entry.Cell != start && (entry.Cell.X - previous.X != side.X || entry.Cell.Z - previous.Z != side.Z);
                float cost = entry.Cost + 1 + .3f * Math.Abs(rise) + (turns ? .25f : 0);
                if (cost > distance * 1.5f + 2 || !floors.Contains(next)
                    || rise > 0 && obstacles.Contains(entry.Cell + new Point3(0, 2, 0))
                    || rise < 0 && obstacles.Contains(next + new Point3(0, 2, 0))
                    || links.TryGetValue(next, out var seen) && seen.Cost <= cost || !allowed(Position(next))) continue;
                links[next] = new(entry.Cell, cost);
                queue.Enqueue((next, cost), cost);
            }
        }
        // 不把朝水面走二十格的理想坐标当成目的地，选一条确实连通、确实向前的干地路径。
        return best is { } end ? new(Position(end), Build(start, end, links), bestGain) : null;
    }

    public static Plan? Find(ActorPanorama? panorama, Vector3 origin, Vector3 target,
        Func<Vector3, Vector3, bool> allowed, float goalReach = 1.2f, float maximumAboveTarget = 1.25f,
        float maximumBelowTarget = 1.25f)
    {
        if (panorama is null || (target - origin).LengthSquared() < 0.04f) return null;
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
            if ((at - target).XZ.LengthSquared() <= goalReach * goalReach && at.Y - target.Y <= maximumAboveTarget
                && target.Y - at.Y <= maximumBelowTarget)
                return Build(start, cell, links);
            foreach (var side in Sides)
            for (int height = -1; height <= 1; height++)
            {
                var next = cell + side + new Point3(0, height, 0);
                var previous = links[cell].Previous;
                bool turns = cell != start && (cell.X - previous.X != side.X || cell.Z - previous.Z != side.Z);
                float newCost = cost + 1f + 0.3f * Math.Abs(height) + (turns ? 0.25f : 0f);
                if (newCost > RouteLimit || !floors.Contains(next)
                    || height > 0 && obstacles.Contains(cell + new Point3(0, 2, 0))
                    || height < 0 && obstacles.Contains(next + new Point3(0, 2, 0))
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

    public static float RemainingWork(Vector3 origin, Vector3 target) => (target - origin).XZ.Length()
        + 2f * MathF.Abs(target.Y - origin.Y);

    public static Frontier? FindProgress(ActorPanorama? panorama, Vector3 origin, Vector3 target,
        Func<Vector3, Vector3, bool> allowed)
    {
        if (panorama is null) return null;
        var obstacles = panorama.Blocks.Where(block => BlocksManager.Blocks[Terrain.ExtractContents(block.Value)]
            .IsCollidable_(block.Value)).Select(block => block.Cell).ToHashSet();
        var floors = panorama.Blocks.Where(block => block.BareTop
                && LocalRouteCandidates.SupportedGround(block.Value, RouteGroundPolicy.ObservedFullCube))
            .Select(block => block.Cell + Up).Where(cell => !obstacles.Contains(cell) && !obstacles.Contains(cell + Up)).ToHashSet();
        var start = Terrain.ToCell(origin + .05f * Vector3.UnitY);
        if (!floors.Contains(start)) return null;
        var links = new Dictionary<Point3, Link> { [start] = new(start, 0) };
        var queue = new PriorityQueue<(Point3 Cell, float Cost), float>();
        queue.Enqueue((start, 0), 0);
        float initial = RemainingWork(origin, target), bestScore = 0, bestGain = 0;
        Point3? best = null;
        while (queue.TryDequeue(out var entry, out _) && links.Count <= VisitLimit)
        {
            if (entry.Cost > links[entry.Cell].Cost) continue;
            var at = Position(entry.Cell);
            float gain = initial - RemainingWork(at, target);
            float heightGain = MathF.Abs(target.Y - origin.Y) - MathF.Abs(target.Y - at.Y);
            float span = (at - origin).XZ.Length();
            bool closeButOtherFloor = (target - origin).XZ.Length() < 4 && MathF.Abs(target.Y - origin.Y) > .5f;
            bool closerFloor = MathF.Abs(target.Y - at.Y) < MathF.Abs(target.Y - origin.Y) - .5f;
            // 目标确实在另一层时优先接近该层；同层远行允许翻过途中台阶，不能为保持原高度绕远。
            float score = gain + (MathF.Abs(target.Y - origin.Y) > .5f ? 10f * heightGain : 0f) - .08f * entry.Cost;
            if ((gain >= .75f || heightGain >= .75f) && span >= .45f && (!closeButOtherFloor || closerFloor) && score > bestScore)
            { best = entry.Cell; bestScore = score; bestGain = gain; }
            foreach (var side in Sides)
            for (int rise = -1; rise <= 1; rise++)
            {
                var next = entry.Cell + side + new Point3(0, rise, 0);
                float cost = entry.Cost + 1 + .3f * Math.Abs(rise);
                if (cost > 34 || !floors.Contains(next)
                    || rise > 0 && obstacles.Contains(entry.Cell + new Point3(0, 2, 0))
                    || rise < 0 && obstacles.Contains(next + new Point3(0, 2, 0))
                    || links.TryGetValue(next, out var seen) && seen.Cost <= cost || !allowed(at, Position(next))) continue;
                links[next] = new(entry.Cell, cost);
                queue.Enqueue((next, cost), cost);
            }
        }
        // 上下层也计入剩余路程；允许先朝反方向走上楼梯，但不把继续下坑当成回地面。
        return best is { } end ? new(Position(end), Build(start, end, links), bestGain) : null;
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
