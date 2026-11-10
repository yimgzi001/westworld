#nullable enable

using Engine;
using Game;
using Game.Westworld;

internal static class PlaySurvivalPanorama
{
    private const int Directions = 8;
    private const float SectorWidth = 360f / Directions;

    internal sealed record Sector(Vector2 Direction, PanoramicBlock[] Blocks, PlaySurvivalFood.Food[] Food,
        NearbyEntityView[] Animals, NearbyPickableView[] Items);

    internal sealed record Sweep(Vector3 Origin, double CompletedAt, ActorPanorama Panorama, Sector[] Sectors, SubsystemTerrain NativeTerrain)
    {
        public object Snapshot(Vector3 at, Vector2 facing, double gameTime) => new
        {
            mode = "panorama",
            radius = Panorama.Radius,
            secondsAgo = Math.Round(gameTime - CompletedAt, 1),
            Panorama.UnloadedColumns,
            visibleBlocks = Panorama.Blocks.Count,
            visibleAnimals = Panorama.Objects.Entities.Count,
            visibleItems = Panorama.Objects.Pickables.Count,
            sectors = Sectors.Select(sector => new
            {
                bearing = Bearing(facing, sector.Direction),
                terrain = sector.Blocks.GroupBy(block => Terrain.ExtractContents(block.Value))
                    .OrderByDescending(group => group.Count()).Take(4).Select(group =>
                    {
                        var nearest = group.MinBy(block => block.Distance)!;
                        return new { name = BlocksManager.Blocks[group.Key].GetDisplayName(NativeTerrain, nearest.Value),
                            count = group.Count(), distance = Math.Round(nearest.Distance, 1) };
                    }).ToArray(),
                dryGround = sector.Blocks.Where(block => block.BareTop && IsDryGround(block.Value) && block.Cell.Y >= at.Y - 2)
                    .OrderBy(block => block.Distance).Take(1).Select(block => Describe("dry_ground", block.Point, at, facing)).FirstOrDefault(),
                softEarth = sector.Blocks.Where(IsSoftEarth).MinBy(block => (block.Point - at).LengthSquared()) is { } earth
                    ? Describe("soft_earth", earth.Point, at, facing) : null,
                wood = sector.Blocks.Where(block => PlaySurvivalProduction.IsMaterial(block.Value, PlaySurvivalProduction.Wood))
                    .MinBy(block => block.Distance) is { } wood ? Describe("wood", wood.Point, at, facing) : null,
                stone = sector.Blocks.Where(block => PlaySurvivalProduction.IsMaterial(block.Value, PlaySurvivalProduction.Stone))
                    .MinBy(block => block.Distance) is { } stone ? Describe("granite", stone.Point, at, facing) : null,
                food = sector.Food.OrderBy(food => food.SicknessRisk).ThenBy(food => food.Distance).Take(4)
                    .Select(food => DescribeFood(food, at, facing)).ToArray(),
                animals = sector.Animals.Where(animal => !string.IsNullOrWhiteSpace(animal.Name)).Take(4)
                    .Select(animal => Describe(animal.Name, animal.Position, at, facing)).ToArray(),
                itemTypes = sector.Items.GroupBy(item => item.Value).Take(2).Select(group =>
                {
                    var nearest = group.First();
                    return Describe(BlocksManager.Blocks[Terrain.ExtractContents(nearest.Value)].GetDisplayName(NativeTerrain, nearest.Value),
                        nearest.Position, at, facing);
                }).ToArray(),
                foodCount = sector.Food.Length,
                animalCount = sector.Animals.Length,
                itemCount = sector.Items.Length
            }).ToArray()
        };

        public Vector3? EarthInDirection(Vector3 origin, Vector2 direction) => GroundInDirection(origin, direction, IsSoftEarth, 3f);

        public Vector3? ShoreInDirection(Vector3 origin, Vector2 direction) => GroundInDirection(origin, direction,
            block => block.BareTop && IsDryGround(block.Value), 2f);

