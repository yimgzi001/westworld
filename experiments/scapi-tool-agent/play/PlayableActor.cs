#nullable enable

using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Capability39;
using Capability39.SocialTasks;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Decisions.Local;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

internal sealed class PlayableActor
{
    private static readonly string[] Actions =
        ["act_MoveTo", "act_FaceTowards", "act_Dig", "act_SelectItem", "act_PickUp", "act_Eat", "act_Dismount", "act_Wait"];
    private const string SmokeSpeech = "一起找食物";
    private static readonly JsonSerializerOptions SurvivalContextJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ComponentPlayer player;
    private readonly NativeToolSession tools;
    private readonly AtomicActions owner;
    private readonly Guid worldId;
    private readonly Action<object> log;
    private readonly bool survival;
    private readonly bool allowSocial;
    private readonly float panoramaRadius;
    private readonly Dictionary<int, JsonElement> items = new();
    private readonly PlaySocialInbox inbox = new();
    private readonly Queue<ToolDecisionStep> planned = new();
    private readonly PlaySurvivalMemory survivalMemory = new();
    private readonly Dictionary<int, Point3> placedStations = new();
    private (Guid Id, string Tool, Vector3 Origin)? active;
    private Harvest? harvest;
    private PlaySurvivalTravel? travel;
    private (PlaySurvivalFood.Food Target, int Before)? foodApproach;
    private readonly List<(PlaySurvivalFood.Food Target, Vector3 Position, double RetryAfter)> blockedFood = new();
    private PlaySurvivalPanorama.Sweep? surroundings;
    private PlayEarthShelter? shelter;
    private PlaySurvivalProduction? production;
    private PlayEarthShelter.Site? earthSite;
    private PlayEarthShelter.Site? lastSleepSite;
    private PlaySurvivalRest? drying;
    private NativeActorSession? travelSenses;
    private LocalObservationMemory? travelMemory;
    private PlaySurvivalSenses.Threat[] predators = [];
    private string[] reportedPredators = [];
    private double nextDecision, nextPoll, nextDangerScan, nextEvade, nextShelter, nextExit, nextProduction;
    private bool retired, restedInEarth, shallowExit;
    private int requests;
    private string? goal;
    private object? outcome;

    private sealed class Harvest(PlaySurvivalFood.Food food, int stockBefore)
    {
        public PlaySurvivalFood.Food Food { get; } = food;
        public int StockBefore { get; } = stockBefore;
        public double CollectUntil { get; set; }
        public bool Repositioning { get; set; }
        public bool Repositioned { get; set; }
        public bool Moved { get; set; }
        public HashSet<string> PickupAttempts { get; } = new();
    }

    public PlayableActor(ComponentPlayer player, Guid worldId, Action<object> log, bool survival, float panoramaRadius, bool allowSocial = true)
    {
        this.player = player;
        this.worldId = worldId;
        this.log = log;
        this.survival = survival;
        this.allowSocial = allowSocial;
        this.panoramaRadius = panoramaRadius;
        owner = new AtomicActions(player.Entity);
        tools = new NativeToolSession(Guid.NewGuid(), player, new ActorTools(owner, true));
    }

    public string Name => player.PlayerData.Name;
    public Guid Id => tools.ActorId;
    public Guid Session => tools.SessionId;
    public int Decisions { get; private set; }
    public int Moves { get; private set; }
    public int Timeouts { get; private set; }
    public int VoluntarySleeps { get; private set; }
    public bool HeardSmokeSpeech { get; private set; }
    public bool SmokeReady => Decisions >= 2 && Moves > 0;
    public bool IsActing => NativeBusy || planned.Count > 0;
    private bool NativeBusy => active.HasValue || owner.IsActionRunning || harvest is not null || travel is not null
        || shelter is not null || drying is not null || production is not null;
    public bool Retired => retired;
    public bool Died => player.ComponentHealth.Health <= 0f;

    public void BindTravel(NativeActorSession senses)
    {
        travelSenses = senses;
        travelMemory = new(senses.SessionId, Id);
    }

