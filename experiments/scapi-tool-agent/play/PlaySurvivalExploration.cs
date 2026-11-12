#nullable enable

using Engine;
using Westworld.Runtime.Core;

internal sealed class PlaySurvivalExploration(Vector3 origin, PlaySurvivalExploration.Course course, double started,
    IEnumerable<PlaySurvivalFood.Food>? knownFood = null, int rawMeatAtStart = 0)
{
    private readonly PlaySurvivalFood.Food[] knownAtStart = knownFood?.Take(64).ToArray() ?? [];
    internal sealed record Course(Vector2 Heading, Vector3 Opening, float OpeningLength, int Visits, bool StartsWithStep,
        bool HuntBirds = false)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public PlayObservedPath.Plan? Path { get; init; }
        public object Snapshot(Vector2 facing) => new
        {
            bearing = Math.Round(MathUtils.RadToDeg(Vector2.Angle(facing, Heading))),
            verifiedMeters = Math.Round(OpeningLength, 1), priorVisits = Visits, startsWithStep = StartsWithStep,
            objective = HuntBirds ? "沿此方向巡猎，遇到射程内的食用鸟即投矛，实际取肉后完成" : "沿此方向探索并报告新发现"
        };
    }

    private const int SectionLimit = 3;
    private const double TimeLimit = 65;
    public const float SectionDistance = 20f;
    public Vector3 Origin { get; } = origin;
    public Vector2 Heading { get; } = course.Heading;
    public bool HuntBirds { get; } = course.HuntBirds;
    public int RawMeatAtStart { get; } = rawMeatAtStart;
    public bool IsNewFood(PlaySurvivalFood.Food food) => !knownAtStart.Any(previous => previous.Matches(food));
    public int Sections { get; private set; } = 1;
    public int NativeLegs { get; private set; }

    public void RecordSection(int legs) => NativeLegs += legs;

    public bool CanContinue(double gameTime) => Sections < SectionLimit && gameTime - started < TimeLimit;

    public void Extend() => Sections++;

    public static Course[] Courses(LocalObservationWindow window, Func<Vector3, bool> permitted,
        Func<Position, int> visits, Func<Vector2, bool>? blocked = null, Vector2? continueToward = null)
    {
        var start = new Vector3((float)window.Position.X, (float)window.Position.Y, (float)window.Position.Z);
        return PlayDryRoutes.ScoutOpenings(window, permitted)
            .Where(leg => (leg.End - start).XZ.LengthSquared() >= 0.48f * 0.48f).Select(leg =>
            {
                var delta = (leg.End - start).XZ;
                return new Course(Vector2.Normalize(delta), leg.End, delta.Length(),
                    visits(new Position(leg.End.X, leg.End.Y, leg.End.Z)), leg.Step is not null);
            }).Where(option => blocked?.Invoke(option.Heading) != true
                && (continueToward is not { } ahead || Vector2.Dot(ahead, option.Heading) >= 0.55f))
            .OrderByDescending(option => option.OpeningLength - Math.Min(5, option.Visits) * 0.9f
                + (continueToward is { } heading ? Vector2.Dot(option.Heading, heading) * 2f : 0f))
            .DistinctBy(option => (int)MathF.Round(MathF.Atan2(option.Heading.Y, option.Heading.X) * 4f / MathF.PI))
            .Take(4).ToArray();
    }
}
