using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using GameEntitySystem;

namespace Game.Westworld {
    public sealed partial class WorldCognitionScheduler {
        sealed record ObservationRequest(CognitionAttempt Attempt, string TurnId, ObservationPose Pose) {
            internal Task<ActorModelReply> Task { get; set; }
        }
        sealed record ObservationFollowUp(ObservationRequest Request, CognitionAttempt Settled, bool Recovered = false);
        readonly record struct ObservationReply(ObservationRequest Request, ActorModelReply Reply);
        readonly record struct ObservationTaskEvent(int ActorId, Guid TaskId, bool Started);

        readonly Dictionary<int, ObservationRequest> m_observationRequests = [];
        readonly Dictionary<int, ObservationFollowUp> m_observationFollowUps = [];
        readonly Queue<ObservationTaskEvent> m_observationTaskEvents = [];
        readonly SortedSet<int> m_observationLookAheadReady = [];
        readonly Dictionary<int, DateTimeOffset> m_nextObservationRequest = [];
        Channel<ObservationReply> m_observationReplies;
        bool m_observationEnded;
        bool m_observationBudgetLimited;
        int m_lastObservationActor;
        int m_observationPeak;
        int m_observationApplied;
        int m_lookAheadBuffered;
        int m_lookAheadStarted;
        int m_observationTaskStarts;
        int m_observationTaskEnds;
        int m_observationRepliesHandled;
        int m_observationSent;
        int m_observationNetworkActive;
        DateTimeOffset m_observationBackoff;
        DateTimeOffset m_observationRateBackoff;

        public void StartObservation() => StartObservation(measure: false);

        public void StartObservation(bool measure) {
            Project project = RequireUnboundWorld();
            if (m_ledger.Exists || m_loadedObservations.TryGetValue(project, out _)) {
                throw new InvalidOperationException("Use the matching observation checkpoint to resume the existing run.");
            }
            m_time = project.FindSubsystem<SubsystemTime>(true);
            BindWorld(project);
            InitializeObservation(measure, resume: false);
            // Publish the experiment identity before its first world checkpoint.
            StageObservationLedger(m_ledger.Data with { }, () => m_initialLedgerReady = true);
        }

        void InitializeObservation(bool measure, bool resume) {
            m_observationData = m_ledger.Data;
            // A request holds its slot until the game thread consumes the reply, including during pause.
            m_observationReplies = Channel.CreateBounded<ObservationReply>(new BoundedChannelOptions(m_parallelism) { SingleReader = true });
            if (measure) {
                m_observationMetrics = new ObservationMetrics();
            }
            foreach ((int actorId, Actor actor) in m_actors) {
                actor.Tools.CognitiveTaskActivityChanged += OnObservationTaskActivity;
                if (!resume && actor.Tools.CognitiveActiveTaskId is Guid taskId) {
                    OnObservationTaskActivity(actorId, taskId, started: true);
                }
            }
        }

        public void StopObservationDispatch() {
            if (m_project == null || m_disposed) {
                throw new InvalidOperationException("An active observation run is required.");
            }
            m_observationEnded = true;
        }

        string ObservationStatusJson() => JsonSerializer.Serialize(new {
            state = m_fault != null ? "faulted" : m_userPaused ? "userPaused"
                : m_observationEnded ? "draining" : m_observationBudgetLimited ? "budgetLimited" : "running",
            fault = m_fault,
            time = m_time?.GameTime,
            checkpointTime = m_checkpointTime,
            reusedReplies = m_reusedReplies,
            inFlight = m_observationRequests.Count,
            activeTasks = m_actors.Values.Count(actor => actor.Tools.CognitiveTaskActive),
            peak = m_observationPeak,
            applied = m_observationApplied,
            lookAheadBuffered = m_lookAheadBuffered,
            lookAheadStarted = m_lookAheadStarted,
            taskStartEvents = m_observationTaskStarts,
            taskEndEvents = m_observationTaskEnds,
            modelReplyEvents = m_observationRepliesHandled,
            ledgerPending = ObservationLedgerPending,
            networkInFlight = m_observationNetworkActive,
            lookAheadRequests = m_observationRequests.Values.Where(request => request.Attempt.PrecedingTask != null
                && request.Task is { IsCompleted: false })
                .Select(request => new { request.Attempt.Id, request.Attempt.ActorId, TaskId = request.Attempt.PrecedingTask }),
            requests = m_observationSent,
            spent = m_ledger.Spent,
            unknown = m_ledger.Unknown,
            reserved = m_observationRequests.Values.Sum(request => request.Attempt.Reserved),
            budget = m_ledger.Data.Budget
        }, CognitionRunLedger.Json);

