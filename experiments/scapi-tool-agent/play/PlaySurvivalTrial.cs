#nullable enable

using Game;
using Game.Westworld;
using GameEntitySystem;

internal sealed class PlaySurvivalTrial
{
    private const int RequiredDays = 2;
    private readonly SubsystemTimeOfDay calendar;
    private readonly double startDay;
    private readonly double startTime;
    private readonly Actor[] actors;
    private readonly Action<object> log;
    private double nextSample;

    private sealed class Actor(ComponentPlayer player)
    {
        public ComponentPlayer Player { get; } = player;
        public Guid NativeId { get; } = NativeAcquisitionAudit.IdentityFor(player.Entity);
        public long InitialMeals { get; } = player.PlayerStats.FoodItemsEaten;
        public long InitialSleeps { get; } = player.PlayerStats.TimesWentToSleep;
        public float MinFood { get; set; } = player.ComponentVitalStats.Food;
        public float MinHealth { get; set; } = player.ComponentHealth.Health;
        public int RemovedFood { get; set; }
        public int PickedFood { get; set; }
        public int PickedFoodMeals { get; private set; }
        public Dictionary<int, int> PickedByValue { get; } = new();
        public List<(int Value, float FoodAfter, bool FromPickup)> NewMeals { get; } = new();
        public long Meals => Player.PlayerStats.FoodItemsEaten - InitialMeals;
        public long Sleeps => Player.PlayerStats.TimesWentToSleep - InitialSleeps;
        public double? DiedAt { get; set; }

        public void OnMeal(int value)
        {
            int remaining = PickedByValue.GetValueOrDefault(value);
            bool fromPickup = remaining > 0
                && NativeAcquisitionAudit.InventoryCount(Player.ComponentMiner.Inventory, value) == remaining - 1;
            PickedByValue[value] = fromPickup ? remaining - 1 : 0;
            if (fromPickup)
            {
                PickedFoodMeals++;
            }
            NewMeals.Add((value, Player.ComponentVitalStats.Food, fromPickup));
        }
    }

    public PlaySurvivalTrial(Project project, IEnumerable<ComponentPlayer> players, double gameTime, Action<object> log)
    {
        this.log = log;
        calendar = project.FindSubsystem<SubsystemTimeOfDay>(true);
        startDay = calendar.Day;
        startTime = gameTime;
        actors = players.Select(player => new Actor(player)).ToArray();
        foreach (var actor in actors)
        {
            var inventory = actor.Player.ComponentMiner.Inventory;
            for (int slot = 0; slot < inventory.SlotsCount; slot++)
            {
                int count = inventory.GetSlotCount(slot);
                if (count <= 0 || !IsFood(inventory.GetSlotValue(slot))) continue;
                if (inventory.RemoveSlotItems(slot, count) != count || inventory.GetSlotCount(slot) != 0)
                    throw new InvalidOperationException("清理开局食物失败。");
                actor.RemovedFood += count;
            }
        }
        log(new { kind = "play_survival_start", requiredDays = RequiredDays, startDay, gameTime,
            actors = actors.Select(actor => new { name = actor.Player.PlayerData.Name, actor.RemovedFood, initialEdibleItems = 0 }) });
        foreach (var actor in actors) actor.Player.ComponentVitalStats.FoodEaten += actor.OnMeal;
        NativeAcquisitionAudit.Recorded += OnAcquisition;
    }

    public bool Passed { get; private set; }
    public int DeathCount => actors.Count(actor => actor.DiedAt.HasValue);
    public bool AllDied => DeathCount == actors.Length;

    private bool MilestoneReached => calendar.Day - startDay >= RequiredDays && actors.All(actor =>
        actor.MinHealth > 0f && actor.MinFood > 0f && actor.Meals >= 2 && actor.PickedFoodMeals >= 2 && actor.Sleeps > 0
        && actor.Player.ComponentVitalStats is { Food: >= 0.25f, Sleep: >= 0.25f });

    public void Sample(double gameTime, bool voluntaryRest)
    {
        foreach (var actor in actors)
        {
            if (actor.DiedAt.HasValue) continue;
            var player = actor.Player;
            actor.MinFood = Math.Min(actor.MinFood, player.ComponentVitalStats.Food);
            actor.MinHealth = Math.Min(actor.MinHealth, player.ComponentHealth.Health);
            foreach (var meal in actor.NewMeals)
                log(new { kind = "play_survival_meal", actor = player.PlayerData.Name, meal.Value, meal.FoodAfter,
                    meal.FromPickup, actor.Meals, gameTime });
            actor.NewMeals.Clear();
            if (player.ComponentHealth.Health <= 0f)
            {
                actor.DiedAt = gameTime;
                log(new { kind = "play_survival_death", actor = player.PlayerData.Name,
                    cause = player.ComponentHealth.CauseOfDeath, gameTime,
                    elapsedGameSeconds = gameTime - startTime, days = calendar.Day - startDay,
                    nativeDeathTime = player.ComponentHealth.DeathTime, food = player.ComponentVitalStats.Food,
                    sleep = player.ComponentVitalStats.Sleep, temperature = player.ComponentVitalStats.Temperature,
                    wetness = player.ComponentVitalStats.Wetness, actor.Meals, actor.PickedFoodMeals, actor.Sleeps });
            }
        }
        if (!Passed && voluntaryRest && MilestoneReached)
        {
            Passed = true;
            LogState("play_survival_milestone", gameTime);
        }
        if (gameTime < nextSample) return;
        nextSample = gameTime + 20;
        LogState("play_survival_sample", gameTime);
    }

    public void Finish(double gameTime)
    {
        NativeAcquisitionAudit.Recorded -= OnAcquisition;
        foreach (var actor in actors) actor.Player.ComponentVitalStats.FoodEaten -= actor.OnMeal;
        LogState("play_survival_summary", gameTime);
    }

    private void OnAcquisition(NativeAcquisitionFact fact)
    {
        var actor = actors.FirstOrDefault(actor => actor.NativeId == fact.ActorId);
        if (actor is null || !IsFood(fact.Value) || fact.InventoryBefore is not int before
            || fact.InventoryAfter is not int after || after <= before || after - before != fact.SourceBefore - fact.SourceAfter)
            return;
        int added = after - before;
        actor.PickedFood += added;
        // 对有其他未归因来源的同值堆叠只记录拾取数量，不猜测之后吃掉的是哪份。
        actor.PickedByValue[fact.Value] = before == actor.PickedByValue.GetValueOrDefault(fact.Value) ? after : 0;
    }

    private static bool IsFood(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)].GetNutritionalValue(value) > 0f;

    private void LogState(string kind, double gameTime) => log(new
    {
        kind, passed = Passed, gameTime, elapsedGameSeconds = gameTime - startTime, days = calendar.Day - startDay,
        deaths = DeathCount,
        actors = actors.Select(actor => new
        {
            name = actor.Player.PlayerData.Name, health = actor.Player.ComponentHealth.Health,
            food = actor.Player.ComponentVitalStats.Food, sleep = actor.Player.ComponentVitalStats.Sleep,
            sleeping = actor.Player.ComponentSleep.IsSleeping, actor.MinHealth, actor.MinFood, actor.DiedAt,
            causeOfDeath = actor.Player.ComponentHealth.CauseOfDeath, temperature = actor.Player.ComponentVitalStats.Temperature,
            actor.Meals, actor.PickedFood, actor.PickedFoodMeals, actor.Sleeps,
            position = new { x = actor.Player.ComponentBody.Position.X, y = actor.Player.ComponentBody.Position.Y,
                z = actor.Player.ComponentBody.Position.Z }
        })
    });
}
