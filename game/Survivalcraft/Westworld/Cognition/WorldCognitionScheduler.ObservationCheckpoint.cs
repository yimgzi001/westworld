using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    internal readonly record struct ObservationPose(float X, float Y, float Z, float FacingX, float FacingY, float FacingZ) {
        internal ObservationPose(SelfView body) : this(body.Position.X, body.Position.Y, body.Position.Z,
            body.Facing.X, body.Facing.Y, body.Facing.Z) { }
        internal Vector3 Position => new(X, Y, Z);
        internal Vector3 Facing => new(FacingX, FacingY, FacingZ);
    }

    public sealed partial class WorldCognitionScheduler {
        internal const string ObservationElement = "WestworldObservation";
        const string ObservationFile = "observation.scworld";
        const double CheckpointInterval = 10;
        const long StorageLimit = 15_000_000_000;
        const long WriteHeadroom = 4_000_000;

        sealed record SavedObservationTurn(string AttemptId, ActorCognition.Pending Input, ObservationPose Pose);
        sealed record SavedObservationActor(int Id, ActorCognition.Snapshot Cognition, ActorTaskSnapshot Task, SavedObservationTurn Turn);
        sealed record ObservationCheckpoint(int Format, string ExperimentId, double Time, int TaskOrderCursor,
            int RequestOrderCursor, int[] LookAheadReady, SavedObservationActor[] Actors);

        static readonly ConditionalWeakTable<Project, string> m_loadedObservations = new();
        readonly HashSet<int> m_observationUncertainActors = [];
        double m_nextCheckpoint;
        double? m_checkpointTime;
        bool m_checkpointRequested;
        bool m_initialLedgerReady;
        bool m_recoveryLedgerPending;
        bool m_restoredRepliesPending;
        int m_reusedReplies;

        internal static void RegisterLoadedObservation(Project project, XElement node) {
            if (node.Element(ObservationElement) is { } value) {
                m_loadedObservations.Add(project, value.Value);
            }
            else if (node.Element("WestworldCohort") != null) {
                // Reject old frozen archives as an implicit new observation run.
                m_loadedObservations.Add(project, string.Empty);
            }
        }

        public void RequestObservationCheckpoint() {
            if (m_project == null || m_disposed || m_observationEnded) {
                throw new InvalidOperationException("An active observation run is required.");
            }
            m_checkpointRequested = true;
            ActorTaskScheduler.For(m_project).HoldStarts = true;
        }

        void SaveObservationCheckpointWhenReady() {
            if ((m_checkpointTime == null && !m_initialLedgerReady) || (m_observationEnded && !m_checkpointRequested)) {
                return;
            }
            if (m_time.GameTime >= m_nextCheckpoint && !m_checkpointRequested) {
                RequestObservationCheckpoint();
            }
            if (!m_checkpointRequested || m_observationPublication != null
                || m_actors.Values.Any(actor => !actor.Tools.CognitiveAtCheckpointBoundary)) {
                return;
            }

            ActorTaskScheduler tasks = ActorTaskScheduler.For(m_project);
            SavedObservationActor[] actors = m_actors.Select(entry => {
                (int id, Actor actor) = entry;
                ObservationRequest request = m_observationRequests.GetValueOrDefault(id)
                    ?? m_observationFollowUps.GetValueOrDefault(id)?.Request;
                ActorCognition.Pending pending = actor.Cognition.PendingTurn;
                SavedObservationTurn turn = request != null && pending?.Context.Id == request.TurnId
                    ? new SavedObservationTurn(request.Attempt.Id, pending, request.Pose) : null;
                return new SavedObservationActor(id, actor.Cognition.CaptureState(), actor.Tools.CaptureWorldTask(), turn);
            }).ToArray();
            ObservationCheckpoint checkpoint = new(1, m_ledger.Data.ExperimentId, m_time.GameTime, tasks.LastFirstActor,
                m_lastObservationActor, m_observationLookAheadReady.ToArray(), actors);
            string payload = JsonSerializer.Serialize(checkpoint, CognitionRunLedger.Json);
            string world = Storage.GetSystemPath(m_project.FindSubsystem<SubsystemGameInfo>(true).DirectoryName);
            CheckStorage(checked(SizeOf(world) + payload.Length * 4L));
            ActorTools.WriteTaskArchive(m_project, Path.Combine(m_ledger.DirectoryPath, ObservationFile), null, null, observation: payload);
            m_checkpointTime = checkpoint.Time;
            m_nextCheckpoint = checkpoint.Time + CheckpointInterval;
            m_checkpointRequested = false;
            tasks.HoldStarts = false;
        }

        public void ResumeObservation() {
            Project project = RequireUnboundWorld();
            m_time = project.FindSubsystem<SubsystemTime>(true);
            if (m_time.GameTime != 0 || !m_loadedObservations.TryGetValue(project, out string payload) || payload.Length == 0) {
                throw new InvalidOperationException("Load the observation checkpoint before resuming.");
            }
            ObservationCheckpoint saved = JsonSerializer.Deserialize<ObservationCheckpoint>(payload, CognitionRunLedger.Json);
            if (saved == null || saved.Format != 1 || saved.ExperimentId != m_ledger.Data.ExperimentId
                || !double.IsFinite(saved.Time) || saved.Time < 0 || saved.Actors == null || saved.LookAheadReady == null
                || !saved.Actors.Select(actor => actor.Id).SequenceEqual(m_actors.Keys)) {
                throw new InvalidDataException("The ledger and observation checkpoint do not match.");
            }
            BindWorld(project);
            try {
                RestoreObservationState(project, saved);
            }
            catch {
                m_fault = "invalid_checkpoint";
                throw;
            }
            InitializeObservation(measure: false, resume: true);
            m_restoredRepliesPending = m_observationFollowUps.Count != 0;
            CognitionAttempt[] recovered = RecoverInterruptedAttempts(m_ledger.Data.Attempts);
            m_observationUncertainActors.UnionWith(recovered.Where(attempt => attempt.Error == "send_outcome_unknown")
                .Select(attempt => attempt.ActorId));
            if (m_ledger.Data.Attempts.Any(attempt => attempt.State == "inFlight")) {
                m_recoveryLedgerPending = true;
                StageObservationLedger(m_observationData with { Attempts = recovered }, () => m_recoveryLedgerPending = false);
            }
        }

        void RestoreObservationState(Project project, ObservationCheckpoint saved) {
            m_time.m_gameTime = saved.Time;
            ActorTaskScheduler.For(project).LastFirstActor = saved.TaskOrderCursor;
            m_lastObservationActor = saved.RequestOrderCursor;
            m_observationLookAheadReady.UnionWith(saved.LookAheadReady);
            m_checkpointTime = saved.Time;
            m_nextCheckpoint = saved.Time + CheckpointInterval;

            Dictionary<string, CognitionAttempt> attempts = m_ledger.Data.Attempts.ToDictionary(attempt => attempt.Id);
            foreach (SavedObservationActor savedActor in saved.Actors) {
                Actor actor = m_actors[savedActor.Id];
                actor.Cognition.RestoreState(savedActor.Cognition);
                if (!actor.Tools.RestoreWorldTask(savedActor.Task)) {
                    throw new InvalidDataException("Cannot restore the actor's task.");
                }
                actor.Tools.RestoreCognitiveLastTerminal(savedActor.Cognition.LastTask);
                if (savedActor.Turn is not { } turn || !attempts.TryGetValue(turn.AttemptId, out CognitionAttempt attempt)) {
                    continue;
                }
                if (attempt.ActorId != savedActor.Id || attempt.TurnId != turn.Input.Context.Id
                    || turn.Input.Context.Version != savedActor.Cognition.Version) {
                    throw new InvalidDataException("The saved turn does not match its ledger attempt.");
                }
                if (attempt is { State: "completed", Decision: not null }) {
                    actor.Cognition.RestorePendingTurn(turn.Input);
                    ObservationRequest request = new(attempt, turn.Input.Context.Id, turn.Pose);
                    m_observationFollowUps.Add(savedActor.Id, new ObservationFollowUp(request, attempt, Recovered: true));
                }
            }
        }

        void ProcessRestoredReplies() {
            if (!m_restoredRepliesPending) {
                return;
            }
            m_restoredRepliesPending = false;
            foreach ((int actorId, ObservationFollowUp followUp) in m_observationFollowUps.ToArray()) {
                if (followUp.Recovered && !AwaitingHandoff(followUp.Request)) {
                    InstallObservationReply(followUp.Request, followUp.Settled, recovered: true);
                    m_observationFollowUps.Remove(actorId);
                }
            }
        }

        void CheckStorage(long extra = 0) {
            long headroom = checked(WriteHeadroom + extra + SizeOf(m_ledger.DirectoryPath));
            if (headroom > StorageLimit || new DriveInfo(Path.GetPathRoot(m_ledger.DirectoryPath)).AvailableFreeSpace < headroom) {
                throw new IOException("Cognition run cannot reserve checkpoint storage.");
            }
        }

        static long SizeOf(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
    }
}
