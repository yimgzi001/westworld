using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GameEntitySystem;

namespace Game.Westworld {
    public sealed partial class WorldCognitionScheduler : IDisposable {
        const int MaxActors = 50;
        const int MaxParallelism = 15;
        const double ThinkingSeconds = 1;
        const int BatchTimeoutSeconds = 120;
        const int OutputTokenLimit = ActorModelClient.MaxOutputTokens;
        sealed record Actor(ActorCognition Cognition, ActorTools Tools);
        sealed record Request(string AttemptId, int ActorId, Task<ActorModelReply> Task);

        static readonly ConditionalWeakTable<Project, WorldCognitionScheduler> m_worlds = new();
        readonly IActorDecisionSource m_source;
        readonly CognitionRunLedger m_ledger;
        readonly int m_parallelism;
        readonly SortedDictionary<int, Actor> m_actors = [];
        readonly List<Request> m_running = [];
        readonly CancellationTokenSource m_cancel = new();
        Project m_project;
        SubsystemTime m_time;
        CognitionBatch m_batch;
        long m_batchStarted;
        bool m_userPaused;
        bool m_scheduled;
        bool m_disposed;
        string m_fault;

        // Monetary values are integer nano-USD. Prices are per token.
        public WorldCognitionScheduler(IActorDecisionSource source, string directory, int parallelism,
            long budget, long inputPrice, long outputPrice, string priceVersion) {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(priceVersion);
            if (parallelism is < 1 or > MaxParallelism || budget <= 0 || inputPrice <= 0 || outputPrice <= 0) {
                throw new ArgumentOutOfRangeException(nameof(parallelism));
            }
            m_source = source;
            m_parallelism = parallelism;
            string sourceIdentity = source is ActorModelClient client ? client.SourceIdentity : source.GetType().FullName;
            m_ledger = new CognitionRunLedger(Engine.Storage.GetSystemPath(directory), budget, inputPrice, outputPrice, priceVersion,
                sourceIdentity, parallelism);
        }

        public void Register(ActorCognition cognition, ActorTools tools) {
            if (m_project != null || m_disposed || !cognition.IsBoundTo(tools) || !tools.CognitiveWorldIsCurrent || m_actors.Count >= MaxActors) {
                throw new InvalidOperationException($"Register up to {MaxActors} actors before starting.");
            }
            m_actors.Add(tools.CognitiveActorId, new Actor(cognition, tools));
        }

        public void Start(bool resume) {
            if (m_project != null || m_disposed || m_actors.Count == 0 || GameManager.Project is not { } project
                || m_actors.Values.Any(actor => !actor.Tools.CognitiveWorldIsCurrent) || m_worlds.TryGetValue(project, out _)) {
                throw new InvalidOperationException("A loaded world and an unbound scheduler are required.");
            }
            m_time = project.FindSubsystem<SubsystemTime>(true);
            if (resume) {
                RestoreCohort(project);
            }
            else if (m_ledger.Data.Batch != null || m_ledger.Data.Attempts.Length != 0 || m_loaded.TryGetValue(project, out _)) {
                throw new InvalidOperationException("Use the matching checkpoint and resume the existing run.");
            }
            m_project = project;
            m_worlds.Add(project, this);
        }

        public void SetPaused(bool paused) {
            if (m_userPaused && !paused && m_batchStarted != 0) {
                m_batchStarted = Stopwatch.GetTimestamp();
            }
            m_userPaused = paused;
        }

        public void IncreaseBudget(long total) {
            if (total <= m_ledger.Data.Budget) {
                throw new ArgumentOutOfRangeException(nameof(total));
            }
            m_ledger.Save(m_ledger.Data with { Budget = total });
            if (m_batchStarted != 0) {
                m_batchStarted = Stopwatch.GetTimestamp();
            }
        }

        public string StatusJson() {
            if (m_observing) {
                return ObservationStatusJson();
            }
            CognitionBatch batch = m_batch ?? m_ledger.Data.Batch;
            return JsonSerializer.Serialize(new {
                state = m_fault != null || m_batch?.Fault != null ? "faulted" : m_userPaused ? "userPaused"
                    : m_batch == null ? "running" : m_scheduled ? "scheduled" : !CanAfford(m_batch) ? "budgetPaused" : "waiting",
                batchId = batch?.Id, time = batch?.Time, fault = m_fault ?? batch?.Fault,
                members = batch?.Members.Select(m => new { m.ActorId, m.TurnId }),
                attempts = batch == null ? [] : m_ledger.Attempts(batch), spent = m_ledger.Spent, unknown = m_ledger.Unknown,
                reserved = m_batch is { Reserved: true } ? m_ledger.Outstanding(m_batch) : 0, budget = m_ledger.Data.Budget
            }, CognitionRunLedger.Json);
        }

