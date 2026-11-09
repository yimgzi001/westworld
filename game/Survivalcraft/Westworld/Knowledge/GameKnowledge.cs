using TemplatesDatabase;

namespace Game.Westworld {
    public sealed record ItemGuideEntry(int Value, string Type, string Name, string Category, string Description,
        string CraftingId, int MaxStack, float Nutrition, float FuelHeat, float FuelDuration, int Durability, bool CanWear, bool CanAim);

    public sealed record RecipeIngredient(int Column, int Row, string CraftingId, int? Data);

    public enum RecipeStation { PersonalOrWorkbench, Workbench, Furnace }

    public sealed record RecipeGuideEntry(int ResultValue, int ResultCount, int RemainsValue, int RemainsCount,
        RecipeStation Station, float RequiredHeat, float RequiredLevel, string Description, IReadOnlyList<RecipeIngredient> Ingredients);

    public sealed record CreatureGuideEntry(string Type, string Name, string Description, float WalkSpeed, float SwimSpeed, float FlySpeed,
        bool CanBeRidden);

    // Static reference data, equivalent to the game's recipe book and bestiary.
    public static class GameKnowledge {
        public static ItemGuideEntry ReadItem(int value) {
            if (FindBlock(value) is not Block block) {
                return null;
            }
            value = Terrain.ReplaceLight(value, 0);
            return new ItemGuideEntry(
                value,
                block.GetType().Name,
                block.GetDisplayName(null, value),
                block.GetCategory(value),
                block.GetDescription(value),
                block.GetCraftingId(value),
                block.GetMaxStacking(value),
                block.GetNutritionalValue(value),
                block.GetFuelHeatLevel(value),
                block.GetFuelFireDuration(value),
                block.GetDurability(value),
                block.CanWear(value),
                block.IsAimable_(value)
            );
        }

        public static IReadOnlyList<ItemGuideEntry> FindItems(string search = null, int offset = 0, int limit = 32) {
            ValidatePage(offset, limit);
            return BlocksManager.Blocks
                .Where(block => block is { BlockIndex: > 0 })
                .SelectMany(block => block.GetCreativeValues())
                .Distinct()
                .Where(value => FindBlock(value) != null)
                .Where(value => {
                    Block block = FindBlock(value);
                    return Matches(search, block.GetType().Name, block.GetDisplayName(null, value), block.GetCraftingId(value));
                })
                .OrderBy(value => FindBlock(value).GetDisplayOrder(value))
                .Skip(offset)
                .Take(limit)
                .Select(ReadItem)
                .ToArray();
        }

        public static IReadOnlyList<RecipeGuideEntry> FindRecipes(int? resultValue = null, int? ingredientValue = null,
            int offset = 0, int limit = 32) {
            ValidatePage(offset, limit);
            if (resultValue.HasValue && FindBlock(resultValue.Value) == null) {
                return [];
            }
            Block ingredient = ingredientValue.HasValue ? FindBlock(ingredientValue.Value) : null;
            if (ingredientValue.HasValue && ingredient == null) {
                return [];
            }
            string ingredientId = ingredientValue.HasValue ? ingredient.GetCraftingId(ingredientValue.Value) : null;
            int ingredientData = ingredientValue.HasValue ? Terrain.ExtractData(ingredientValue.Value) : 0;
            return CraftingRecipesManager.Recipes
                .Where(recipe => recipe.ResultValue != 0 && (!resultValue.HasValue
                    || Terrain.ReplaceLight(recipe.ResultValue, 0) == Terrain.ReplaceLight(resultValue.Value, 0)))
                .Where(recipe => !ingredientValue.HasValue || recipe.Ingredients.Any(raw => MatchesIngredient(raw, ingredientId, ingredientData)))
                .Skip(offset)
                .Take(limit)
                .Select(SnapshotRecipe)
                .ToArray();
        }

