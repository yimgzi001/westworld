#nullable enable

using Engine;
using Game;
using Game.Westworld;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

internal static class PlayDryRoutes
{
    internal sealed record Leg(AtomicAction Action, IReadOnlyList<LocalObservedCell> Evidence, ObservedStepRoute? Step = null)
    {
        public bool Contains(ComponentBody body, double elapsed)
        {
            if (Evidence.Max(cell => cell.Age) + elapsed > 3) return false;
            var bounds = body.BoundingBox;
            var minimum = new Position(bounds.Min.X, bounds.Min.Y, bounds.Min.Z);
            var maximum = new Position(bounds.Max.X, bounds.Max.Y, bounds.Max.Z);
            return Step is null ? LocalNavigationSkillGeometry.InObservedAir(Evidence, minimum, maximum)
                : ObservedStepGuard.Check(Step, minimum, maximum, body.IsSmoothRiseEnabled, body.MaxSmoothRiseHeight).Inside;
        }
    }

    private static readonly Vector2[] Cardinal = [Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY];

    public static Leg? Next(LocalObservationWindow window, Vector2 direction, float remaining, bool canAlign,
        Func<Position, int>? visits = null)
    {
        var origin = window.Position;
        Vector3 PositionOf(Position value) => new((float)value.X, (float)value.Y, (float)value.Z);
        Leg StepLeg(ObservedStepRoute step) => new(new StepTo(PositionOf(step.Destination)), step.Evidence, step);
        double Score(Position target) => (target.X - origin.X) * direction.X + (target.Z - origin.Z) * direction.Y
            - Math.Min(0.35, 0.07 * (visits?.Invoke(target) ?? 0));
        var steps = ObservedStepRoutes.Project(window, allowObservedSandAndGravel: true)
            .Where(step => step.MaximumAge <= 1.25).ToArray();
        var forwardStep = steps.Where(step => step.Kind == StepKind.Up
                && Vector2.Dot(Vector2.Normalize((PositionOf(step.Destination) - PositionOf(origin)).XZ), direction) >= 0.9f)
            .MaxBy(step => Score(step.Destination));
        // 目标前方已经证实有一格台阶时，直接跳过去，避免绕去侧面的平地。
        if (forwardStep is not null) return StepLeg(forwardStep);
        int maxLength = Math.Min(4, (int)MathF.Floor(remaining + 0.4f));
        if (maxLength >= 2)
        {
            var route = Cardinal.Where(axis => Vector2.Dot(axis, direction) > 0.3f)
                .SelectMany(axis => Enumerable.Range(2, maxLength - 1).Reverse().Select(length => ObservedFlatRoutes.Inspect(window,
                    new(origin.X + length * axis.X, origin.Y, origin.Z + length * axis.Y), maximumEvidenceAge: 1.75,
                    groundPolicy: RouteGroundPolicy.ObservedFullCube).Route).OfType<ObservedFlatRoute>().Take(1))
                .MaxBy(candidate => Score(candidate.Destination));
            if (route is not null)
                return new(new MoveAlongObservedRoute(route.Points.Select(PositionOf).ToArray(), LevelGaze: true), route.Evidence);
        }
        var flat = LocalRouteCandidates.Project(window, 1, RouteGroundPolicy.ObservedFullCube);
        var options = flat.Candidates.Where(candidate => flat.Provenance[candidate.EvidenceId].All(cell => cell.Age <= 1.75))
            .Select(candidate => (Score: Score(candidate.Destination), Route: new Leg(new MoveAlongObservedRoute(
                [PositionOf(candidate.Destination)], 0.24f, 3f, LevelGaze: true), flat.Provenance[candidate.EvidenceId])))
            .Concat(steps.Select(step => (Score: Score(step.Destination) - 0.04, Route: StepLeg(step))))
            .Where(option => option.Score > 0.12).OrderByDescending(option => option.Score).ToArray();
        if (options.Length > 0) return options[0].Route;

        var alignment = canAlign ? LocalNavigationAlignment.Project(window, allowObservedSandAndGravel: true,
            allowLeavingSmallOverhang: true) : null;
        return alignment is { MaximumAge: <= 1.5 }
            ? new(new MoveTo(PositionOf(alignment.Destination), 0.04f, 3f), alignment.Evidence) : null;
    }
}
