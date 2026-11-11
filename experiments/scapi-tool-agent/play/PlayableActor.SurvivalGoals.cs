#nullable enable

using System.Text.Json;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Decisions.Local;
using Choice = PlaySurvivalGoals.Choice;
using GoalKind = PlaySurvivalGoals.Kind;

internal sealed partial class PlayableActor
{
    private sealed record SurvivalMenu(Vector3 Origin, double CapturedAt, bool NightCoverDue, bool ColdRisk, float Temperature, Choice[] Choices);
    private sealed record SurvivalGoal(Choice Choice, string Source, double StartedAt, bool ColdRisk, float Temperature,
        bool NightDue, float Food, float Wetness, float Health);
    private SurvivalMenu? pendingSurvivalMenu;
    private SurvivalGoal? survivalGoal;
    private object? lastSurvivalGoal;
    private PlaySurvivalExploration? exploration;
    private readonly List<(Vector3 Origin, Vector2 Heading, double Until)> refusedCourses = new();
    private (string Task, double Until, int Inventory, Vector3 Origin)? refusedSupply;
    private bool restPending;
    private bool needsLocalFallback;
    private double nextExplorationFoodRead;
    private double nextHuntRead;
    private PlaySurvivalFood.Food[] explorationFood = [];

    private bool InOwnCamp() => campSite?.Built == true && campSite.Contains(player.ComponentBody);
    private bool InOwnShelter() => earthSite?.Contains(player.ComponentBody) == true || InOwnCamp();

