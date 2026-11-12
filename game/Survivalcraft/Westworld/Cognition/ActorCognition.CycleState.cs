using System.Globalization;
using System.Text.Json;
using Engine;

namespace Game.Westworld {
    public sealed partial class ActorCognition {
        public const string DeliberativeProtocol = "deliberative-v1";
        internal sealed record CycleBelief(string Id, long Revision, string Text, string Source, double At);
        internal sealed record CycleGoal(string Id, long Revision, string Purpose, string Status, string Why, string Source, double At);
        internal sealed record CycleConcern(string Kind, string Description, string Source);
        internal sealed record CycleTechnical(string Id, string Diagnostic, double At, int AffectedCount);
        internal sealed record CycleEpisode(string Id, string GoalId, string Method, string Kind, string Outcome,
            double At, double Elapsed, int Count, double Progress);
        internal sealed record CycleMetrics(double Observations, double Inventory, double Food, double Interactions,
            float X, float Y, float Z) {
            public float Health { get; init; }
            public float Air { get; init; }
        }
        internal sealed record CycleMethod(string Signature, long Revision, string Strategy, string Metric, int? ItemValue,
            double WindowSeconds, double StartedAt, double WindowAt, CycleMetrics Baseline, int ZeroWindows);
        internal sealed record CycleReceipt(string DecisionId, string[] AcceptedBeliefs, string[] RejectedBeliefs,
            string Deliberation, string Action, string GoalId, long AuthorizationRevision, double At);
        internal sealed record CycleReadSet(long GoalRevision, string GoalId, long AuthorizationRevision,
            Guid TaskId, float Health, CognitiveBody Body, ObservationPose Pose, string[] Evidence,
            CycleBelief[] Beliefs, CycleGoal[] Goals, CycleMetrics Metrics, int ProgressSerial, int SituationSerial) {
            public int ActivityAttempts { get; init; }
            public SomaticView Somatic { get; init; }
            public int VisualSerial { get; init; }
            public string[] VisualEventIds { get; init; } = [];
        }

        internal sealed class CycleState {
            public string Protocol { get; init; } = DeliberativeProtocol;
            public List<CycleBelief> Beliefs { get; init; } = [];
            public List<CycleGoal> Goals { get; init; } = [];
            public List<CycleGoal> ArchivedGoals { get; init; } = [];
            public List<CycleEpisode> Episodes { get; init; } = [];
            public List<CycleTechnical> TechnicalDiagnostics { get; init; } = [];
            public List<CycleReceipt> Receipts { get; init; } = [];
            public List<string> ObservedAreas { get; init; } = [];
            public List<string> SeenMessages { get; init; } = [];
            public List<CycleInjury> Injuries { get; init; } = [];
            public List<string> VisitedPlaces { get; init; } = [];
            public CycleActivity Activity { get; set; }
            public SomaticView ReviewedSomatic { get; set; }
            public string VisibleFingerprint { get; set; }
            public double NextVisualPollAt { get; set; }
            public int VisualSerial { get; set; }
            public int ReviewedVisualSerial { get; set; }
            public List<CycleVisualEvent> VisualEvents { get; init; } = [];
            public int DroppedVisualEvents { get; set; }
            public string ActiveGoal { get; set; }
            public CycleMethod Method { get; set; }
            public long AuthorizationRevision { get; set; }
            public Guid AuthorizedTask { get; set; }
            public Guid LastClosedTask { get; set; }
            public Guid LastTechnicalTask { get; set; }
            public string IntentionStatus { get; set; } = "none";
            public float? LastHealth { get; set; }
            public int InteractionCount { get; set; }
            public int ProgressSerial { get; set; }
            public int ReviewedProgressSerial { get; set; }
            public int SituationSerial { get; set; }
            public int ReviewedSituationSerial { get; set; }
            public int ProtocolErrors { get; set; }
            public string ProtocolIssue { get; set; }
        }

