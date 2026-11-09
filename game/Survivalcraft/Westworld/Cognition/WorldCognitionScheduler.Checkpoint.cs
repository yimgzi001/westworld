using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using GameEntitySystem;

namespace Game.Westworld {
    public sealed partial class WorldCognitionScheduler {
        const long StorageLimit = 15_000_000_000;
        const long WriteHeadroom = 4_000_000;
        sealed record SavedActor(int Id, ActorCognition.Snapshot Cognition, ActorCognition.Pending Input, ActorTaskSnapshot Task);
        sealed record CohortCheckpoint(int Format, string ExperimentId, string BatchId, double Time, int TaskOrderCursor, SavedActor[] Actors);
        static readonly ConditionalWeakTable<Project, string> m_loaded = new();

        internal static void RegisterLoadedCohort(Project project, XElement node) {
            if (node.Element("WestworldCohort") is { } value) {
                m_loaded.Add(project, value.Value);
            }
        }

        void SaveCohort(CognitionBatch batch) {
            SavedActor[] actors = m_actors.Select(entry => new SavedActor(entry.Key, entry.Value.Cognition.CaptureState(),
                entry.Value.Cognition.ScheduledInput, entry.Value.Tools.CaptureCohortTask())).ToArray();
            if (actors.Count(actor => actor.Input != null) != batch.Members.Length) {
                throw new InvalidOperationException("Unexpected pending decision outside this batch.");
            }
            CohortCheckpoint checkpoint = new(1, m_ledger.Data.ExperimentId, batch.Id, batch.Time,
                ActorTaskScheduler.For(m_project).LastFirstActor, actors);
            string data = JsonSerializer.Serialize(checkpoint, CognitionRunLedger.Json);
            string world = Engine.Storage.GetSystemPath(m_project.FindSubsystem<SubsystemGameInfo>(true).DirectoryName);
            CheckStorage(checked(SizeOf(world) + data.Length * 4L));
            string archive = Path.Combine(m_ledger.DirectoryPath, batch.Id + ".scworld");
            ActorTools.WriteTaskArchive(m_project, archive, null, null, data);
        }

        void RestoreCohort(Project project) {
            CognitionBatch batch = m_ledger.Data.Batch;
            if (batch == null || m_time.GameTime != 0 || !m_loaded.TryGetValue(project, out string data)) {
                throw new InvalidOperationException("Load the matching cohort archive before resuming.");
            }
            CohortCheckpoint saved = JsonSerializer.Deserialize<CohortCheckpoint>(data, CognitionRunLedger.Json);
            if (saved == null || saved.Format != 1 || saved.ExperimentId != m_ledger.Data.ExperimentId
                || saved.BatchId != batch.Id || saved.Time != batch.Time || !saved.Actors.Select(a => a.Id).SequenceEqual(m_actors.Keys)) {
                throw new InvalidDataException("The ledger and cohort archive do not match.");
            }
            foreach (CognitionMember member in batch.Members) {
                ActorCognition.Pending input = saved.Actors.Single(a => a.Id == member.ActorId).Input;
                if (input == null || input.Context.Id != member.TurnId || input.Input != member.Input) {
                    throw new InvalidDataException("The frozen input differs from the ledger.");
                }
            }
            m_time.m_gameTime = saved.Time;
            ActorTaskScheduler.For(project).LastFirstActor = saved.TaskOrderCursor;
            foreach (SavedActor actor in saved.Actors) {
                Actor binding = m_actors[actor.Id];
                binding.Cognition.RestoreState(actor.Cognition);
                if (!binding.Tools.RestoreCohortTask(actor.Task)) {
                    throw new InvalidDataException("Cannot restore the actor's task.");
                }
                binding.Cognition.RestoreScheduledInput(actor.Input);
            }
            CognitionAttempt[] attempts = m_ledger.Data.Attempts.Select(attempt => attempt.State == "inFlight"
                ? attempt with { State = "unknown", Error = "send_outcome_unknown" } : attempt).ToArray();
            bool unknownSend = attempts.Any(attempt => attempt.BatchId == batch.Id && attempt.Error == "send_outcome_unknown");
            SaveBatch(unknownSend ? batch with { Fault = "send_outcome_unknown" } : batch, attempts);
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