    private SurvivalMenu ReadSurvivalMenu(double now, PlaySurvivalFood.Food[] nearby, string? sleepBlocker)
    {
        var body = player.ComponentBody;
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        var vitals = player.ComponentVitalStats;
        bool dry = PlaySurvivalSenses.OnDryGround(body);
        bool inShelter = InOwnShelter();
        bool night = PlaySurvivalRest.NightCoverDue(calendar);
        bool cold = PlaySurvivalRest.ColdNeedsShelter(vitals.Temperature, now - (coldSince ?? now), player.ComponentFlu.HasFlu);
        bool recovering = PlaySurvivalRest.NeedsRecovery(player);
        var choices = new List<Choice>();
        if (!dry && IsOnWater(body))
        {
            var shore = surroundings?.ShoreInDirection(body.Position, body.Matrix.Forward.XZ);
            choices.Add(new("shore", GoalKind.Shore, new { observedLanding = shore.HasValue }));
            return new(body.Position, now, night, cold, vitals.Temperature, choices.ToArray());
        }

        bool cover = dry && (inShelter || night || recovering || cold || RestWindow()) && PlaySurvivalRest.HasEnclosure(player);
        if (dry && earthSite?.Contains(body) != true)
        {
            choices.AddRange(FoodChoices(nearby, now));
            if (!cover && now >= nextShelter && surroundings is { } ground)
            {
                var sites = PlayShelterSites.Candidates(ground.Panorama, body.Position, PlayEarthShelter.At(body),
                    end => PermitSurvivalLeg(end, now) && campSite?.Protects(Terrain.ToCell(end) - new Point3(0, 1, 0)) != true);
                choices.AddRange(sites.Select((site, i) => new Choice(i == 0 ? "build" : $"cover_{i + 1}", GoalKind.Build,
                    new { goal = "前往已见软土挖临时庇护并封顶", site.Outlets,
                        distance = Math.Round((site.Site.Center(site.Site.Surface) - body.Position).XZ.Length(), 1),
                        nearWater = false }, Shelter: site.Site)));
            }
            if (!InOwnCamp() && (!night || !cover))
            {
                var courses = ReadCourses(now);
                choices.AddRange(courses.Select((course, i) => new Choice($"route_{i + 1}", GoalKind.Explore,
                    course.Snapshot(body.Matrix.Forward.XZ), Course: course)));
                if (PlaySurvivalProduction.HasHuntingGear(player))
                    choices.AddRange(courses.Select((course, i) => course with { HuntBirds = true }).Select((course, i) =>
                        new Choice($"hunt_route_{i + 1}", GoalKind.Explore, course.Snapshot(body.Matrix.Forward.XZ), Course: course)));
            }
            if (now >= nextProduction && surroundings is { } sweep)
            {
                var ready = PlaySurvivalProduction.AvailableTasks(player, sweep.Panorama, placedStations, true,
                    allowProjects: false, permitted: end => PermitSurvivalLeg(end, now));
                var tasks = PlaySurvivalProduction.PlanningTasks(player, sweep.Panorama, placedStations,
                    end => PermitSurvivalLeg(end, now));
                choices.AddRange(tasks.Where(task => task != PlaySurvivalProduction.Hunt && (refusedSupply is not { } refused
                    || refused.Task != task || now >= refused.Until || refused.Inventory != InventoryStamp()
                    || (refused.Origin - body.Position).XZ.LengthSquared() > 1f))
                    .Select(task => new Choice(task, GoalKind.Supply, new
                    {
                        goal = PlaySurvivalProduction.DescribeTask(task),
                        readyNow = ready.Contains(task),
                        prerequisites = PlaySurvivalProduction.Prerequisites(task)
                    }, Supply: task)));
                choices.AddRange(PlaySurvivalProduction.HuntingTargets(player, sweep.Panorama.Objects.Entities).Select((bird, i) =>
                    new Choice($"prey_{i + 1}", GoalKind.Supply, new { goal = "猎取这只食用鸟并取得生肉", bird.Name,
                        distance = Math.Round(bird.Distance, 1), readyNow = PlaySurvivalProduction.HasHuntingGear(player),
                        prerequisites = "先持有木矛，猎物仍可见时再执行" }, Supply: PlaySurvivalProduction.Hunt, Bird: bird.EntityId)));
                if (!InOwnCamp())
                {
                    var sites = PlaySurvivalProduction.QuarrySites(ReadDryWindow(), ProtectedOwnedGround)
                        .Where(site => PermitSurvivalLeg(new Vector3(site.Column(4).X + 0.5f, site.EntryCell.Y, site.Column(4).Z + 0.5f), now))
                        .Take(3).ToArray();
                    choices.AddRange(sites.Select((site, i) => new Choice($"quarry_{i + 1}", GoalKind.Supply,
                        site.Snapshot(body.Matrix.Forward.XZ), Supply: PlaySurvivalProduction.Quarry, Quarry: site)));
                    if (sites.Length > 0)
                        foreach (string future in new[] { PlaySurvivalProduction.StoneSupply, PlaySurvivalProduction.Axe, PlaySurvivalProduction.Furnace })
                            if (!choices.Any(choice => choice.Id == future))
                                choices.Add(new(future, GoalKind.Supply, new { goal = PlaySurvivalProduction.DescribeTask(future),
                                    readyNow = false, prerequisites = PlaySurvivalProduction.Prerequisites(future),
                                    dependency = "可先浅探石，实际发现岩石后再按库存安排后续制作与采集" }, Supply: future));
                }
            }
        }
        if (dry && earthSite?.Contains(body) != true)
        {
            var site = campSite ?? PlaySurvivalProduction.PlanCamp(ReadDryWindow());
            if (site is not null && !site.Built && !night)
                choices.Add(new(PlaySurvivalProduction.BuildCamp, GoalKind.Supply, new { goal = PlaySurvivalProduction.DescribeTask(PlaySurvivalProduction.BuildCamp),
                    readyNow = PlaySurvivalProduction.CampStock(player) >= 40, site.X, site.Y, site.Z,
                    materials = "四十块土或木板；需要先收够材料" }, Supply: PlaySurvivalProduction.BuildCamp, Camp: site));
            if (campSite is { Built: true } home)
                choices.Add(new(InOwnCamp() && home.DoorClosed ? PlaySurvivalProduction.LeaveCamp : PlaySurvivalProduction.ReturnCamp,
                    GoalKind.Supply, new { goal = InOwnCamp() && home.DoorClosed ? "打开入口并离开自己的土屋" : "回自己的土屋并封好入口",
                        distance = Math.Round((home.Inside - body.Position).XZ.Length(), 1) },
                    Supply: InOwnCamp() && home.DoorClosed ? PlaySurvivalProduction.LeaveCamp : PlaySurvivalProduction.ReturnCamp, Camp: home));
        }
        if (dry && vitals.Food < 0.98f)
            choices.Add(new("eat", GoalKind.Eat, new { goal = "吃背包中一份适合当前身体状态的食物",
                readyNow = HasMeal(), prerequisites = "先持有可以吃的食物；生肉不直接吃，生蛋仅在饥饿时应急" }));
        if (cover && CanRestHere())
            choices.Add(new("rest", GoalKind.Rest, "在掩体里睡觉，太湿时先晾干"));
        if (inShelter && !InOwnCamp() && CanLeaveHere(cover, night, recovering || cold, now))
            choices.Add(new("leave", GoalKind.Leave, "检查坑口并回到地面"));
        if (heldSurvivalPlan is { } saved && now - saved.Time < 1200 && !night && !cold)
            choices.Add(new("resume", GoalKind.Resume, new { saved.Purpose, saved.Reason,
                remaining = saved.Remaining.Select(choice => choice.Snapshot()).ToArray() }));
        choices.Add(new("wait", GoalKind.Wait, new { reason = "short_pause", seconds = 5,
            buildsCover = false, foodGained = 0 }, Seconds: 5));
        return new(body.Position, now, night, cold, vitals.Temperature, choices.ToArray());
    }