        CycleState m_cycle;
        internal bool UsesDeliberativeCycle => m_cycle != null;
        internal bool HasCognitionProtocolFault => m_cycle?.ProtocolErrors >= 2;
        public string ReadCycleStateJson() => JsonSerializer.Serialize(m_cycle, m_json);

        CycleGoal ActiveCycleGoal => m_cycle.Goals.FirstOrDefault(goal => goal.Id == m_cycle.ActiveGoal && goal.Status == "active");

        CycleMetrics SampleCycleMetrics(SelfView self, int? itemValue = null) {
            int inventory = m_tools.CognitiveInventory()?.Slots.Where(slot => itemValue == null || slot.Value == itemValue)
                .Sum(slot => slot.Count) ?? 0;
            return new CycleMetrics(m_cycle.VisitedPlaces.Count, inventory, self.Food ?? 0, m_cycle.InteractionCount,
                self.Position.X, self.Position.Y, self.Position.Z) { Health = self.Health, Air = m_tools.CognitiveSomatic?.Air ?? 1 };
        }

        static double CycleProgress(string metric, CycleMetrics baseline, CycleMetrics actual) => metric switch {
            "observation" => actual.Observations - baseline.Observations,
            "inventory" => actual.Inventory - baseline.Inventory,
            "food" => actual.Food - baseline.Food,
            "interaction" => actual.Interactions - baseline.Interactions,
            "position" => Math.Sqrt(Math.Pow(actual.X - baseline.X, 2) + Math.Pow(actual.Y - baseline.Y, 2) + Math.Pow(actual.Z - baseline.Z, 2)),
            _ => 0
        };

        void ObserveCycle(SelfView self) {
            if (m_cycle.LastHealth is float previous && self.Health < previous - .001f) m_cycle.SituationSerial++;
            m_cycle.LastHealth = self.Health;
            foreach (CognitiveMessage message in m_inbox.Where(message => message.Ready)) {
                if (m_cycle.SeenMessages.Contains(message.Id)) continue;
                m_cycle.SeenMessages.Add(message.Id);
                m_cycle.InteractionCount++;
                if (m_cycle.SeenMessages.Count > 128) m_cycle.SeenMessages.RemoveAt(0);
            }
            // These are only places and directions actually observed by this actor, never a world-resource map.
            string area = FormattableString.Invariant($"{(int)MathF.Floor(self.Position.X / 4)}:{(int)MathF.Floor(self.Position.Z / 4)}:{(int)MathF.Floor((MathF.Atan2(self.Facing.X, self.Facing.Z) + MathF.PI) / (MathF.PI / 4))}");
            if (m_cycle.ObservedAreas.Count < 2048 && !m_cycle.ObservedAreas.Contains(area)) m_cycle.ObservedAreas.Add(area);
            ObserveActivity(self, false);
            if (m_cycle.IntentionStatus != "executing" || m_cycle.Method is not { } method) return;
            if (!m_tools.CognitiveTaskActive) {
                if (m_cycle.LastClosedTask != m_cycle.AuthorizedTask) {
                    ObserveActivity(self, m_cycle.LastTechnicalTask != m_cycle.AuthorizedTask);
                    CycleMetrics final = SampleCycleMetrics(self, method.ItemValue);
                    double amount = CycleProgress(method.Metric, method.Baseline, final);
                    AddCycleEpisode(new CycleEpisode("closed:" + m_cycle.AuthorizedTask.ToString("N"), m_cycle.ActiveGoal,
                        method.Signature, "taskClosed", "taskEnded", Now, Math.Max(0, Now - method.WindowAt), 1, amount));
                    // Completing a real attempt is also a progress boundary, including attempts shorter than the time window.
                    // Idle time is never an attempt. Host failures cannot establish that a world method is ineffective.
                    int closedZero = amount > (method.Metric == "position" ? .5 : 1e-4) ? 0
                        : m_cycle.LastTechnicalTask == m_cycle.AuthorizedTask ? method.ZeroWindows : method.ZeroWindows + 1;
                    m_cycle.Method = method with { ZeroWindows = closedZero, WindowAt = Now, Baseline = final };
                    if (closedZero >= 2) m_cycle.ProgressSerial++;
                    m_cycle.LastClosedTask = m_cycle.AuthorizedTask;
                }
                m_cycle.IntentionStatus = "awaitingTask";
                return;
            }
            if (Now < method.WindowAt + method.WindowSeconds) return;
            CycleMetrics actual = SampleCycleMetrics(self, method.ItemValue);
            double progress = CycleProgress(method.Metric, method.Baseline, actual);
            int zero = progress > (method.Metric == "position" ? .5 : 1e-4) ? 0 : method.ZeroWindows + 1;
            AddCycleEpisode(new CycleEpisode($"window:{method.Signature}:{method.WindowAt.ToString("R", CultureInfo.InvariantCulture)}",
                m_cycle.ActiveGoal, method.Signature, "progressWindow", zero == 0 ? "progress" : "noProgress", Now,
                Now - method.WindowAt, 1, progress));
            m_cycle.Method = method with { WindowAt = Now, Baseline = actual, ZeroWindows = zero };
            if (zero >= 2) m_cycle.ProgressSerial++;
        }

