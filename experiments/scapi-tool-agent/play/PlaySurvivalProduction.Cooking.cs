#nullable enable

using Game;
using Game.Westworld;

internal sealed partial class PlaySurvivalProduction
{
    private RecipeGuideEntry? cookingRecipe;
    private int cookingInput, retrievedValue;

    private static bool IsCookedMeat(int value)
    {
        int contents = Terrain.ExtractContents(value);
        return contents == CookedMeatBlock.Index || contents == CookedBirdBlock.Index || contents == CookedFishBlock.Index;
    }

    private static bool FuelSuitable(int value, float heat, float seconds) =>
        BlocksManager.Blocks[Terrain.ExtractContents(value)] is { } block && block.GetExplosionPressure(value) <= 0f
        && block.GetFuelHeatLevel(value) >= heat && block.GetFuelFireDuration(value) >= seconds;

    private void AdvanceCook(double gameTime)
    {
        if (retrievedValue != 0)
        {
            if (Added > 0)
                log(new { kind = "play_meat_cooked", actor = player.PlayerData.Name, input = cookingInput,
                    value = retrievedValue, count = Added, gameTime });
            Finish(Added > 0, Added > 0 ? "cooked_meat_retrieved" : "cooked_meat_inventory_unconfirmed");
            return;
        }
        if (gameTime < nextPoll) return;
        nextPoll = gameTime + 0.2;

        var oven = ObservedStation<ComponentFurnace>();
        if (oven is null || owner.Queries.ReadStorage(oven)?.Furnace is null)
        {
            Finish(false, "furnace_not_in_reach");
            return;
        }
        var carry = player.ComponentMiner.Inventory;
        int resultSlot = oven.ResultSlotIndex;
        if (oven.GetSlotCount(resultSlot) > 0)
        {
            int value = oven.GetSlotValue(resultSlot);
            int to = Enumerable.Range(0, carry.SlotsCount).FirstOrDefault(slot =>
                (carry.GetSlotCount(slot) == 0 || carry.GetSlotValue(slot) == value)
                && carry.GetSlotCapacity(slot, value) > carry.GetSlotCount(slot), -1);
            if (!IsCookedMeat(value) || to < 0) { Finish(false, "furnace_output_unavailable"); return; }
            retrievedValue = value;
            Transfer(oven, resultSlot, carry, to);
            return;
        }

        // 当前只烹饪单种生肉，避免误用需要水桶或其他材料的原生配方。
        if (Enumerable.Range(1, Math.Max(0, oven.FuelSlotIndex - 1)).Any(slot => oven.GetSlotCount(slot) > 0))
        {
            Finish(false, "furnace_has_other_ingredients");
            return;
        }
        bool loaded = oven.GetSlotCount(0) > 0;
        int input = loaded ? -1 : FindSlot(carry, PlaySurvivalFood.IsRawMeat);
        int raw = loaded ? oven.GetSlotValue(0) : input >= 0 ? carry.GetSlotValue(input) : 0;
        if (!PlaySurvivalFood.IsRawMeat(raw) || oven.SmeltSpeed <= 0f || oven.FuelTimeEfficiency <= 0f)
        {
            Finish(false, "raw_meat_or_furnace_unavailable");
            return;
        }
        if (cookingRecipe is null || raw != cookingInput)
        {
            cookingInput = raw;
            cookingRecipe = GameKnowledge.FindRecipes(ingredientValue: raw).FirstOrDefault(recipe => recipe.Station == RecipeStation.Furnace
                && recipe.Ingredients.Count == 1 && IsCookedMeat(recipe.ResultValue));
        }
        if (cookingRecipe is not { } recipeEntry) { Finish(false, "native_meat_recipe_missing"); return; }

        float seconds = (1f - Math.Clamp(oven.SmeltingProgress, 0f, 1f)) / oven.SmeltSpeed;
        float unscaledSeconds = seconds / oven.FuelTimeEfficiency;
        int fuelSlot = oven.FuelSlotIndex;
        bool burning = oven.HeatLevel >= recipeEntry.RequiredHeat && oven.FireTimeRemaining >= seconds;
        bool fueled = oven.GetSlotCount(fuelSlot) > 0 && FuelSuitable(oven.GetSlotValue(fuelSlot), recipeEntry.RequiredHeat, unscaledSeconds);
        if (!burning && !fueled)
        {
            int fuel = FindSlot(carry, value => FuelSuitable(value, recipeEntry.RequiredHeat, unscaledSeconds));
            if (fuel < 0 || oven.GetSlotCount(fuelSlot) > 0) { Finish(false, "compatible_furnace_fuel_missing"); return; }
            Transfer(carry, fuel, oven, fuelSlot);
            return;
        }
        if (!loaded) Transfer(carry, input, oven, 0);
    }
}
