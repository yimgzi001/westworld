#nullable enable

using Engine;
using Game;
using Game.Westworld;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

internal static class PlayDryRoutes
{
    internal sealed record Leg(AtomicAction Action, IReadOnlyList<LocalObservedCell> Evidence, ObservedStepRoute? Step = null,
        Func<LocalObservedCell, BoundingBox, bool>? PartialClearance = null)
    {
        public Vector3 End => Action switch
        {
            MoveAlongObservedRoute flat => flat.Points[^1],
            MoveTo align => align.Destination,
            StepTo step => step.Destination,
            _ => throw new InvalidOperationException("无落脚点的路线动作。")
        };

        public bool Contains(ComponentBody body, double elapsed)
        {
            if (Evidence.Max(cell => cell.Age) + elapsed > 3) return false;
            var bounds = body.BoundingBox;
            var minimum = new Position(bounds.Min.X, bounds.Min.Y, bounds.Min.Z);
            var maximum = new Position(bounds.Max.X, bounds.Max.Y, bounds.Max.Z);
            return Step is null ? LocalNavigationSkillGeometry.InObservedAir(Evidence, minimum, maximum, OpenCell,
                    PartialClearance is { } clearance ? cell => clearance(cell, bounds) : null)
                : ObservedStepGuard.Check(Step, minimum, maximum, body.IsSmoothRiseEnabled, body.MaxSmoothRiseHeight, OpenCell).Inside;
        }
    }

    private static readonly Vector2[] Cardinal = [Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY];

    public static Leg? Next(LocalObservationWindow window, Vector2 direction, float remaining, bool canAlign,
        Func<Position, int>? visits = null, bool towardLowerDrop = false, Func<Vector3, bool>? permitted = null)
    {
        var origin = window.Position;
        double Score(Position target) => (target.X - origin.X) * direction.X + (target.Z - origin.Z) * direction.Y
            - Math.Min(0.35, 0.07 * (visits?.Invoke(target) ?? 0));
        var steps = ReadSteps(window).Where(step => permitted?.Invoke(PositionOf(step.Destination)) != false).ToArray();
        var forwardStep = steps.Where(step => (step.Kind == StepKind.Up || towardLowerDrop)
                && Vector2.Dot(Vector2.Normalize((PositionOf(step.Destination) - PositionOf(origin)).XZ), direction)
                    >= (step.Kind == StepKind.Up ? 0.9f : 0.78f))
            .MaxBy(step => Score(step.Destination));
        // 目标前方已经证实有一格台阶时，直接跳过去，避免绕去侧面的平地。
        if (forwardStep is not null) return StepLeg(forwardStep);
        var longRoute = LongRoutes(window, remaining, Cardinal.Where(axis => Vector2.Dot(axis, direction) > 0.3f), permitted)
            .MaxBy(candidate => Score(candidate.Destination));
        if (longRoute is not null) return FlatLeg(longRoute);
        if (towardLowerDrop && canAlign && Alignment(window) is { } centered) return centered;
        return ShortRoutes(window).Concat(steps.Select(StepLeg))
            .Where(leg => permitted?.Invoke(leg.End) != false)
            .Select(leg => (Leg: leg, Score: Score(new Position(leg.End.X, leg.End.Y, leg.End.Z)) - (leg.Step is null ? 0 : 0.04)))
            .Where(option => option.Score > 0.12).OrderByDescending(option => option.Score).FirstOrDefault().Leg
            ?? (canAlign ? Alignment(window) : null);
    }

    public static Leg? Escape(LocalObservationWindow window, Vector2 threat, float remaining, bool canAlign)
    {
        var origin = PositionOf(window.Position);
        var separation = origin.XZ - threat;
        float distance = separation.Length();
        if (distance <= 0.05f) return null;
        var away = separation / distance;
        var recent = window.Cells.Where(cell => cell.Age <= 1.75).ToDictionary(cell => (cell.X, cell.Y, cell.Z));
        var best = LongRoutes(window, remaining, Cardinal).Select(FlatLeg)
            .Concat(ShortRoutes(window)).Concat(ReadSteps(window).Select(StepLeg))
            .Select(leg => (Leg: leg, Score: Score(leg)))
            .Where(option => float.IsFinite(option.Score)).OrderByDescending(option => option.Score).FirstOrDefault().Leg;
        return best ?? (canAlign ? Alignment(window) : null);

        float Score(Leg leg)
        {
            var point = leg.End;
            var delta = point.XZ - origin.XZ;
            float gain = (point.XZ - threat).Length() - distance;
            // 允许沿崖横移。退路的整段地面和净空已确认，路线还得拉开与威胁的距离。
            if (Vector2.Dot(delta, away) < -0.08f || gain <= 0.02f) return float.NegativeInfinity;
            int x = Terrain.ToCell(point.X), y = (int)MathF.Round(point.Y), z = Terrain.ToCell(point.Z);
            int ledges = 0;
            foreach (var side in Cardinal)
            {
                int sx = x + (int)side.X, sz = z + (int)side.Y;
                if (IsAir(sx, y - 1, sz)) ledges += IsAir(sx, y - 2, sz) ? 2 : 1;
            }
            return 2f * gain + 0.45f * delta.Length() - 1.6f * ledges - (leg.Step?.Kind == StepKind.Down ? 0.5f : 0f);
        }

        bool IsAir(int x, int y, int z) => recent.TryGetValue((x, y, z), out var cell) && OpenCell(cell.Value);
    }

