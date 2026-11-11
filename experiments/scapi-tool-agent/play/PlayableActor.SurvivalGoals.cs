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
    private sealed record SurvivalMenu(Vector3 Origin, double CapturedAt, bool NightCoverDue, Choice[] Choices);
    private sealed record SurvivalGoal(Choice Choice, string Source, double StartedAt);
    private SurvivalMenu? pendingSurvivalMenu;
    private SurvivalGoal? survivalGoal;
    private object? lastSurvivalGoal;
    private PlaySurvivalExploration? exploration;
    private readonly List<(Vector3 Origin, Vector2 Heading, double Until)> refusedCourses = new();
    private (string Task, double Until)? refusedSupply;
    private bool restPending;
    private bool needsLocalFallback;
    private double nextExplorationFoodRead;
    private PlaySurvivalFood.Food[] explorationFood = [];

    private SurvivalMenu ReadSurvivalMenu(double now, PlaySurvivalFood.Food[] nearby, string? sleepBlocker)
    {
        var body = player.ComponentBody;
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        var vitals = player.ComponentVitalStats;
        bool dry = PlaySurvivalSenses.OnDryGround(body);
        bool inShelter = earthSite?.Contains(body) == true;
        bool night = PlaySurvivalRest.NightCoverDue(calendar);
        bool cold = PlaySurvivalRest.ColdNeedsShelter(vitals.Temperature, now - (coldSince ?? now), player.ComponentFlu.HasFlu);
        bool recovering = PlaySurvivalRest.NeedsRecovery(player);
        var choices = new List<Choice>();
        if (!dry && IsOnWater(body))
        {
            var shore = surroundings?.ShoreInDirection(body.Position, body.Matrix.Forward.XZ);
            choices.Add(new("shore", GoalKind.Shore, new { observedLanding = shore.HasValue }));
            return new(body.Position, now, night, choices.ToArray());
        }

        bool cover = dry && (inShelter || night || recovering || cold || RestWindow()) && PlaySurvivalRest.HasEnclosure(player);
        if (dry && !inShelter)
        {
            if (!night || vitals.Food < 0.06f)
                choices.AddRange(FoodChoices(nearby, now));
            if (!cover && now >= nextShelter && (night || cold || recovering) && PlayEarthShelter.At(body) is not null)
                choices.Add(new("build", GoalKind.Build, "在当前软土地面挖掩体并封顶"));
            if (!night || !cover && !choices.Any(choice => choice.Activity == GoalKind.Build))
            {
                var courses = ReadCourses(now);
                choices.AddRange(courses.Select((course, i) => new Choice($"route_{i + 1}", GoalKind.Explore,
                    course.Snapshot(body.Matrix.Forward.XZ), Course: course)));
            }
            if (!night && now >= nextProduction && surroundings is { } sweep)
            {
                bool routine = !cold && !recovering && !NeedsDuskFood(calendar) && player.ComponentHealth.Health >= 0.6f && vitals.Food > 0.28f;
                var tasks = PlaySurvivalProduction.AvailableTasks(player, sweep.Panorama, placedStations, routine,
                    allowProjects: !cold && !recovering && ProjectVitalsReady(now), permitted: end => PermitSurvivalLeg(end, now));
                choices.AddRange(tasks.Where(task => refusedSupply is not { } refused || refused.Task != task || now >= refused.Until)
                    .Select(task => new Choice(task, GoalKind.Supply, new
                    {
                        goal = PlaySurvivalProduction.DescribeTask(task),
                        nextStage = PlaySurvivalProduction.NextStage(player, task, sweep.Panorama, placedStations,
                            end => PermitSurvivalLeg(end, now))
                    }, Supply: task)));
            }
        }
        if (dry && HasMeal() && (vitals.Food < 0.65f || inShelter && PlaySurvivalRest.NeedsFoodForHealing(player)))
            choices.Add(new("eat", GoalKind.Eat, "按原生营养、致病和同类食物累积选择背包里的一份"));
        if (cover && CanRestHere())
            choices.Add(new("rest", GoalKind.Rest, "在掩体里睡觉，太湿时先晾干"));
        if (inShelter && CanLeaveHere(cover, night, recovering || cold, now))
            choices.Add(new("leave", GoalKind.Leave, "检查坑口并回到地面"));
        if (choices.Count == 0)
            choices.Add(new("wait", GoalKind.Wait, new { reason = sleepBlocker ?? "no_supported_goal", seconds = 5 }, Seconds: 5));
        return new(body.Position, now, night, choices.ToArray());
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

    private bool CanRestHere() => RestWindow() && RestThreatClear && !player.ComponentSleep.IsSleeping
        && (player.ComponentSleep.CanSleep(out _) || PlaySurvivalRest.TiredEnough(player) && PlaySurvivalRest.WetnessBlocksSleep(player));

    private bool CanLeaveHere(bool cover, bool night, bool recovery, double now) => now >= nextExit
        && (!cover || restedInEarth && !RestWindow()
            || player.ComponentVitalStats.Food < (recovery || night ? 0.06f : 0.18f));

    private bool TryTakeKnownFood(IEnumerable<Choice> choices, PlayDecisionQueue queue, double now, string reason,
        bool urgent, Action? beforeTake = null)
    {
        foreach (var choice in choices)
        {
            if (choice.Food is not { } food) continue;
            var origin = player.ComponentBody.Position;
            float distance = (food.Point - origin).XZ.Length();
            var path = distance >= 5f ? PlanFoodPath(origin, food.Point, now) : null;
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
        survivalGoal = new(choice, source, now);
        log(new { kind = "play_survival_goal_started", actor = Name, choice = choice.Snapshot(), source, gameTime = now });
    }

    private void EndSurvivalGoal(string status, string? reason, double now)
    {
        if (survivalGoal is not { } current) return;
        survivalGoal = null;
        if (status == "failed" && current.Choice.Supply is { } supply)
            refusedSupply = (supply, now + 60);
        lastSurvivalGoal = new
        {
            activity = current.Choice.Activity.ToString().ToLowerInvariant(),
            target = current.Choice.Food?.Name ?? current.Choice.Supply, status, reason,
            duration = Math.Round(now - current.StartedAt, 1), actual = outcome
        };
        log(new { kind = "play_survival_goal_finished", actor = Name, goal = lastSurvivalGoal,
            current.Source, gameTime = now });
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
        bool inShelter = earthSite?.Contains(body) == true;
        switch (choice.Activity)
        {
            case GoalKind.Collect when dry && !inShelter && choice.Food is { } food
                && (!night || player.ComponentVitalStats.Food < 0.06f)
                && PlaySurvivalFood.CanSeek(player, food) && CanRetryFood(food, body.Position, now)
                && PermitSurvivalLeg(food.Point, now):
                StartFoodApproach(food with { InReach = false }, queue, now);
                return;
            case GoalKind.Explore when dry && !inShelter && choice.Course is { } course && PermitSurvivalLeg(course.Opening, now)
                && (!night || !PlaySurvivalRest.HasSecureCover(player)):
                exploration = new(body.Position, course, now);
                StartExplorationSection(now);
                return;
            case GoalKind.Supply when dry && !inShelter && !night && now >= nextProduction && choice.Supply is { } task:
                StartProduction(task, now);
                return;
            case GoalKind.Eat when dry:
                DispatchIntent(PlaySurvivalIntents.Eat, queue, now);
                return;
            case GoalKind.Build when dry && now >= nextShelter && !PlaySurvivalRest.HasSecureCover(player):
                DispatchIntent(PlaySurvivalIntents.BuildShelter, queue, now);
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
                if ((route.Reason == "food_within_reach" || food.Any(choice => choice.Food is { InReach: true })
                    || player.ComponentVitalStats.Food < 0.42f || NeedsDuskFood(calendar))
                    && TryTakeKnownFood(food, queue, now, "exploration_discovery", urgent: player.ComponentVitalStats.Food <= 0.24f,
                        beforeTake: () => EndExploration(search, "succeeded", "observed_food_found", now))) return;
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
        if (surroundings is { } sweep)
            log(new { kind = "play_survival_panorama", actor = Name, gameTime = now, sectors = sweep.Sectors.Length,
                sweep.Panorama.Radius, blocks = sweep.Panorama.Blocks.Count, animals = sweep.Panorama.Objects.Entities.Count,
                items = sweep.Panorama.Objects.Pickables.Count, durationMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds });
    }
}