    private Choice[] FoodChoices(IEnumerable<PlaySurvivalFood.Food> nearby, double now)
    {
        var body = player.ComponentBody;
        var known = (surroundings is { } fresh && now - fresh.CompletedAt <= 24)
            ? fresh.FoodTargets : [];
        var foods = new List<PlaySurvivalFood.Food>(6);
        foreach (var food in known.Concat(nearby.Where(food => food.Source == "harvest" && food.CropCell.HasValue))
            .Where(food => !food.Underwater && PlaySurvivalFood.CanSeek(player, food) && CanRetryFood(food, body.Position, now)
                && PermitSurvivalLeg(food.Point, now))
            .OrderBy(food => food.SicknessRisk).ThenBy(food => (food.Point - body.Position).XZ.Length() - 12f * food.Nutrition))
        {
            if (foods.Any(food.Matches)) continue;
            bool inReach = nearby.Any(live => live.InReach && live.Matches(food));
            foods.Add(food with { InReach = inReach });
            if (foods.Count == 6) break;
        }
        return foods.Select((food, i) =>
        {
            var offset = food.Point - body.Position;
            return new Choice($"food_{i + 1}", GoalKind.Collect, new
            {
                food.Name, food.Source, food.Nutrition, food.SicknessRisk,
                distance = Math.Round(offset.XZ.Length(), 1), height = Math.Round(offset.Y, 1), food.InReach,
                bearing = offset.XZ.LengthSquared() < 0.01f ? 0 : Math.Round(MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, offset.XZ)))
            }, Food: food);
        }).ToArray();
    }

    private PlaySurvivalExploration.Course[] ReadCourses(double now, Vector2? forward = null)
    {
        var at = player.ComponentBody.Position;
        refusedCourses.RemoveAll(blocked => now >= blocked.Until);
        return PlaySurvivalExploration.Courses(ReadDryWindow(), end => PermitSurvivalLeg(end, now), survivalMemory.VisitsTo,
            heading => refusedCourses.Any(blocked => (blocked.Origin - at).XZ.LengthSquared() < 16f
                && Vector2.Dot(blocked.Heading, heading) > 0.72f), forward);
    }

    private bool NeedsDuskFood(SubsystemTimeOfDay calendar)
    {
        if (!PlayFoodBudget.StockWindow(calendar)) return false;
        return player.ComponentVitalStats.Food + PlayFoodBudget.Count(player).Safe < PlayFoodBudget.NeedThroughMorning(player, calendar);
    }

    private bool CanRestHere() => RestThreatClear && !player.ComponentSleep.IsSleeping
        && (player.ComponentSleep.CanSleep(out _) || PlaySurvivalRest.TiredEnough(player) && PlaySurvivalRest.WetnessBlocksSleep(player));

    private bool CanLeaveHere(bool cover, bool night, bool recovery, double now) => now >= nextExit;

    private bool TryTakeKnownFood(IEnumerable<Choice> choices, PlayDecisionQueue queue, double now, string reason,
        bool urgent, Action? beforeTake = null)
    {
        foreach (var choice in choices)
        {
            if (choice.Food is not { } food) continue;
            var origin = player.ComponentBody.Position;
            float distance = (food.Point - origin).XZ.Length();
            var path = distance >= 5f ? PlanFoodPath(origin, food, now) : null;
            if (!food.InReach && (distance > (urgent ? 18f : 12f) || distance >= 5f && path is null)) continue;
            if (!food.InReach && distance is >= 1.5f and < 5f
                && PlayDryRoutes.Next(ReadDryWindow(), Vector2.Normalize((food.Point - origin).XZ), distance, canAlign: true,
                    permitted: end => PermitSurvivalLeg(end, now)) is null) continue;
            beforeTake?.Invoke();
            BeginSurvivalGoal(choice, reason, now);
            StartFoodApproach(food with { InReach = false }, queue, now, path: path);
            return true;
        }
        return false;
    }

    private void RunFallback(SurvivalMenu menu, PlayDecisionQueue queue, double now)
    {
        if (TryTakeKnownFood(menu.Choices, queue, now, "model_fallback_food", urgent: player.ComponentVitalStats.Food <= 0.24f)) return;
        var choice = menu.Choices.FirstOrDefault(option => option.Activity is GoalKind.Shore or GoalKind.Build or GoalKind.Leave)
            ?? menu.Choices.FirstOrDefault(option => option.Activity == GoalKind.Explore)
            ?? new Choice("wait", GoalKind.Wait, "暂时没有已验证的路线", Seconds: 5);
        DispatchSurvivalGoal(choice, menu, queue, now, "model_fallback");
    }

    private void BeginSurvivalGoal(Choice choice, string source, double now)
    {
        var vitals = player.ComponentVitalStats;
        survivalGoal = new(choice, source, now, ColdRisk(now), vitals.Temperature,
            PlaySurvivalRest.NightCoverDue(player.Project.FindSubsystem<SubsystemTimeOfDay>(true)), vitals.Food, vitals.Wetness,
            player.ComponentHealth.Health);
        log(new { kind = "play_survival_goal_started", actor = Name, choice = choice.Snapshot(), source, gameTime = now });
    }

    private void EndSurvivalGoal(string status, string? reason, double now)
    {
        if (survivalGoal is not { } current) return;
        survivalGoal = null;
        if (status == "failed" && current.Choice.Supply is { } supply)
            refusedSupply = (supply, now + 60, InventoryStamp(), player.ComponentBody.Position);
        lastSurvivalGoal = new
        {
            activity = current.Choice.Activity.ToString().ToLowerInvariant(),
            target = current.Choice.Food?.Name ?? current.Choice.Supply, status, reason,
            duration = Math.Round(now - current.StartedAt, 1), actual = outcome,
            change = new { food = Math.Round(player.ComponentVitalStats.Food - current.Food, 3),
                temperature = Math.Round(player.ComponentVitalStats.Temperature - current.Temperature, 1),
                wetness = Math.Round(player.ComponentVitalStats.Wetness - current.Wetness, 2),
                health = Math.Round(player.ComponentHealth.Health - current.Health, 2),
                hasCover = PlaySurvivalRest.HasSecureCover(player) }
        };
        log(new { kind = "play_survival_goal_finished", actor = Name, goal = lastSurvivalGoal,
            current.Source, gameTime = now });
    }

    private int InventoryStamp()
    {
        var stamp = new HashCode();
        var inventory = player.ComponentMiner.Inventory;
        for (int slot = 0; slot < inventory.SlotsCount; slot++)
        {
            stamp.Add(inventory.GetSlotValue(slot));
            stamp.Add(inventory.GetSlotCount(slot));
        }
        return stamp.ToHashCode();
    }

    private void DispatchSurvivalGoal(Choice choice, SurvivalMenu menu, PlayDecisionQueue queue, double now, string source)
    {
        var body = player.ComponentBody;
        BeginSurvivalGoal(choice, source, now);
        if (now - menu.CapturedAt > 30 || (menu.Origin - body.Position).LengthSquared() > 2.25f)
        {
            FailGoal("decision_observation_expired", now);
            return;
        }
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        bool night = PlaySurvivalRest.NightCoverDue(calendar);
        bool dry = PlaySurvivalSenses.OnDryGround(body);
        bool inShelter = InOwnShelter();
        switch (choice.Activity)
        {
            case GoalKind.Resume:
                activeSurvivalPlan = null;
                planned.Clear();
                RestoreSurvivalPlan(queue, now);
                return;
            case GoalKind.Collect when dry && !inShelter && choice.Food is { } food
                && PlaySurvivalFood.CanSeek(player, food) && CanRetryFood(food, body.Position, now)
                && PermitSurvivalLeg(food.Point, now):
                StartFoodApproach(food with { InReach = false }, queue, now);
                return;
            case GoalKind.Explore when dry && !inShelter && choice.Course is { } course && PermitSurvivalLeg(course.Opening, now)
                && (!night || !PlaySurvivalRest.HasSecureCover(player)):
                exploration = new(body.Position, course, now);
                StartExplorationSection(now);
                return;
            case GoalKind.Supply when dry && earthSite?.Contains(body) != true && now >= nextProduction
                && choice.Supply is { } task:
                if (choice.Camp is { } home) campSite = home;
                StartProduction(task, now, choice.Bird, choice.Quarry);
                return;
            case GoalKind.Eat when dry:
                DispatchIntent(PlaySurvivalIntents.Eat, queue, now);
                return;
            case GoalKind.Build when dry && now >= nextShelter && !PlaySurvivalRest.HasSecureCover(player):
                if (choice.Shelter is { } site) StartEarthShelterSite(site, queue, now);
                else DispatchIntent(PlaySurvivalIntents.BuildShelter, queue, now);
                return;
            case GoalKind.Rest when dry && CanRestHere():
                DispatchIntent(PlaySurvivalIntents.Rest, queue, now);
                return;
            case GoalKind.Leave when dry && inShelter && CanLeaveHere(PlaySurvivalRest.HasEnclosure(player), night,
                PlaySurvivalRest.NeedsRecovery(player) || player.ComponentVitalStats.Temperature < 6f, now):
                DispatchIntent(PlaySurvivalIntents.LeaveShelter, queue, now);
                return;
            case GoalKind.Shore when !dry && IsOnWater(body):
                StartShoreRoute(now);
                return;
            case GoalKind.Wait:
                Dispatch("act_Wait", JsonSerializer.SerializeToElement(new { duration = choice.Seconds }), queue, now);
                return;
            default:
                FailGoal("goal_conditions_changed", now);
                return;
        }
    }

    private void FailGoal(string reason, double now)
    {
        Report(PlaySurvivalGoals.Tool, "failed", reason, now);
        FinishStep(false, reason, now);
    }

    private void StartShoreRoute(double now)
    {
        var body = player.ComponentBody;
        var facing = body.Matrix.Forward.XZ;
        var sweep = surroundings;
        var land = sweep?.Sectors.Select(sector => sweep.ShoreInDirection(body.Position, sector.Direction))
            .Where(point => point.HasValue).OrderBy(point => (point!.Value - body.Position).XZ.LengthSquared()).FirstOrDefault();
        float bearing = land is { } point && (point - body.Position).XZ.LengthSquared() > 0.04f
            ? MathUtils.RadToDeg(Vector2.Angle(facing, (point - body.Position).XZ)) : 0f;
        travel = new(player, owner, bearing, 12f, "dry_land", false, now, ObserveSurvival, ReadDryWindow, log,
            survivalMemory.VisitsTo, shoreTarget: land, permitLandLeg: PermitSurvivalLeg);
    }

    private void StartExplorationSection(double now)
    {
        var body = player.ComponentBody;
        var heading = exploration!.Heading;
        float bearing = MathUtils.RadToDeg(Vector2.Angle(body.Matrix.Forward.XZ, heading));
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        string seek = PlaySurvivalRest.NightCoverDue(calendar) ? "shelter" : "search";
        if (seek == "shelter")
        {
            var site = surroundings?.EarthInDirection(body.Position, heading);
            travel = new(player, owner, bearing, PlaySurvivalExploration.SectionDistance, seek, false, now,
                ObserveSurvival, ReadDryWindow, log, survivalMemory.VisitsTo, shelterTarget: site, permitLandLeg: PermitSurvivalLeg);
        }
        else
        {
            nextExplorationFoodRead = now;
            explorationFood = [];
            travel = new(player, owner, bearing, PlaySurvivalExploration.SectionDistance, seek, false, now,
                () => ObserveExplorationFood(), ReadDryWindow, log, survivalMemory.VisitsTo, permitLandLeg: PermitSurvivalLeg);
        }
    }

    private (JsonElement Observation, PlaySurvivalFood.Food[] Foods) ObserveExplorationFood()
    {
        if (IsOnWater(player.ComponentBody)) return ObserveSurvival();
        double now = player.Project.FindSubsystem<SubsystemTime>(true).GameTime;
        if (now >= nextExplorationFoodRead)
        {
            nextExplorationFoodRead = now + 1.2;
            explorationFood = ObserveSurvival().Foods.Where(food => PlaySurvivalFood.CanSeek(player, food)
                && PermitSurvivalLeg(food.Point, now)).ToArray();
        }
        return (default, explorationFood);
    }

    private void FinishExploration(PlaySurvivalTravel route, PlayDecisionQueue queue, double now)
    {
        var search = exploration!;
        search.RecordSection(route.Legs);
        var body = player.ComponentBody;
        var movement = (body.Position - route.Origin).XZ;
        if (movement.LengthSquared() < 9f && route.Reason != "food_within_reach")
        {
            if (refusedCourses.Count >= 6) refusedCourses.RemoveAt(0);
            refusedCourses.Add((route.Origin, search.Heading, now + 75));
        }
        log(new { kind = "play_survival_exploration_section", actor = Name, section = search.Sections,
            meters = movement.Length(), route.Status, route.Reason, gameTime = now });
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        bool night = PlaySurvivalRest.NightCoverDue(calendar);
        if (!night && PlaySurvivalSenses.OnDryGround(body) && ThreatInRange(body, false) is null)
        {
            if (route.Reason == "food_within_reach" || search.CanContinue(now) && movement.LengthSquared() >= 9f)
            {
                RefreshSurroundings(now);
                var food = FoodChoices(ObserveSurvival().Foods, now);
                if (food.Length > 0)
                {
                    // 搜索发现新食物后交回模型判断，不把“探索”擅自改为采集。
                    EndExploration(search, "succeeded", "observed_food_found", now);
                    return;
                }
                if (search.CanContinue(now) && movement.LengthSquared() >= 9f
                    && ReadCourses(now, search.Heading).Length > 0)
                {
                    search.Extend();
                    StartExplorationSection(now);
                    return;
                }
            }
        }
        EndExploration(search, route.Status!, route.Reason, now);
    }

    private void EndExploration(PlaySurvivalExploration search, string status, string? reason, double now)
    {
        exploration = null;
        var displacement = (player.ComponentBody.Position - search.Origin).XZ;
        if (displacement.Length() > 0.15f && status == "succeeded") Moves++;
        Report(PlayNavigationMenu.ScoutTool, status, reason, now, search.NativeLegs, displacement);
        FinishStep(status == "succeeded", reason, now);
    }

    private void RefreshSurroundings(double now)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        surroundings = PlaySurvivalPanorama.Capture(player, owner.Queries, panoramaRadius, now);
        if (surroundings is { } crops)
            foreach (var block in crops.Panorama.Blocks.Where(block => ownFarm.ContainsKey(block.Cell)))
                if (Terrain.ExtractContents(block.Value) == PumpkinBlock.Index)
                {
                    var previous = ownFarm[block.Cell];
                    ownFarm[block.Cell] = previous with { LastSize = BasePumpkinBlock.GetSize(Terrain.ExtractData(block.Value)),
                        LastDead = BasePumpkinBlock.GetIsDead(Terrain.ExtractData(block.Value)), LastSeen = now };
                }
        if (surroundings is { } sweep)
            log(new { kind = "play_survival_panorama", actor = Name, gameTime = now, sectors = sweep.Sectors.Length,
                sweep.Panorama.Radius, blocks = sweep.Panorama.Blocks.Count, animals = sweep.Panorama.Objects.Entities.Count,
                items = sweep.Panorama.Objects.Pickables.Count, durationMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds });
    }
}
