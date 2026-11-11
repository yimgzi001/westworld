using System.Text.Json;

namespace Game.Westworld {
    public sealed partial class ActorCognition {
        sealed record PlanProposal(string Operation, int? Version, string Purpose, string Next, string Completion, string Source);
        sealed record WaitProposal(string Kind, string Person, string Offer, int? ItemValue, int? Count, double Timeout);
        internal sealed record PersonalPlan(string Id, int Version, string Status, string Purpose, string Next,
            string Completion, string Source, string Reason, double UpdatedAt);
        internal sealed record EventWait(string Id, string Kind, string Person, string Offer, int? ItemValue, int? Count,
            double Since, double Deadline, string[] ExcludedMessages, string Status = "pending", string MatchedMessage = null,
            bool Considered = false);
        internal sealed record RecentProcess(string Id, double At, string Kind, JsonElement Details);

        const int RecentProcessLimit = 16;
        PersonalPlan m_plan;
        EventWait m_wait;
        readonly List<RecentProcess> m_recentProcess = [];

        void RecordRecent(string id, double at, string kind, object details) {
            JsonElement data = JsonSerializer.SerializeToElement(details, m_json);
            if (data.GetRawText().Length > 4096) return;
            RecentProcess entry = new(id, at, kind, data);
            int index = m_recentProcess.FindIndex(item => item.Id == id);
            if (index >= 0) {
                m_recentProcess[index] = entry;
                return;
            }
            if (m_recentProcess.Count == RecentProcessLimit) m_recentProcess.RemoveAt(0);
            m_recentProcess.Add(entry);
        }

        void RecordStep(Guid taskId, ActorPlanStep plan, ActorTaskStepView result) => RecordRecent(
            $"{taskId:N}:{result.Index}:{result.Attempts}", Now, "actionResult", new {
                taskId, step = result.Index, tool = plan?.Tool ?? result.Name, arguments = plan?.Arguments,
                result.Status, result.AtomicFailure, result.AffectedCount, result.ReceivedItems
            });

        string ValidatePersonalPlan(Decision decision, CognitiveContext context) {
            PlanProposal proposal = decision.Plan;
            if (m_plan?.Status == "active" && decision.Goal != null && decision.Goal != m_goal && proposal == null) {
                return "goal_change_requires_plan_revision";
            }
            if (proposal != null) {
                if (proposal.Operation is not ("create" or "revise" or "complete" or "cancel")
                    || EvidenceKind(proposal.Source, context) == null) {
                    return "invalid_personal_plan";
                }
                if (proposal.Operation == "create") {
                    if (m_plan?.Status == "active" || proposal.Version is not (null or 0)) return "personal_plan_exists";
                }
                else if (m_plan?.Status != "active" || proposal.Version != m_plan.Version) {
                    return "personal_plan_version_changed";
                }
                if (proposal.Operation is "create" or "revise"
                    && (!ValidText(proposal.Purpose, 256) || !ValidText(proposal.Next, 256) || !ValidText(proposal.Completion, 256))) {
                    return "invalid_personal_plan";
                }
            }
            WaitProposal wait = decision.Wait;
            if (wait == null || wait.Kind == "cancel") return null;
            if (wait.Kind is not ("itemOffered" or "itemsTransferred" or "speech")
                || !double.IsFinite(wait.Timeout) || wait.Timeout is < .1 or > 120
                || wait.Count is <= 0 || wait.ItemValue is <= 0
                || wait.Offer != null && !Guid.TryParseExact(wait.Offer, "N", out _)) return "invalid_event_wait";
            if (wait.Person != null && !context.Messages.Any(message => message.Value.Source == wait.Person)
                && !(context.Observation.TryGetProperty("entities", out JsonElement entities)
                    && entities.EnumerateArray().Any(entity => entity.GetProperty("reference").GetString() == wait.Person))) {
                return "unobserved_wait_person";
            }
            if (wait.Offer != null && !context.Messages.Any(message => message.Value.OfferReference == wait.Offer)
                && !(context.Observation.TryGetProperty("production", out JsonElement production)
                    && production.GetProperty("outgoingOffers").EnumerateArray().Any(offer =>
                        offer.GetProperty("offerReference").GetString() == wait.Offer))) return "unobserved_wait_offer";
            return null;
        }