        public PlaySurvivalFood.Food? FoodInDirection(ComponentPlayer player, Vector2 direction, float distance)
        {
            var origin = player.ComponentBody.Position;
            return Sectors.SelectMany(sector => sector.Food).Where(food => PlaySurvivalFood.CanSeek(player, food))
                .Where(food =>
                {
                    var offset = food.Point - origin;
                    float horizontal = offset.XZ.Length();
                    return offset.Y is >= -2f and <= 3f && horizontal > 0.1f && horizontal <= distance + 0.5f
                        && Vector2.Dot(offset.XZ / horizontal, direction) >= 0.65f;
                }).OrderBy(food => food.SicknessRisk).ThenBy(food => (food.Point - origin).LengthSquared()).FirstOrDefault();
        }

        private Vector3? GroundInDirection(Vector3 origin, Vector2 direction, Func<PanoramicBlock, bool> matching, float height)
        {
            var ground = Panorama.Blocks.Where(matching).Where(block =>
            {
                var offset = block.Point - origin;
                return MathF.Abs(offset.Y) <= height && offset.XZ.LengthSquared() > 0.25f
                    && Vector2.Dot(Vector2.Normalize(offset.XZ), direction) >= 0.55f;
            }).MinBy(block => (block.Point - origin).LengthSquared());
            return ground is null ? null : new Vector3(ground.Cell.X + 0.5f, ground.Cell.Y + 1f, ground.Cell.Z + 0.5f);
        }

        private static object Describe(string name, Vector3 point, Vector3 at, Vector2 facing)
        {
            var delta = point - at;
            return new { name, bearing = Bearing(facing, delta.XZ), distance = Math.Round(delta.Length(), 1),
                x = Math.Round(point.X, 1), y = Math.Round(point.Y, 1), z = Math.Round(point.Z, 1) };
        }

        private static object DescribeFood(PlaySurvivalFood.Food food, Vector3 at, Vector2 facing)
        {
            var point = food.Point;
            var delta = point - at;
            return new { name = food.Name, bearing = Bearing(facing, delta.XZ), distance = Math.Round(delta.Length(), 1),
                x = Math.Round(point.X, 1), y = Math.Round(point.Y, 1), z = Math.Round(point.Z, 1),
                sicknessRisk = food.SicknessRisk, nutrition = food.Nutrition };
        }

        private static bool IsDryGround(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)]
            is GrassBlock or DirtBlock or SandBlock or GravelBlock or GraniteBlock or SandstoneBlock or BasaltBlock;

        private static bool IsSoftEarth(PanoramicBlock block) => block.BareTop
            && BlocksManager.Blocks[Terrain.ExtractContents(block.Value)] is GrassBlock or DirtBlock;
    }

    public static Sweep? Capture(ComponentPlayer player, AtomicActions.ActorQueries queries, float radius, double gameTime)
    {
        var panorama = queries.ReadPanorama(radius);
        if (panorama is null) return null;
        var origin = player.ComponentBody.Position;
        var facing = player.ComponentBody.Matrix.Forward.XZ;
        int SectorOf(Vector3 point)
        {
            var offset = (point - origin).XZ;
            double angle = offset.LengthSquared() < 0.0001f ? 0 : Vector2.Angle(facing, offset) * 180 / Math.PI;
            return ((int)Math.Floor((angle + SectorWidth * 0.5f) / SectorWidth) + Directions) % Directions;
        }

        // 八个方向来自同一次原生读数。地形、动物和拾取物的全量留在游戏线程，模型收到按方向压缩的摘要。
        var terrain = panorama.Blocks.ToLookup(block => SectorOf(block.Point));
        var food = PlaySurvivalFood.VisibleIn(panorama).Where(item => !item.Underwater).ToLookup(item => SectorOf(item.Point));
        var animals = panorama.Objects.Entities.ToLookup(item => SectorOf(item.Position));
        var items = panorama.Objects.Pickables.ToLookup(item => SectorOf(item.Position));
        return new(origin, gameTime, panorama, Enumerable.Range(0, Directions).Select(index => new Sector(
            PlaySurvivalTravel.Rotate(facing, index * SectorWidth), terrain[index].ToArray(), food[index].ToArray(),
            animals[index].ToArray(), items[index].ToArray())).ToArray(), player.Project.FindSubsystem<SubsystemTerrain>(true));
    }

    private static double Bearing(Vector2 facing, Vector2 direction) => direction.LengthSquared() < 0.0001f
        ? 0 : Math.Round(Vector2.Angle(facing, direction) * 180 / Math.PI);
}