        void PumpObservation() {
            long started = m_observationMetrics != null ? Stopwatch.GetTimestamp() : 0;
            try {
                ProcessObservation();
                if (m_fault == null) {
                    SaveObservationCheckpointWhenReady();
                    WriteObservationBatch();
                }
            }
            finally {
                m_observationMetrics?.Frame(Stopwatch.GetTimestamp() - started);
            }
        }

        void ProcessObservation() {
            if (m_fault != null || (m_userPaused && !m_observationEnded)) {
                return;
            }
            ProcessObservationTaskEvents();
            if (m_observationEnded && m_observationFollowUps.Count > 0) {
                foreach (ObservationFollowUp followUp in m_observationFollowUps.Values) {
                    InstallObservationReply(followUp.Request, followUp.Settled, followUp.Recovered);
                }
                m_observationFollowUps.Clear();
            }
            while (m_observationReplies.Reader.TryRead(out ObservationReply completed)) {
                CompleteObservation(completed.Request, completed.Reply);
                m_observationNetworkActive--;
                m_observationRepliesHandled++;
            }
            ConfirmObservationBatch();
            ProcessRestoredReplies();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (m_observationEnded || m_checkpointTime == null || m_recoveryLedgerPending
                || now < m_observationBackoff || now < m_observationRateBackoff
                || m_observationRequests.Count >= m_parallelism) {
                return;
            }
            m_observationBudgetLimited = false;
            // Keep idle requests from taking every slot while an actor's current task is still running.
            int lookAheadLimit = Math.Max(1, m_parallelism / 3);
            int idleLimit = Math.Max(1, m_parallelism - lookAheadLimit);
            int lookAheadInFlight = m_observationRequests.Values.Count(request => request.Attempt.PrecedingTask != null);
            int idleInFlight = m_observationRequests.Count - lookAheadInFlight;
            if (idleInFlight >= idleLimit && m_observationLookAheadReady.Count == 0) {
                return;
            }
            long available = m_ledger.Data.Budget - m_ledger.Spent - m_ledger.Unknown
                - m_observationRequests.Values.Sum(request => request.Attempt.Reserved);
            IEnumerable<int> candidates = idleInFlight < idleLimit ? m_actors.Keys : m_observationLookAheadReady;
            foreach (int actorId in candidates.OrderBy(id => id <= m_lastObservationActor).ToArray()) {
                Actor actor = m_actors[actorId];
                Guid? precedingTask = actor.Tools.CognitiveActiveTaskId;
                bool lookAhead = precedingTask != null;
                if (m_observationUncertainActors.Contains(actorId) || m_observationRequests.ContainsKey(actorId)
                    || m_nextObservationRequest.GetValueOrDefault(actorId) > now
                    || m_observationFollowUps.ContainsKey(actorId) || !actor.Tools.CognitiveWorldIsCurrent
                    || (lookAhead ? lookAheadInFlight >= lookAheadLimit : idleInFlight >= idleLimit)
                    || (lookAhead ? !m_observationLookAheadReady.Contains(actorId) : actor.Tools.CognitiveActionRunning)
                    || actor.Cognition.PrepareTurn(checkpointable: true, lookAhead: lookAhead) is not string input) {
                    continue;
                }
                string turnId = actor.Cognition.PendingTurn.Context.Id;
                string schema = actor.Tools.DescribeDecisionTools(checkpointable: true);
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
                CognitionAttempt attempt = new(Guid.NewGuid().ToString("N"), turnId, actorId,
                    estimate, "inFlight", DateTimeOffset.UtcNow, PrecedingTask: precedingTask);
                ObservationRequest request = new(attempt, turnId, new ObservationPose(actor.Tools.CognitiveBody));
                m_observationRequests.Add(actorId, request);
                StageObservationLedger(m_observationData with { Attempts = [..m_observationData.Attempts, attempt] },
                    () => SendObservationRequest(request, input, schema));
                if (lookAhead) {
                    m_observationLookAheadReady.Remove(actorId);
                    lookAheadInFlight++;
                }
                else {
                    idleInFlight++;
                }
                m_lastObservationActor = actorId;
                available -= estimate;
                if (m_observationRequests.Count >= m_parallelism) {
                    break;
                }
            }
        }