    public void Update(PlayDecisionQueue queue, double gameTime)
    {
        if (!retired && (Died || !player.Entity.IsAddedToProject))
            Retire(queue, Died ? "actor_died" : "actor_unavailable");

        var reply = queue.Take(Id);
        if (retired) return;

        if (survival && gameTime >= nextDangerScan)
        {
            nextDangerScan = gameTime + 0.55;
            var body = player.ComponentBody;
            survivalMemory.VisitPosition(body.Position, PlaySurvivalSenses.OnDryGround(body), gameTime);
            if (CheckDanger(queue, gameTime)) reply = null;
            if (retired) return;
        }
        if (shelter is { } work)
        {
            work.Update(gameTime);
            if (work.Status is { } result)
            {
                shelter = null;
                if (earthSite?.Contains(player.ComponentBody) != true)
                {
                    earthSite = null;
                    restedInEarth = false;
                }
                if (result != "succeeded" && earthSite is null) nextShelter = gameTime + 30;
                if (work.Leaving && result != "succeeded") nextExit = gameTime + (work.Reason == "predator_at_exit_resealed" ? 24 : 8);
                Report(work.Leaving ? PlaySurvivalIntents.LeaveShelter : PlaySurvivalIntents.BuildShelter,
                    result, work.Reason, gameTime);
                FinishStep(result == "succeeded", work.Reason, gameTime);
            }
            return;
        }
        if (drying is { } wait)
        {
            var state = wait.Update(gameTime);
            if (state == PlaySurvivalRest.State.Drying) return;
            drying = null;
            if (state == PlaySurvivalRest.State.Ready)
            {
                log(new { kind = "play_survival_dried", actor = Name, wetness = player.ComponentVitalStats.Wetness, gameTime });
                Dispatch("act_Sleep", JsonSerializer.SerializeToElement(new { }), queue, gameTime);
                return;
            }
            string reason = state == PlaySurvivalRest.State.TimedOut ? "shelter_drying_timed_out" : "shelter_cover_lost";
            Report(PlaySurvivalIntents.Rest, "failed", reason, gameTime);
            FinishStep(false, reason, gameTime);
            return;
        }
        if (production is { } supply)
        {
            supply.Update(gameTime);
            if (tools.RequiresReconciliation.HasValue)
            {
                Retire(queue, "native_production_outcome_unknown");
                return;
            }
            if (supply.Status is { } result)
            {
                production = null;
                if (result != "succeeded") nextProduction = gameTime + 20;
                Report(PlaySurvivalProduction.Tool, result, supply.Reason, gameTime, supply.Added);
                FinishStep(result == "succeeded", supply.Reason, gameTime);
            }
            return;
        }
        if (travel is { } route)
        {
            if (foodApproach is { } target && FoodStock(target.Target.Value) > target.Before)
            {
                route.Stop();
                travel = null;
                foodApproach = null;
                Report(PlaySurvivalIntents.Gather, "succeeded", "edible_stock_increased", gameTime,
                    FoodStock(target.Target.Value) - target.Before);
                FinishStep(true, null, gameTime);
                return;
            }
            route.Update(gameTime);
            if (route.Status is { } terminal)
            {
                travel = null;
                if (route.Evading)
                {
                    var body = player.ComponentBody;
                    bool closeThreat = ThreatInRange(body, IsOnWater(body)) is { Distance: <= 8f, Predator: true };
                    bool moved = (body.Position - route.Origin).XZ.LengthSquared() >= 0.25f;
                    nextEvade = Math.Min(nextEvade, gameTime + (closeThreat && moved ? 0 : 1));
                }
                if (shallowExit)
                {
                    shallowExit = false;
                    var body = player.ComponentBody;
                    bool climbed = earthSite is { } pit && !pit.Contains(body) && body.Position.Y >= pit.Surface - 0.05f;
                    if (climbed)
                    {
                        earthSite = null;
                        restedInEarth = false;
                    }
                    else nextExit = gameTime + 8;
                    string reason = climbed ? "surface_reached_by_observed_step" : route.Reason ?? "shallow_exit_blocked";
                    Report(PlaySurvivalIntents.LeaveShelter, climbed ? "succeeded" : "failed", reason, gameTime);
                    FinishStep(climbed, reason, gameTime);
                    return;
                }
                if (harvest is { Repositioning: true } crop)
                {
                    crop.Repositioning = false;
                    var (_, observed) = ObserveSurvival();
                    var nearby = observed.FirstOrDefault(item => item.InReach && item.Matches(crop.Food));
                    if (nearby is not null && (player.ComponentBody.Position - route.Origin).XZ.LengthSquared() >= 0.16f)
                        DigFood(nearby, queue, gameTime);
                    else EndHarvest("failed", route.Reason ?? "harvest_position_unavailable", 0, gameTime);
                    return;
                }
                if (harvest is { CollectUntil: > 0 } collecting)
                {
                    if (terminal == "failed" && FoodStock(collecting.Food.Value) <= collecting.StockBefore)
                        EndHarvest("failed", route.Reason ?? "food_drop_not_reached", 0, gameTime);
                    else CollectHarvest(collecting, queue, gameTime);
                    return;
                }
                if (foodApproach is { } approach)
                {
                    foodApproach = null;
                    FinishFoodApproach(approach.Target, route.Reason, queue, gameTime);
                    return;
                }
                var displacement = (player.ComponentBody.Position - route.Origin).XZ;
                if (displacement.Length() > 0.15f && terminal == "succeeded") Moves++;
                Report(PlayNavigationMenu.ScoutTool, terminal, route.Reason, gameTime, route.Legs, displacement);
                if (route.Reason == "no_observed_dry_route" && displacement.LengthSquared() < 0.04f)
                    nextDecision = gameTime + 8;
                FinishStep(terminal == "succeeded", route.Reason, gameTime);
            }
            return;
        }

        if (active is { } run && gameTime >= nextPoll)
        {
            nextPoll = gameTime + 0.15;
            var status = tools.Call("action_status", new { });
            if (RunId(status) != run.Id)
            {
                Retire(queue, "native_run_mismatch");
                return;
            }
            if (Status(status) != "running")
            {
                active = null;
                Complete(run.Tool, run.Origin, status, gameTime);
            }
        }

        if (harvest is { CollectUntil: > 0 } runHarvest && !active.HasValue && !owner.IsActionRunning
            && gameTime >= nextDecision)
            CollectHarvest(runHarvest, queue, gameTime);

        if (reply is not null)
        {
            if (reply.Plan is not { } plan)
            {
                if (reply.Error == "model_timeout") Timeouts++;
                Report(null, reply.Error ?? "model_failed", null, gameTime);
                return;
            }
            Decisions++;
            goal = plan.Goal;
            log(new { kind = "play_decision", actor = Name, actorId = Id, goal, steps = plan.Steps.Select(step => step.Tool), gameTime });
            if (plan.Steps.Count == 0)
            {
                Report(null, "model_chose_wait", null, gameTime);
                if (survival) nextDecision = gameTime + 4;
            }
            foreach (var step in plan.Steps) planned.Enqueue(step);
        }
        if (!NativeBusy && (!survival || !player.ComponentSleep.IsSleeping) && gameTime >= nextDecision
            && planned.TryDequeue(out var next))
            DispatchStep(next, queue, gameTime);
    }

    private void DispatchStep(ToolDecisionStep step, PlayDecisionQueue queue, double gameTime)
    {
        if (survival && step.Tool == PlayNavigationMenu.ScoutTool && Text(step.Arguments, "seek") == "dry_land"
            && PlaySurvivalSenses.OnDryGround(player.ComponentBody))
        {
            Report(step.Tool, "succeeded", "dry_ground_already_reached", gameTime);
            FinishStep(true, null, gameTime);
            return;
        }
        PlaySurvivalFood.Food? foodToHarvest = null;
        PlaySurvivalFood.Food[]? foods;
        try
        {
            // 原引用和参数必须在当前角色的新快照里仍然合法。
            var current = Capture();
            current.Prompt.ParseResponse(JsonSerializer.Serialize(new
            {
                goal,
                steps = new[] { new { tool = step.Tool, arguments = step.Arguments } }
            }));
            foods = current.Foods;
            if (step.Tool == PlaySurvivalFood.HarvestTool)
            {
                string target = step.Arguments.GetProperty("target").GetString()!;
                foodToHarvest = current.Foods!.Single(food => food.Reference == target);
            }
        }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException or ArgumentException)
        {
            Report(step.Tool, "action_no_longer_available", null, gameTime);
            FinishStep(false, "action_no_longer_available", gameTime);
            return;
        }

