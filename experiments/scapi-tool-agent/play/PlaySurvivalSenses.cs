#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Engine;
using Game;
using Game.Westworld;

internal static class PlaySurvivalSenses
{
    internal sealed record Threat(string Name, float Distance, bool Aquatic, bool Predator, float X, float Y, float Z)
    {
        [JsonIgnore] public int EntityId { get; init; }
    }
    internal sealed record Shore(float X, float Y, float Z, float Distance);

    public static bool OnDryGround(ComponentBody body) => body.ParentBody is null && body.StandingOnBody is null
        && body.ImmersionDepth < 0.04f && body.StandingOnValue is int value && Terrain.ExtractContents(value) != 0
        && BlocksManager.Blocks[Terrain.ExtractContents(value)] is { } block && block is not FluidBlock && block.IsCollidable_(value);

    public static ComponentBody? BoatDeck(ComponentBody body) => body.StandingOnBody is { } support
        && support.Entity.FindComponent<ComponentBoat>() is not null ? support : null;

    public static Threat[] Threats(ComponentPlayer player, AtomicActions.ActorQueries queries, float radius = 24f)
    {
        var visible = queries.FindVisibleObjectsAround(radius).Entities;
        if (visible.Count == 0) return [];
        var ids = visible.Select(entity => entity.EntityId).ToHashSet();
        // 类型补充只连接原生全向视野实际看见的实体。遮挡后的动物不进入风险判断。
        var predators = player.Project.Entities.Where(entity => ids.Contains(entity.Id)
            && entity.FindComponent<ComponentChaseBehavior>() is { AttacksPlayer: true }
            && entity.FindComponent<ComponentHealth>() is { Health: > 0f })
            .ToDictionary(entity => entity.Id);
        return visible.Where(entity => predators.ContainsKey(entity.EntityId)).Select(entity =>
        {
            var native = predators[entity.EntityId];
            string template = native.ValuesDictionary.DatabaseObject.Name;
            var category = native.FindComponent<ComponentCreature>()?.Category ?? 0;
            bool aquatic = template.StartsWith("Shark", StringComparison.Ordinal)
                || (category & CreatureCategory.WaterPredator) != 0;
            bool predator = (category & (CreatureCategory.LandPredator | CreatureCategory.WaterPredator)) != 0
                || template.StartsWith("Shark", StringComparison.Ordinal);
            string name = string.IsNullOrWhiteSpace(entity.Name) ? template : entity.Name;
            return new Threat(name, entity.Distance, aquatic, predator, entity.Position.X, entity.Position.Y, entity.Position.Z)
                { EntityId = entity.EntityId };
        }).Take(5).ToArray();
    }

    public static bool NeedsAvoidance(Threat animal, bool inWater) =>
        (!animal.Aquatic || inWater) && animal.Distance <= (animal.Predator ? animal.Aquatic ? 14f : 22f : 5f);

    public static Shore? VisibleShore(JsonElement observation, ComponentBody body, ComponentBody? boat)
    {
        var surfaceBody = boat ?? body;
        float waterLevel = surfaceBody.Position.Y + surfaceBody.ImmersionDepth;
        var blocks = observation.GetProperty("visibleBlocks").EnumerateArray()
            .Select(block => (Cell: Cell(block.GetProperty("cell")), Value: block.GetProperty("value").GetInt32())).ToArray();
        var obstruction = blocks.Where(block => BlocksManager.Blocks[Terrain.ExtractContents(block.Value)]
            is FluidBlock || BlocksManager.Blocks[Terrain.ExtractContents(block.Value)].IsCollidable_(block.Value))
            .Select(block => block.Cell).ToHashSet();
        return blocks.Where(block =>
            BlocksManager.Blocks[Terrain.ExtractContents(block.Value)] is GrassBlock or DirtBlock or SandBlock
                or GravelBlock or GraniteBlock or SandstoneBlock or BasaltBlock)
            .Where(block => block.Cell.Y + 1 >= waterLevel - 0.12f && block.Cell.Y + 1 <= body.Position.Y + 1.5f
                && !obstruction.Contains(new Point3(block.Cell.X, block.Cell.Y + 1, block.Cell.Z))
                && !obstruction.Contains(new Point3(block.Cell.X, block.Cell.Y + 2, block.Cell.Z)))
            .Select(block =>
            {
                var target = new Vector3(block.Cell.X + 0.5f, block.Cell.Y + 1, block.Cell.Z + 0.5f);
                return new Shore(target.X, target.Y, target.Z, (target - body.Position).XZ.Length());
            }).Where(point => point.Distance is >= 0.3f and <= 5f)
            .MinBy(point => point.Distance + 0.75f * Math.Max(0, point.Y - waterLevel - 1));
    }

    private static Point3 Cell(JsonElement value) =>
        new(value.GetProperty("x").GetInt32(), value.GetProperty("y").GetInt32(), value.GetProperty("z").GetInt32());
}