        void AddCycleEpisode(CycleEpisode episode) {
            if (m_cycle.Episodes.Any(old => old.Id == episode.Id)) return;
            if (m_cycle.Episodes.Count == 16) m_cycle.Episodes.RemoveAt(0);
            m_cycle.Episodes.Add(episode);
        }

        void RecordCycleStep(Guid taskId, ActorPlanStep plan, ActorTaskStepView result) {
            // A reply frozen before the guard fired cannot revoke its now-active authorized branch.
            if (result.InterruptedBy == "body_worsened") m_cycle.AuthorizationRevision++;
            if (m_cycle.Method is not { } method) return;
            CognitiveFeedbackClassification classification = CognitiveFeedbackPolicy.Classify(result.AtomicFailure, result.AffectedCount);
            if (classification.Technical) {
                m_cycle.LastTechnicalTask = taskId;
                string diagnosticId = $"technical:{taskId:N}:{result.Index}:{result.Attempts}";
                if (!m_cycle.TechnicalDiagnostics.Any(item => item.Id == diagnosticId)) {
                    if (m_cycle.TechnicalDiagnostics.Count == 16) m_cycle.TechnicalDiagnostics.RemoveAt(0);
                    m_cycle.TechnicalDiagnostics.Add(new(diagnosticId, result.AtomicFailure.ToString(), Now, classification.PhysicalEffects));
                    if (classification.PhysicalEffects > 0) AddCycleEpisode(new CycleEpisode("effect:" + diagnosticId, m_cycle.ActiveGoal,
                        method.Signature, "partialEffect", "effectRecorded", Now, Math.Max(0, Now - method.StartedAt), 1, classification.PhysicalEffects));
                }
                return;
            }
            AddCycleEpisode(new CycleEpisode($"task:{taskId:N}:{result.Index}:{result.Attempts}", m_cycle.ActiveGoal,
                method.Signature, "actionResult", result.Status == ActorTaskStatus.Succeeded ? "succeeded" : result.AtomicFailure.ToString(),
                Now, Math.Max(0, Now - method.StartedAt), 1, result.AffectedCount));
            if (result.Status is ActorTaskStatus.Failed or ActorTaskStatus.Cancelled) m_cycle.ProgressSerial++;
        }