        void InstallPersonalPlan(Decision decision, CognitiveContext context) {
            if (decision.Plan is { } proposal) {
                m_plan = proposal.Operation switch {
                    "create" => new PersonalPlan(Guid.NewGuid().ToString("N"), 1, "active", proposal.Purpose,
                        proposal.Next, proposal.Completion, proposal.Source, decision.Reason, Now),
                    "revise" => m_plan with { Version = m_plan.Version + 1, Purpose = proposal.Purpose, Next = proposal.Next,
                        Completion = proposal.Completion, Source = proposal.Source, Reason = decision.Reason, UpdatedAt = Now },
                    _ => m_plan with { Version = m_plan.Version + 1, Status = proposal.Operation == "complete" ? "completed" : "cancelled",
                        Source = proposal.Source, Reason = decision.Reason, UpdatedAt = Now }
                };
                RecordRecent($"plan:{m_plan.Id}:{m_plan.Version}", Now, "planChange", m_plan);
                if (m_plan.Status != "active") m_wait = null;
            }
            if (decision.Wait is { } wait) {
                m_wait = wait.Kind == "cancel" ? null : new EventWait(Guid.NewGuid().ToString("N"), wait.Kind, wait.Person,
                    wait.Offer, wait.ItemValue, wait.Count, context.GameTime, Now + wait.Timeout,
                    context.Messages.Select(message => message.Id).ToArray());
                RecordRecent($"wait:{m_version + 1}", Now, "waitChange", m_wait);
            }
        }

        void UpdateEventWait(CognitiveBody body, ActorTaskView outcome) {
            if (m_wait?.Status != "pending") return;
            CognitiveMessage received = m_inbox.FirstOrDefault(message => message.Ready
                && message.Value.GameTime >= m_wait.Since && !m_wait.ExcludedMessages.Contains(message.Id)
                && (m_wait.Person == null || message.Value.Source == m_wait.Person)
                && (m_wait.Offer == null || message.Value.OfferReference == m_wait.Offer)
                && (message.Value.Kind == AtomicSocialEventKind.OfferDeclined
                    || (m_wait.Kind, message.Value.Kind) is ("itemOffered", AtomicSocialEventKind.ItemOffered)
                        or ("itemsTransferred", AtomicSocialEventKind.ItemsTransferred) or ("speech", AtomicSocialEventKind.HeardSpeech))
                && (message.Value.Kind == AtomicSocialEventKind.OfferDeclined || m_wait.ItemValue == null
                    || message.Value.ItemValue == m_wait.ItemValue));
            string status = null;
            if (received != null) {
                if (received.Value.Kind == AtomicSocialEventKind.OfferDeclined) status = "declined";
                else if (m_wait.Count is int count && received.Value.Count < count) status = "partial";
                else status = "satisfied";
            }
            else if (Now >= m_wait.Deadline) status = "timedOut";
            else if (m_body != null && body != m_body) status = "bodyChanged";
            else if (outcome?.TaskId != m_seenTask && outcome?.Status is ActorTaskStatus.Failed or ActorTaskStatus.Cancelled) status = "taskFailed";
            if (status == null) return;
            m_wait = m_wait with { Status = status, MatchedMessage = received?.Id };
            RecordRecent($"wait:{m_wait.Id}:result", Now, "waitResult", m_wait);
        }

        static bool ValidPersonalState(PersonalPlan plan, EventWait wait, RecentProcess[] recent) =>
            (plan == null || Guid.TryParseExact(plan.Id, "N", out _) && plan.Version > 0
                && plan.Status is "active" or "completed" or "cancelled" && ValidText(plan.Purpose, 256)
                && ValidText(plan.Next, 256) && ValidText(plan.Completion, 256) && ValidText(plan.Source, 64)
                && ValidText(plan.Reason, 320) && double.IsFinite(plan.UpdatedAt) && plan.UpdatedAt >= 0)
            && (wait == null || Guid.TryParseExact(wait.Id, "N", out _) && wait.Kind is "itemOffered" or "itemsTransferred" or "speech"
                && wait.Status is "pending" or "satisfied" or "declined" or "partial" or "timedOut" or "bodyChanged" or "taskFailed"
                && double.IsFinite(wait.Since) && double.IsFinite(wait.Deadline) && wait.Since >= 0 && wait.Deadline >= wait.Since
                && wait.ExcludedMessages is { Length: <= 6 } && wait.ExcludedMessages.All(id => Guid.TryParseExact(id, "N", out _))
                && wait.Count is null or > 0 && wait.ItemValue is null or > 0 && wait.Person is not { Length: > 128 }
                && (wait.Offer == null || Guid.TryParseExact(wait.Offer, "N", out _)))
            && (recent == null || recent.Length <= RecentProcessLimit && recent.All(entry => entry != null
                && ValidText(entry.Id, 128) && double.IsFinite(entry.At) && entry.At >= 0
                && entry.Kind is "received" or "actionResult" or "planChange" or "waitChange" or "waitResult" or "decisionRejected"
                && entry.Details.ValueKind is JsonValueKind.Object or JsonValueKind.Null && entry.Details.GetRawText().Length <= 4096)
                && recent.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() == recent.Length);
    }
}
