using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Westworld {
    public sealed partial class ActorCognition {
        sealed record BeliefPatch(string Id, long ExpectedRevision, string Text, string Source);
        sealed record ProgressProposal(string Metric, double WindowSeconds = 30, int? ItemValue = null);
        sealed record MethodProposal(string Strategy, ProgressProposal ExpectedProgress);
        sealed record DispositionProposal(string Operation, string TaskId, long ExpectedRevision);
        sealed record DeliberationProposal(string Operation, string GoalId, long ExpectedRevision, string Purpose,
            string Source, string Why, MethodProposal Method, string PreviousGoalAction,
            DispositionProposal TaskDisposition, string[] DependsOnBeliefs);
        sealed record TaskProposal(string Operation,
            [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement Steps);
        sealed record CycleDecision(string Reason, BeliefPatch[] UnderstandingPatch, DeliberationProposal Deliberation,
            TaskProposal TaskRequest, double? ReviewAfter, string[] Handled);

        static string CycleId(string decision, string local) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(decision + ":" + local)))[..32].ToLowerInvariant();

        static string NormalizeCycleStep(JsonElement value) => value.ValueKind switch {
            JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().Where(p => p.Name is not ("timeout" or "retries"))
                .OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.Name + ":" + NormalizeCycleStep(p.Value))) + "}",
            JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(NormalizeCycleStep)) + "]",
            JsonValueKind.Number when value.TryGetDouble(out double number) => Math.Round(number, 3).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            _ => value.GetRawText()
        };

        string CycleMethodSignature(TaskProposal task, ProgressProposal progress) {
            string normalized = task?.Steps.ValueKind == JsonValueKind.Array ? NormalizeCycleStep(task.Steps) : "none";
            return CycleId(progress.Metric + ":" + progress.ItemValue, normalized);
        }

        string CommitCycleTurn(string id, string response, ObservationPose? pose) {
            CycleReceipt prior = m_cycle.Receipts.FirstOrDefault(receipt => receipt.DecisionId == id);
            if (prior != null) return JsonSerializer.Serialize(new { ok = true, duplicate = true, receipt = prior }, m_json);
            if (m_pending?.Cycle is not { } read || m_pending.Context.Id != id) return Result("unknown_turn");
            if (!m_tools.CognitiveWorldIsCurrent || !CanThink(m_tools.CognitiveBody)) return Result("stale_turn");
            // A network reply may arrive after the old physical task ended. Close that real attempt
            // before any new decision can overwrite its authorization or progress baseline.
            CollectInbox();
            ObserveCycle(m_tools.CognitiveBody);
            CycleDecision decision;
            try { decision = response is { Length: > 0 and <= 32768 } ? JsonSerializer.Deserialize<CycleDecision>(response, m_json) : null; }
            catch (JsonException error) { return RejectCycleProtocol("invalid_cycle_decision", error.Message); }
            if (decision == null || !ValidText(decision.Reason, 320) || decision.UnderstandingPatch is { Length: > 8 }
                || decision.Deliberation == null || decision.TaskRequest?.Operation is not ("continue" or "submit" or "replace" or "stop" or "none")
                || !double.IsFinite(decision.ReviewAfter ?? 20) || (decision.ReviewAfter ?? 20) is < .1 or > 120
                || (decision.TaskRequest.Operation is "submit" or "replace" ? decision.TaskRequest.Steps.ValueKind != JsonValueKind.Array
                    : decision.TaskRequest.Steps.ValueKind != JsonValueKind.Undefined)) {
                return RejectCycleProtocol("invalid_cycle_decision", "Use the required deliberation and taskRequest object shapes; omit steps for continue, stop and none.");
            }
            if (decision.Handled != null && (decision.Handled.Length > m_pending.Context.Messages.Length + read.VisualEventIds.Length
                || decision.Handled.Distinct().Count() != decision.Handled.Length
                || decision.Handled.Any(eventId => !m_pending.Context.Messages.Any(known => known.Id == eventId)
                    && !read.VisualEventIds.Contains(eventId)))) {
                return RejectCycleProtocol("invalid_handled", "handled may contain only distinct IDs from this input's messages or visualEvents, not other evidence IDs.");
            }
            m_cycle.ProtocolErrors = 0;
            m_cycle.ProtocolIssue = null;

            Pending pending = m_pending;
            List<string> accepted = [], rejected = [];
            foreach (BeliefPatch patch in decision.UnderstandingPatch ?? []) {
                string local = patch?.Id;
                CycleBelief observed = read.Beliefs.FirstOrDefault(b => b.Id == local);
                CycleBelief current = m_cycle.Beliefs.FirstOrDefault(b => b.Id == local);
                bool create = observed == null && patch?.ExpectedRevision == 0;
                if (patch == null || !ValidText(local, 64) || !ValidText(patch.Text, 256) || !read.Evidence.Contains(patch.Source)
                    || accepted.Contains(local) || rejected.Contains(local)
                    || (create ? current != null || m_cycle.Beliefs.Count >= 32 : observed == null || current == null
                        || patch.ExpectedRevision != observed.Revision || current.Revision != observed.Revision)) {
                    rejected.Add(local ?? "invalid");
                    continue;
                }
                string beliefId = create ? CycleId(id, local) : local;
                CycleBelief belief = new(beliefId, create ? 1 : current.Revision + 1, patch.Text,
                    patch.Source is "body" or "observation" or "method" ? id + ":" + patch.Source : patch.Source, pending.Context.GameTime);
                if (create) m_cycle.Beliefs.Add(belief);
                else m_cycle.Beliefs[m_cycle.Beliefs.IndexOf(current)] = belief;
                accepted.Add(local);
            }

            string deliberation = ApplyCycleDeliberation(id, decision, pending, accepted);
            string action = deliberation == null ? ApplyCycleTask(decision.TaskRequest, pending, pose ?? read.Pose) : "dependency_rejected";
            CycleReceipt receipt = new(id, accepted.ToArray(), rejected.ToArray(), deliberation ?? "accepted",
                action ?? "accepted", m_cycle.ActiveGoal, m_cycle.AuthorizationRevision, Now);
            if (m_cycle.Receipts.Count == 64) m_cycle.Receipts.RemoveAt(0);
            m_cycle.Receipts.Add(receipt);
            m_version++;
            m_goal = ActiveCycleGoal?.Purpose;
            m_body = pending.Context.Body;
            m_reviewAt = Now + (action != null || deliberation != null ? Math.Min(3, decision.ReviewAfter ?? 20) : decision.ReviewAfter ?? 20);
            m_nextTurnAt = Now + MinimumThinkInterval;
            if (deliberation == null) {
                m_seenTask = pending.Context.LastOutcome?.TaskId ?? m_seenTask;
                m_cycle.ReviewedProgressSerial = read.ProgressSerial;
                m_cycle.ReviewedSituationSerial = read.SituationSerial;
                m_cycle.ReviewedSomatic = read.Somatic;
                m_cycle.ReviewedVisualSerial = read.VisualSerial;
                for (int index = 0; index < m_cycle.VisualEvents.Count; index++) {
                    CycleVisualEvent seen = m_cycle.VisualEvents[index];
                    if (read.VisualEventIds.Contains(seen.Id)) m_cycle.VisualEvents[index] = seen with { Considered = true };
                }
                if (m_cycle.Activity is { } activity) m_cycle.Activity = activity with {
                    LastReviewedAttempts = Math.Min(read.ActivityAttempts, activity.UnproductiveAttempts)
                };
            }
            HashSet<string> handled = (decision.Handled ?? []).ToHashSet(StringComparer.Ordinal);
            for (int index = 0; index < m_inbox.Count; index++) {
                if (handled.Contains(m_inbox[index].Id)) m_inbox[index] = m_inbox[index] with { Considered = true };
            }
            RecordRecent("cycle:" + id, Now, "planChange", receipt);
            if (action != null || deliberation != null) RecordRecent("rejected:" + id, Now, "decisionRejected", new { error = deliberation ?? action });
            LastDecisionJson = JsonSerializer.Serialize(new { input = JsonSerializer.Deserialize<JsonElement>(pending.Input),
                decision, receipt }, m_json);
            m_pending = null;
            // A separately accepted cognition group is a successful cognitive turn even if its action was rejected.
            return JsonSerializer.Serialize(new { ok = true, receipt }, m_json);
        }

        string RejectCycleProtocol(string error, string detail) {
            m_cycle.ProtocolErrors = Math.Min(2, m_cycle.ProtocolErrors + 1);
            m_cycle.ProtocolIssue = detail.Length <= 256 ? detail : detail[..256];
            return Result(error);
        }

        string ApplyCycleDeliberation(string id, CycleDecision decision, Pending pending, List<string> accepted) {
            DeliberationProposal change = decision.Deliberation;
            CycleReadSet read = pending.Cycle;
            CycleGoal active = ActiveCycleGoal;
            if (change.DependsOnBeliefs?.Any(local => !accepted.Contains(local)) == true) return "belief_dependency_rejected";
            if (!ValidText(change.Why, 320) || !read.Evidence.Contains(change.Source)) return "invalid_deliberation_source";
            if (read.AuthorizationRevision != m_cycle.AuthorizationRevision) return "task_authorization_changed";
            if (change.Operation is not ("create" or "maintain" or "revise" or "suspend" or "abandon" or "achieve" or "resume" or "none")) return "invalid_goal_operation";
            SelfView self = m_tools.CognitiveBody;
            // These are interpretations and intentions based on the actor's dated evidence, not writes to body facts.
            // New injury stays unreviewed (its serial is newer than the frozen read set), while physical actions
            // are independently checked at admission. Otherwise continuous injury can veto every reconsideration.
            if ((active?.Id) != read.GoalId || (active?.Revision ?? 0) != read.GoalRevision) return "goal_version_changed";
            CycleGoal selected = change.GoalId == null ? null : m_cycle.Goals.FirstOrDefault(goal => goal.Id == change.GoalId);
            CycleGoal seen = change.GoalId == null ? null : read.Goals.FirstOrDefault(goal => goal.Id == change.GoalId);
            if (change.Operation == "none" && active != null) return "active_goal_requires_review";
            if (change.Operation == "create") {
                if (change.ExpectedRevision != 0 || !ValidText(change.GoalId, 64) || selected != null || !ValidText(change.Purpose, 256)
                    || m_cycle.Goals.Count >= 8 && !m_cycle.Goals.Any(goal => goal.Status is "abandoned" or "achieved")
                        && !(active != null && change.PreviousGoalAction == "abandon")) return "invalid_new_goal";
            }
            else if (change.Operation != "none" && (selected == null || seen == null || seen.Revision != change.ExpectedRevision || selected.Revision != change.ExpectedRevision
                || change.Operation != "resume" && selected.Id != active?.Id
                || change.Operation == "resume" && selected.Status != "suspended")) return "goal_version_changed";
            if (change.Operation is "create" or "resume" && active != null && change.PreviousGoalAction is not ("suspend" or "abandon")) return "previous_goal_disposition_required";
            if (change.Operation == "revise" && !ValidText(change.Purpose, 256)) return "invalid_goal_purpose";

            CycleMethod method = m_cycle.Method;
            ProgressProposal progress = change.Method?.ExpectedProgress;
            string signature = method?.Signature;
            if (change.Method != null) {
                if (!ValidText(change.Method.Strategy, 320) || progress?.Metric is not ("observation" or "position" or "inventory" or "food" or "interaction")
                    || !double.IsFinite(progress.WindowSeconds) || progress.WindowSeconds is < 5 or > 120
                    || progress.ItemValue is <= 0) return "invalid_method";
                if (decision.TaskRequest.Operation is "submit" or "replace") signature = CycleMethodSignature(decision.TaskRequest, progress);
                else if (method == null) signature = CycleId(id, progress.Metric);
                else if (progress.Metric != method.Metric || progress.ItemValue != method.ItemValue) return "method_change_requires_task";
                if (signature == method?.Signature && method.ZeroWindows >= 2 && decision.TaskRequest.Operation is "submit" or "replace") return "method_has_no_progress";
            }
            if (decision.TaskRequest.Operation is "submit" or "replace" && change.Operation is "suspend" or "abandon" or "achieve" or "none") return "task_requires_active_goal";
            if (decision.TaskRequest.Operation is "submit" or "replace" && change.Method == null) return "method_required";
            bool changing = change.Operation is not ("maintain" or "none") || signature != method?.Signature
                || decision.TaskRequest.Operation is "replace" or "stop";
            if (m_tools.CognitiveTaskActive) {
                DispositionProposal disposition = change.TaskDisposition;
                if (disposition == null || disposition.Operation is not ("retain" or "revoke")
                    || !Guid.TryParse(disposition.TaskId, out Guid taskId)
                    || taskId != m_tools.CognitiveTask.TaskId || disposition.ExpectedRevision != m_cycle.AuthorizationRevision
                    || read.AuthorizationRevision != m_cycle.AuthorizationRevision || read.TaskId != taskId) return "task_authorization_changed";
                if (changing && disposition.Operation != "revoke") return "old_task_revocation_required";
                if (disposition.Operation == "revoke") {
                    if (!m_tools.CancelCognitiveTask()) return "cannot_revoke_task";
                    m_cycle.AuthorizationRevision++;
                    m_cycle.IntentionStatus = "awaitingTask";
                }
            }
            else if (change.TaskDisposition is { } ended && (ended.Operation is not ("retain" or "revoke")
                || !Guid.TryParse(ended.TaskId, out Guid endedId) || endedId != read.TaskId
                || ended.ExpectedRevision != read.AuthorizationRevision || m_cycle.AuthorizationRevision != read.AuthorizationRevision)) return "task_authorization_changed";

            if (change.Operation is "create" or "resume" && active != null) {
                m_cycle.Goals[m_cycle.Goals.IndexOf(active)] = active with { Revision = active.Revision + 1,
                    Status = change.PreviousGoalAction == "abandon" ? "abandoned" : "suspended", Why = change.Why, At = Now };
            }
            switch (change.Operation) {
                case "create":
                    if (m_cycle.Goals.Count >= 8) {
                        CycleGoal retired = m_cycle.Goals.Where(goal => goal.Status is "abandoned" or "achieved").MinBy(goal => goal.At);
                        if (m_cycle.ArchivedGoals.Count == 64) m_cycle.ArchivedGoals.RemoveAt(0);
                        m_cycle.ArchivedGoals.Add(retired);
                        m_cycle.Goals.Remove(retired);
                    }
                    selected = new(CycleId(id, change.GoalId), 1, change.Purpose, "active", change.Why, id + ":" + change.Source, Now);
                    m_cycle.Goals.Add(selected);
                    m_cycle.ActiveGoal = selected.Id;
                    break;
                case "maintain":
                    m_cycle.Goals[m_cycle.Goals.IndexOf(selected)] = selected with { Why = change.Why,
                        Source = id + ":" + change.Source, At = Now };
                    break;
                case "none": break;
                default:
                    string status = change.Operation switch { "suspend" => "suspended", "abandon" => "abandoned", "achieve" => "achieved", _ => "active" };
                    CycleGoal revised = selected with { Revision = selected.Revision + 1, Purpose = change.Operation == "revise" ? change.Purpose : selected.Purpose,
                        Status = status, Why = change.Why, Source = id + ":" + change.Source, At = Now };
                    m_cycle.Goals[m_cycle.Goals.IndexOf(selected)] = revised;
                    m_cycle.ActiveGoal = status == "active" ? selected.Id : null;
                    break;
            }
            if (m_cycle.ActiveGoal == null) { m_cycle.Method = null; m_cycle.IntentionStatus = "none"; }
            else if (change.Method != null) {
                bool same = method != null && signature == method.Signature && active?.Id == m_cycle.ActiveGoal;
                m_cycle.Method = same ? method with { Strategy = change.Method.Strategy }
                    : new CycleMethod(signature, (method?.Revision ?? 0) + 1, change.Method.Strategy, progress.Metric, progress.ItemValue,
                        progress.WindowSeconds, Now, Now, SampleCycleMetrics(self, progress.ItemValue), 0);
                if (!m_tools.CognitiveTaskActive) m_cycle.IntentionStatus = "awaitingTask";
            }
            else if (active?.Id != m_cycle.ActiveGoal) { m_cycle.Method = null; m_cycle.IntentionStatus = "awaitingTask"; }
            return null;
        }

        string ApplyCycleTask(TaskProposal task, Pending pending, ObservationPose pose) {
            if (task.Operation is "none" or "continue") return null;
            if (task.Operation == "stop") {
                if (m_tools.CognitiveTaskActive && !m_tools.CancelCognitiveTask()) return "cannot_stop_task";
                return null;
            }
            if (ActiveCycleGoal == null) return "task_requires_active_goal";
            if (!m_tools.ProductionTargetsWereObserved(task.Steps, pending.Context.Observation, pending.Context.Messages, pending.Context.RecentProcess)) return "unobserved_production_target";
            string error = m_tools.ValidateCognitiveTask("start", task.Steps);
            if (error != null) return error;
            error = m_tools.RecheckAnchoredPlan(task.Steps, pose, m_tools.CognitiveBody, pending.Context.GameTime);
            if (error != null) return error;
            error = m_tools.ApplyCognitiveTask("start", task.Steps, pose);
            if (error != null) return error;
            m_cycle.AuthorizedTask = m_tools.CognitiveTask.TaskId;
            m_cycle.AuthorizationRevision++;
            m_cycle.IntentionStatus = "executing";
            if (m_cycle.Method is { } method) m_cycle.Method = method with {
                StartedAt = Now, WindowAt = Now, Baseline = SampleCycleMetrics(m_tools.CognitiveBody, method.ItemValue)
            };
            return null;
        }
    }
}