        log(new { kind = "play_plan_step", actor = Name, tool = step.Tool, remaining = planned.Count, gameTime });
        if (step.Tool == PlaySurvivalProduction.Tool)
        {
            production = new(player, owner, tools, step.Arguments.GetProperty("task").GetString()!, gameTime, log,
                placedStations, ReadDryWindow);
            return;
        }
        if (step.Tool is PlaySurvivalIntents.Gather or PlaySurvivalIntents.Eat or PlaySurvivalIntents.Rest
            or PlaySurvivalIntents.BuildShelter or PlaySurvivalIntents.LeaveShelter)
        {
            DispatchIntent(step.Tool, foods!, queue, gameTime);
            return;
        }
        if (step.Tool == PlayNavigationMenu.ScoutTool)
        {
            float bearing = step.Arguments.GetProperty("bearing").GetSingle();
            float distance = step.Arguments.GetProperty("distance").GetSingle();
            string seek = step.Arguments.GetProperty("seek").GetString()!;
            var body = player.ComponentBody;
            Vector3? earth = null;
            if (seek == "camp" && ReusableSite(body) is { } site)
            {
                earth = site.Center(site.Surface);
                var offset = (earth.Value - body.Position).XZ;
                distance = Math.Max(2f, offset.Length());
                bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset));
            }
            var direction = PlaySurvivalTravel.Rotate(body.Matrix.Forward.XZ, bearing);
            var sweep = surroundings is { } current && gameTime - current.CompletedAt < 30 ? current : null;
            var targetFood = seek == "food" ? sweep?.FoodInDirection(player, direction, distance) : null;
            if (targetFood is not null && (!CanRetryFood(targetFood, body.Position, gameTime)
                || !PermitSurvivalLeg(targetFood.Point, gameTime))) targetFood = null;
            if (seek == "shelter") earth = sweep?.EarthInDirection(body.Position, direction);
            var shore = seek == "dry_land" ? sweep?.ShoreInDirection(body.Position, direction) : null;
            travel = new(player, owner, bearing, distance, seek, false, gameTime, ObserveSurvival, ReadDryWindow, log,
                survivalMemory.VisitsTo, foodTarget: targetFood, shelterTarget: earth, shoreTarget: shore,
                permitLandLeg: PermitSurvivalLeg);
        }
        else if (foodToHarvest is { } food)
        {
            harvest = new(food, FoodStock(food.Value));
            DigFood(food, queue, gameTime);
        }
        else Dispatch(step.Tool, step.Arguments, queue, gameTime);
    }

    private void DispatchIntent(string tool, PlaySurvivalFood.Food[] foods,
        PlayDecisionQueue queue, double gameTime)
    {
        var body = player.ComponentBody;
        bool dry = PlaySurvivalSenses.OnDryGround(body);
        if (tool == PlaySurvivalIntents.BuildShelter && (RepairableShelter() ?? PlayEarthShelter.At(body)) is { } ground)
        {
            earthSite = ground;
            restedInEarth = false;
            shelter = new(player, owner, ground, false, gameTime, log);
            return;
        }
        if (tool == PlaySurvivalIntents.LeaveShelter && earthSite is { } pit && pit.Contains(body))
        {
            if (dry && body.Position.Y >= pit.Surface - 1.05f && PlayDryRoutes.ShallowExit(ReadDryWindow(), pit.Surface) is { } exit)
            {
                var offset = (exit - body.Position).XZ;
                float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset));
                shallowExit = true;
                travel = new(player, owner, bearing, offset.Length(), "survey", false, gameTime, ObserveSurvival, ReadDryWindow, log);
                return;
            }
            shelter = new(player, owner, pit, true, gameTime, log);
            return;
        }
        if (tool == PlaySurvivalIntents.Rest && dry && RestWindow() && !player.ComponentSleep.IsSleeping
            && PlaySurvivalRest.HasSecureCover(player) && !predators.Any(animal => !animal.Aquatic && animal.Distance < 12f))
        {
            if (player.ComponentSleep.CanSleep(out _))
            {
                Dispatch("act_Sleep", JsonSerializer.SerializeToElement(new { }), queue, gameTime);
                return;
            }
            if (PlaySurvivalRest.WetnessBlocksSleep(player))
            {
                drying = new(player, gameTime);
                log(new { kind = "play_survival_drying", actor = Name, wetness = player.ComponentVitalStats.Wetness, gameTime });
                return;
            }
        }
        if (tool == PlaySurvivalIntents.Eat && TryEatOwned(queue, gameTime, safeOnly: false)) return;
        if (tool == PlaySurvivalIntents.Gather && dry)
        {
            var food = NearbyFood(foods, body.Position, gameTime)
                .OrderBy(item => PlaySurvivalFood.IsRawMeat(item.Value)).ThenBy(item => item.SicknessRisk)
                .ThenByDescending(item => item.InReach).ThenBy(item => (item.Point - body.Position).XZ.LengthSquared()).FirstOrDefault();
            if (food is not null)
            {
                if (food.InReach) CollectFood(food, queue, gameTime);
                else
                {
                    var offset = (food.Point - body.Position).XZ;
                    if (offset.LengthSquared() < 0.04f)
                    {
                        FinishFoodApproach(food, "food_outside_pickup_height", queue, gameTime);
                        return;
                    }
                    float bearing = Vector2.Angle(body.Matrix.Forward.XZ, offset) * (180f / MathF.PI);
                    foodApproach = (food, FoodStock(food.Value));
                    travel = new(player, owner, bearing, Math.Clamp(offset.Length() + 1f, 2f, 9f), "food", false, gameTime,
                        ObserveSurvival, ReadDryWindow, log, foodTarget: food, permitLandLeg: PermitSurvivalLeg);
                    log(new { kind = "play_food_approach", actor = Name, food.Name, distance = offset.Length(), gameTime });
                }
                return;
            }
        }
        Report(tool, "failed", "intent_conditions_not_met", gameTime);
        FinishStep(false, "intent_conditions_not_met", gameTime);
    }

    private bool TryEatOwned(PlayDecisionQueue queue, double gameTime, bool safeOnly, bool automatic = false)
    {
        var inventory = player.ComponentMiner.Inventory;
        var meal = Enumerable.Range(0, inventory.SlotsCount).Where(slot => inventory.GetSlotCount(slot) > 0)
            .Select(slot => (Slot: slot, Value: inventory.GetSlotValue(slot)))
            .Where(item => PlaySurvivalFood.CanEat(player, item.Value)).Select(item =>
            {
                var block = BlocksManager.Blocks[Terrain.ExtractContents(item.Value)];
                return (item.Slot, item.Value, Risk: block.GetSicknessProbability(item.Value), Nutrition: block.GetNutritionalValue(item.Value));
            }).Where(item => !safeOnly || item.Risk <= 0f)
            .OrderBy(item => item.Risk).ThenByDescending(item => item.Nutrition).FirstOrDefault();
        if (meal.Nutrition <= 0f) return false;
        if (automatic) log(new { kind = "play_survival_emergency_meal", actor = Name, value = meal.Value,
            meal.Nutrition, meal.Risk, gameTime });
        Dispatch("act_Eat", JsonSerializer.SerializeToElement(new { slot = meal.Slot, value = meal.Value, count = 1 }), queue, gameTime);
        return true;
    }

    private IEnumerable<PlaySurvivalFood.Food> NearbyFood(IEnumerable<PlaySurvivalFood.Food> foods, Vector3 origin, double gameTime)
    {
        var pastFood = surroundings is { } sweep && gameTime - sweep.CompletedAt <= 24
            ? sweep.Sectors.SelectMany(sector => sector.Food).Select(item => item with { InReach = false })
            : Enumerable.Empty<PlaySurvivalFood.Food>();
        return foods.Concat(pastFood).Where(item => PlaySurvivalFood.CanApproachOnLand(item, origin)
            && (PlaySurvivalFood.CanSeek(player, item) || PlaySurvivalFood.IsRawMeat(item.Value))
            && CanRetryFood(item, origin, gameTime) && survivalMemory.AvoidsPredatorAreas(origin, item.Point, gameTime));
    }

    private bool CanRetryFood(PlaySurvivalFood.Food food, Vector3 position, double gameTime) =>
        !blockedFood.Any(blocked => gameTime < blocked.RetryAfter && food.Matches(blocked.Target)
            && (position - blocked.Position).LengthSquared() < 1f);

    private void DelayFood(PlaySurvivalFood.Food food, double gameTime)
    {
        blockedFood.RemoveAll(item => item.RetryAfter <= gameTime);
        if (blockedFood.Count >= 8) blockedFood.RemoveAt(0);
        blockedFood.Add((food, player.ComponentBody.Position, gameTime + 24));
    }

    private void FinishFoodApproach(PlaySurvivalFood.Food target, string? reason, PlayDecisionQueue queue, double gameTime)
    {
        var (_, foods) = ObserveSurvival();
        var live = foods.FirstOrDefault(food => food.InReach && food.Matches(target));
        if (live is not null)
        {
            CollectFood(live, queue, gameTime);
            return;
        }
        DelayFood(target, gameTime);
        Report(PlaySurvivalIntents.Gather, "failed", reason ?? "food_out_of_reach", gameTime);
        FinishStep(false, reason, gameTime);
    }

    private void CollectFood(PlaySurvivalFood.Food food, PlayDecisionQueue queue, double gameTime)
    {
        if (food.Source == "pickup")
        {
            Dispatch("act_PickUp", JsonSerializer.SerializeToElement(new { target = food.Reference, count = 1 }), queue, gameTime);
            return;
        }
        harvest = new(food, FoodStock(food.Value));
        DigFood(food, queue, gameTime);
    }

    private void DigFood(PlaySurvivalFood.Food food, PlayDecisionQueue queue, double gameTime)
    {
        float seconds = player.ComponentMiner.CalculateDigTime(food.Value, player.ComponentMiner.ActiveBlockValue);
        var timeout = float.IsFinite(seconds) ? Math.Clamp(seconds + 2f, 5f, 25f) : 5f;
        Dispatch("act_Dig", JsonSerializer.SerializeToElement(new { target = food.Reference, timeout }), queue, gameTime);
    }

    private bool TryRepositionForHarvest(Harvest attempt, double gameTime)
    {
        var body = player.ComponentBody;
        if (attempt.Repositioned || !PlaySurvivalSenses.OnDryGround(body)) return false;
        var offset = (attempt.Food.Point - body.Position).XZ;
        float distance = offset.Length();
        if (distance < 1.15f || distance > 4f) return false;
        attempt.Repositioning = true;
        attempt.Repositioned = true;
        float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset));
        travel = new(player, owner, bearing, Math.Max(1f, distance - 0.85f), "survey", false, gameTime,
            ObserveSurvival, ReadDryWindow, log, permitLandLeg: PermitSurvivalLeg);
        log(new { kind = "play_harvest_reposition", actor = Name, attempt.Food.Name, distance, gameTime });
        return true;
    }

    private void Dispatch(string tool, JsonElement arguments, PlayDecisionQueue queue, double gameTime)
    {
        if (owner.IsActionRunning)
        {
            Retire(queue, "native_owner_busy");
            return;
        }

        var origin = player.ComponentBody.Position;
        var receipt = tools.Call(tool, arguments);
        if (tools.RequiresReconciliation.HasValue)
        {
            Retire(queue, "native_outcome_unknown");
            return;
        }
        if (tool == "act_RespondToOffer" && (Status(receipt) == "succeeded" || Failure(receipt) == "targetChanged"))
        {
            inbox.RetireOffer(arguments.GetProperty("offer").GetString()!);
        }
        if (Status(receipt) == "running")
        {
            if (RunId(receipt) is { } id)
            {
                active = (id, tool, origin);
                nextPoll = gameTime;
                log(new { kind = "play_action_started", actor = Name, actorId = Id, tool, runId = id, gameTime });
            }
            else Retire(queue, "native_run_missing");
        }
        else Complete(tool, origin, receipt, gameTime);
    }

    public bool Request(PlayDecisionQueue queue, StructuredFileController? bridge, double gameTime,
        bool delayFirst, bool smokeSpeaker)
    {
        if (retired || IsActing || queue.IsPending(Id) || gameTime < nextDecision
            || (survival && (player.ComponentSleep.IsSleeping
                || player.Entity.FindComponent<ComponentRider>()?.m_isAnimating == true))
            || (bridge is null && SmokeReady)) return false;
        // 已确认的危险由游戏线程处置。避险重试间隔内发出的请求很快会被下一次避险取消。
        if (survival && ThreatInRange(player.ComponentBody, IsOnWater(player.ComponentBody)) is not null) return false;
        float foodLevel = player.ComponentVitalStats.Food;
        if (survival && foodLevel <= 0.24f && PlaySurvivalSenses.OnDryGround(player.ComponentBody)
            && TryEatOwned(queue, gameTime, safeOnly: foodLevel > 0.09f, automatic: true)) return false;
        if (gameTime < nextExit && earthSite?.Contains(player.ComponentBody) == true
            && player.ComponentVitalStats.Food >= 0.18f && PlaySurvivalRest.HasSecureCover(player)) return false;

        (Guid Observation, ToolDecisionPrompt Prompt, JsonElement Context, IReadOnlyList<ToolOffer> Offers,
            PlaySurvivalFood.Food[]? Foods) snapshot;
        try
        {
            if (survival)
            {
                var started = Stopwatch.GetTimestamp();
                surroundings = PlaySurvivalPanorama.Capture(player, owner.Queries, panoramaRadius, gameTime);
                if (surroundings is { } sweep)
                    log(new { kind = "play_survival_panorama", actor = Name, gameTime, sectors = sweep.Sectors.Length,
                        sweep.Panorama.Radius, blocks = sweep.Panorama.Blocks.Count,
                        animals = sweep.Panorama.Objects.Entities.Count, items = sweep.Panorama.Objects.Pickables.Count,
                        durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds });
            }
            snapshot = Capture(includePanorama: true);
        }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException or ArgumentException)
        {
            Report(null, "observation_unavailable", error.Message, gameTime);
            return false;
        }
        var session = Session;
        var actor = Id;
        var requestId = Guid.NewGuid();
        var window = TimeSpan.FromSeconds(bridge is null ? 7 : 45);
        int round = requests + 1;
        if (!queue.TryStart(session, actor, snapshot.Observation, window, token => bridge is null
            ? SmokeDecision(snapshot.Prompt, snapshot.Context, delayFirst && round == 1, smokeSpeaker && round == 2, round, token)
            : bridge.DecideAsync(snapshot.Prompt, session, actor, snapshot.Observation, snapshot.Context,
                snapshot.Offers, gameTime, token, requestId, remainingBudget: window))) return false;

        requests = round;
        log(new { kind = "play_request", actor = Name, actorId = Id, round, gameTime, requestId,
            observationId = snapshot.Observation, promptBytes = Encoding.UTF8.GetByteCount(snapshot.Prompt.Prompt) });
        return true;
    }

    private (JsonElement Observation, JsonElement Inventory) Observe(int blockLimit)
    {
        var read = tools.Call("observe", new
        {
            look_distance = survival ? 12 : 8, object_distance = survival ? 16 : 8,
            limit = survival ? 8 : 4, block_limit = blockLimit
        });
        if (read.State != "returned") throw new InvalidOperationException("角色观察不可用。");
        return ToolResultProjection.SplitObservation(read.Data);
    }

    private (JsonElement Observation, PlaySurvivalFood.Food[] Foods) ObserveSurvival()
    {
        var (observation, _) = Observe(128);
        var (_, food) = PlaySurvivalFood.Project(observation);
        double gameTime = observation.GetProperty("gameTime").GetDouble();
        survivalMemory.SeeFood(food, gameTime);
        return (observation, food.Where(item => CanRetryFood(item, player.ComponentBody.Position, gameTime)).ToArray());
    }

    private LocalObservationWindow ReadDryWindow() =>
        travelMemory!.Observe(travelSenses!.Observe(travelSenses.SessionId, Id, allDirections: true));

    private bool PermitSurvivalLeg(Vector3 end, double gameTime) =>
        survivalMemory.AvoidsPredatorAreas(player.ComponentBody.Position, end, gameTime);

    private bool RestWindow()
    {
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        float start = earthSite?.Contains(player.ComponentBody) == true ? calendar.DuskStart - 0.1f : calendar.DuskStart;
        return calendar.TimeOfDay >= start || calendar.TimeOfDay < calendar.DayStart
            || player.ComponentVitalStats.Sleep <= 0.25f || PlaySurvivalRest.NeedsRecovery(player);
    }

    private PlayEarthShelter.Site? RepairableShelter()
    {
        var body = player.ComponentBody;
        return earthSite is { } site && site.Contains(body) && body.Position.Y <= site.Surface - 2.95f
            && PlaySurvivalSenses.OnDryGround(body) ? site : null;
    }

    private PlayEarthShelter.Site? ReusableSite(ComponentBody body)
    {
        if (lastSleepSite is not { } site || MathF.Abs(body.Position.Y - site.Surface) > 2f) return null;
        float squaredDistance = (site.Center(site.Surface) - body.Position).XZ.LengthSquared();
        return squaredDistance is >= 1.44f and <= 324f ? site : null;
    }

    private (Guid Observation, ToolDecisionPrompt Prompt, JsonElement Context, IReadOnlyList<ToolOffer> Offers,
        PlaySurvivalFood.Food[]? Foods) Capture(bool includePanorama = false)
    {
        var (observation, inventory) = Observe(survival ? 128 : 8);
        var body = player.ComponentBody;
        var mountBody = survival ? player.Entity.FindComponent<ComponentRider>()?.Mount?.ComponentBody : null;
        bool mounted = mountBody is not null;
        var deck = survival ? PlaySurvivalSenses.BoatDeck(body) : null;
        bool onDryGround = survival && PlaySurvivalSenses.OnDryGround(body);
        bool exposedToWater = survival && !onDryGround;
        var shore = exposedToWater ? PlaySurvivalSenses.VisibleShore(observation, body, mountBody ?? deck) : null;
        bool canDismount = !mounted || shore is { Distance: <= 2.5f }
            || mountBody!.StandingOnValue.HasValue || mountBody.ImmersionFactor > 0.92f;
        PlaySurvivalFood.Food[]? visibleFood = null;
        if (survival) (observation, visibleFood) = PlaySurvivalFood.Project(observation);
        double gameTime = observation.GetProperty("gameTime").GetDouble();
        if (visibleFood is not null) survivalMemory.SeeFood(visibleFood, gameTime);
        var slots = inventory.GetProperty("slots").EnumerateArray()
            .Where(slot => slot.GetProperty("count").GetInt32() > 0)
            .Select(slot => new
            {
                slot = slot.GetProperty("slot").GetInt32(),
                value = slot.GetProperty("value").GetInt32(),
                count = slot.GetProperty("count").GetInt32(),
                item = ReadItem(slot.GetProperty("value").GetInt32())
            }).ToArray();
        var visibleInventory = JsonSerializer.SerializeToElement(new { slots });
        var eatSlots = survival ? slots.Where(item => PlaySurvivalFood.CanEat(player, item.value)).ToArray() : slots;
        var eatInventory = survival ? JsonSerializer.SerializeToElement(new { slots = eatSlots }) : visibleInventory;
        var supported = tools.GetTools();
        string? sleepBlocker = null;
        bool nativeCanSleep = survival && !player.ComponentSleep.IsSleeping && player.ComponentSleep.CanSleep(out sleepBlocker);
        bool restWindow = survival && RestWindow();
        var calendar = survival ? player.Project.FindSubsystem<SubsystemTimeOfDay>(true) : null;
        bool nearNight = calendar is not null && (calendar.TimeOfDay >= calendar.DuskStart - 0.1f
            || calendar.TimeOfDay < calendar.DayStart);
        bool recovering = survival && PlaySurvivalRest.NeedsRecovery(player);
        float foodLevel = player.ComponentVitalStats.Food;
        float temperature = player.ComponentVitalStats.Temperature;
        bool severeCold = temperature < 3.5f && player.ComponentHealth.Health < 0.4f;
        bool preferShelter = onDryGround && (nearNight && foodLevel > 0.23f
            || (recovering || temperature < 5f) && foodLevel > 0.08f || severeCold);
        bool inEarthShelter = earthSite?.Contains(body) == true;
        var gatherCandidates = onDryGround && !inEarthShelter ? NearbyFood(visibleFood!, body.Position, gameTime).ToArray()
            : Array.Empty<PlaySurvivalFood.Food>();
        var buildSite = survival && !inEarthShelter ? PlayEarthShelter.At(body) : null;
        bool coverUsable = onDryGround && (inEarthShelter || preferShelter || restWindow) && PlaySurvivalRest.HasSecureCover(player);
        var reuse = preferShelter && !inEarthShelter && !coverUsable && gameTime >= nextShelter ? ReusableSite(body) : null;
        bool canSleep = nativeCanSleep && restWindow && coverUsable;
        bool needsDrying = coverUsable && PlaySurvivalRest.WetnessBlocksSleep(player);
        bool canLeaveShelter = inEarthShelter && gameTime >= nextExit
            && (!coverUsable || restedInEarth && !restWindow || foodLevel < (recovering || severeCold ? 0.06f : 0.18f));
        bool forageInWater = !exposedToWater || player.ComponentVitalStats.Food < 0.16f;
        var nativeOffers = NativeToolOffers.Build(supported.Where(tool =>
            (Actions.Contains(tool.Name) || (survival && (tool.Name == "act_Jump" || tool.Name == "act_Sleep" && canSleep)))
            && !(mounted && (tool.Name is "act_MoveTo" or "act_FaceTowards" or "act_Jump"))
            && (!survival || tool.Name is not ("act_MoveTo" or "act_FaceTowards" or "act_Dig"))
            && (!inEarthShelter || tool.Name is "act_Wait" or "act_Eat" or "act_Sleep")
            && (canDismount || tool.Name != "act_Dismount")
            && (forageInWater || tool.Name is not ("act_Dig" or "act_PickUp"))).ToArray(),
            observation, inventory, Array.Empty<JsonElement>(), visibleEatInventory: eatInventory,
            waitSeconds: survival ? 60 : 10, pickupInReachOnly: survival);
        Guid id = Guid.NewGuid();
        var messages = allowSocial ? CaptureInbox(gameTime) : null;
        var scope = new SocialCaptureId(worldId, Session, Id, id);
        var empty = JsonSerializer.SerializeToElement(new { });
        var reads = new List<CapabilityRead>
        {
            new(scope, "observe", empty, observation),
            new(scope, "read_inventory", empty, inventory)
        };
        if (messages is { } current) reads.Add(new(scope, "read_social", empty, current));
        var social = allowSocial
            ? CapabilityFacts.Build(scope, reads, supported.Select(tool => tool.Name).ToArray(), !owner.IsActionRunning).Social : null;
        var socialOffers = social is null ? Enumerable.Empty<ToolOffer>() : SocialTaskFamily.Build(scope, social).Entries
            .Where(entry => entry.Descriptor.Name.StartsWith("act_", StringComparison.Ordinal))
            .Select(entry => entry.Offer).OfType<ToolOffer>();
        var offers = nativeOffers.Select(PlayNavigationMenu.LimitRelativeMove).Concat(socialOffers).ToList();
        if (survival && !player.ComponentSleep.IsSleeping)
        {
            if (!inEarthShelter)
                offers.Add(exposedToWater ? PlayNavigationMenu.ShoreOffer
                    : reuse is not null ? PlayNavigationMenu.CampOffer
                    : preferShelter && !coverUsable && buildSite is null ? PlayNavigationMenu.ShelterOffer : PlayNavigationMenu.ScoutOffer);
            if (gatherCandidates.Length > 0) offers.Add(PlaySurvivalIntents.GatherOffer);
            if (eatSlots.Length > 0 || gatherCandidates.Any(food => PlaySurvivalFood.CanEat(player, food.Value)))
                offers.Add(PlaySurvivalIntents.EatOffer);
            if (preferShelter || restWindow) offers.Add(PlaySurvivalIntents.RestOffer);
            if (onDryGround && !inEarthShelter && gameTime >= nextProduction
                && PlaySurvivalProduction.Offer(player, surroundings is { } recent && gameTime - recent.CompletedAt <= 24
                    ? recent.Panorama : null, placedStations,
                    routineWork: !preferShelter && player.ComponentHealth.Health >= 0.6f && foodLevel > 0.28f) is { } supplyOffer)
                offers.Add(supplyOffer);
            if (canLeaveShelter) offers.Add(PlaySurvivalIntents.LeaveOffer);
            if ((inEarthShelter && !coverUsable && RepairableShelter() is not null)
                || (!inEarthShelter && preferShelter && !coverUsable && gameTime >= nextShelter))
                offers.Add(PlaySurvivalIntents.BuildOffer);
        }
        if (visibleFood is not null && forageInWater && PlaySurvivalFood.HarvestOffer(visibleFood.Where(food =>
                PlaySurvivalFood.CanSeek(player, food) && CanRetryFood(food, body.Position, gameTime))) is { } harvestOffer)
            offers.Add(harvestOffer);
        var eggReserve = survival ? PlaySurvivalFood.RawEggReserve(player) : default;
        var context = JsonSerializer.SerializeToElement(new
        {
            objective = survival
                ? "先保命。全向摘要的 bearing 相对朝向，height 相对脚下。探索优先用 walkableBearings 中可起步的方向，绕开刚看到猎食者的位置。水上先用 dry_land 上岸。优先成熟南瓜和熟食，储粮按营养看，小蛋的饱食收益较低。生蛋在饱食不高于 24% 才吃。可用木矛捕食用鸟，生肉留着用熔炉和燃料做熟。黄昏、降温或生病低血量时找封闭庇护，土坑能自行取得封顶材料，太湿先在顶下晾干。最多四步，每步核对实际条件。"
                : "照顾自己的生存。根据身体、背包和局部视野行动。侧后方先转身看清。移动受阻后，根据实际位移和新观察调整方向。需要合作时可简短交流或交接。只选一个现在可执行的动作。",
            rows = survival && includePanorama
                ? "地形点[方向,距离,高度差]。动物和物品在行首加名称，食物还在行尾带致病概率和营养。visited[x,z,访问次数,干地,距今秒]，pastFoodSightings[名称,x,y,z,距今秒]，pastPredators[名称,x,z,距今秒]。"
                : null,
            actor = Name,
            previousGoal = goal,
            previousOutcome = outcome,
            observation = survival ? ModelSurvivalObservation(observation) : (object)observation,
            inventory = visibleInventory,
            visibleFood = visibleFood?.Where(food => CanRetryFood(food, body.Position, gameTime)).ToArray(),
            visibleShore = shore,
            visiblePredators = survival ? predators : null,
            environment = survival ? owner.Queries.ReadEnvironment() : null,
            survivalPriority = survival ? (exposedToWater ? "dry_land" : preferShelter ? "shelter" : "food_and_shelter") : null,
            sickness = survival ? new { sick = player.ComponentSickness.IsSick, flu = player.ComponentFlu.HasFlu, recovering } : null,
            rawEggReserve = survival ? eggReserve.Count : (int?)null,
            rawEggNutrition = survival ? Math.Round(eggReserve.Nutrition, 2) : (double?)null,
            nearNight = survival ? nearNight : (bool?)null,
            restWindow = survival ? restWindow : (bool?)null,
            inEarthShelter = survival ? inEarthShelter : (bool?)null,
            canBuildHere = survival ? (inEarthShelter ? !coverUsable && RepairableShelter() is not null : buildSite is not null) : (bool?)null,
            canRestHere = survival ? canSleep : (bool?)null,
            needsDrying = survival ? needsDrying : (bool?)null,
            hasUsableCover = survival ? coverUsable : (bool?)null,
            onBoatDeck = survival ? deck is not null : (bool?)null,
            onDryGround = survival ? onDryGround : (bool?)null,
            localMemory = survival ? survivalMemory.Snapshot(gameTime) : null,
            rememberedCamp = reuse is not null ? CampSummary(reuse, body) : null,
            walkableBearings = includePanorama && onDryGround && !inEarthShelter
                ? PlayDryRoutes.WalkableBearings(ReadDryWindow(), body.Matrix.Forward.XZ) : null,
            surroundings = includePanorama && surroundings is { } surveyed
                ? surveyed.Snapshot(body.Position, body.Matrix.Forward.XZ, gameTime, onDryGround) : null,
            sleepBlocker = string.IsNullOrEmpty(sleepBlocker) ? null : sleepBlocker,
            receivedSocial = messages
        }, survival ? SurvivalContextJson : null);
        return (id, new ToolDecisionPrompt(Session, Id, id, context, offers, maxSteps: survival ? 4 : 1), context, offers, visibleFood);
    }

    private static object CampSummary(PlayEarthShelter.Site site, ComponentBody body)
    {
        var offset = (site.Center(site.Surface) - body.Position).XZ;
        return new { distance = Math.Round(offset.Length(), 1),
            bearing = Math.Round(MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset))) };
    }

    private static object ModelSurvivalObservation(JsonElement observation) => new
    {
        gameTime = Math.Round(observation.GetProperty("gameTime").GetDouble(), 1),
        self = observation.GetProperty("self"),
        // 可直接拾取的对象保留原生引用。地形和远处对象由同帧全向摘要提供。
        pickables = observation.GetProperty("pickables").EnumerateArray()
            .Where(item => item.TryGetProperty("inReach", out var reach) && reach.ValueKind == JsonValueKind.True).ToArray()
    };

    private JsonElement? CaptureInbox(double gameTime)
    {
        var read = tools.Call("read_social", new { });
        if (read.State != "returned" || read.Data.ValueKind != JsonValueKind.Array) return null;
        foreach (var message in read.Data.EnumerateArray())
        {
            // 原生读取会排空队列。保留本人收到的少量事件供决策和复核共用。
            inbox.Add(message);
            string? kind = Text(message, "kind");
            string? text = Text(message, "text");
            if (kind == "heardSpeech" && text == SmokeSpeech) HeardSmokeSpeech = true;
            log(new { kind = "play_social_received", actor = Name, actorId = Id, eventKind = kind, text,
                source = Text(message, "source"), itemValue = Number(message, "itemValue"), count = Number(message, "count"),
                receivedGameTime = message.GetProperty("gameTime").GetDouble(), gameTime });
        }
        return inbox.Snapshot(gameTime);
    }

    private JsonElement ReadItem(int value)
    {
        if (items.TryGetValue(value, out var facts)) return facts;
        var receipt = tools.Call("read_item", new { value });
        if (receipt.State != "returned") return JsonSerializer.SerializeToElement(new { });
        var data = receipt.Data;
        float? sicknessRisk = survival ? BlocksManager.Blocks[Terrain.ExtractContents(value)]?.GetSicknessProbability(value) : null;
        facts = JsonSerializer.SerializeToElement(new { name = data.GetProperty("name"), nutrition = data.GetProperty("nutrition"), sicknessRisk });
        items.Add(value, facts);
        return facts;
    }

    private static async Task<ToolDecisionPlan> SmokeDecision(ToolDecisionPrompt prompt, JsonElement context,
        bool delayed, bool speak, int round, CancellationToken token)
    {
        await Task.Delay(delayed ? Timeout.Infinite : 100, token);
        var previous = context.GetProperty("previousOutcome");
        bool turn = Text(previous, "tool") == "act_MoveTo" && Text(previous, "status") == "failed";
        string tool = speak ? "act_Speak" : turn ? "act_FaceTowards" : "act_MoveTo";
        object arguments = tool switch
        {
            "act_Speak" => new { text = SmokeSpeech, mode = "normal" },
            "act_FaceTowards" => new { bearing = 180, timeout = 4 },
            _ => new { bearing = round % 2 == 0 ? 30 : -30, distance = 1.5, range = 0.3, timeout = 4 }
        };
        return prompt.ParseResponse(JsonSerializer.Serialize(new
        {
            goal = speak ? "验证实际听见发言" : "验证原生移动",
            steps = new[] { new { tool, arguments } }
        }));
    }

    private void Complete(string tool, Vector3 origin, ToolReceipt receipt, double gameTime)
    {
        var status = Status(receipt) ?? receipt.State;
        Vector2? movement = tool == "act_MoveTo" ? (player.ComponentBody.Position - origin).XZ : null;
        if (status == "succeeded" && movement?.Length() > 0.15f) Moves++;
        if (tool == "act_Sleep" && status == "succeeded" && player.ComponentSleep.IsSleeping)
        {
            VoluntarySleeps++;
            restedInEarth = earthSite?.Contains(player.ComponentBody) == true;
            if (restedInEarth) lastSleepSite = earthSite;
            log(new { kind = "play_survival_voluntary_sleep", actor = Name, gameTime, VoluntarySleeps });
        }
        Report(tool, status, receipt.Error ?? Failure(receipt), gameTime, Number(receipt.Data, "affectedCount"), movement);
        if (harvest is { } attempt && tool == "act_Dig")
        {
            if (status != "succeeded")
            {
                string reason = receipt.Error ?? Failure(receipt) ?? "dig_failed";
                if (reason is not ("blocked" or "outOfReach") || !TryRepositionForHarvest(attempt, gameTime))
                    EndHarvest("failed", reason, 0, gameTime);
            }
            else
            {
                attempt.CollectUntil = gameTime + 6;
                nextDecision = gameTime + 0.15;
            }
        }
        else if (harvest is null) FinishStep(status == "succeeded", receipt.Error ?? Failure(receipt), gameTime);
    }

    private void CollectHarvest(Harvest attempt, PlayDecisionQueue queue, double gameTime)
    {
        int added = FoodStock(attempt.Food.Value) - attempt.StockBefore;
        if (added > 0)
        {
            EndHarvest("succeeded", "edible_stock_increased", added, gameTime);
            return;
        }
        if (gameTime >= attempt.CollectUntil || player.ComponentSleep.IsSleeping)
        {
            EndHarvest("failed", "food_drop_not_collected", 0, gameTime);
            return;
        }

        nextDecision = gameTime + 0.3;
        var (observation, _) = Observe(8);
        var origin = Position(attempt.Food.Position);
        var drop = PlaySurvivalFood.VisibleDrops(observation).Where(food =>
            Terrain.ExtractContents(food.Value) == Terrain.ExtractContents(attempt.Food.Value)
            && (Position(food.Position) - origin).LengthSquared() <= 4f).MinBy(food => food.Distance);
        if (drop is null) return;
        if (drop.InReach && attempt.PickupAttempts.Add(drop.Reference))
        {
            Dispatch("act_PickUp", JsonSerializer.SerializeToElement(new { target = drop.Reference, count = 1 }), queue, gameTime);
        }
        else if (!drop.InReach && !attempt.Moved && PlaySurvivalSenses.OnDryGround(player.ComponentBody))
        {
            var body = player.ComponentBody;
            var offset = (drop.Point - body.Position).XZ;
            if (offset.LengthSquared() < 0.04f || !PlaySurvivalFood.CanApproachOnLand(drop, body.Position)) return;
            attempt.Moved = true;
            attempt.CollectUntil = gameTime + 6;
            float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset));
            travel = new(player, owner, bearing, Math.Clamp(offset.Length() + 1f, 2f, 9f), "food", false, gameTime,
                ObserveSurvival, ReadDryWindow, log, foodTarget: drop, permitLandLeg: PermitSurvivalLeg);
        }
    }

    private int FoodStock(int value)
    {
        var inventory = player.ComponentMiner.Inventory;
        int contents = Terrain.ExtractContents(value);
        int count = 0;
        for (int slot = 0; slot < inventory.SlotsCount; slot++)
        {
            int held = inventory.GetSlotValue(slot);
            if (Terrain.ExtractContents(held) == contents
                && BlocksManager.Blocks[contents].GetNutritionalValue(held) > 0f)
                count += inventory.GetSlotCount(slot);
        }
        return count;
    }

    private void EndHarvest(string status, string reason, int count, double gameTime)
    {
        if (status != "succeeded" && harvest is { } failed) DelayFood(failed.Food, gameTime);
        harvest = null;
        Report(PlaySurvivalFood.HarvestTool, status, reason, gameTime, count);
        FinishStep(status == "succeeded", reason, gameTime);
    }

    private void FinishStep(bool succeeded, string? reason, double gameTime)
    {
        if (planned.Count == 0) return;
        if (succeeded)
        {
            nextDecision = gameTime + 0.12;
            return;
        }
        log(new { kind = "play_plan_replan", actor = Name, skipped = planned.Count, reason, gameTime });
        planned.Clear();
    }

    private bool CheckDanger(PlayDecisionQueue queue, double gameTime)
    {
        predators = PlaySurvivalSenses.Threats(player, owner.Queries);
        survivalMemory.SeePredators(predators, gameTime);
        var names = predators.Select(threat => threat.Name).Distinct().Order(StringComparer.Ordinal).ToArray();
        if (!names.SequenceEqual(reportedPredators))
        {
            reportedPredators = names;
            log(new { kind = "play_predator_sight", actor = Name, predators, gameTime });
        }
        var body = player.ComponentBody;
        bool inWater = IsOnWater(body);
        var threat = ThreatInRange(body, inWater);
        if (threat is null || gameTime < nextEvade || travel?.Evading == true
            || (inWater && travel?.HeadingForLand == true)) return false;

        var away = body.Position.XZ - new Vector2(threat.X, threat.Z);
        if (away.LengthSquared() < 0.04f) return false;
        queue.Cancel(Id);
        if (!Stop())
        {
            Retire(queue, "cannot_interrupt_for_predator");
            return true;
        }
        nextEvade = gameTime + 12;
        float bearing = Vector2.Angle(body.Matrix.Forward.XZ, away) * (180f / MathF.PI);
        var lastSeen = threat;
        travel = new(player, owner, bearing, inWater ? 9 : 12, inWater ? "dry_land" : "survey", true, gameTime,
            ObserveSurvival, ReadDryWindow, log, escapeFrom: inWater ? null : () =>
            {
                lastSeen = ThreatInRange(body, false) ?? lastSeen;
                return new(lastSeen.X, lastSeen.Z);
            }, escapeClearance: !inWater && threat.Predator ? 10f : 0f);
        log(new { kind = "play_predator_evade", actor = Name, threat, gameTime, bearing });
        return true;
    }

    private bool IsOnWater(ComponentBody body) => body.ImmersionDepth > 0.12f
        || player.Entity.FindComponent<ComponentRider>()?.Mount is not null || PlaySurvivalSenses.BoatDeck(body) is not null;

    private PlaySurvivalSenses.Threat? ThreatInRange(ComponentBody body, bool inWater) =>
        predators.FirstOrDefault(animal => PlaySurvivalSenses.NeedsAvoidance(animal, inWater)
            && !(earthSite is { } site && site.SameColumn(body) && animal.Y >= site.Surface - 0.2f
                && (body.Position.Y <= site.Surface - 2 || shelter?.Leaving == true
                    && (body.Position.Y < site.Surface - 0.05f || !PlaySurvivalSenses.OnDryGround(body)))));

    private static Vector3 Position(JsonElement point) =>
        new(point.GetProperty("x").GetSingle(), point.GetProperty("y").GetSingle(), point.GetProperty("z").GetSingle());

    private void Report(string? tool, string status, string? reason, double gameTime,
        int? affectedCount = null, Vector2? movement = null)
    {
        nextDecision = gameTime + 0.6;
        var displacement = movement is { } delta ? new { dx = delta.X, dz = delta.Y, meters = delta.Length() } : null;
        outcome = new { tool, status, reason, affectedCount, displacement };
        log(new { kind = "play_outcome", actor = Name, actorId = Id, tool, status, reason, affectedCount,
            displacement, moves = Moves, gameTime });
    }

    private void Retire(PlayDecisionQueue queue, string reason)
    {
        retired = true;
        queue.Cancel(Id);
        Stop();
        log(new { kind = "play_actor_stopped", actor = Name, actorId = Id, reason });
    }

    public bool Stop()
    {
        planned.Clear();
        shallowExit = false;
        harvest = null;
        foodApproach = null;
        shelter?.Stop();
        shelter = null;
        production?.Stop();
        production = null;
        drying = null;
        travel?.Stop();
        travel = null;
        active = null;
        if (owner.IsActionRunning && player.Entity.IsAddedToProject) tools.Call("cancel_action", new { });
        return !owner.IsActionRunning;
    }

    private static string? Status(ToolReceipt receipt) => receipt.State == "returned"
        ? Text(receipt.Data, "status") : null;

    private static string? Failure(ToolReceipt receipt) => Text(receipt.Data, "failure");

    private static string? Text(JsonElement data, string property) => data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString() : null;

    private static int? Number(JsonElement data, string property) => data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int number) ? number : null;

    private static Guid? RunId(ToolReceipt receipt) => receipt.State == "returned"
        && receipt.Data.ValueKind == JsonValueKind.Object && receipt.Data.TryGetProperty("runId", out var run)
        && run.TryGetGuid(out var id) && id != Guid.Empty ? id : null;
}