        void SendObservationRequest(ObservationRequest request, string input, string schema) {
            int actorId = request.Attempt.ActorId;
            Actor actor = m_actors[actorId];
            bool rateLimited = DateTimeOffset.UtcNow < m_observationRateBackoff;
            if (m_observationEnded || !actor.Tools.CognitiveWorldIsCurrent
                || actor.Tools.CognitiveActiveTaskId != request.Attempt.PrecedingTask
                || (request.Attempt.PrecedingTask == null && actor.Tools.CognitiveActionRunning) || rateLimited) {
                actor.Cognition.AbandonTurn(request.TurnId);
                if (rateLimited && request.Attempt.PrecedingTask is Guid taskId && actor.Tools.CognitiveActiveTaskId == taskId) {
                    m_observationLookAheadReady.Add(actorId);
                }
                SaveObservationAttempt(request.Attempt with {
                    State = "unsent", Error = rateLimited ? "rate_backoff_before_send" : "invalidated_before_send"
                },
                    () => m_observationRequests.Remove(actorId));
                return;
            }
            m_observationMetrics?.Dispatched(actorId, request.Attempt.PrecedingTask);
            m_observationSent++;
            m_observationNetworkActive++;
            m_observationPeak = Math.Max(m_observationPeak, m_observationNetworkActive);
            CancellationToken token = m_cancel.Token;
            request.Task = Task.Run(() => m_source.RequestAsync(input, schema, token), token);
            _ = PublishObservationReply(request, m_observationReplies.Writer);
        }

        void OnObservationTaskActivity(int actorId, Guid taskId, bool started) {
            m_observationTaskEvents.Enqueue(new ObservationTaskEvent(actorId, taskId, started));
            m_observationMetrics?.TaskChanged(actorId, taskId, started);
        }

        void ProcessObservationTaskEvents() {
            while (m_observationTaskEvents.TryDequeue(out ObservationTaskEvent task)) {
                if (task.Started) {
                    m_observationTaskStarts++;
                    m_observationLookAheadReady.Add(task.ActorId);
                    continue;
                }
                m_observationTaskEnds++;
                m_observationLookAheadReady.Remove(task.ActorId);
                if (m_observationFollowUps.TryGetValue(task.ActorId, out ObservationFollowUp followUp)
                    && followUp.Request.Attempt.PrecedingTask == task.TaskId) {
                    InstallObservationReply(followUp.Request, followUp.Settled, followUp.Recovered);
                    m_observationFollowUps.Remove(task.ActorId);
                }
            }
        }

        static async Task PublishObservationReply(ObservationRequest request, ChannelWriter<ObservationReply> writer) {
            ActorModelReply reply;
            try {
                reply = await request.Task.ConfigureAwait(false) ?? new ActorModelReply(null, Error: "decision_source_failed");
            }
            catch (Exception) {
                reply = new ActorModelReply(null, Error: "decision_source_failed");
            }
            // With one reply per occupied slot, a failed write means the world has closed the channel.
            writer.TryWrite(new ObservationReply(request, reply));
        }

        void CompleteObservation(ObservationRequest request, ActorModelReply reply) {
            bool known = reply.InputTokens is >= 0 && reply.OutputTokens is >= 0;
            string error = reply.Error ?? (!known ? "missing_usage" : null);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            CognitionAttempt settled = request.Attempt with {
                State = known ? "completed" : "unknown", ReceivedAt = now,
                Charge = known ? checked(reply.InputTokens.Value * m_ledger.Data.InputPrice
                    + reply.OutputTokens.Value * m_ledger.Data.OutputPrice) : 0,
                Model = reply.Model, RequestId = reply.RequestId, InputTokens = reply.InputTokens,
                OutputTokens = reply.OutputTokens, HttpStatus = reply.Status, Error = error,
                Decision = error == null ? reply.DecisionJson : null
            };
            SaveObservationAttempt(settled, () => ApplyObservationSettlement(request, settled, now));
            if (reply.Status == 429) {
                DateTimeOffset retry = now.AddSeconds(Math.Clamp(reply.RetryAfterSeconds ?? 2, 1, 120));
                if (retry > m_observationRateBackoff) {
                    m_observationRateBackoff = retry;
                }
            }
        }