        internal static bool PumpWorld(Project project) {
            if (!m_worlds.TryGetValue(project, out WorldCognitionScheduler scheduler)) {
                return true;
            }
            scheduler.Guard(scheduler.m_observing ? scheduler.PumpObservation : scheduler.Pump);
            return scheduler.CanAdvance;
        }

        internal static bool BeforeNativeStep(Project project) =>
            !m_worlds.TryGetValue(project, out WorldCognitionScheduler scheduler) || scheduler.CanAdvance;

        internal static bool OwnsTaskPhase(Project project) =>
            m_worlds.TryGetValue(project, out WorldCognitionScheduler scheduler) && !scheduler.m_observing;

        internal static void AfterNativeStep(Project project, float dt) {
            if (m_worlds.TryGetValue(project, out WorldCognitionScheduler scheduler) && !scheduler.m_observing) {
                scheduler.Guard(() => scheduler.AfterStep(dt));
            }
        }

        internal static void CloseWorld(Project project) {
            if (m_worlds.TryGetValue(project, out WorldCognitionScheduler scheduler)) {
                scheduler.Dispose();
            }
        }

        bool CanAdvance => !m_userPaused && m_fault == null && (m_batch == null || m_scheduled && m_batch.Fault == null);

        void Guard(Action step) {
            try {
                step();
            }
            catch (Exception error) {
                m_fault = error is IOException or UnauthorizedAccessException ? "persistence_error" : "scheduler_error";
                Engine.Log.Error("World cognition paused: " + error.GetType().Name);
            }
        }

        void AfterStep(float dt) {
            if (m_batch != null) {
                if (m_scheduled && m_time.GameTime >= m_batch.Time + ThinkingSeconds) {
                    foreach (CognitionMember member in m_batch.Members) {
                        m_actors[member.ActorId].Cognition.InstallScheduled(member.TurnId, member.Decision);
                    }
                    m_batch = null;
                    m_scheduled = false;
                }
                ActorTaskScheduler.For(m_project).RunPhase(dt);
                return;
            }
            ActorTaskScheduler.For(m_project).RunPhase(dt);
            // A cohort checkpoint cannot capture an in-flight atomic action.
            if (m_actors.Values.Any(actor => actor.Tools.CognitiveActionRunning)) {
                return;
            }
            List<CognitionMember> members = [];
            foreach ((int actorId, Actor actor) in m_actors) {
                if (actor.Tools.CognitiveWorldIsCurrent && actor.Cognition.PrepareTurn(cohort: true) is string input) {
                    string id = actor.Cognition.ScheduledInput.Context.Id;
                    if (!actor.Cognition.TryClaimModelTurn(id)) {
                        continue;
                    }
                    string schema = actor.Tools.DescribeDecisionTools(cohort: true);
                    long estimate = checked((Encoding.UTF8.GetByteCount(input) + Encoding.UTF8.GetByteCount(schema) + 4096L)
                        * m_ledger.Data.InputPrice + OutputTokenLimit * m_ledger.Data.OutputPrice);
                    members.Add(new CognitionMember(actorId, id, input, schema, estimate));
                }
            }
            if (members.Count == 0) {
                return;
            }
            CognitionBatch batch = new(Guid.NewGuid().ToString("N"), m_time.GameTime, members.ToArray());
            SaveCohort(batch);
            m_batchStarted = 0;
            SaveBatch(batch);
        }

        bool CanAfford(CognitionBatch batch) =>
            checked(m_ledger.Spent + m_ledger.Unknown + m_ledger.Outstanding(batch)) <= m_ledger.Data.Budget;

