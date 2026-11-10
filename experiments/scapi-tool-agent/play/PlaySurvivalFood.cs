#nullable enable

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Decisions.Local;
using Westworld.Scapi.Adapter;

internal static class PlaySurvivalFood
{
    private const int NearbyBlockLimit = 8;
    private const int FoodBlockLimit = 8;
    private const float EmergencyFood = 0.24f;
    public const string HarvestTool = "harvest_visible_food";

    public static bool IsRawMeat(int value)
    {
        int contents = Terrain.ExtractContents(value);
        return contents == RawMeatBlock.Index || contents == RawBirdBlock.Index || contents == RawFishBlock.Index;
    }

    public static bool CanEat(ComponentPlayer player, int value)
    {
        int contents = Terrain.ExtractContents(value);
        if (BlocksManager.Blocks[contents] is not { } block) return false;
        var vitals = player.ComponentVitalStats;
        bool sick = player.ComponentSickness.IsSick;
        float nutrition = block.GetNutritionalValue(value);
        if (nutrition <= 0f || vitals.Food >= 0.98f || IsRawMeat(value)
            || !AcceptableRisk(block.GetSicknessProbability(value), vitals.Food, sick)) return false;

        // 原生按方块种类累计同类食物。提前排除这次进食就会超过原生致病线的食物。
        float added = Math.Max(0.5f, nutrition * (sick ? 0.75f : 1f));
        return vitals.m_satiation.GetValueOrDefault(contents) + added <= 3.5f;
    }

    public static bool CanSeek(ComponentPlayer player, Food food) => !food.Underwater && !IsRawMeat(food.Value)
        && (food.SicknessRisk <= 0f || food.SicknessRisk <= 0.15f
            && BlocksManager.Blocks[Terrain.ExtractContents(food.Value)] is EggBlock && RawEggStock(player) < 2);

    public static int RawEggStock(ComponentPlayer player)
    {
        var inventory = player.ComponentMiner.Inventory;
        return Enumerable.Range(0, inventory.SlotsCount).Where(slot =>
        {
            int value = inventory.GetSlotValue(slot);
            return inventory.GetSlotCount(slot) > 0 && BlocksManager.Blocks[Terrain.ExtractContents(value)] is EggBlock egg
                && egg.GetSicknessProbability(value) is > 0f and <= 0.15f;
        }).Sum(inventory.GetSlotCount);
    }

    private static bool AcceptableRisk(float risk, float food, bool sick) =>
        risk <= 0f || !sick && food <= EmergencyFood && risk <= 0.15f;

    internal sealed record Food(string Reference, string Name, string Source, float Distance, float Nutrition,
        float SicknessRisk, bool Underwater, bool InReach, int Value, JsonElement Position)
    {
        [JsonIgnore]
        public Vector3 Point => ReadPosition(Position);

        public bool Matches(Food other) => Source == other.Source && Value == other.Value
            && (Point - other.Point).LengthSquared() <= 1f;
    }

    public static Vector3 ReadPosition(JsonElement position) => new(position.GetProperty("x").GetSingle(),
        position.GetProperty("y").GetSingle(), position.GetProperty("z").GetSingle());

    public static bool CanApproachOnLand(Food food, Vector3 origin)
    {
        var offset = food.Point - origin;
        return !food.Underwater && offset.Y is >= -1.05f and <= 1.9f && offset.XZ.LengthSquared() <= 64f;
    }

    public static ToolOffer? HarvestOffer(IEnumerable<Food> foods)
    {
        var targets = foods.Where(food => food.Source == "harvest" && food.Distance <= 3.2f)
            .Select(food => food.Reference).ToArray();
        if (targets.Length == 0) return null;
        return new(HarvestTool, "Dig a nearby visible food block, look for its drop, and try to collect it. Does not eat it.",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { target = new { type = "string" } },
                required = new[] { "target" },
                additionalProperties = false
            }), new Dictionary<string, IReadOnlyList<string>> { ["target"] = targets });
    }

    public static (JsonElement Observation, Food[] Foods) Project(JsonElement observation)
    {
        var blocks = observation.GetProperty("visibleBlocks").EnumerateArray().ToArray();
        var edibleBlocks = blocks.Select(block => (Block: block, Food: Describe(block, "value", "harvest")))
            .Where(entry => entry.Food is not null).Take(FoodBlockLimit).ToArray();
        var foods = new List<Food>();
        if (observation.TryGetProperty("focus", out var observedFocus)
            && Describe(observedFocus, "blockValue", "harvest") is { } focus)
            foods.Add(focus);
        foods.AddRange(edibleBlocks.Select(entry => entry.Food!));
        foods.AddRange(VisibleDrops(observation));

        // 只提高眼前食物的排序优先级。引用仍由该角色的原生可见扫描签发。
        var view = JsonNode.Parse(observation.GetRawText())!.AsObject();
        var projected = blocks.Take(NearbyBlockLimit).Concat(edibleBlocks.Select(entry => entry.Block))
            .DistinctBy(block => block.GetProperty("reference").GetString()).ToArray();
        view["visibleBlocks"] = JsonSerializer.SerializeToNode(projected);
        view["coverage"]!["modelOmittedBlocks"] = blocks.Length - projected.Length;
        return (JsonSerializer.SerializeToElement(view), foods.DistinctBy(food => food.Reference).ToArray());
    }

    public static IEnumerable<Food> VisibleDrops(JsonElement observation) =>
        observation.GetProperty("pickables").EnumerateArray()
            .Select(item => Describe(item, "value", "pickup")).OfType<Food>();

    public static IEnumerable<Food> VisibleIn(ActorPanorama panorama)
    {
        foreach (var block in panorama.Blocks)
            if (FoodCapabilities.Describe(block.Value)?.TerrainHarvest == true
                && Describe(block.Value, "harvest", string.Empty, block.Distance, false, () => PointJson(block.Point)) is { } food)
                yield return food;
        foreach (var item in panorama.Objects.Pickables)
            if (Describe(item.Value, "pickup", string.Empty, item.Distance, false, () => PointJson(item.Position)) is { } food)
                yield return food;
    }

    private static JsonElement PointJson(Vector3 point) => JsonSerializer.SerializeToElement(new { x = point.X, y = point.Y, z = point.Z });

    private static Food? Describe(JsonElement entry, string valueProperty, string source)
    {
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty(valueProperty, out var raw)
            || raw.ValueKind != JsonValueKind.Number || !raw.TryGetInt32(out int value)
            || !entry.TryGetProperty("reference", out var target) || target.GetString() is not { } reference)
            return null;
        float distance = entry.GetProperty("distance").GetSingle();
        bool inReach = source == "pickup" ? entry.TryGetProperty("inReach", out var reach) && reach.ValueKind == JsonValueKind.True
            : distance <= 3.1f;
        return Describe(value, source, reference, distance, inReach, () => entry.GetProperty("position"));
    }

    private static Food? Describe(int value, string source, string reference, float distance, bool inReach, Func<JsonElement> position)
    {
        var known = FoodCapabilities.Describe(value);
        if (source == "harvest" && known?.TerrainHarvest != true) return null;
        var block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
        float nutrition = block.GetNutritionalValue(value);
        if (nutrition <= 0f) return null;
        return new(reference, known?.Name ?? block.GetDisplayName(null, value), source,
            distance, nutrition, block.GetSicknessProbability(value),
            source == "harvest" && known?.Contents == SeaUrchinBlock.Index, inReach, value, position());
    }
}
