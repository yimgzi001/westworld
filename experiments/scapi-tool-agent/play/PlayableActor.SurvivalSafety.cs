#nullable enable

using Engine;
using Game;
using Game.Westworld;

internal sealed partial class PlayableActor
{
    private readonly PlayPredatorHearing hearing;
    private PlayThermalRecovery? thermalRecovery;
    private double nextHeat;
    private double listenUntil;
    private (PlaySurvivalExploration Search, PlaySurvivalGoals.Choice Choice, double Until)? pausedExploration;

    public void HearPredator(NativePredatorAcoustics.Signal signal)
    {
        if (!retired && hearing.Hear(signal) is { } cue)
            log(new { kind = "play_predator_heard", actor = Name, cue, gameTime = signal.GameTime });
    }

    private object[]? HearingSnapshot(double now) => hearing.Snapshot(now);

    private bool PauseForSurvivalSafety(PlayDecisionQueue queue, string reason, double now)
    {
        if (reason == "heard_predator")
        {
            listenUntil = now + 6.2;
        }
        if (reason is "heard_predator" or "need_meal" && exploration is { } search && survivalGoal is { } work)
            pausedExploration = (search, work.Choice, now + 24);
        if (foodApproach is { Interruptions: < 3 } food && now < food.ResumeUntil)
        {
            pausedFood = (food.Target, food.ResumeUntil, food.Interruptions + 1);
            log(new { kind = "play_food_objective_paused", actor = Name, target = food.Target.Point.ToString(), reason, gameTime = now });
        }
        if (production is { } supply && PlaySurvivalProduction.IsProject(supply.Task)) PauseSupply(supply, reason, now);
        HoldSurvivalPlan(reason, now);
        if (queue.IsPending(Id))
            log(new { kind = "play_survival_request_interrupted", actor = Name, reason, gameTime = now });
        queue.Cancel(Id);
        pendingSurvivalMenu = null;
        outcome = null;
        EndSurvivalGoal("paused", reason, now);
        if (Stop()) return true;
        Retire(queue, "cannot_interrupt_survival_action");
        return false;
    }

    private bool CheckSurvivalHandoff(PlayDecisionQueue queue, double now)
    {
        var body = player.ComponentBody;
        if ((queue.IsPending(Id) || exploration is not null || production is not null)
            && player.ComponentVitalStats.Food <= 0.06f && HasMeal())
        {
            PauseForSurvivalSafety(queue, "need_meal", now);
            return true;
        }
        var endpoint = travel?.ActiveLandEndpoint ?? production?.ActiveLandEndpoint;
        if (travel?.Evading != true && endpoint is { } end && !hearing.AllowsRoute(body.Position, end, now))
        {
            log(new { kind = "play_predator_heard_route_paused", actor = Name, gameTime = now });
            PauseForSurvivalSafety(queue, "heard_predator", now);
            nextDecision = now + 0.8;
            return true;
        }
        // 已在低温观察中做出的决定应有机会执行。只在新出现冷险或体温进一步明显恶化时抢占。
        var coldContext = queue.IsPending(Id) && pendingSurvivalMenu is { } pending
            ? (pending.ColdRisk, pending.Temperature)
            : exploration is not null && survivalGoal is { } active ? (active.ColdRisk, active.Temperature) : ((bool, float)?)null;
        if (coldContext is { } before && earthSite?.Contains(body) != true && ColdRisk(now)
            && (!before.Item1 || player.ComponentVitalStats.Temperature < 3.5f
                && player.ComponentVitalStats.Temperature <= before.Item2 - 1f))
        {
            PauseForSurvivalSafety(queue, "cold_weather", now);
            return true;
        }
        bool daytimeWork = exploration is not null && survivalGoal is { NightDue: false } && travel?.HeadingForShelter == false
            || pendingSurvivalMenu is { NightCoverDue: false } && !PlaySurvivalRest.HasSecureCover(player);
        if (daytimeWork && PlaySurvivalRest.NightCoverDue(player.Project.FindSubsystem<SubsystemTimeOfDay>(true)))
        {
            PauseForSurvivalSafety(queue, "return_before_dark", now);
            return true;
        }
        return false;
    }

    private bool ColdRisk(double now) => PlaySurvivalRest.ColdNeedsShelter(player.ComponentVitalStats.Temperature,
        now - (coldSince ?? now), player.ComponentFlu.HasFlu);