        public static IReadOnlyList<CreatureGuideEntry> FindCreatures(string search = null, int offset = 0, int limit = 32) {
            ValidatePage(offset, limit);
            return DatabaseManager.EntitiesValuesDictionaries
                .Select(ReadCreature)
                .Where(entry => entry != null && Matches(search, entry.Type, entry.Name))
                .Skip(offset)
                .Take(limit)
                .ToArray();
        }

        internal static RecipeGuideEntry SnapshotRecipe(CraftingRecipe recipe) {
            List<RecipeIngredient> ingredients = [];
            for (int index = 0; index < recipe.Ingredients.Length; index++) {
                string raw = recipe.Ingredients[index];
                if (!string.IsNullOrEmpty(raw)) {
                    CraftingRecipesManager.DecodeIngredient(raw, out string id, out int? data);
                    ingredients.Add(new RecipeIngredient(index % CraftingRecipe.MaxSize, index / CraftingRecipe.MaxSize, id, data));
                }
            }

            bool needsWorkbench = ingredients.Count > 0 && (ingredients.Max(item => item.Column) - ingredients.Min(item => item.Column) >= 2
                || ingredients.Max(item => item.Row) - ingredients.Min(item => item.Row) >= 2);
            RecipeStation station = recipe.RequiredHeatLevel > 0f ? RecipeStation.Furnace
                : needsWorkbench ? RecipeStation.Workbench : RecipeStation.PersonalOrWorkbench;
            return new RecipeGuideEntry(
                recipe.ResultValue, recipe.ResultCount, recipe.RemainsValue, recipe.RemainsCount, station,
                recipe.RequiredHeatLevel, recipe.RequiredPlayerLevel, recipe.Description, ingredients.ToArray()
            );
        }

        static CreatureGuideEntry ReadCreature(ValuesDictionary template) {
            ValuesDictionary creature = DatabaseManager.FindValuesDictionaryForComponent(template, typeof(ComponentCreature));
            ValuesDictionary egg = template.GetValue<ValuesDictionary>("CreatureEggData", null);
            ValuesDictionary player = template.GetValue<ValuesDictionary>("Player", null);
            if (creature == null || (egg == null && player == null) || (egg != null && egg.GetValue<int>("EggTypeIndex") < 0)) {
                return null;
            }
            string name = TranslateCreatureText(creature.GetValue<string>("DisplayName", string.Empty), "DisplayName");
            if (string.IsNullOrEmpty(name)) {
                return null;
            }
            ValuesDictionary motion = DatabaseManager.FindValuesDictionaryForComponent(template, typeof(ComponentLocomotion));
            return new CreatureGuideEntry(
                template.DatabaseObject.Name,
                name,
                TranslateCreatureText(creature.GetValue<string>("Description", string.Empty), "Description"),
                motion?.GetValue<float>("WalkSpeed") ?? 0f,
                motion?.GetValue<float>("SwimSpeed") ?? 0f,
                motion?.GetValue<float>("FlySpeed") ?? 0f,
                DatabaseManager.FindValuesDictionaryForComponent(template, typeof(ComponentMount)) != null
            );
        }

        static string TranslateCreatureText(string value, string group) {
            int separator = value.IndexOf(':');
            return value.StartsWith('[') && value.EndsWith(']') && separator >= 0 && separator < value.Length - 2
                ? LanguageControl.GetDatabase(group, value[(separator + 1)..^1]) : value;
        }

        static Block FindBlock(int value) {
            int index = Terrain.ExtractContents(value);
            return index > 0 && index < BlocksManager.Blocks.Length && BlocksManager.Blocks[index] is Block block && block.BlockIndex == index
                ? block : null;
        }

        static bool MatchesIngredient(string raw, string wantedId, int wantedData) {
            if (string.IsNullOrEmpty(raw)) {
                return false;
            }
            CraftingRecipesManager.DecodeIngredient(raw, out string id, out int? data);
            return wantedId == id && (!data.HasValue || data.Value == wantedData);
        }

        static bool Matches(string search, params string[] values) =>
            string.IsNullOrWhiteSpace(search) || values.Any(value => value?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true);

        static void ValidatePage(int offset, int limit) {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);
        }
    }
}