        string PrepareCycleTurn(bool checkpointable) {
            if (HasCognitionProtocolFault) return null;
            SampleSituation();
            SelfView self = m_tools.CognitiveBody;
            if (!CanThink(self)) return null;
            if (m_pending != null) return m_pending.Input;
            if (Now < m_nextTurnAt) return null;
            CognitiveBody body = CognitiveBody.From(self);
            body = body with { Condition = self.Health < .3f ? "critical" : self.Health < .99f ? "hurt" : "well" };
            ActorTaskView task = AtCognitiveTime(m_tools.CognitiveTask);
            ActorTaskView outcome = LatestOutcome();
            List<string> reasons = [];
            SomaticView somatic = m_tools.CognitiveSomatic;
            SomaticView reviewed = m_cycle.ReviewedSomatic;
            if (somatic != null && (somatic.Air < (reviewed?.Air ?? 1) - .15f
                || somatic.Burning != (reviewed?.Burning ?? false)
                || (somatic.Immersion > .5f) != ((reviewed?.Immersion ?? 0) > .5f))) reasons.Add("bodyRiskChanged");
            if (m_version == 0) reasons.Add("initial");
            if (m_body != null && body != m_body) reasons.Add("bodyChanged");
            if (m_cycle.SituationSerial != m_cycle.ReviewedSituationSerial) reasons.Add("newInjury");
            if (m_cycle.ProgressSerial != m_cycle.ReviewedProgressSerial) reasons.Add("methodReview");
            if (m_cycle.VisualSerial != m_cycle.ReviewedVisualSerial || m_cycle.VisualEvents.Any(item => !item.Considered)) reasons.Add("perceptionChanged");
            if (m_cycle.Activity is { } activity && activity.UnproductiveAttempts >= 2
                && activity.UnproductiveAttempts != activity.LastReviewedAttempts) reasons.Add("stalledActivity");
            if (Now >= m_reviewAt) reasons.Add("review");
            if (outcome != null && outcome.TaskId != m_seenTask) reasons.Add("taskEnded");
            if (m_inbox.Any(message => message.Ready && !message.Considered)) reasons.Add("social");
            if (reasons.Count == 0) return null;
            CognitiveContext context = ActorCognitiveContext.Assemble(m_tools, m_version, Now, reasons.ToArray(), m_profile,
                m_goal, body, task, outcome, m_inbox, m_commitments, m_memory, m_recentProcess);
            context = context with { Actions = m_tools.CognitiveActions(checkpointable: true) };
            List<CycleConcern> concerns = [];
            if (body.Hunger != "fed") concerns.Add(new("body", "Food reserve feels low; consider current and future needs.", "body"));
            if (body.Condition != "well" || reasons.Contains("newInjury")) concerns.Add(new("body", "You are hurt or have just felt new injury; the cause is not automatically known.", "body"));
            if (somatic?.Air < .4f || somatic?.Burning == true) concerns.Add(new("body", "Your current breathing or heat sensations are under strain; compare them with recent injury and your current method.", "body"));
            if (m_cycle.Injuries.Any(injury => injury.At >= Now - 30)) concerns.Add(new("body",
                "Compare recent harm with what the current approach was expected to improve; progress and bodily cost can occur together. The cause of unexplained harm remains uncertain.", "body"));
            if (m_cycle.Method?.ZeroWindows >= 2) concerns.Add(new("progress", "The current method has repeated windows without observable progress.", "method"));
            if (m_cycle.Activity?.UnproductiveAttempts >= 2) concerns.Add(new("progress",
                "Changing methods has not produced new places, inventory, food reserve or received interaction. Review the broader aim and evidence, not only the last successful step.", "method"));
            CycleVisualEvent[] visualEvents = m_cycle.VisualEvents.OrderBy(item => item.Considered)
                .ThenByDescending(item => item.Salient).ThenByDescending(item => item.Sequence).Take(6).ToArray();
            string[] evidence = new[] { "body", "observation", "method" }.Concat(visualEvents.Select(item => item.Id))
                .Concat(context.Messages.Select(message => message.Id)).Concat(m_cycle.Episodes.Select(episode => episode.Id))
                .Concat(m_cycle.Beliefs.Select(belief => belief.Id)).Distinct().ToArray();
            CycleReadSet read = new(ActiveCycleGoal?.Revision ?? 0, ActiveCycleGoal?.Id, m_cycle.AuthorizationRevision,
                m_tools.CognitiveTask.TaskId, self.Health, body, new ObservationPose(self), evidence,
                m_cycle.Beliefs.ToArray(), m_cycle.Goals.ToArray(), SampleCycleMetrics(self, m_cycle.Method?.ItemValue),
                m_cycle.ProgressSerial, m_cycle.SituationSerial) {
                    ActivityAttempts = m_cycle.Activity?.UnproductiveAttempts ?? 0, Somatic = somatic, VisualSerial = m_cycle.VisualSerial,
                    VisualEventIds = visualEvents.Select(item => item.Id).ToArray()
                };
            string input = JsonSerializer.Serialize(new { context.Id, context.Version, context.GameTime, context.Reasons,
                context.Profile, context.Body, context.Observation, context.Inventory, context.Environment, context.Messages,
                context.Actions, context.Task, context.LastOutcome, context.RecentProcess,
                cognitionCycle = new { protocol = DeliberativeProtocol, concerns, beliefs = m_cycle.Beliefs,
                    goals = m_cycle.Goals, activeGoal = m_cycle.ActiveGoal, method = m_cycle.Method,
                    archivedGoalCount = m_cycle.ArchivedGoals.Count,
                    methodEpisodes = m_cycle.Episodes, receipts = m_cycle.Receipts.TakeLast(4),
                    technicalDiagnostics = m_cycle.TechnicalDiagnostics.TakeLast(4),
                    intentionStatus = m_cycle.IntentionStatus, authorizationRevision = m_cycle.AuthorizationRevision,
                    taskId = m_tools.CognitiveTaskActive ? m_tools.CognitiveTask.TaskId.ToString("N") : null,
                    supportsTaskSuspend = false, evidence, observedAreaCount = m_cycle.VisitedPlaces.Count,
                    repeatWithoutProgress = m_cycle.Method?.ZeroWindows >= 2, protocolIssue = m_cycle.ProtocolIssue,
                    activity = m_cycle.Activity, secondsWithoutProgress = Math.Max(0, Now - (m_cycle.Activity?.LastProgressAt ?? Now)),
                    injuries = m_cycle.Injuries.TakeLast(8),
                    bodyTrajectory = new { WindowSeconds = 30, InjuryLoss = m_cycle.Injuries.Where(injury => injury.At >= Now - 30).Sum(injury => injury.Loss) },
                    methodAssessment = AssessCurrentMethod(self), spatialOptions = SpatialOptions(context.Observation, self),
                    visualEvents, droppedVisualEvents = m_cycle.DroppedVisualEvents }
            }, m_json);
            m_pending = new Pending(context, input, m_droppedMessages) { Cycle = read };
            return input;
        }