        void ApplyObservationSettlement(ObservationRequest request, CognitionAttempt settled, DateTimeOffset now) {
            m_observationRequests.Remove(request.Attempt.ActorId);
            if (settled.Error != null) {
                RejectObservation(request, now);
            }
            else if (!m_observationEnded && AwaitingHandoff(request)) {
                m_observationFollowUps.Add(request.Attempt.ActorId, new ObservationFollowUp(request, settled));
                m_lookAheadBuffered++;
            }
            else {
                InstallObservationReply(request, settled);
            }
        }

        bool AwaitingHandoff(ObservationRequest request) {
            Actor actor = m_actors[request.Attempt.ActorId];
            return request.Attempt.PrecedingTask is Guid task && actor.Tools.CognitiveWorldIsCurrent && actor.Tools.CognitiveActiveTaskId == task;
        }

        void InstallObservationReply(ObservationRequest request, CognitionAttempt settled, bool recovered = false) {
            Actor actor = m_actors[request.Attempt.ActorId];
            string error = "observation_ended";
            if (!m_observationEnded) {
                using JsonDocument result = JsonDocument.Parse(actor.Cognition.CommitObservation(
                    request.TurnId, settled.Decision, request.Pose, request.Attempt.PrecedingTask));
                if (result.RootElement.GetProperty("ok").GetBoolean()) {
                    m_observationApplied++;
                    if (recovered) {
                        m_reusedReplies++;
                    }
                    if (request.Attempt.PrecedingTask is Guid task && actor.Tools.CognitiveActiveTaskId is Guid nextTask && nextTask != task) {
                        m_lookAheadStarted++;
                    }
                    return;
                }
                error = result.RootElement.GetProperty("error").GetString();
            }
            SaveObservationAttempt(settled with { Error = error });
            RejectObservation(request, DateTimeOffset.UtcNow);
        }

        void RejectObservation(ObservationRequest request, DateTimeOffset now) {
            int actorId = request.Attempt.ActorId;
            m_actors[actorId].Cognition.AbandonTurn(request.TurnId);
            m_nextObservationRequest[actorId] = now.AddSeconds(2);
        }

        void SaveObservationAttempt(CognitionAttempt attempt, Action confirmed = null) =>
            StageObservationLedger(m_observationData with { Attempts = m_observationData.Attempts
                .Select(previous => previous.Id == attempt.Id ? attempt : previous).ToArray() }, confirmed);

        void CloseObservation() {
            ActorTaskScheduler.For(m_project).HoldStarts = false;
            if (m_observationReplies != null) {
                foreach (Actor actor in m_actors.Values) {
                    actor.Tools.CognitiveTaskActivityChanged -= OnObservationTaskActivity;
                }
                m_observationReplies.Writer.TryComplete();
                m_observationTaskEvents.Clear();
                m_observationLookAheadReady.Clear();
            }
            foreach (ObservationRequest request in m_observationRequests.Values.Concat(m_observationFollowUps.Values.Select(reply => reply.Request))) {
                m_actors[request.Attempt.ActorId].Cognition.AbandonTurn(request.TurnId);
            }
            HashSet<string> discarded = m_observationFollowUps.Values.Select(reply => reply.Settled.Id).ToHashSet();
            HashSet<string> unsent = m_observationRequests.Values.Where(request => request.Task == null)
                .Select(request => request.Attempt.Id).ToHashSet();
            CognitionLedgerData final = ObservationLedgerPending || m_observationRequests.Count != 0 || discarded.Count != 0
                ? m_observationData with { Attempts = m_observationData.Attempts.Select(attempt =>
                    attempt.State == "inFlight" ? unsent.Contains(attempt.Id)
                        ? attempt with { State = "unsent", Error = "observation_ended" }
                        : attempt with { State = "unknown", Error = "send_outcome_unknown" }
                    : discarded.Contains(attempt.Id) ? attempt with { Error = "observation_ended" } : attempt).ToArray() }
                : null;
            m_observationRequests.Clear();
            m_observationFollowUps.Clear();
            FinishObservationLedger(m_observationPublication?.Write, final);
        }
    }
}
