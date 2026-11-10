#nullable enable

using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;

internal sealed class PlaySurvivalSurvey
{
    private static readonly float[] Bearings = [0, 90, 180, -90];
    private readonly ComponentPlayer player;
    private readonly AtomicActions actions;
    private readonly Func<(JsonElement Observation, PlaySurvivalFood.Food[] Foods)> observe;
    private readonly Vector2 forward;
    private readonly double deadline;
    private readonly List<Sector> sectors = new();
    private AtomicActionRun? turn;

    internal sealed record Sighting(string Name, Vector3 Point, float Distance);
    internal sealed record Sector(Vector2 Direction, string[] Terrain, PlaySurvivalFood.Food[] Food, Sighting[] Animals);
    internal sealed record Sweep(Vector3 Origin, double CompletedAt, Sector[] Sectors)
    {
        public object Snapshot(Vector2 facing, double gameTime) => new
        {
            secondsAgo = Math.Round(gameTime - CompletedAt, 1),
            sectors = Sectors.Select(sector => new
            {
                bearing = Math.Round(Vector2.Angle(facing, sector.Direction) * 180 / Math.PI),
                terrain = sector.Terrain,
                food = sector.Food.Select(item => Describe(new(item.Name, item.Point, item.Distance))).ToArray(),
                animals = sector.Animals.Select(Describe).ToArray()
            }).ToArray()
        };

        private static object Describe(Sighting sighting) => new
        {
            name = sighting.Name, distance = Math.Round(sighting.Distance, 1),
            x = Math.Round(sighting.Point.X, 1), y = Math.Round(sighting.Point.Y, 1), z = Math.Round(sighting.Point.Z, 1)
        };
    }

    public PlaySurvivalSurvey(ComponentPlayer player, AtomicActions actions, double gameTime,
        Func<(JsonElement Observation, PlaySurvivalFood.Food[] Foods)> observe)
    {
        this.player = player;
        this.actions = actions;
        this.observe = observe;
        Origin = player.ComponentBody.Position;
        forward = player.ComponentBody.Matrix.Forward.XZ;
        deadline = gameTime + 7;
    }

    private Vector3 Origin { get; }
    public bool Done { get; private set; }
    public Sweep? Result { get; private set; }

    public void Update(double gameTime)
    {
        var body = player.ComponentBody;
        if (gameTime >= deadline || body.ImmersionDepth > 0.04f || !body.StandingOnValue.HasValue
            || (body.Position - Origin).LengthSquared() > 0.36f || player.ComponentSleep.IsSleeping)
        {
            Stop();
            return;
        }
        if (turn is not null)
        {
            if (turn.Result.Status == AtomicActionStatus.Running) return;
            if (turn.Result.Status != AtomicActionStatus.Succeeded)
            {
                Stop();
                return;
            }
            if (sectors.Count == Bearings.Length)
            {
                Result = new(Origin, gameTime, sectors.ToArray());
                Done = true;
                return;
            }
            sectors.Add(Capture(body.Matrix.Forward.XZ));
        }

        // 只合并四次实际转头得到的局部观察，最后回到初始朝向。
        var direction = PlaySurvivalTravel.Rotate(forward, Bearings[sectors.Count % Bearings.Length]);
        turn = actions.Start(new FaceTowards(Origin + 4 * new Vector3(direction.X, 0, direction.Y), 2, -12));
    }

    private Sector Capture(Vector2 direction)
    {
        var (observation, food) = observe();
        var terrain = observation.GetProperty("visibleBlocks").EnumerateArray()
            .Select(block => block.TryGetProperty("name", out var name) ? name.GetString() : null)
            .OfType<string>().Distinct().Take(5).ToArray();
        var animals = observation.GetProperty("entities").EnumerateArray().Where(entity =>
                entity.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            .Take(4).Select(entity => new Sighting(entity.GetProperty("name").GetString()!,
                PlaySurvivalFood.ReadPosition(entity.GetProperty("position")), entity.GetProperty("distance").GetSingle())).ToArray();
        return new(direction, terrain, food.Where(item => !item.Underwater).OrderBy(item => item.Distance).Take(4).ToArray(), animals);
    }

    public void Stop()
    {
        if (turn?.Result.Status == AtomicActionStatus.Running) actions.Cancel(turn);
        Done = true;
    }
}
