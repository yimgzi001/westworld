using System.Globalization;

namespace Game.Westworld {
    public static class RecipeTaskComposer {
        readonly record struct SlotState(int Value, int Count);

        // Plan against materials already carried or in the selected grid. Each action rechecks the live game state.
        public static IReadOnlyList<ActorTaskStep> Compose(AtomicActions port, IInventory inventory, ComponentCraftingTable workspace,
            RecipeGuideEntry recipe, int outputCount) {
            if (port == null || recipe?.Ingredients == null || recipe.Station is not (RecipeStation.PersonalOrWorkbench or RecipeStation.Workbench)
                || !float.IsFinite(recipe.RequiredHeat) || recipe.RequiredHeat > 0f
                || recipe.ResultCount <= 0 || outputCount <= 0 || outputCount % recipe.ResultCount != 0
                || Terrain.ExtractContents(recipe.ResultValue) == 0 || !port.CanComposeRecipe(inventory, workspace)) {
                return null;
            }

            int size = workspace.m_craftingGridSize;
            int maxSize = CraftingRecipe.MaxSize;
            RecipeIngredient[] ingredients = recipe.Ingredients.ToArray();
            if (size is < 1 || size > maxSize || workspace.ResultSlotIndex != size * size
                || (recipe.Station == RecipeStation.Workbench && size != maxSize)
                || ingredients.Length == 0 || ingredients.Length > size * size
                || ingredients.Any(item => item == null || item.Column < 0 || item.Column >= maxSize
                    || item.Row < 0 || item.Row >= maxSize || string.IsNullOrEmpty(item.CraftingId))
                || ingredients.Select(item => (item.Column, item.Row)).Distinct().Count() != ingredients.Length) {
                return null;
            }

            int batches = outputCount / recipe.ResultCount;
            if (!CanHoldRemains(workspace, recipe, batches)) {
                return null;
            }

            int left = ingredients.Min(item => item.Column);
            int top = ingredients.Min(item => item.Row);
            int width = ingredients.Max(item => item.Column) - left + 1;
            int height = ingredients.Max(item => item.Row) - top + 1;
            SlotState[] carried = ReadCarried(inventory);
            SlotState[] grid = Enumerable.Range(0, size * size).Select(slot => ReadSlot(workspace, slot)).ToArray();

            for (int row = 0; row <= size - height; row++) {
                for (int column = 0; column <= size - width; column++) {
                    for (int mirror = 0; mirror <= (width > 1 ? 1 : 0); mirror++) {
                        RecipeIngredient[] pattern = new RecipeIngredient[grid.Length];
                        foreach (RecipeIngredient ingredient in ingredients) {
                            int x = ingredient.Column - left;
                            int slot = column + (mirror == 0 ? x : width - 1 - x) + size * (row + ingredient.Row - top);
                            pattern[slot] = ingredient;
                        }
                        IReadOnlyList<ActorTaskStep> steps = TryCompose(port, inventory, workspace, recipe, outputCount, batches,
                            carried, grid, pattern);
                        if (steps != null) {
                            return steps;
                        }
                    }
                }
            }
            return null;
        }

        static IReadOnlyList<ActorTaskStep> TryCompose(AtomicActions port, IInventory inventory, ComponentCraftingTable workspace,
            RecipeGuideEntry recipe, int outputCount, int batches, SlotState[] carried, SlotState[] grid, RecipeIngredient[] pattern) {
            for (int slot = 0; slot < grid.Length; slot++) {
                if (grid[slot].Count > 0 && !MatchesIngredient(pattern[slot], grid[slot].Value)) {
                    return null;
                }
            }

            SlotState[] source = carried.ToArray();
            SlotState[] target = grid.ToArray();
            List<ActorTaskStep> steps = [];
            IEnumerable<int> orderedSlots = Enumerable.Range(0, pattern.Length).Where(slot => pattern[slot] != null)
                .OrderByDescending(slot => target[slot].Count > 0).ThenByDescending(slot => pattern[slot].Data.HasValue);
            foreach (int slot in orderedSlots) {
                int needed = batches - target[slot].Count;
                if (needed <= 0) {
                    continue;
                }
                int value = target[slot].Value;
                if (target[slot].Count <= 0) {
                    value = source.Where(item => item.Count > 0 && MatchesIngredient(pattern[slot], item.Value))
                        .GroupBy(item => item.Value)
                        .Where(items => items.Sum(item => (long)item.Count) >= needed && workspace.GetSlotCapacity(slot, items.Key) >= batches)
                        .OrderBy(items => items.Min(item => item.Count))
                        .Select(items => items.Key).FirstOrDefault();
                }
                if (Terrain.ExtractContents(value) == 0 || workspace.GetSlotCapacity(slot, value) < batches) {
                    return null;
                }

                IEnumerable<int> sourceSlots = Enumerable.Range(0, source.Length)
                    .Where(index => source[index].Count > 0 && source[index].Value == value).OrderBy(index => source[index].Count);
                foreach (int index in sourceSlots) {
                    if (needed == 0) {
                        break;
                    }
                    int moved = Math.Min(needed, source[index].Count);
                    TransferItems transfer = new(new InventorySlot(inventory, index, value),
                        new InventorySlot(workspace, slot, target[slot].Value), moved);
                    steps.Add(new ActorTaskStep($"Fill crafting slot {slot}", () => transfer, MinimumAffectedCount: moved));
                    source[index] = source[index] with { Count = source[index].Count - moved };
                    target[slot] = new SlotState(value, target[slot].Count + moved);
                    needed -= moved;
                }
                if (needed > 0 || steps.Count >= ActorTaskExecutor.MaxSteps) {
                    return null;
                }
            }

            string[] actualIngredients = new string[CraftingRecipe.MaxSize * CraftingRecipe.MaxSize];
            int size = workspace.m_craftingGridSize;
            for (int slot = 0; slot < target.Length; slot++) {
                if (target[slot].Count > 0) {
                    int value = target[slot].Value;
                    string id = BlocksManager.Blocks[Terrain.ExtractContents(value)].GetCraftingId(value);
                    int data = Terrain.ExtractData(value);
                    actualIngredients[slot % size + slot / size * CraftingRecipe.MaxSize] = $"{id}:{data.ToString(CultureInfo.InvariantCulture)}";
                }
            }
            if (!SameResult(port.MatchPlannedRecipe(workspace, actualIngredients), recipe)
                || !HasOutputCapacity(inventory, source, recipe, outputCount)) {
                return null;
            }

            steps.Add(new ActorTaskStep("Craft recipe", () => {
                if (!port.CanComposeRecipe(inventory, workspace) || !CanHoldRemains(workspace, recipe, batches)
                    || !HasOutputCapacity(inventory, ReadCarried(inventory), recipe, outputCount)) {
                    return null;
                }
                for (int slot = 0; slot < target.Length; slot++) {
                    SlotState state = ReadSlot(workspace, slot);
                    if (target[slot].Count > 0 ? state.Value != target[slot].Value || state.Count < batches : state.Count > 0) {
                        return null;
                    }
                }
                (CraftingRecipe current, int availableBatches) = workspace.ReadCraftingRecipe(port.RecipeActorLevel);
                return availableBatches >= batches && SameResult(current, recipe) ? new Craft(workspace, recipe.ResultValue, outputCount) : null;
            }, MinimumAffectedCount: outputCount));
            return steps.AsReadOnly();
        }