    public static (Leg Approach, Vector3 Exit)? EscapeTurn(LocalObservationWindow window, Vector2 threat)
    {
        var origin = PositionOf(window.Position);
        var away = origin.XZ - threat;
        if (away.LengthSquared() < 16f * 16f) return null;
        away = Vector2.Normalize(away);
        // 岸边只剩向威胁方向的路时，先确认两格内有侧向出口，再允许短暂靠近。
        foreach (int length in new[] { 1, 2 })
        foreach (var approach in Cardinal.Where(axis => Vector2.Dot(axis, away) < -0.08f))
        {
            var first = FlatRoute(window, approach, length);
            if (first is null) continue;
            var corner = first.Destination;
            foreach (var side in Cardinal.Where(axis => Vector2.Dot(axis, approach) == 0f
                && MathF.Abs(Vector2.Dot(axis, away)) <= 0.35f))
            {
                var exit = UpStep(corner, side) ?? FlatRoute(window, side, 2, corner)?.Destination;
                if (exit is null && FlatRoute(window, side, 1, corner) is { } approachToStep)
                    exit = UpStep(approachToStep.Destination, side);
                if (exit is { } point)
                    return (new(new MoveAlongObservedRoute(first.Points.Select(PositionOf).ToArray(),
                        FinalRange: 0.1f, LevelGaze: true), first.Evidence), PositionOf(point));
            }
        }
        return null;

        Position? UpStep(Position from, Vector2 side) => ReadSteps(window, from)
            .FirstOrDefault(step => step.Kind == StepKind.Up
                && Vector2.Dot((PositionOf(step.Destination) - PositionOf(from)).XZ, side) >= 0.65f)?.Destination;
    }

    public static Leg? Detour(LocalObservationWindow window, Vector2 direction, float remaining, Func<Position, int>? visits,
        Func<Vector3, bool>? permitted = null)
    {
        var origin = PositionOf(window.Position);
        return LongRoutes(window, Math.Min(remaining, 4f), Cardinal, permitted).Select(FlatLeg)
            .Concat(ShortRoutes(window)).Concat(ReadSteps(window).Select(StepLeg))
            .Where(leg => permitted?.Invoke(leg.End) != false).MaxBy(leg =>
            {
                var point = leg.End;
                var offset = point.XZ - origin.XZ;
                int seen = visits?.Invoke(new Position(point.X, point.Y, point.Z)) ?? 0;
                return 0.7f * offset.Length() + 0.9f * Vector2.Dot(Vector2.Normalize(offset), direction)
                    - Math.Min(1f, 0.25f * seen) - (leg.Step?.Kind == StepKind.Down ? 0.2f : 0f);
            });
    }

    public static int[] WalkableBearings(LocalObservationWindow window, Vector2 facing, Func<Vector3, bool>? permitted = null)
    {
        var origin = PositionOf(window.Position).XZ;
        return ShortRoutes(window).Concat(ReadSteps(window).Select(StepLeg))
            .Where(leg => permitted?.Invoke(leg.End) != false)
            .Select(leg => (int)MathF.Round(MathUtils.RadToDeg(Vector2.Angle(facing, leg.End.XZ - origin)) / 15) * 15)
            .Distinct().Order().ToArray();
    }

    private static Vector3 PositionOf(Position value) => new((float)value.X, (float)value.Y, (float)value.Z);
    private static Leg StepLeg(ObservedStepRoute step) => new(new StepTo(PositionOf(step.Destination)), step.Evidence, step);
    private static Leg FlatLeg(ObservedFlatRoute flat) =>
        new(new MoveAlongObservedRoute(flat.Points.Select(PositionOf).ToArray(), LevelGaze: true), flat.Evidence);

    private static ObservedStepRoute[] ReadSteps(LocalObservationWindow window, Position? plannedOrigin = null) =>
        ObservedStepRoutes.Project(window, allowObservedSandAndGravel: true, openCell: OpenCell, plannedOrigin: plannedOrigin)
            .Where(step => step.MaximumAge <= 1.25).ToArray();

