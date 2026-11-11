#nullable enable

using Game;
using Game.Westworld;
using GameEntitySystem;

internal sealed class PlaySurvivalTrial
{
    private const int RequiredDays = 10;
    private const int CheckpointDays = 2;
    private readonly SubsystemTimeOfDay calendar;
    private readonly double startDay;
    private readonly double startTime;
    private readonly Actor[] actors;
    private readonly Action<object> log;
    private double nextSample;
    private bool checkpointLogged;

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
        public int PreparedFood { get; set; }
        public int PreparedFoodMeals { get; private set; }
        public Dictionary<int, int> PickedByValue { get; } = new();
        public Dictionary<int, int> PreparedByValue { get; } = new();
        public List<(int Value, float FoodAfter, bool FromPickup, bool FromPreparation)> NewMeals { get; } = new();
        public long Meals => Player.PlayerStats.FoodItemsEaten - InitialMeals;
        public long Sleeps => Player.PlayerStats.TimesWentToSleep - InitialSleeps;
        public double? DiedAt { get; set; }
        public bool WasSick { get; set; } = player.ComponentSickness.IsSick;
        public bool HadFlu { get; set; } = player.ComponentFlu.HasFlu;

        public void OnMeal(int value)
        {
            int key = FoodKey(value);
            int remaining = PickedByValue.GetValueOrDefault(key);
            bool fromPickup = remaining > 0 && InventoryFoodCount(Player.ComponentMiner.Inventory, key) == remaining - 1;
            PickedByValue[key] = fromPickup ? remaining - 1 : 0;
            int prepared = PreparedByValue.GetValueOrDefault(key);
            bool fromPreparation = !fromPickup && prepared > 0
                && InventoryFoodCount(Player.ComponentMiner.Inventory, key) == prepared - 1;
            PreparedByValue[key] = fromPreparation ? prepared - 1 : 0;
            if (fromPickup)
            {
                PickedFoodMeals++;
            }
            if (fromPreparation) PreparedFoodMeals++;
            NewMeals.Add((value, Player.ComponentVitalStats.Food, fromPickup, fromPreparation));
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

    public void Sample(double gameTime, bool voluntaryRest)
    {
        foreach (var actor in actors)
        {
            if (actor.DiedAt.HasValue) continue;
            var player = actor.Player;
            actor.MinFood = Math.Min(actor.MinFood, player.ComponentVitalStats.Food);
            actor.MinHealth = Math.Min(actor.MinHealth, player.ComponentHealth.Health);
            bool sick = player.ComponentSickness.IsSick;
            bool flu = player.ComponentFlu.HasFlu;
            if (sick != actor.WasSick || flu != actor.HadFlu)
            {
                actor.WasSick = sick;
                actor.HadFlu = flu;
                log(new { kind = "play_survival_illness", actor = player.PlayerData.Name, sick, flu, gameTime,
                    health = player.ComponentHealth.Health, food = player.ComponentVitalStats.Food });
            }
            foreach (var meal in actor.NewMeals)
                log(new { kind = "play_survival_meal", actor = player.PlayerData.Name, meal.Value, meal.FoodAfter,
                    meal.FromPickup, meal.FromPreparation, actor.Meals, gameTime });
            actor.NewMeals.Clear();
            if (player.ComponentHealth.Health <= 0f)
            {
                actor.DiedAt = gameTime;
                log(new { kind = "play_survival_death", actor = player.PlayerData.Name,
                    cause = player.ComponentHealth.CauseOfDeath, gameTime,
                    elapsedGameSeconds = gameTime - startTime, days = calendar.Day - startDay,
                    nativeDeathTime = player.ComponentHealth.DeathTime, food = player.ComponentVitalStats.Food,
                    sleep = player.ComponentVitalStats.Sleep, temperature = player.ComponentVitalStats.Temperature,
                    wetness = player.ComponentVitalStats.Wetness, actor.Meals, actor.PickedFoodMeals, actor.PreparedFoodMeals, actor.Sleeps });
            }
        }
        double days = calendar.Day - startDay;
        if (!checkpointLogged && days >= CheckpointDays)
        {
            checkpointLogged = true;
            LogState("play_survival_checkpoint", gameTime);
        }
        if (!Passed && days >= RequiredDays && DeathCount == 0 && voluntaryRest
            && actors.All(actor => actor.PickedFoodMeals + actor.PreparedFoodMeals >= 2 && actor.Sleeps > 0))
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
        bool prepared = fact.Method == "furnace_output";
        if (prepared) actor.PreparedFood += added; else actor.PickedFood += added;
        int key = FoodKey(fact.Value);
        int allFreshness = InventoryFoodCount(actor.Player.ComponentMiner.Inventory, key);
        // 新鲜和存放过的同类食物合计对账。存在其他未归因来源时不猜测吃掉的是哪份。
        var ledger = prepared ? actor.PreparedByValue : actor.PickedByValue;
        ledger[key] = allFreshness >= after && allFreshness - added == ledger.GetValueOrDefault(key)
            ? allFreshness : 0;
    }

    private static int FoodKey(int value)
    {
        var block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
        // 原生存放改变食物的损坏位。保留食物种类、蛋类型和烹饪状态。
        return block.GetRotPeriod(value) > 0 ? block.SetDamage(value, 0) : value;
    }

    private static int InventoryFoodCount(IInventory inventory, int key)
    {
        int count = 0;
        for (int slot = 0; slot < inventory.SlotsCount; slot++)
        {
            int held = inventory.GetSlotCount(slot);
            int value = inventory.GetSlotValue(slot);
            if (held > 0 && Terrain.ExtractContents(value) == Terrain.ExtractContents(key) && FoodKey(value) == key)
                count += held;
        }
        return count;
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
            wetness = actor.Player.ComponentVitalStats.Wetness, sick = actor.WasSick, flu = actor.HadFlu,
            actor.Meals, actor.PickedFood, actor.PickedFoodMeals, actor.PreparedFood, actor.PreparedFoodMeals, actor.Sleeps,
            position = new { x = actor.Player.ComponentBody.Position.X, y = actor.Player.ComponentBody.Position.Y,
                z = actor.Player.ComponentBody.Position.Z }
        })
    });
}
