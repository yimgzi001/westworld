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
        public float WarningDistance { get; init; }
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
        // 类型补充只连接原生全向视野实际看见的实体。遮挡后的动物不进入风险判断。
        var threats = new List<Threat>(5);
        foreach (var entity in visible)
        {
            var native = player.Project.FindEntity(entity.EntityId);
            if (native?.FindComponent<ComponentChaseBehavior>() is not { AttacksPlayer: true } chase
                || native.FindComponent<ComponentHealth>() is not { Health: > 0f }) continue;
            string template = native.ValuesDictionary.DatabaseObject.Name;
            var creature = native.FindComponent<ComponentCreature>();
            var category = creature?.Category ?? 0;
            bool aquatic = template.StartsWith("Shark", StringComparison.Ordinal)
                || (category & CreatureCategory.WaterPredator) != 0;
            bool predator = (category & (CreatureCategory.LandPredator | CreatureCategory.WaterPredator)) != 0
                || template.StartsWith("Shark", StringComparison.Ordinal);
            // 奶牛等动物有被攻击后的反击能力。未主动追击、碰撞也不会反击时，仅在看到攻击动作后列为威胁。
            if (!predator && chase.m_dayChaseRange <= 0 && chase.m_nightChaseRange <= 0 && chase.m_chaseOnTouchProbability <= 0
                && creature?.ComponentCreatureModel.AttackOrder != true) continue;
            string name = string.IsNullOrWhiteSpace(entity.Name) ? template : entity.Name;
            float chaseRange = chase.m_subsystemSky.SkyLightIntensity < 0.2f ? chase.m_nightChaseRange : chase.m_dayChaseRange;
            // 食性和主动攻击范围分别判断。犀牛等草食动物也会在原生范围内主动追击。
            float warning = aquatic ? 14f : Math.Min(40f, Math.Max(predator ? 22f : 8f, chaseRange + 6f));
            threats.Add(new Threat(name, entity.Distance, aquatic, predator, entity.Position.X, entity.Position.Y, entity.Position.Z)
                { EntityId = entity.EntityId, WarningDistance = warning });
            if (threats.Count == 5) break;
        }
        return threats.ToArray();
    }

    public static bool NeedsAvoidance(Threat animal, bool inWater) =>
        (!animal.Aquatic || inWater) && animal.Distance <= (animal.WarningDistance > 0f ? animal.WarningDistance
            : animal.Predator ? animal.Aquatic ? 14f : 22f : 5f);

    public static bool IsDryTerrain(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)] is GrassBlock
        or DirtBlock or SandBlock or GravelBlock or GraniteBlock or SandstoneBlock or BasaltBlock;

    public static Shore? VisibleShore(JsonElement observation, ComponentBody body, ComponentBody? boat)
    {
        var surfaceBody = boat ?? body;
        float waterLevel = surfaceBody.Position.Y + surfaceBody.ImmersionDepth;
        var blocks = observation.GetProperty("visibleBlocks").EnumerateArray()
            .Select(block => (Cell: Cell(block.GetProperty("cell")), Value: block.GetProperty("value").GetInt32())).ToArray();
        var obstruction = blocks.Where(block => BlocksManager.Blocks[Terrain.ExtractContents(block.Value)]
            is FluidBlock || BlocksManager.Blocks[Terrain.ExtractContents(block.Value)].IsCollidable_(block.Value))
            .Select(block => block.Cell).ToHashSet();
        return blocks.Where(block => IsDryTerrain(block.Value))
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