        void Pump() {
            if (m_batch == null || m_scheduled || m_fault != null) {
                return;
            }
            foreach (Request request in m_running.Where(r => r.Task.IsCompleted).ToArray()) {
                Complete(request);
                m_running.Remove(request);
            }
            if (m_batch.Fault != null || m_userPaused || !CanAfford(m_batch)) {
                return;
            }
            if (m_batch.Members.All(member => member.Decision != null)) {
                m_scheduled = true;
                return;
            }
            if (m_batchStarted != 0 && Stopwatch.GetElapsedTime(m_batchStarted).TotalSeconds >= BatchTimeoutSeconds) {
                SaveBatch(m_batch with { Fault = "batch_timeout" });
                return;
            }
            if (!m_batch.Reserved) {
                SaveBatch(m_batch with { Reserved = true });
            }

            CognitionAttempt[] attempts = m_ledger.Attempts(m_batch);
            IEnumerable<CognitionMember> ready = m_batch.Members
                .Where(member => member.Decision == null && !m_running.Any(r => r.ActorId == member.ActorId)
                    && (member.RetryAt == null || member.RetryAt <= DateTimeOffset.UtcNow))
                .OrderBy(member => attempts.Count(a => a.ActorId == member.ActorId));
            foreach (CognitionMember member in ready.Take(m_parallelism - m_running.Count).ToArray()) {
                Dispatch(member, attempts.Count(a => a.ActorId == member.ActorId) + 1);
            }
        }

        void Dispatch(CognitionMember member, int number) {
            if (number > 2) {
                throw new InvalidOperationException("Retry limit exceeded.");
            }
            CheckStorage();
            CognitionAttempt attempt = new(Guid.NewGuid().ToString("N"), m_batch.Id, member.ActorId,
                number, member.PerAttempt, "inFlight", DateTimeOffset.UtcNow);
            m_ledger.Save(m_ledger.Data with { Attempts = [..m_ledger.Data.Attempts, attempt] });
            if (m_batchStarted == 0) {
                m_batchStarted = Stopwatch.GetTimestamp();
            }
            CancellationToken token = m_cancel.Token;
            Task<ActorModelReply> task = Task.Run(() => m_source.RequestAsync(member.Input, member.Tools, token), token);
            m_running.Add(new Request(attempt.Id, member.ActorId, task));
        }

        void Complete(Request request) {
            ActorModelReply reply;
            try {
                reply = request.Task.GetAwaiter().GetResult() ?? new ActorModelReply(null, Error: "decision_source_failed");
            }
            catch (Exception) {
                reply = new ActorModelReply(null, Error: "decision_source_failed");
            }
            CognitionAttempt old = m_ledger.Data.Attempts.Single(a => a.Id == request.AttemptId);
            bool known = reply.InputTokens is >= 0 && reply.OutputTokens is >= 0;
            long charge = known ? checked(reply.InputTokens.Value * m_ledger.Data.InputPrice + reply.OutputTokens.Value * m_ledger.Data.OutputPrice) : 0;
            string error = reply.Error ?? (!known ? "missing_usage" : m_actors[request.ActorId].Cognition.ValidateScheduled(reply.DecisionJson));
            bool retry = old.Number == 1 && (reply.Status == 429
                && (reply.RetryAfterSeconds ?? 1) is >= 0 and <= BatchTimeoutSeconds
                || known && reply.Error == null && error != null);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            CognitionAttempt updated = old with {
                State = known ? "completed" : "unknown", Charge = charge, ReceivedAt = now, Model = reply.Model, RequestId = reply.RequestId,
                InputTokens = reply.InputTokens, OutputTokens = reply.OutputTokens, Error = error, HttpStatus = reply.Status
            };
            CognitionBatch batch = m_batch with {
                Members = m_batch.Members.Select(member => member.ActorId == request.ActorId ? member with {
                    Decision = error == null ? reply.DecisionJson : null,
                    RetryAt = retry ? now.AddSeconds(reply.RetryAfterSeconds ?? 1) : null
                } : member).ToArray(),
                Fault = m_batch.Fault ?? (retry ? null : error)
            };
            SaveBatch(batch, m_ledger.Data.Attempts.Select(a => a.Id == updated.Id ? updated : a).ToArray());
        }

        void SaveBatch(CognitionBatch batch, CognitionAttempt[] attempts = null) {
            m_ledger.Save(m_ledger.Data with { Batch = batch, Attempts = attempts ?? m_ledger.Data.Attempts });
            m_batch = batch;
        }

        public void Dispose() {
            if (m_disposed) {
                return;
            }
            m_disposed = true;
            if (m_project != null) {
                m_worlds.Remove(m_project);
            }
            try {
                m_cancel.Cancel();
                CloseObservation();
            }
            finally {
                m_cancel.Dispose();
                m_ledger.Dispose();
            }
        }
    }
}
