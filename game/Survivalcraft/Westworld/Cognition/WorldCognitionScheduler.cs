using System.Runtime.CompilerServices;
using GameEntitySystem;

namespace Game.Westworld {
    public sealed partial class WorldCognitionScheduler : IDisposable {
        const int MaxActors = 50;
        const int MaxParallelism = 15;
        const int OutputTokenLimit = ActorModelClient.MaxOutputTokens;
        sealed record Actor(ActorCognition Cognition, ActorTools Tools);

        static readonly ConditionalWeakTable<Project, WorldCognitionScheduler> m_worlds = new();
        readonly IActorDecisionSource m_source;
        readonly CognitionRunLedger m_ledger;
        readonly int m_parallelism;
        readonly SortedDictionary<int, Actor> m_actors = [];
        readonly CancellationTokenSource m_cancel = new();
        Project m_project;
        SubsystemTime m_time;
        bool m_userPaused;
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

        Project RequireUnboundWorld() {
            if (m_project != null || m_disposed || m_observationRecovery != null || m_actors.Count == 0 || GameManager.Project is not { } project
                || m_actors.Values.Any(actor => !actor.Tools.CognitiveWorldIsCurrent) || m_worlds.TryGetValue(project, out _)) {
                throw new InvalidOperationException("A loaded world and an unbound scheduler are required.");
            }
            return project;
        }

        void BindWorld(Project project) {
            m_project = project;
            m_worlds.Add(project, this);
        }

        public void SetPaused(bool paused) => m_userPaused = paused;

        public void IncreaseBudget(long total) {
            if (m_project == null || m_disposed) {
                throw new InvalidOperationException("No active ledger can accept a budget change.");
            }
            if (total <= m_observationData.Budget) {
                throw new ArgumentOutOfRangeException(nameof(total));
            }
            StageObservationLedger(m_observationData with { Budget = total });
        }

        public string StatusJson() => ObservationStatusJson();

        internal static bool PumpWorld(Project project) {
            if (!m_worlds.TryGetValue(project, out WorldCognitionScheduler scheduler)) {
                return true;
            }
            try {
                scheduler.PumpObservation();
            }
            catch (Exception error) {
                scheduler.m_fault = error is IOException or UnauthorizedAccessException ? "persistence_error" : "scheduler_error";
                Engine.Log.Error("World cognition paused: " + error.GetType().Name);
            }
            return scheduler.CanAdvance;
        }

        internal static bool BeforeNativeStep(Project project) =>
            !m_worlds.TryGetValue(project, out WorldCognitionScheduler scheduler) || scheduler.CanAdvance;

        internal static void CloseWorld(Project project) {
            if (m_worlds.TryGetValue(project, out WorldCognitionScheduler scheduler)) {
                scheduler.Dispose();
            }
        }

        bool CanAdvance => !m_userPaused && m_fault == null && m_quotaPause == null;

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
            }
            finally {
                m_cancel.Dispose();
                try {
                    if (m_observationReplies != null) {
                        CloseObservation();
                    }
                    else if (m_observationRecovery != null) {
                        FinishObservationLedger(m_observationRecovery);
                    }
                    else {
                        m_ledger.Dispose();
                    }
                }
                finally {
                    m_quota?.Dispose();
                }
            }
        }
    }
}
