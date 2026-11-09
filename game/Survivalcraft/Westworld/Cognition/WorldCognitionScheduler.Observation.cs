using System.Text;
using System.Text.Json;

namespace Game.Westworld {
    public sealed partial class WorldCognitionScheduler {
        sealed record ObservationRequest(CognitionAttempt Attempt, string TurnId, SelfView Body, Task<ActorModelReply> Task);

        readonly Dictionary<int, ObservationRequest> m_observationRequests = [];
        readonly Dictionary<int, DateTimeOffset> m_nextObservationRequest = [];
        bool m_observing;
        bool m_observationEnded;
        bool m_observationBudgetLimited;
        int m_lastObservationActor;
        int m_observationPeak;
        int m_observationApplied;
        DateTimeOffset m_observationBackoff;

        public void StartObservation() {
            Start(resume: false);
            m_observing = true;
        }

        public void StopObservationDispatch() {
            if (!m_observing || m_disposed) {
                throw new InvalidOperationException("An active observation run is required.");
            }
            m_observationEnded = true;
        }

        string ObservationStatusJson() => JsonSerializer.Serialize(new {
            state = m_fault != null ? "faulted" : m_userPaused ? "userPaused"
                : m_observationEnded ? "draining" : m_observationBudgetLimited ? "budgetLimited" : "running",
            fault = m_fault,
            time = m_time.GameTime,
            inFlight = m_observationRequests.Count,
            activeTasks = m_actors.Values.Count(actor => actor.Tools.CognitiveTaskActive),
            peak = m_observationPeak,
            applied = m_observationApplied,
            requests = m_ledger.Data.Attempts.Length,
            spent = m_ledger.Spent,
            unknown = m_ledger.Unknown,
            reserved = m_observationRequests.Values.Sum(request => request.Attempt.Reserved),
            budget = m_ledger.Data.Budget
        }, CognitionRunLedger.Json);

        void PumpObservation() {
            if (m_fault != null || (m_userPaused && !m_observationEnded)) {
                return;
            }
            foreach (ObservationRequest request in m_observationRequests.Values.Where(request => request.Task.IsCompleted).ToArray()) {
                CompleteObservation(request);
                m_observationRequests.Remove(request.Attempt.ActorId);
            }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (m_observationEnded || now < m_observationBackoff || m_observationRequests.Count >= m_parallelism) {
                return;
            }
            m_observationBudgetLimited = false;
            long available = m_ledger.Data.Budget - m_ledger.Spent - m_ledger.Unknown
                - m_observationRequests.Values.Sum(request => request.Attempt.Reserved);
            foreach (int actorId in m_actors.Keys.OrderBy(id => id <= m_lastObservationActor).ToArray()) {
                Actor actor = m_actors[actorId];
                if (m_observationRequests.ContainsKey(actorId) || m_nextObservationRequest.GetValueOrDefault(actorId) > now
                    || !actor.Tools.CognitiveWorldIsCurrent || actor.Tools.CognitiveTaskActive
                    || actor.Cognition.PrepareTurn(cohort: true) is not string input) {
                    continue;
                }
                string turnId = actor.Cognition.ScheduledInput.Context.Id;
                string schema = actor.Tools.DescribeDecisionTools(cohort: true);
                long estimate = checked((Encoding.UTF8.GetByteCount(input) + Encoding.UTF8.GetByteCount(schema) + 4096L)
                    * m_ledger.Data.InputPrice + OutputTokenLimit * m_ledger.Data.OutputPrice);
                if (available < estimate) {
                    actor.Cognition.AbandonTurn(turnId);
                    m_observationBudgetLimited = true;
                    m_observationBackoff = now.AddSeconds(1);
                    break;
                }
                if (!actor.Cognition.TryClaimModelTurn(turnId)) {
                    continue;
                }
                CheckStorage();
                CognitionAttempt attempt = new(Guid.NewGuid().ToString("N"), turnId, actorId, 1,
                    estimate, "inFlight", DateTimeOffset.UtcNow);
                m_ledger.Save(m_ledger.Data with { Attempts = [..m_ledger.Data.Attempts, attempt] });
                CancellationToken token = m_cancel.Token;
                SelfView body = actor.Tools.CognitiveBody;
                Task<ActorModelReply> task = Task.Run(() => m_source.RequestAsync(input, schema, token), token);
                m_observationRequests.Add(actorId, new ObservationRequest(attempt, turnId, body, task));
                m_observationPeak = Math.Max(m_observationPeak, m_observationRequests.Count);
                m_lastObservationActor = actorId;
                available -= estimate;
                if (m_observationRequests.Count >= m_parallelism) {
                    break;
                }
            }
        }

        void CompleteObservation(ObservationRequest request) {
            ActorModelReply reply;
            try {
                reply = request.Task.GetAwaiter().GetResult() ?? new ActorModelReply(null, Error: "decision_source_failed");
            }
            catch (Exception) {
                reply = new ActorModelReply(null, Error: "decision_source_failed");
            }
            int actorId = request.Attempt.ActorId;
            Actor actor = m_actors[actorId];
            bool known = reply.InputTokens is >= 0 && reply.OutputTokens is >= 0;
            string error = reply.Error ?? (!known ? "missing_usage" : m_observationEnded ? "observation_ended" :
                actor.Cognition.ValidateObservation(request.TurnId, reply.DecisionJson, request.Body));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            CognitionAttempt settled = request.Attempt with {
                State = known ? "completed" : "unknown", ReceivedAt = now,
                Charge = known ? checked(reply.InputTokens.Value * m_ledger.Data.InputPrice
                    + reply.OutputTokens.Value * m_ledger.Data.OutputPrice) : 0,
                Model = reply.Model, RequestId = reply.RequestId, InputTokens = reply.InputTokens,
                OutputTokens = reply.OutputTokens, HttpStatus = reply.Status, Error = error
            };
            SaveObservationAttempt(settled);
            if (error == null) {
                using JsonDocument result = JsonDocument.Parse(actor.Cognition.CommitTurn(request.TurnId, reply.DecisionJson));
                if (result.RootElement.GetProperty("ok").GetBoolean()) {
                    m_observationApplied++;
                    return;
                }
                error = result.RootElement.GetProperty("error").GetString();
                SaveObservationAttempt(settled with { Error = error });
            }
            actor.Cognition.AbandonTurn(request.TurnId);
            m_nextObservationRequest[actorId] = now.AddSeconds(2);
            if (reply.Status == 429) {
                DateTimeOffset retry = now.AddSeconds(Math.Clamp(reply.RetryAfterSeconds ?? 2, 1, 120));
                if (retry > m_observationBackoff) {
                    m_observationBackoff = retry;
                }
            }
        }

        void SaveObservationAttempt(CognitionAttempt attempt) =>
            m_ledger.Save(m_ledger.Data with { Attempts = m_ledger.Data.Attempts
                .Select(previous => previous.Id == attempt.Id ? attempt : previous).ToArray() });

        void CloseObservation() {
            if (m_observationRequests.Count == 0) {
                return;
            }
            foreach (ObservationRequest request in m_observationRequests.Values) {
                m_actors[request.Attempt.ActorId].Cognition.AbandonTurn(request.TurnId);
            }
            m_ledger.Save(m_ledger.Data with { Attempts = m_ledger.Data.Attempts.Select(attempt =>
                attempt.State == "inFlight" ? attempt with { State = "unknown", Error = "send_outcome_unknown" } : attempt).ToArray() });
            m_observationRequests.Clear();
        }
    }
}
