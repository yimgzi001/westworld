#nullable enable

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

internal sealed partial class PlayableActor
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
    private readonly Dictionary<Point3, PlaySurvivalProduction.Patch> ownFarm = new();
    private PlaySurvivalProduction.CampSite? campSite;
    private (Guid Id, string Tool, Vector3 Origin)? active;
    private Harvest? harvest;
    private PlaySurvivalTravel? travel;
    private (PlaySurvivalFood.Food Target, int Before, double ResumeUntil, int Interruptions)? foodApproach;
    private (PlaySurvivalFood.Food Target, double Until, int Interruptions)? pausedFood;
    private readonly List<(PlaySurvivalFood.Food Target, Vector3 Position, double RetryAfter)> blockedFood = new();
    private PlaySurvivalPanorama.Sweep? surroundings;
    private PlayEarthShelter? shelter;
    private PlaySurvivalProduction? production;
    private (string Task, string? Stage, string Reason, double Time, bool Gear)? pausedSupply;
    private PlayEarthShelter.Site? earthSite;
    private BlockTarget? earthRoofAnchor;
    private PlayEarthShelter.Site? nightShelterGoal;
    private (PlayEarthShelter.Site Site, double Until)? failedNightSite;
    private (PlaySurvivalSenses.Threat Threat, double SeenAt)? blockedExit;
    private PlaySurvivalRest? drying;
    private NativeActorSession? travelSenses;
    private LocalObservationMemory? travelMemory;
    private PlaySurvivalSenses.Threat[] predators = [];
    private string[] reportedPredators = [];
    private double nextDecision, nextPoll, nextDangerScan, nextEvade, nextEscapeTurn, nextShelter, nextExit, nextProduction;
    private double nextPreSleepMeal;
    private double? coldSince;
    private (Vector3 Origin, double Time, Vector3[] Frozen)? nearbyCold;
    private bool retired, restedInEarth, shallowExit, leavingShelterLane;
    private int requests, shelterThreatRetreats;
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

    public PlayableActor(ComponentPlayer player, AtomicActions actionOwner, Guid worldId, Action<object> log, bool survival,
        float panoramaRadius, bool allowSocial = true)
    {
        ArgumentNullException.ThrowIfNull(actionOwner);
        if (!actionOwner.Queries.IsBoundTo(player.Entity)) throw new ArgumentException("动作端必须属于当前角色。", nameof(actionOwner));
        this.player = player;
        this.worldId = worldId;
        this.log = log;
        this.survival = survival;
        this.allowSocial = allowSocial;
        this.panoramaRadius = panoramaRadius;
        hearing = new(player);
        owner = actionOwner;
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
    public bool IsActing => NativeBusy || restPending || planned.Count > 0;
    private bool NativeBusy => active.HasValue || owner.IsActionRunning || harvest is not null || travel is not null
        || shelter is not null || drying is not null || production is not null || thermalRecovery is not null || heatEscape is not null;
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
        var menu = reply is null ? null : pendingSurvivalMenu;
        if (reply is not null) pendingSurvivalMenu = null;
        if (retired) return;

        if (survival && gameTime >= nextDangerScan)
        {
            nextDangerScan = gameTime + 0.55;
            var body = player.ComponentBody;
            coldSince = player.ComponentVitalStats.Temperature < 6f ? coldSince ?? gameTime : null;
            survivalMemory.VisitPosition(body.Position, PlaySurvivalSenses.OnDryGround(body), gameTime);
            if (CheckDanger(queue, gameTime)) reply = null;
            if (retired) return;
        }
        if (heatEscape is not null)
        {
            UpdateFireEscape(gameTime);
            return;
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
                    earthRoofAnchor = null;
                    restedInEarth = false;
                }
                else if (work.RoofAnchor is { } anchor) earthRoofAnchor = anchor;
                if (result != "succeeded" && earthSite is null) nextShelter = gameTime + 30;
                if (work.Leaving && result != "succeeded")
                {
                    nextExit = gameTime + 8;
                    if (work.LastExitThreat is { } threat && earthSite is not null)
                    {
                        blockedExit = (threat, gameTime);
                        shelterThreatRetreats++;
                        int delay = PlayEarthShelter.ExitRetryDelay(shelterThreatRetreats, player.ComponentVitalStats.Food);
                        nextExit = gameTime + delay;
                        log(new { kind = "play_shelter_exit_cooldown", actor = Name, attempts = shelterThreatRetreats, seconds = delay, gameTime });
                    }
                }
                if (work.Leaving && result == "succeeded")
                {
                    if (work.FarExitThreat is { } farThreat) blockedExit = (farThreat, gameTime);
                    if (TryDepartBlockedExit(gameTime)) return;
                }
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
        if (thermalRecovery is { } recovery)
        {
            UpdateThermalRecovery(recovery, gameTime);
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
                if (ContinueWarmWork(supply, result, gameTime)) return;
                if (result != "succeeded") nextProduction = gameTime + 0.6;
                if (PlaySurvivalProduction.IsProject(supply.Task) && result != "succeeded")
                    PauseSupply(supply, supply.Reason ?? "project_interrupted", gameTime);
                Report(PlaySurvivalProduction.Tool, result, supply.Reason, gameTime, supply.Added);
                FinishStep(result == "succeeded", supply.Reason, gameTime);
            }
            return;
        }
        if (exploration is { HuntBirds: true } && travel is not null && !travel.Evading
            && PlaySurvivalSenses.OnDryGround(player.ComponentBody) && gameTime >= nextHuntRead)
        {
            nextHuntRead = gameTime + 0.3;
            var prey = PlaySurvivalProduction.HuntingTargets(player, owner.Queries.FindVisibleObjectsAround(11f).Entities, 11f)
                .FirstOrDefault(bird => PermitSurvivalLeg(bird.Position, gameTime));
            if (prey is not null)
            {
                // 巡猎由模型选择。进入射程后的停步、持矛和投掷留在游戏线程，避免再等一轮网络请求。
                travel.Stop();
                travel = null;
                exploration = null;
                log(new { kind = "play_hunt_search_contact", actor = Name, prey.Name, prey.Distance, gameTime });
                StartProduction(PlaySurvivalProduction.Hunt, gameTime, prey.EntityId);
                return;
            }
        }
        if (travel is { } route)
        {
            route.Update(gameTime);
            if (route.Status is { } terminal)
            {
                travel = null;
                if (exploration is not null)
                {
                    FinishExploration(route, queue, gameTime);
                    return;
                }
                if (nightShelterGoal is { } nightGoal)
                {
                    nightShelterGoal = null;
                    if (terminal != "succeeded") failedNightSite = (nightGoal, gameTime + 90);
                    if (ThreatInRange(player.ComponentBody, false) is null && PlayEarthShelter.At(player.ComponentBody) is not null
                        && !PlaySurvivalRest.HasSecureCover(player))
                    {
                        DispatchIntent(PlaySurvivalIntents.BuildShelter, queue, gameTime);
                        return;
                    }
                    if (terminal != "succeeded") nextShelter = gameTime + 12;
                    Report(PlaySurvivalIntents.BuildShelter, terminal, route.Reason, gameTime);
                    FinishStep(terminal == "succeeded", route.Reason, gameTime);
                    return;
                }
                if (leavingShelterLane)
                {
                    leavingShelterLane = false;
                    bool cleared = terminal == "succeeded" && route.Reason == "escaped_along_supported_ground";
                    Report(PlaySurvivalIntents.LeaveShelter, "succeeded",
                        cleared ? "surface_reached_and_cleared" : "surface_reached_departure_blocked", gameTime,
                        movement: (player.ComponentBody.Position - route.Origin).XZ);
                    FinishStep(true, null, gameTime);
                    return;
                }
                if (route.Evading)
                {
                    if (route.UsedEscapeTurn) nextEscapeTurn = gameTime + 12;
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
                        earthRoofAnchor = null;
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
                    if (route.Reason == "food_collected_en_route")
                    {
                        int count = route.AcquiredTargetCount > 0 ? route.AcquiredTargetCount : FoodStock(approach.Target.Value) - approach.Before;
                        var foodMovement = (player.ComponentBody.Position - route.Origin).XZ;
                        if (foodMovement.Length() > 0.15f) Moves++;
                        Report(PlaySurvivalIntents.Gather, "succeeded", "target_food_acquired", gameTime, count, foodMovement);
                        FinishStep(true, null, gameTime);
                    }
                    else FinishFoodApproach(approach.Target, route.Reason, queue, gameTime);
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
                if (survival && reply.Error != "model_canceled") needsLocalFallback = true;
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
            if (menu is not null && plan.Steps.All(step => step.Tool == PlaySurvivalGoals.Tool))
            {
                StartSurvivalPlan(plan, menu, queue, gameTime);
                return;
            }
            foreach (var step in plan.Steps) planned.Enqueue(step);
        }
        if (restPending && !NativeBusy && gameTime >= nextDecision)
        {
            restPending = false;
            DispatchIntent(PlaySurvivalIntents.Rest, queue, gameTime);
            return;
        }
        if (!NativeBusy && (!survival || !player.ComponentSleep.IsSleeping) && gameTime >= nextDecision
            && planned.TryDequeue(out var next))
            DispatchStep(next, queue, gameTime);
    }

    private void DispatchStep(ToolDecisionStep step, PlayDecisionQueue queue, double gameTime)
    {
        if (survival && step.Tool == PlaySurvivalGoals.Tool && ContinueSurvivalPlan(step, queue, gameTime)) return;
        try
        {
            // 社交和旧试玩入口继续按最新角色引用复核。
            var current = Capture();
            current.Prompt.ParseResponse(JsonSerializer.Serialize(new
            {
                goal,
                steps = new[] { new { tool = step.Tool, arguments = step.Arguments } }
            }));
        }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException or ArgumentException)
        {
            Report(step.Tool, "action_no_longer_available", null, gameTime);
            FinishStep(false, "action_no_longer_available", gameTime);
            return;
        }

        log(new { kind = "play_plan_step", actor = Name, tool = step.Tool, remaining = planned.Count, gameTime });
        Dispatch(step.Tool, step.Arguments, queue, gameTime);
    }

    private void DispatchIntent(string tool, PlayDecisionQueue queue, double gameTime)
    {
        if (survivalGoal is null)
        {
            var kind = tool switch
            {
                PlaySurvivalIntents.BuildShelter => PlaySurvivalGoals.Kind.Build,
                PlaySurvivalIntents.LeaveShelter => PlaySurvivalGoals.Kind.Leave,
                PlaySurvivalIntents.Rest => PlaySurvivalGoals.Kind.Rest,
                _ => PlaySurvivalGoals.Kind.Eat
            };
            BeginSurvivalGoal(new(tool, kind, tool), "local_intent", gameTime);
        }
        var body = player.ComponentBody;
        bool dry = PlaySurvivalSenses.OnDryGround(body);
        if (tool == PlaySurvivalIntents.BuildShelter && (RepairableShelter() ?? PlayEarthShelter.At(body)) is { } ground)
        {
            if (earthSite != ground)
            {
                nextExit = 0;
                shelterThreatRetreats = 0;
                blockedExit = null;
                earthRoofAnchor = null;
            }
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
            shelter = new(player, owner, pit, true, gameTime, log, (threat, now) => PermitFarShelterExit(pit, threat, now),
                earthRoofAnchor);
            return;
        }
        if (tool == PlaySurvivalIntents.Rest && dry && !player.ComponentSleep.IsSleeping
            && RestThreatClear && PlaySurvivalRest.HasSecureCover(player))
        {
            if (player.ComponentSleep.CanSleep(out _))
            {
                Dispatch("act_Sleep", JsonSerializer.SerializeToElement(new { }), queue, gameTime);
                return;
            }
            if (PlaySurvivalRest.TiredEnough(player) && PlaySurvivalRest.WetnessBlocksSleep(player))
            {
                drying = new(player, gameTime);
                log(new { kind = "play_survival_drying", actor = Name, wetness = player.ComponentVitalStats.Wetness, gameTime });
                return;
            }
        }
        if (tool == PlaySurvivalIntents.Eat && TryEatOwned(queue, gameTime, safeOnly: false)) return;
        Report(tool, "failed", "intent_conditions_not_met", gameTime);
        FinishStep(false, "intent_conditions_not_met", gameTime);
    }

    private void StartFoodApproach(PlaySurvivalFood.Food food, PlayDecisionQueue queue, double gameTime, bool resuming = false,
        PlayObservedPath.Plan? path = null)
    {
        var checkpoint = resuming ? pausedFood : null;
        pausedFood = null;
        var body = player.ComponentBody;
        var offset = (food.Point - body.Position).XZ;
        log(new { kind = "play_food_objective", actor = Name, food.Name, target = food.Point.ToString(),
            distance = offset.Length(), resuming, gameTime });
        if (food.InReach)
        {
            CollectFood(food, queue, gameTime);
            return;
        }
        if (offset.LengthSquared() < 0.04f || food.SourceId.HasValue && offset.LengthSquared() <= 2.25f)
        {
            FinishFoodApproach(food, "food_outside_pickup_range", queue, gameTime);
            return;
        }
        float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset));
        path ??= PlanFoodPath(body.Position, food.Point, gameTime);
        if (path is not null)
            log(new { kind = "play_food_observed_path", actor = Name, cells = path.Cells, waypoints = path.Waypoints.Length, gameTime });
        foodApproach = (food, FoodStock(food.Value), checkpoint?.Until ?? gameTime + 120, checkpoint?.Interruptions ?? 0);
        travel = new(player, owner, bearing, Math.Clamp(offset.Length() + 1f, 2f, 40f), "food", false, gameTime,
            ObserveSurvival, ReadDryWindow, log, survivalMemory.VisitsTo, foodTarget: food, permitLandLeg: PermitSurvivalLeg,
            observedPath: path);
    }

    private PlayObservedPath.Plan? PlanFoodPath(Vector3 origin, Vector3 target, double gameTime)
    {
        if ((target - origin).XZ.LengthSquared() < 25f || surroundings is not { } sweep || gameTime - sweep.CompletedAt > 30
            || (origin - sweep.Origin).XZ.LengthSquared() > 36f) return null;
        var frozen = PlaySurvivalCold.CanEnterSnow(player) ? Array.Empty<Vector3>() : sweep.FrozenGround;
        return PlayObservedPath.Find(sweep.Panorama, origin, target, (from, to) => survivalMemory.AvoidsPredatorAreas(from, to, gameTime)
            && hearing.AllowsRoute(from, to, gameTime)
            && PlaySurvivalCold.AllowsRoute(from, to, frozen));
    }

    private (int Slot, int Value, float Risk, float Nutrition) FindMeal(bool safeOnly)
    {
        var inventory = player.ComponentMiner.Inventory;
        return Enumerable.Range(0, inventory.SlotsCount).Where(slot => inventory.GetSlotCount(slot) > 0)
            .Select(slot => (Slot: slot, Value: inventory.GetSlotValue(slot)))
            .Where(item => PlaySurvivalFood.CanEat(player, item.Value)).Select(item =>
            {
                var block = BlocksManager.Blocks[Terrain.ExtractContents(item.Value)];
                return (item.Slot, item.Value, Risk: block.GetSicknessProbability(item.Value), Nutrition: block.GetNutritionalValue(item.Value));
            }).Where(item => !safeOnly || item.Risk <= 0f)
            .OrderBy(item => item.Risk).ThenByDescending(item => item.Nutrition).FirstOrDefault();
    }

    private bool HasMeal() => FindMeal(safeOnly: false).Nutrition > 0f;

    private bool TryEatOwned(PlayDecisionQueue queue, double gameTime, bool safeOnly, string? automaticReason = null)
    {
        var meal = FindMeal(safeOnly);
        if (meal.Nutrition <= 0f) return false;
        if (survivalGoal is null)
            BeginSurvivalGoal(new("eat", PlaySurvivalGoals.Kind.Eat, "消耗背包中的实际食物"), automaticReason ?? "local_meal", gameTime);
        if (automaticReason is not null) log(new { kind = "play_survival_emergency_meal", actor = Name, value = meal.Value,
            meal.Nutrition, meal.Risk, reason = automaticReason, gameTime });
        Dispatch("act_Eat", JsonSerializer.SerializeToElement(new { slot = meal.Slot, value = meal.Value, count = 1 }), queue, gameTime);
        return true;
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
        if (target.SourceId is Guid source)
        {
            var drop = owner.Queries.FindVisibleObjectsAround(3.2f).Pickables
                .FirstOrDefault(item => item.Value == target.Value && NativeAcquisitionAudit.IdentityFor(item.CreatePickup(1).Target) == source);
            if (drop is not null)
            {
                var pickup = owner.Start(drop.CreatePickup(1));
                if (pickup.Result.Status == AtomicActionStatus.Succeeded)
                {
                    Report(PlaySurvivalIntents.Gather, "succeeded", "target_food_acquired", gameTime, 1);
                    FinishStep(true, null, gameTime);
                    return;
                }
                reason = pickup.Result.Failure.ToString();
            }
            DelayFood(target, gameTime);
            Report(PlaySurvivalIntents.Gather, "failed", reason ?? "target_food_unavailable", gameTime);
            FinishStep(false, reason, gameTime);
            return;
        }
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

    private bool TryEatBeforeNightSleep(PlayDecisionQueue queue, double gameTime)
    {
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        if (gameTime < nextPreSleepMeal || !PlaySurvivalRest.NightCoverDue(calendar)
            || player.ComponentVitalStats.Food >= PlayFoodBudget.NeedThroughMorning(player, calendar)) return false;
        restPending = true;
        if (!TryEatOwned(queue, gameTime, safeOnly: true, automaticReason: "before_night"))
        {
            restPending = false;
            return false;
        }
        nextPreSleepMeal = gameTime + 4;
        return true;
    }

    public bool Request(PlayDecisionQueue queue, StructuredFileController? bridge, double gameTime,
        bool delayFirst, bool smokeSpeaker)
    {
        if (retired || IsActing || queue.IsPending(Id) || gameTime < nextDecision
            || (survival && (player.ComponentSleep.IsSleeping
                || player.Entity.FindComponent<ComponentRider>()?.m_isAnimating == true))
            || (bridge is null && SmokeReady)) return false;
        // 已确认的危险由游戏线程处置。避险重试间隔内发出的请求很快会被下一次避险取消。
        var body = player.ComponentBody;
        if (survival && ThreatInRange(body, IsOnWater(body)) is not null) return false;
        if (survival && gameTime < listenUntil)
        {
            nextDecision = Math.Min(listenUntil, gameTime + 1);
            return false;
        }
        bool inEarthShelter = InOwnShelter();
        float foodLevel = player.ComponentVitalStats.Food;
        bool eatForHealing = inEarthShelter && PlaySurvivalRest.NeedsFoodForHealing(player);
        if (survival && foodLevel <= 0.06f && PlaySurvivalSenses.OnDryGround(body)
            && TryEatOwned(queue, gameTime, safeOnly: foodLevel > 0.24f,
                automaticReason: foodLevel > 0.24f ? "recover_in_shelter" : "hunger")) return false;
        if (survival && player.Project.FindSubsystem<SubsystemTimeOfDay>(true) is { } emergencyCalendar
            && (emergencyCalendar.TimeOfDay < emergencyCalendar.DayStart || emergencyCalendar.TimeOfDay >= emergencyCalendar.DuskStart)
            && !inEarthShelter && gameTime >= nextShelter
            && TryNightShelter(queue, body, gameTime)) return false;
        if (survival && player.ComponentVitalStats.Temperature < 3.8f && TryWarmOrPrepare(gameTime)) return false;
        if (survival && inEarthShelter && HandleBlockedExit(queue, body, gameTime)) return false;
        if (gameTime < nextExit && inEarthShelter && foodLevel >= 0.18f && PlaySurvivalRest.HasSecureCover(player))
        {
            nextDecision = Math.Min(nextExit, gameTime + 1);
            return false;
        }
        if (survival && inEarthShelter && foodLevel >= 0.18f && hearing.Nearby(gameTime)
            && PlaySurvivalRest.HasSecureCover(player) && !RestWindow())
        {
            nextDecision = gameTime + 2;
            return false;
        }

        if (survival && !PlaySurvivalSenses.OnDryGround(body) && !IsOnWater(body)) return false;
        if (!queue.HasSlot) return false;

        (Guid Observation, ToolDecisionPrompt Prompt, JsonElement Context, IReadOnlyList<ToolOffer> Offers,
            SurvivalMenu? Menu) snapshot;
        try
        {
            if (survival)
            {
                RefreshSurroundings(gameTime);
                if (TryResumeChosenPlan(queue, gameTime)) return false;
            }
            snapshot = Capture(includePanorama: true);
            if (snapshot.Menu is { } menu)
            {
                if (needsLocalFallback)
                {
                    needsLocalFallback = false;
                    RunFallback(menu, queue, gameTime);
                    return false;
                }
            }
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
        pendingSurvivalMenu = snapshot.Menu;
        log(new { kind = "play_request", actor = Name, actorId = Id, round, gameTime, requestId,
            observationId = snapshot.Observation, promptBytes = Encoding.UTF8.GetByteCount(snapshot.Prompt.Prompt) });
        return true;
    }

    private void StartProduction(string task, double gameTime, int? bird = null)
    {
        bool resumeGear = false;
        if (pausedSupply is { } paused && paused.Task == task)
        {
            resumeGear = paused.Gear;
            log(new { kind = "play_production_resumed", actor = Name, task, previousStage = paused.Stage, gameTime });
            pausedSupply = null;
        }
        production = new(player, owner, tools, task, gameTime, log, placedStations, ReadDryWindow, PermitSurvivalLeg,
            selectedBird: bird, resumeGear: resumeGear, ownFarm: ownFarm, camp: campSite);
    }

    private void PauseSupply(PlaySurvivalProduction supply, string reason, double gameTime)
    {
        pausedSupply = (supply.Task, supply.CurrentStage, reason, gameTime, supply.PreparingGear);
        log(new { kind = "play_production_paused", actor = Name, task = supply.Task, stage = supply.CurrentStage, reason, gameTime });
    }

    private bool TryResumeSurvivalWork(PlayDecisionQueue queue, double gameTime)
    {
        var body = player.ComponentBody;
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        if (surroundings is not { } fresh || earthSite?.Contains(body) == true || !PlaySurvivalSenses.OnDryGround(body)
            || PlaySurvivalRest.NightCoverDue(calendar)) return false;
        if (pausedFood is { } food)
        {
            var seen = gameTime <= food.Until ? fresh.Sectors.SelectMany(sector => sector.Food).FirstOrDefault(item => item.Matches(food.Target)
                && PlaySurvivalFood.CanSeek(player, item) && CanRetryFood(item, body.Position, gameTime)
                && PermitSurvivalLeg(item.Point, gameTime)) : null;
            if (seen is not null)
            {
                BeginSurvivalGoal(new("food_resume", PlaySurvivalGoals.Kind.Collect, seen.Name, Food: seen), "resume_after_safety", gameTime);
                StartFoodApproach(seen, queue, gameTime, resuming: true);
                return true;
            }
            if (gameTime > food.Until)
            {
                pausedFood = null;
                log(new { kind = "play_food_objective_released", actor = Name, target = food.Target.Point.ToString(),
                    reason = "target_not_reconfirmed_before_deadline", gameTime });
            }
        }
        if (pausedExploration is { } search)
        {
            pausedExploration = null;
            if (gameTime <= search.Until && search.Search.CanContinue(gameTime)
                && ReadCourses(gameTime, search.Search.Heading).Length > 0)
            {
                BeginSurvivalGoal(search.Choice, "resume_after_sound", gameTime);
                exploration = search.Search;
                StartExplorationSection(gameTime);
                return true;
            }
        }
        if (pausedSupply is not { } supply || gameTime - supply.Time > 2400 || gameTime < nextProduction
            || supply.Reason is not ("predator" or "heard_predator" or "need_meal" or "project_paused_for_night") || !ProjectVitalsReady(gameTime)
            || !PlaySurvivalProduction.CanAdvanceProject(player, supply.Task, fresh.Panorama, placedStations,
                end => PermitSurvivalLeg(end, gameTime), supply.Gear)) return false;
        BeginSurvivalGoal(new(supply.Task, PlaySurvivalGoals.Kind.Supply, supply.Stage ?? supply.Task, Supply: supply.Task),
            "resume_after_safety", gameTime);
        StartProduction(supply.Task, gameTime);
        return true;
    }

    private bool ProjectVitalsReady(double gameTime) => player.ComponentHealth.Health >= 0.6f
        && player.ComponentVitalStats.Food > 0.11f && !PlaySurvivalRest.ColdNeedsShelter(player.ComponentVitalStats.Temperature,
            gameTime - (coldSince ?? gameTime), player.ComponentFlu.HasFlu);

    private bool HandleBlockedExit(PlayDecisionQueue queue, ComponentBody body, double gameTime)
    {
        if (blockedExit is null || !PlaySurvivalSenses.OnDryGround(body)) return false;
        if (!PlaySurvivalRest.HasEnclosure(player))
        {
            if (gameTime < nextShelter || RepairableShelter() is null) return false;
            log(new { kind = "play_shelter_blocked_exit_repair", actor = Name, gameTime });
            DispatchIntent(PlaySurvivalIntents.BuildShelter, queue, gameTime);
            return true;
        }
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        if (calendar.TimeOfDay < calendar.DayStart || calendar.TimeOfDay >= calendar.DuskStart - 0.06f
            || PlaySurvivalRest.NeedsRecovery(player) || PlaySurvivalRest.WetnessBlocksSleep(player)
            || player.ComponentVitalStats.Temperature < (player.ComponentFlu.HasFlu ? 8f : 6f)) return false;

        // 白天坑口受阻时按原生危险状态重查。保留进食、伤病恢复和夜间睡眠的优先级。
        if (earthSite is { } site && predators.Any(animal => !animal.Aquatic && animal.Y >= site.Surface - 1.05f
            && PlaySurvivalSenses.NeedsAvoidance(animal, inWater: false)))
            nextExit = Math.Max(nextExit, gameTime + 12);
        if (hearing.Nearby(gameTime) && player.ComponentVitalStats.Food >= 0.18f)
            nextExit = Math.Max(nextExit, gameTime + 2);
        if (gameTime < nextExit)
        {
            nextDecision = Math.Min(nextExit, gameTime + 2);
            return true;
        }
        log(new { kind = "play_shelter_blocked_exit_recheck", actor = Name, attempts = shelterThreatRetreats, gameTime });
        DispatchIntent(PlaySurvivalIntents.LeaveShelter, queue, gameTime);
        return true;
    }

    private bool TryNightShelter(PlayDecisionQueue queue, ComponentBody body, double gameTime)
    {
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        if (!PlaySurvivalRest.NightCoverDue(calendar) || !PlaySurvivalSenses.OnDryGround(body) || InOwnCamp()
            || PlaySurvivalRest.HasSecureCover(player)) return false;
        var standing = PlayEarthShelter.At(body);
        bool beforeDusk = calendar.TimeOfDay >= calendar.DayStart && calendar.TimeOfDay < calendar.DuskStart;
        var choice = PlayShelterSites.Choose(owner.Queries.ReadPanorama(13f),
            body.Position, standing, end => PermitSurvivalLeg(end, gameTime)
                && campSite?.Protects(Terrain.ToCell(end) - new Point3(0, 1, 0)) != true
                && !(failedNightSite is { } failed && gameTime < failed.Until
                    && failed.Site.X == Terrain.ToCell(end.X) && failed.Site.Z == Terrain.ToCell(end.Z)));
        if (choice is null) return false;
        log(new { kind = "play_survival_night_shelter", actor = Name, foodLevel = player.ComponentVitalStats.Food,
            choice.Site, choice.Outlets, travelNeeded = choice.Site != standing, gameTime });
        BeginSurvivalGoal(new("night_cover", PlaySurvivalGoals.Kind.Build, new { choice.Outlets }), "night_cover", gameTime);
        StartEarthShelterSite(choice.Site, queue, gameTime);
        return true;
    }

    private void StartEarthShelterSite(PlayEarthShelter.Site site, PlayDecisionQueue queue, double gameTime)
    {
        var body = player.ComponentBody;
        if (site == PlayEarthShelter.At(body))
        {
            DispatchIntent(PlaySurvivalIntents.BuildShelter, queue, gameTime);
            return;
        }
        nightShelterGoal = site;
        var point = site.Center(site.Surface);
        var delta = (point - body.Position).XZ;
        float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, delta));
        travel = new(player, owner, bearing, delta.Length(), "shelter", false, gameTime, ObserveSurvival, ReadDryWindow, log,
            survivalMemory.VisitsTo, shelterTarget: point, permitLandLeg: PermitSurvivalLeg);
    }

    private bool TryDepartBlockedExit(double gameTime)
    {
        if (blockedExit is not { } blocked || gameTime - blocked.SeenAt > 240) return false;
        var body = player.ComponentBody;
        var threat = new Vector2(blocked.Threat.X, blocked.Threat.Z);
        var window = ReadDryWindow();
        bool Permitted(Vector3 end) => PermitSurvivalLeg(end, gameTime);
        var exit = PlayDryRoutes.Escape(window, threat, 6f, canAlign: false, Permitted);
        if (exit is null && PlayDryRoutes.EscapeTurn(window, threat) is { } turn && Permitted(turn.Approach.End) && Permitted(turn.Exit))
            exit = turn.Approach;
        if (exit is null) return false;
        var delta = (exit.End - body.Position).XZ;
        if (delta.LengthSquared() < 0.64f) return false;
        float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, delta));
        const float minimumClearance = 18f;
        leavingShelterLane = true;
        log(new { kind = "play_shelter_surface_departure", actor = Name, firstTarget = exit.End.ToString(), minimumClearance, gameTime });
        travel = new(player, owner, bearing, Math.Max(8f, delta.Length()), "survey", false, gameTime, ObserveSurvival, ReadDryWindow, log,
            survivalMemory.VisitsTo, escapeFrom: () => threat, permitLandLeg: PermitSurvivalLeg,
            escapeClearance: minimumClearance, allowEscapeTurn: true);
        return true;
    }

    private bool PermitFarShelterExit(PlayEarthShelter.Site site, PlaySurvivalSenses.Threat threat, double gameTime)
    {
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        if (shelterThreatRetreats < 2 || player.ComponentHealth.Health < 0.65f || player.ComponentVitalStats.Stamina < 0.35f
            || calendar.TimeOfDay < calendar.DayStart || calendar.TimeOfDay >= calendar.DuskStart - 0.06f) return false;
        return PlayDryRoutes.HasObservedShelterOutlet(ReadDryWindow(), site.Center(site.Surface), new Vector2(threat.X, threat.Z),
            end => PermitSurvivalLeg(end, gameTime));
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

    private bool PermitSurvivalLeg(Vector3 end, double gameTime)
    {
        var origin = player.ComponentBody.Position;
        return survivalMemory.AvoidsPredatorAreas(origin, end, gameTime)
            && PlayFireSafety.AllowsRoute(player.ComponentBody, end, observedHeat)
            && hearing.AllowsRoute(origin, end, gameTime)
            && (PlaySurvivalCold.CanEnterSnow(player) || PlaySurvivalCold.AllowsRoute(origin, end, FrozenAround(origin, gameTime)));
    }

    private Vector3[] FrozenAround(Vector3 origin, double gameTime)
    {
        if (surroundings is { } sweep && gameTime - sweep.CompletedAt <= 12 && (origin - sweep.Origin).XZ.LengthSquared() <= 36f)
            return sweep.FrozenGround;
        if (nearbyCold is { } nearby && gameTime - nearby.Time <= 3 && (origin - nearby.Origin).XZ.LengthSquared() <= 4f)
            return nearby.Frozen;
        var frozen = PlaySurvivalCold.Observe(owner.Queries.ReadPanorama(9f));
        nearbyCold = (origin, gameTime, frozen);
        return frozen;
    }

    private bool RestWindow()
    {
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        float start = earthSite?.Contains(player.ComponentBody) == true ? calendar.DuskStart - 0.1f : calendar.DuskStart;
        return calendar.TimeOfDay >= start || calendar.TimeOfDay < calendar.DayStart
            || player.ComponentVitalStats.Sleep <= 0.25f || PlaySurvivalRest.NeedsRecovery(player);
    }

    private bool RestThreatClear => !predators.Any(animal => !animal.Aquatic && animal.Distance < 12f);

    private PlayEarthShelter.Site? RepairableShelter()
    {
        var body = player.ComponentBody;
        return earthSite is { } site && site.Contains(body) && body.Position.Y <= site.Surface - 2.95f
            && PlaySurvivalSenses.OnDryGround(body) ? site : null;
    }

    private (Guid Observation, ToolDecisionPrompt Prompt, JsonElement Context, IReadOnlyList<ToolOffer> Offers,
        SurvivalMenu? Menu) Capture(bool includePanorama = false)
    {
        var (observation, inventory) = Observe(survival && includePanorama ? 128 : 8);
        var body = player.ComponentBody;
        double gameTime = observation.GetProperty("gameTime").GetDouble();
        var supported = tools.GetTools();
        Guid id = Guid.NewGuid();
        var messages = allowSocial ? CaptureInbox(gameTime) : null;
        var scope = new SocialCaptureId(worldId, Session, Id, id);
        var empty = JsonSerializer.SerializeToElement(new { });
        var offers = new List<ToolOffer>();
        if (allowSocial)
        {
            var reads = new List<CapabilityRead>
            {
                new(scope, "observe", empty, observation),
                new(scope, "read_inventory", empty, inventory)
            };
            if (messages is { } current) reads.Add(new(scope, "read_social", empty, current));
            var social = CapabilityFacts.Build(scope, reads, supported.Select(tool => tool.Name).ToArray(), !owner.IsActionRunning).Social;
            if (social is not null)
                offers.AddRange(SocialTaskFamily.Build(scope, social).Entries
                    .Where(entry => entry.Descriptor.Name.StartsWith("act_", StringComparison.Ordinal))
                    .Select(entry => entry.Offer).OfType<ToolOffer>());
        }
        var slots = inventory.GetProperty("slots").EnumerateArray()
            .Where(slot => slot.GetProperty("count").GetInt32() > 0)
            .Select(slot => new
            {
                slot = slot.GetProperty("slot").GetInt32(),
                value = slot.GetProperty("value").GetInt32(),
                count = slot.GetProperty("count").GetInt32(),
                item = ReadItem(slot.GetProperty("value").GetInt32())
            }).ToArray();

        if (!survival)
        {
            bool mounted = player.Entity.FindComponent<ComponentRider>()?.Mount is not null;
            offers.AddRange(NativeToolOffers.Build(supported.Where(tool => Actions.Contains(tool.Name)
                && !(mounted && tool.Name is "act_MoveTo" or "act_FaceTowards")).ToArray(),
                observation, inventory, Array.Empty<JsonElement>(), waitSeconds: 10).Select(PlayNavigationMenu.LimitRelativeMove));
            var ordinary = JsonSerializer.SerializeToElement(new
            {
                objective = "根据自己的身体、背包和局部视野行动。移动受阻后，根据实际位移和新观察调整方向。需要合作时可以交流或交接。",
                actor = Name, previousGoal = goal, previousOutcome = outcome, observation, inventory = new { slots }, receivedSocial = messages
            });
            return (id, new ToolDecisionPrompt(Session, Id, id, ordinary, offers, maxSteps: 1), ordinary, offers, null);
        }

        if (!includePanorama)
        {
            var socialOnly = JsonSerializer.SerializeToElement(new { receivedSocial = messages });
            return (id, new ToolDecisionPrompt(Session, Id, id, socialOnly, offers, maxSteps: 1), socialOnly, offers, null);
        }

        var (_, nearby) = PlaySurvivalFood.Project(observation);
        survivalMemory.SeeFood(nearby, gameTime);
        player.ComponentSleep.CanSleep(out string sleepBlocker);
        var menu = ReadSurvivalMenu(gameTime, nearby, sleepBlocker);
        offers.Insert(0, PlaySurvivalGoals.Offer(menu.Choices));
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        var vitals = player.ComponentVitalStats;
        var reserves = PlayFoodBudget.Count(player);
        bool dry = PlaySurvivalSenses.OnDryGround(body);
        bool inShelter = InOwnShelter();
        bool nightDue = PlaySurvivalRest.NightCoverDue(calendar);
        bool snowReady = PlaySurvivalCold.CanEnterSnow(player);
        var context = JsonSerializer.SerializeToElement(new
        {
            actor = Name,
            strategyNotes = PlaySurvivalKnowledge.ForSituation(player, campSite?.Built == true,
                placedStations.ContainsKey(FurnaceBlock.Index)),
            situation = new
            {
                timeOfDay = Math.Round(calendar.TimeOfDay, 3),
                daylightLeft = calendar.TimeOfDay >= calendar.DayStart && calendar.TimeOfDay < calendar.DuskStart
                    ? Math.Round((calendar.DuskStart - calendar.TimeOfDay) * calendar.DayDuration) : 0,
                dry, nightCoverDue = nightDue, needsFoodBeforeDusk = NeedsDuskFood(calendar), inShelter,
                sleepBlocker = string.IsNullOrEmpty(sleepBlocker) ? null : sleepBlocker,
                snowReady, insulation = Math.Round(PlaySurvivalCold.Insulation(player), 2),
                bodyTemperature = Math.Round(vitals.Temperature, 1), outsideTemperature = Math.Round(vitals.EnvironmentTemperature, 1),
                flu = player.ComponentFlu.HasFlu, sick = player.ComponentSickness.IsSick,
                onFire = player.Entity.FindComponent<ComponentOnFire>()?.IsOnFire == true,
                touchingFire = player.Entity.FindComponent<ComponentOnFire>()?.CheckIfBodyTouchesFire() == true
            },
            self = observation.GetProperty("self"),
            inventory = new { slots },
            foodReserve = new { safe = Math.Round(reserves.Safe, 2), emergency = Math.Round(reserves.Risky, 2),
                dailyTarget = Math.Round(PlayFoodBudget.DailyNeed(player), 2), needsSupply = PlayFoodBudget.NeedsSupply(player),
                throughMorning = Math.Round(PlayFoodBudget.NeedThroughMorning(player, calendar), 2) },
            lastGoal = lastSurvivalGoal,
            previousPlan = lastSurvivalPlan,
            plantedPumpkins = ownFarm.Values.Select(patch => new { cell = patch.Cell.ToString(),
                lastSeenSize = patch.LastSize, lastSeenDead = patch.LastDead, lastSeenSecondsAgo = Math.Round(gameTime - patch.LastSeen),
                plantedSecondsAgo = Math.Round(gameTime - patch.PlantedAt) }).ToArray(),
            ownCamp = campSite is { } home ? new { home.X, home.Y, home.Z, home.Built, home.DoorClosed,
                distance = Math.Round((home.Inside - body.Position).XZ.Length(), 1), inside = InOwnCamp(),
                lastConfirmedSecondsAgo = Math.Round(gameTime - home.LastConfirmed) } : null,
            worksites = placedStations.Select(site => new { type = site.Key == CraftingTableBlock.Index ? "workbench"
                : site.Key == FurnaceBlock.Index ? "furnace" : "campfire", cell = site.Value.ToString(),
                distance = Math.Round((new Vector3(site.Value) - body.Position).XZ.Length(), 1),
                currentlySeen = surroundings?.Panorama.Blocks.Any(block => block.Cell == site.Value) == true }).ToArray(),
            recentOutcome = outcome,
            unfinishedSupply = pausedSupply is { } unfinished && gameTime - unfinished.Time <= 2400
                ? new { task = unfinished.Task, stage = unfinished.Stage, reason = unfinished.Reason } : null,
            visiblePredators = predators.Length == 0 ? null : predators,
            heardPredators = HearingSnapshot(gameTime),
            localMemory = survivalMemory.Snapshot(gameTime),
            surroundings = surroundings?.Snapshot(body.Position, body.Matrix.Forward.XZ, gameTime, dry),
            goalOptions = menu.Choices.Select(choice => choice.Snapshot()).ToArray(),
            receivedSocial = messages
        }, SurvivalContextJson);
        return (id, new ToolDecisionPrompt(Session, Id, id, context, offers, maxSteps: 4, minSteps: 1,
            decisionGuide: PlaySurvivalGoals.Guide), context, offers, menu);
    }

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
        else if (harvest is null && !(restPending && tool == "act_Eat")) FinishStep(status == "succeeded", receipt.Error ?? Failure(receipt), gameTime);
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
        var origin = attempt.Food.Point;
        var native = owner.Queries.FindVisibleObjectsAround(4f).Pickables.Where(drop =>
            Terrain.ExtractContents(drop.Value) == Terrain.ExtractContents(attempt.Food.Value)
            && (drop.Position - origin).LengthSquared() <= 4f).MinBy(drop => drop.Distance);
        if (native is null || PlaySurvivalFood.DescribeDrop(native) is not { } observed) return;
        var drop = observed with { InReach = native.InReach };
        string identity = drop.SourceId!.Value.ToString("N");
        if (drop.InReach && attempt.PickupAttempts.Add(identity))
        {
            // 原生全向可见拾取物保留对象身份，低头角度不会影响掉落物收尾。
            var pickup = owner.Start(native.CreatePickup(1));
            int acquired = FoodStock(attempt.Food.Value) - attempt.StockBefore;
            if (pickup.Result.Status == AtomicActionStatus.Succeeded && acquired > 0)
                EndHarvest("succeeded", "edible_stock_increased", acquired, gameTime);
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
        if (status == "succeeded" && harvest?.Food.CropCell is { } harvested) ownFarm.Remove(harvested);
        if (status != "succeeded" && harvest is { } failed) DelayFood(failed.Food, gameTime);
        harvest = null;
        Report(PlaySurvivalFood.HarvestTool, status, reason, gameTime, count);
        FinishStep(status == "succeeded", reason, gameTime);
    }

    private void FinishStep(bool succeeded, string? reason, double gameTime)
    {
        EndSurvivalGoal(succeeded ? "succeeded" : "failed", reason, gameTime);
        RecordSurvivalPlanStep(succeeded, reason, gameTime);
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
        if (CheckFireDanger(queue, gameTime)) return true;
        predators = PlaySurvivalSenses.Threats(player, owner.Queries, panoramaRadius);
        survivalMemory.SeePredators(predators, gameTime);
        var names = predators.Select(threat => threat.Name).Distinct().Order(StringComparer.Ordinal).ToArray();
        if (!names.SequenceEqual(reportedPredators))
        {
            reportedPredators = names;
            log(new { kind = "play_predator_sight", actor = Name, predators, gameTime });
        }
        var body = player.ComponentBody;
        // 坑里悬空时由出坑动作决定回落和封顶，站稳后才交给地面撤离。
        if (shelter is { } exit && exit.OwnsExitMotion(body))
            return exit.TryRetreatFromPredators(predators, gameTime);
        bool inWater = IsOnWater(body);
        var threat = ThreatInRange(body, inWater);
        if (threat is null) return CheckSurvivalHandoff(queue, gameTime);
        if (gameTime < nextEvade || travel?.Evading == true
            || (inWater && travel?.HeadingForLand == true)) return false;

        var away = body.Position.XZ - new Vector2(threat.X, threat.Z);
        if (away.LengthSquared() < 0.04f) return false;
        if (!PauseForSurvivalSafety(queue, "predator", gameTime)) return true;
        BeginSurvivalGoal(new("evade", PlaySurvivalGoals.Kind.Evade, new { threat.Name, threat.WarningDistance }),
            "visible_threat", gameTime);
        nextEvade = gameTime + 12;
        float bearing = Vector2.Angle(body.Matrix.Forward.XZ, away) * (180f / MathF.PI);
        var lastSeen = threat;
        travel = new(player, owner, bearing, inWater ? 9 : 12, inWater ? "dry_land" : "survey", true, gameTime,
            ObserveSurvival, ReadDryWindow, log, escapeFrom: inWater ? null : () =>
            {
                lastSeen = ThreatInRange(body, false) ?? lastSeen;
                return new(lastSeen.X, lastSeen.Z);
            }, escapeClearance: !inWater ? Math.Max(10f, threat.WarningDistance - 2f) : 0f,
            allowEscapeTurn: !inWater && threat.Predator && gameTime >= nextEscapeTurn);
        log(new { kind = "play_predator_evade", actor = Name, threat, gameTime, bearing });
        return true;
    }

    private bool IsOnWater(ComponentBody body) => body.ImmersionDepth > 0.12f
        || player.Entity.FindComponent<ComponentRider>()?.Mount is not null || PlaySurvivalSenses.BoatDeck(body) is not null;

    private PlaySurvivalSenses.Threat? ThreatInRange(ComponentBody body, bool inWater) =>
        predators.FirstOrDefault(animal => PlaySurvivalSenses.NeedsAvoidance(animal, inWater)
            && !(earthSite is { } site && site.SameColumn(body) && animal.Y >= site.Surface - 0.2f
                && body.Position.Y <= site.Surface - 2));

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
        pendingSurvivalMenu = null;
        activeSurvivalPlan = null;
        survivalGoal = null;
        exploration = null;
        restPending = false;
        shallowExit = false;
        leavingShelterLane = false;
        nightShelterGoal = null;
        harvest = null;
        foodApproach = null;
        shelter?.Stop();
        shelter = null;
        production?.Stop();
        production = null;
        drying = null;
        thermalRecovery = null;
        heatEscape?.Stop();
        heatEscape = null;
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