    private bool TryWarmOrPrepare(double now)
    {
        var body = player.ComponentBody;
        if (now < nextHeat || earthSite?.Contains(body) == true || !PlaySurvivalSenses.OnDryGround(body)) return false;
        var calendar = player.Project.FindSubsystem<SubsystemTimeOfDay>(true);
        if (PlaySurvivalRest.NightCoverDue(calendar)) return false;
        var vitals = player.ComponentVitalStats;
        bool cold = vitals.Temperature < 6.5f || vitals.Wetness >= 0.4f && vitals.Temperature < 8f;
        if (cold && placedStations.TryGetValue(CampfireBlock.Index, out var knownFire)
            && PlayThermalRecovery.FireWithinReach(player, knownFire) && vitals.m_targetTemperature > vitals.Temperature + 0.4f)
        {
            BeginSurvivalGoal(new("warm", PlaySurvivalGoals.Kind.Supply, "靠近已确认的原生营火回暖",
                Supply: PlaySurvivalProduction.WarmthProject), "known_campfire", now);
            StartThermalRecovery(knownFire, now);
            return true;
        }
        if (now < nextProduction || vitals.Food <= 0.16f && vitals.Temperature >= 3.8f) return false;
        int stock = PlaySurvivalProduction.Stock(player, PlaySurvivalProduction.Fire);
        bool reserve = !cold && vitals.Food > 0.36f && !NeedsDuskFood(calendar)
            && (vitals.EnvironmentTemperature < 10f || PlaySurvivalCold.NearSnow(body.Position, FrozenAround(body.Position, now)));
        if (!cold && !reserve) return false;
        if (cold)
        {
            var known = surroundings is { } recent && now - recent.CompletedAt <= 20 ? recent.Panorama : owner.Queries.ReadPanorama(24f);
            if (!PlaySurvivalProduction.CanAdvanceProject(player, PlaySurvivalProduction.WarmthProject, known,
                placedStations, end => PermitSurvivalLeg(end, now))) return false;
            StartWarmWork(PlaySurvivalProduction.WarmthProject, "body_cold", now);
            return true;
        }
        if (stock != 0 || !PlaySurvivalProduction.CanCraftFireFromStock(player)) return false;
        StartWarmWork(PlaySurvivalProduction.Fire, "campfire_reserve", now);
        return true;
    }

    private void StartWarmWork(string task, string reason, double now)
    {
        BeginSurvivalGoal(new(task, PlaySurvivalGoals.Kind.Supply, PlaySurvivalProduction.DescribeTask(task), Supply: task), reason, now);
        StartProduction(task, now);
    }

    private bool ContinueWarmWork(PlaySurvivalProduction supply, string status, double now)
    {
        if (status != "succeeded")
        {
            if (supply.Task is PlaySurvivalProduction.Fire or PlaySurvivalProduction.Warm or PlaySurvivalProduction.WarmthProject)
                nextHeat = now + 60;
            return false;
        }
        if (supply.Task is not (PlaySurvivalProduction.Warm or PlaySurvivalProduction.WarmthProject)
            || !placedStations.TryGetValue(CampfireBlock.Index, out var fire)) return false;
        StartThermalRecovery(fire, now);
        return true;
    }

    private void StartThermalRecovery(Point3 fire, double now)
    {
        thermalRecovery = new(player, fire, now);
        log(new { kind = "play_survival_warming", actor = Name, temperature = thermalRecovery.Before, gameTime = now });
    }

    private void UpdateThermalRecovery(PlayThermalRecovery recovery, double now)
    {
        var state = recovery.Update(now);
        if (state == PlayThermalRecovery.State.Warming) return;
        thermalRecovery = null;
        bool warmed = state == PlayThermalRecovery.State.Warmed;
        nextHeat = now + (warmed ? 25 : 90);
        string reason = state switch
        {
            PlayThermalRecovery.State.Warmed => "native_body_recovery_confirmed",
            PlayThermalRecovery.State.FireUnavailable => "campfire_no_longer_usable",
            PlayThermalRecovery.State.UnsafeHeat => "native_fire_contact_or_burning",
            _ => "body_did_not_warm"
        };
        log(new { kind = "play_survival_warming_result", actor = Name, reason, before = recovery.Before,
            after = player.ComponentVitalStats.Temperature, target = player.ComponentVitalStats.m_targetTemperature, gameTime = now });
        Report(PlaySurvivalProduction.Warm, warmed ? "succeeded" : "failed", reason, now);
        FinishStep(warmed, reason, now);
    }
}