        static bool ValidCycleState(CycleState state) => state == null || state.Protocol == DeliberativeProtocol
            && state.Beliefs is { Count: <= 32 } && state.Goals is { Count: <= 8 }
            && state.ArchivedGoals is { Count: <= 64 } && state.TechnicalDiagnostics is { Count: <= 16 }
            && state.Episodes is { Count: <= 16 } && state.Receipts is { Count: <= 64 }
            && state.ObservedAreas is { Count: <= 2048 } && state.SeenMessages is { Count: <= 128 }
            && state.Injuries is { Count: <= 16 } && state.VisitedPlaces is { Count: <= 2048 }
            && state.Injuries.All(item => item != null && double.IsFinite(item.At) && item.At >= 0
                && float.IsFinite(item.Loss) && item.Loss is > 0 and <= 1)
            && (state.Activity == null || state.Activity.Baseline != null && double.IsFinite(state.Activity.LastProgressAt)
                && state.Activity.LastProgressAt >= 0 && state.Activity.UnproductiveAttempts >= 0
                && state.Activity.Items is { Count: <= 96 } && state.Activity.Items.All(item => item.Key > 0 && item.Value > 0)
                && state.Activity.Health is >= 0 and <= 1 && state.Activity.Air is >= 0 and <= 1)
            && state.AuthorizationRevision >= 0
            && state.ProtocolErrors is >= 0 and <= 2 && state.ProtocolIssue is not { Length: > 256 }
            && state.IntentionStatus is "none" or "awaitingTask" or "executing"
            && state.ProgressSerial >= state.ReviewedProgressSerial && state.ReviewedProgressSerial >= 0
            && state.SituationSerial >= state.ReviewedSituationSerial && state.ReviewedSituationSerial >= 0
            && state.VisualSerial >= state.ReviewedVisualSerial && state.ReviewedVisualSerial >= 0
            && state.VisualEvents is { Count: <= 16 } && state.DroppedVisualEvents >= 0
            && state.VisualEvents.All(item => item != null && item.Contacts is { Length: <= 16 }
                && double.IsFinite(item.At) && item.At >= 0 && item.Sequence > 0 && item.Sequence <= state.VisualSerial
                && item.Contacts.All(contact => contact != null && ValidText(contact.Reference, 64)
                    && float.IsFinite(contact.X + contact.Y + contact.Z + contact.Distance + contact.ClosingSpeed)))
            && double.IsFinite(state.NextVisualPollAt) && state.NextVisualPollAt >= 0
            && state.InteractionCount >= 0 && (state.LastHealth == null || state.LastHealth is >= 0 and <= 1)
            && state.Beliefs.All(item => item != null && ValidText(item.Id, 64) && item.Revision > 0 && ValidText(item.Text, 256) && ValidText(item.Source, 128))
            && state.Goals.All(item => item != null && ValidText(item.Id, 64) && item.Revision > 0 && ValidText(item.Purpose, 256)
                && ValidText(item.Source, 128) && ValidText(item.Why, 320) && double.IsFinite(item.At) && item.At >= 0
                && item.Status is "active" or "suspended" or "abandoned" or "achieved")
            && state.Episodes.All(item => item != null && ValidText(item.Id, 160) && double.IsFinite(item.Progress)
                && double.IsFinite(item.At) && item.At >= 0 && double.IsFinite(item.Elapsed) && item.Elapsed >= 0 && item.Count > 0)
            && state.Receipts.All(item => item != null && Guid.TryParseExact(item.DecisionId, "N", out _)
                && item.AcceptedBeliefs is { Length: <= 8 } && item.RejectedBeliefs is { Length: <= 8 }
                && ValidText(item.Deliberation, 128) && ValidText(item.Action, 128) && item.AuthorizationRevision >= 0)
            && state.Receipts.Select(item => item.DecisionId).Distinct().Count() == state.Receipts.Count
            && state.Beliefs.Select(item => item.Id).Distinct().Count() == state.Beliefs.Count
            && state.Goals.Select(item => item.Id).Distinct().Count() == state.Goals.Count
            && state.ArchivedGoals.All(item => item != null && item.Status is "abandoned" or "achieved")
            && state.TechnicalDiagnostics.All(item => item != null && ValidText(item.Id, 160) && ValidText(item.Diagnostic, 128)
                && double.IsFinite(item.At) && item.At >= 0 && item.AffectedCount >= 0)
            && state.Goals.Count(item => item.Status == "active") <= 1
            && (state.ActiveGoal == null ? state.Goals.All(item => item.Status != "active")
                : state.Goals.Any(item => item.Id == state.ActiveGoal && item.Status == "active"))
            && (state.Method == null || state.ActiveGoal != null && state.Method.Revision > 0
                && state.Method.Metric is "observation" or "position" or "inventory" or "food" or "interaction"
                && double.IsFinite(state.Method.WindowSeconds) && state.Method.WindowSeconds is >= 5 and <= 120
                && state.Method.Baseline != null && state.Method.ZeroWindows >= 0);
    }
}