        static SlotState ReadSlot(IInventory inventory, int slot) => new(inventory.GetSlotValue(slot), inventory.GetSlotCount(slot));

        static SlotState[] ReadCarried(IInventory inventory) => Enumerable.Range(0, inventory.SlotsCount)
            .Select(slot => AtomicActions.IsRecipeInventorySlot(inventory, slot) ? ReadSlot(inventory, slot) : default).ToArray();

        static bool MatchesIngredient(RecipeIngredient ingredient, int value) {
            int contents = Terrain.ExtractContents(value);
            return ingredient != null && contents > 0 && contents < BlocksManager.Blocks.Length
                && BlocksManager.Blocks[contents]?.GetCraftingId(value) == ingredient.CraftingId
                && (!ingredient.Data.HasValue || ingredient.Data.Value == Terrain.ExtractData(value));
        }

        static bool SameResult(CraftingRecipe actual, RecipeGuideEntry recipe) => actual != null
            && actual.ResultValue == recipe.ResultValue && actual.ResultCount == recipe.ResultCount
            && actual.RemainsValue == recipe.RemainsValue && actual.RemainsCount == recipe.RemainsCount;

        static bool CanHoldRemains(ComponentCraftingTable workspace, RecipeGuideEntry recipe, int batches) {
            if (recipe.RemainsValue == 0 || recipe.RemainsCount <= 0) {
                return true;
            }
            SlotState remains = ReadSlot(workspace, workspace.RemainsSlotIndex);
            int contents = Terrain.ExtractContents(recipe.RemainsValue);
            return contents > 0 && contents < BlocksManager.Blocks.Length && BlocksManager.Blocks[contents] is Block block
                && (remains.Count <= 0 || remains.Value == recipe.RemainsValue)
                && (long)recipe.RemainsCount * batches <= block.GetMaxStacking(recipe.RemainsValue) - remains.Count;
        }

        static bool HasOutputCapacity(IInventory inventory, SlotState[] slots, RecipeGuideEntry recipe, int outputCount) {
            int remaining = outputCount;
            for (int slot = 0; slot < slots.Length && remaining > 0; slot++) {
                if (!AtomicActions.IsRecipeInventorySlot(inventory, slot) || slots[slot].Count > 0 && slots[slot].Value != recipe.ResultValue) {
                    continue;
                }
                int capacity = Math.Max(0, inventory.GetSlotCapacity(slot, recipe.ResultValue) - slots[slot].Count);
                remaining -= Math.Min(remaining, capacity / recipe.ResultCount * recipe.ResultCount);
            }
            return remaining == 0;
        }
    }

    public sealed partial class AtomicActions {
        internal float RecipeActorLevel => m_actor.FindComponent<ComponentPlayer>()?.PlayerData.Level ?? 1f;

        internal bool CanComposeRecipe(IInventory inventory, ComponentCraftingTable workspace) => IsActorAvailable && m_body != null
            && inventory != null && inventory is not ComponentCreativeInventory && inventory == CarryInventory && workspace != inventory
            && workspace != null && WorldPickables != null && m_sleep?.IsSleeping != true
            && (workspace.Entity == m_actor || CanTargetWorld())
            && CheckInventoryAccess(inventory) == AtomicActionFailure.None && CheckInventoryAccess(workspace) == AtomicActionFailure.None;

        internal static bool IsRecipeInventorySlot(IInventory inventory, int slot) => IsInventorySlot(inventory, slot);

        internal CraftingRecipe MatchPlannedRecipe(ComponentCraftingTable workspace, string[] ingredients) =>
            CraftingRecipesManager.FindMatchingRecipe(workspace.Project.FindSubsystem<SubsystemTerrain>(true), ingredients, 0f, RecipeActorLevel);
    }
}
