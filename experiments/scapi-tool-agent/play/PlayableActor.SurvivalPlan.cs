#nullable enable

using System.Text.Json;
using Westworld.Decisions.Local;

internal sealed partial class PlayableActor
{
    private sealed class SurvivalPlan(string purpose, SurvivalMenu menu, double started)
    {
        public string Purpose { get; } = purpose;
        public SurvivalMenu Menu { get; } = menu;
        public double Started { get; } = started;
        public List<string> Completed { get; } = [];
        public PlaySurvivalGoals.Choice? Current { get; set; }
    }

    private SurvivalPlan? activeSurvivalPlan;
    private (string Purpose, SurvivalMenu Menu, PlaySurvivalGoals.Choice[] Remaining, string Reason, double Time)? heldSurvivalPlan;
    private object? lastSurvivalPlan;

    private void StartSurvivalPlan(ToolDecisionPlan plan, SurvivalMenu menu, PlayDecisionQueue queue, double now)
    {
        if (now - menu.CapturedAt > 30 || (menu.Origin - player.ComponentBody.Position).LengthSquared() > 2.25f)
        {
            FailGoal("decision_observation_expired", now);
            return;
        }
        activeSurvivalPlan = new(plan.Goal, menu, now);
        var chosenCamp = plan.Steps.Select(step => menu.Choices.Single(choice => choice.Id
            == step.Arguments.GetProperty("target").GetString()).Camp).FirstOrDefault(site => site is not null);
        if (chosenCamp is not null) campSite = chosenCamp;
        if (!plan.Steps.Any(step => step.Arguments.GetProperty("target").GetString() == "resume")) heldSurvivalPlan = null;
        foreach (var step in plan.Steps) planned.Enqueue(step);
        log(new { kind = "play_survival_plan_started", actor = Name, purpose = plan.Goal,
            actions = plan.Steps.Select(step => menu.Choices.Single(choice => choice.Id
                == step.Arguments.GetProperty("target").GetString()).Snapshot()), gameTime = now });
        if (planned.TryDequeue(out var first)) DispatchStep(first, queue, now);
    }

    private bool ContinueSurvivalPlan(ToolDecisionStep step, PlayDecisionQueue queue, double now)
    {
        if (activeSurvivalPlan is not { } plan) return false;
        string? target = step.Arguments.GetProperty("target").GetString();
        var choice = plan.Menu.Choices.SingleOrDefault(option => option.Id == target);
        if (choice is null || now - plan.Started > 240)
        {
            FailGoal(choice is null ? "plan_target_not_bound" : "plan_needs_fresh_assessment", now);
            return true;
        }
        plan.Current = choice;
        log(new { kind = "play_plan_step", actor = Name, tool = step.Tool, target, remaining = planned.Count, gameTime = now });
        // 编号仍指向模型当时选定的对象。每个动作按当前库存、目标身份、路线和天气重新准入。
        var fresh = plan.Menu with { Origin = player.ComponentBody.Position, CapturedAt = now,
            ColdRisk = ColdRisk(now), Temperature = player.ComponentVitalStats.Temperature };
        DispatchSurvivalGoal(choice, fresh, queue, now, "model_plan");
        return true;
    }

    private void RecordSurvivalPlanStep(bool succeeded, string? reason, double now)
    {
        if (activeSurvivalPlan is not { } plan) return;
        if (succeeded && plan.Current is { } current) plan.Completed.Add(current.Id);
        if (succeeded && planned.Count > 0) return;
        lastSurvivalPlan = new { purpose = plan.Purpose, status = succeeded ? "succeeded" : "needs_revision",
            completed = plan.Completed.ToArray(), stoppedAt = succeeded ? null : plan.Current?.Id, reason,
            actual = outcome, duration = Math.Round(now - plan.Started, 1) };
        log(new { kind = "play_survival_plan_finished", actor = Name, plan = lastSurvivalPlan, gameTime = now });
        activeSurvivalPlan = null;
    }

    private void HoldSurvivalPlan(string reason, double now)
    {
        if (activeSurvivalPlan is not { } plan) return;
        var rest = planned.Select(step => step.Arguments.GetProperty("target").GetString())
            .Select(id => plan.Menu.Choices.Single(choice => choice.Id == id));
        var remaining = (plan.Current is { } current ? new[] { current } : []).Concat(rest).ToArray();
        if (remaining.Length > 0) heldSurvivalPlan = (plan.Purpose, plan.Menu, remaining, reason, now);
        lastSurvivalPlan = new { purpose = plan.Purpose, status = "interrupted", reason, completed = plan.Completed.ToArray(),
            remaining = remaining.Select(choice => choice.Snapshot()).ToArray() };
        log(new { kind = "play_survival_plan_paused", actor = Name, plan = lastSurvivalPlan, gameTime = now });
        activeSurvivalPlan = null;
    }

    private bool TryResumeChosenPlan(PlayDecisionQueue queue, double now)
    {
        // 短暂声音警戒和保命进食后继续执行模型已有安排。新的冷险或天黑交回模型重新评估。
        if (heldSurvivalPlan is not { Reason: "heard_predator" or "need_meal" } held
            || now - held.Time > 30 || now < listenUntil || player.ComponentVitalStats.Food <= 0.06f
            || ColdRisk(now) || PlaySurvivalRest.NightCoverDue(player.Project.FindSubsystem<Game.SubsystemTimeOfDay>(true))) return false;
        RestoreSurvivalPlan(queue, now);
        return true;
    }

    private void RestoreSurvivalPlan(PlayDecisionQueue queue, double now)
    {
        if (heldSurvivalPlan is not { } held) { FailGoal("saved_plan_unavailable", now); return; }
        heldSurvivalPlan = null;
        activeSurvivalPlan = new(held.Purpose, held.Menu, now);
        foreach (var choice in held.Remaining)
            planned.Enqueue(new(PlaySurvivalGoals.Tool, JsonSerializer.SerializeToElement(new { target = choice.Id })));
        log(new { kind = "play_survival_plan_resumed", actor = Name, purpose = held.Purpose, held.Reason,
            remaining = held.Remaining.Length, gameTime = now });
        if (planned.TryDequeue(out var first)) DispatchStep(first, queue, now);
    }
}