    public static Vector3? ShallowExit(LocalObservationWindow window, int surface) => ReadSteps(window)
        .FirstOrDefault(step => step.Kind == StepKind.Up && step.Destination.Y == surface) is { } exit
            ? PositionOf(exit.Destination) : null;

    private static IEnumerable<ObservedFlatRoute> LongRoutes(LocalObservationWindow window, float remaining,
        IEnumerable<Vector2> headings, Func<Vector3, bool>? permitted = null)
    {
        int maxLength = Math.Min(4, (int)MathF.Floor(remaining + 0.4f));
        if (maxLength < 2) return [];
        return headings.SelectMany(axis => Enumerable.Range(2, maxLength - 1).Reverse()
            .Select(length => FlatRoute(window, axis, length)).OfType<ObservedFlatRoute>()
            .Where(route => permitted?.Invoke(PositionOf(route.Destination)) != false).Take(1));
    }

    private static ObservedFlatRoute? FlatRoute(LocalObservationWindow window, Vector2 axis, int length, Position? plannedOrigin = null)
    {
        var origin = plannedOrigin ?? window.Position;
        return ObservedFlatRoutes.Inspect(window, new(origin.X + length * axis.X, origin.Y, origin.Z + length * axis.Y),
            maximumEvidenceAge: 1.75, groundPolicy: RouteGroundPolicy.ObservedFullCube, openCell: OpenCell,
            plannedOrigin: plannedOrigin).Route;
    }

    private static IEnumerable<Leg> ShortRoutes(LocalObservationWindow window)
    {
        var flat = LocalRouteCandidates.Project(window, 1, RouteGroundPolicy.ObservedFullCube, OpenCell);
        return flat.Candidates.Where(candidate => flat.Provenance[candidate.EvidenceId].All(cell => cell.Age <= 1.75))
            .Select(candidate => new Leg(new MoveAlongObservedRoute([PositionOf(candidate.Destination)], 0.24f, 3f, LevelGaze: true),
                flat.Provenance[candidate.EvidenceId]));
    }

    public static Leg? RecoverBesidePumpkin(LocalObservationWindow window, SubsystemTerrain terrain)
    {
        var alignment = ObserveAlignment(window, value => OpenCell(value) || IsPumpkin(value));
        if (alignment is not { MaximumAge: <= 1.5 }
            || !alignment.Evidence.Any(cell => IsPumpkin(cell.Value))) return null;

        // 格子边角可以落在身体范围内。用已观察到的南瓜尺寸核对整段收脚范围和每帧身体位置。
        var start = PositionOf(alignment.Origin);
        var end = PositionOf(alignment.Destination);
        var sweep = new BoundingBox(Vector3.Min(start, end) - new Vector3(0.25f, 0f, 0.25f),
            Vector3.Max(start, end) + new Vector3(0.25f, 1.9f, 0.25f));
        if (alignment.Evidence.Any(cell => IsPumpkin(cell.Value) && !ClearOfPumpkin(cell, sweep))) return null;
        return new(new MoveTo(end, 0.04f, 3f), alignment.Evidence, PartialClearance: ClearOfPumpkin);

        bool ClearOfPumpkin(LocalObservedCell cell, BoundingBox bounds)
        {
            if (BlocksManager.Blocks[Terrain.ExtractContents(cell.Value)] is not BasePumpkinBlock pumpkin
                || pumpkin.ShouldAvoid(cell.Value)) return false;
            var offset = new Vector3(cell.X, cell.Y, cell.Z);
            return pumpkin.GetCustomCollisionBoxes(terrain, cell.Value)
                .All(box => !bounds.Intersection(new BoundingBox(box.Min + offset, box.Max + offset)));
        }
    }

    private static bool IsPumpkin(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)] is BasePumpkinBlock;

    private static Leg? Alignment(LocalObservationWindow window) => ObserveAlignment(window, OpenCell) is { MaximumAge: <= 1.5 } alignment
        ? new(new MoveTo(PositionOf(alignment.Destination), 0.04f, 3f), alignment.Evidence) : null;

    private static ObservedAlignmentRoute? ObserveAlignment(LocalObservationWindow window, Func<int, bool> openCell)
    {
        return LocalNavigationAlignment.Project(window, allowObservedSandAndGravel: true, allowLeavingSmallOverhang: true,
            openCell: openCell, recoverToObservedSupport: true);
    }

    private static bool OpenCell(int value)
    {
        int contents = Terrain.ExtractContents(value);
        var block = BlocksManager.Blocks[contents];
        // 草、花和棉花保留原始证据。只有原生无碰撞且无需避让的这类植物算作净空。
        return contents == 0 || block is CrossBlock && !block.IsCollidable_(value) && !block.ShouldAvoid(value);
    }
}
