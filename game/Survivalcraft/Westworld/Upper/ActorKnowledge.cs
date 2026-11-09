namespace Game.Westworld {
    public sealed record KnownRecipeView(string Reference, RecipeGuideEntry Recipe);

    public sealed class ActorKnowledge {
        const int MaxIssuedRecipes = 1024;

        readonly bool m_publicGuide;
        readonly HashSet<int> m_items = [];
        readonly List<RecipeGuideEntry> m_recipes = [];
        readonly Dictionary<string, CreatureGuideEntry> m_creatures = new(StringComparer.Ordinal);
        readonly Dictionary<string, RecipeGuideEntry> m_issuedRecipes = new(StringComparer.Ordinal);
        readonly Queue<string> m_issueOrder = new();
        readonly string m_scope = Guid.NewGuid().ToString("N");
        int m_sequence;

        public ActorKnowledge(bool publicGuide = false) => m_publicGuide = publicGuide;

        // These grants are available to the simulation host. Tool callers only read this knowledge.
        public void TeachItem(int value) {
            if (GameKnowledge.ReadItem(value) is ItemGuideEntry item) {
                m_items.Add(item.Value);
            }
        }

        public void TeachRecipe(RecipeGuideEntry recipe) {
            ArgumentNullException.ThrowIfNull(recipe);
            if (!m_recipes.Any(known => SameRecipe(known, recipe))) {
                m_recipes.Add(recipe with { Ingredients = Array.AsReadOnly(recipe.Ingredients.ToArray()) });
            }
            TeachItem(recipe.ResultValue);
            if (recipe.RemainsValue != 0) {
                TeachItem(recipe.RemainsValue);
            }
        }

        public bool TeachCreature(string type) {
            CreatureGuideEntry entry = GameKnowledge.FindCreatures(type, 0, 100).FirstOrDefault(item => item.Type == type);
            if (entry == null) {
                return false;
            }
            m_creatures[entry.Type] = entry;
            return true;
        }

        public ItemGuideEntry ReadItem(int value) =>
            m_publicGuide || m_items.Contains(Terrain.ReplaceLight(value, 0)) ? GameKnowledge.ReadItem(value) : null;

        public IReadOnlyList<ItemGuideEntry> FindItems(string search = null, int offset = 0, int limit = 32) {
            CheckPage(offset, limit);
            return m_publicGuide ? GameKnowledge.FindItems(search, offset, limit) : m_items.Order()
                .Select(GameKnowledge.ReadItem)
                .Where(item => item != null && Matches(search, item.Type, item.Name, item.CraftingId))
                .Skip(offset)
                .Take(limit)
                .ToArray();
        }

        public IReadOnlyList<CreatureGuideEntry> FindCreatures(string search = null, int offset = 0, int limit = 32) {
            CheckPage(offset, limit);
            return m_publicGuide ? GameKnowledge.FindCreatures(search, offset, limit) : m_creatures.Values
                .Where(item => Matches(search, item.Type, item.Name))
                .OrderBy(item => item.Type, StringComparer.Ordinal)
                .Skip(offset)
                .Take(limit)
                .ToArray();
        }

        public IReadOnlyList<KnownRecipeView> FindRecipes(int? resultValue = null, int? ingredientValue = null,
            int offset = 0, int limit = 32) {
            CheckPage(offset, limit);
            IReadOnlyList<RecipeGuideEntry> recipes;
            if (m_publicGuide) {
                recipes = GameKnowledge.FindRecipes(resultValue, ingredientValue, offset, limit);
            }
            else {
                ItemGuideEntry ingredient = ingredientValue.HasValue ? ReadItem(ingredientValue.Value) : null;
                if (ingredientValue.HasValue && ingredient == null) {
                    return [];
                }
                int? ingredientData = ingredientValue.HasValue ? Terrain.ExtractData(ingredientValue.Value) : null;
                recipes = m_recipes
                    .Where(recipe => !resultValue.HasValue || recipe.ResultValue == Terrain.ReplaceLight(resultValue.Value, 0))
                    .Where(recipe => ingredient == null || recipe.Ingredients.Any(item =>
                        item.CraftingId == ingredient.CraftingId && (!item.Data.HasValue || item.Data == ingredientData)))
                    .Skip(offset)
                    .Take(limit)
                    .ToArray();
            }
            return recipes.Select(PublishRecipe).ToArray();
        }

        internal RecipeGuideEntry ResolveRecipe(string reference) => reference != null
            && m_issuedRecipes.TryGetValue(reference, out RecipeGuideEntry recipe) ? recipe : null;

        KnownRecipeView PublishRecipe(RecipeGuideEntry recipe) {
            foreach ((string reference, RecipeGuideEntry published) in m_issuedRecipes) {
                if (SameRecipe(recipe, published)) {
                    return new KnownRecipeView(reference, published);
                }
            }
            if (m_issuedRecipes.Count == MaxIssuedRecipes) {
                m_issuedRecipes.Remove(m_issueOrder.Dequeue());
            }
            string next = $"recipe_{m_scope}_{++m_sequence}";
            m_issuedRecipes.Add(next, recipe);
            m_issueOrder.Enqueue(next);
            return new KnownRecipeView(next, recipe);
        }

        static bool SameRecipe(RecipeGuideEntry a, RecipeGuideEntry b) => a.ResultValue == b.ResultValue
            && a.ResultCount == b.ResultCount && a.RemainsValue == b.RemainsValue && a.RemainsCount == b.RemainsCount
            && a.RequiredHeat == b.RequiredHeat && a.RequiredLevel == b.RequiredLevel && a.Ingredients.SequenceEqual(b.Ingredients);

        static bool Matches(string search, params string[] values) => string.IsNullOrWhiteSpace(search)
            || values.Any(value => value?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true);

        static void CheckPage(int offset, int limit) {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);
        }
    }
}
