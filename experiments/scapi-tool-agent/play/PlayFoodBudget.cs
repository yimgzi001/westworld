#nullable enable

using Game;

internal static class PlayFoodBudget
{
    internal sealed record Reserve(float Safe, float Risky);

    public static float DailyNeed(ComponentPlayer player)
    {
        var vitals = player.ComponentVitalStats;
        float day = player.Project.FindSubsystem<SubsystemTimeOfDay>(true).DayDuration;
        // 留足一天基础消耗，并计入采集走动和冷病恢复的余量。生肉和生蛋不能冒充可靠口粮。
        return Math.Clamp(player.ComponentLevel.HungerFactor * day / 2880f * 1.35f
            + (vitals.Temperature < 6f || player.ComponentFlu.HasFlu ? 0.25f : 0.15f), 0.8f, 1.3f);
    }

    public static bool NeedsSupply(ComponentPlayer player) => player.ComponentVitalStats.Food + Count(player).Safe < DailyNeed(player);

    public static bool StockWindow(SubsystemTimeOfDay calendar)
    {
        float seconds = (calendar.DuskStart - calendar.TimeOfDay) * calendar.DayDuration;
        return calendar.TimeOfDay >= calendar.DayStart && seconds is > 45f and <= 210f;
    }

    public static float NeedThroughMorning(ComponentPlayer player, SubsystemTimeOfDay calendar)
    {
        float now = calendar.TimeOfDay;
        float days = calendar.DayStart - now + (now >= calendar.DayStart ? 1f : 0f);
        // 原生基础饥饿消耗为饥饿系数乘游戏秒数除以 2880。再给醒来找食物留出余量。
        return Math.Min(0.7f, player.ComponentLevel.HungerFactor * days * calendar.DayDuration / 2880f + 0.12f);
    }

    public static Reserve Count(ComponentPlayer player)
    {
        var inventory = player.ComponentMiner.Inventory;
        bool sick = player.ComponentSickness.IsSick;
        var food = Enumerable.Range(0, inventory.SlotsCount).Where(slot => inventory.GetSlotCount(slot) > 0)
            .Select(slot => (Value: inventory.GetSlotValue(slot), Count: inventory.GetSlotCount(slot)))
            .Where(item => !PlaySurvivalFood.IsRawMeat(item.Value)).Select(item =>
            {
                int contents = Terrain.ExtractContents(item.Value);
                var block = BlocksManager.Blocks[contents];
                return (Contents: contents, item.Count, Nutrition: block.GetNutritionalValue(item.Value) * (sick ? 0.75f : 1f),
                    Risk: block.GetSicknessProbability(item.Value));
            }).Where(item => item.Nutrition > 0f && item.Risk <= (sick ? 0f : 0.15f))
            .OrderBy(item => item.Risk).ThenByDescending(item => item.Nutrition);
        var remaining = new Dictionary<int, float>();
        float safe = 0f, risky = 0f;
        foreach (var item in food)
        {
            float allowance = remaining.GetValueOrDefault(item.Contents,
                3.5f - player.ComponentVitalStats.m_satiation.GetValueOrDefault(item.Contents));
            float serving = Math.Max(0.5f, item.Nutrition);
            int count = Math.Clamp((int)MathF.Floor(allowance / serving), 0, item.Count);
            remaining[item.Contents] = allowance - count * serving;
            if (item.Risk <= 0f) safe += count * item.Nutrition;
            else risky += count * item.Nutrition;
        }
        return new(safe, risky);
    }
}
