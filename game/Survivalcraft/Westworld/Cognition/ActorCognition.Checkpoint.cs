using System.Text.Json;

namespace Game.Westworld {
    public sealed partial class ActorCognition {
        internal sealed record Snapshot(int Format, int ActorId, double Time, long Version, string Profile, string Goal,
            CognitiveBody Body, Guid SeenTask, ActorTaskView LastTask, double ReviewIn, double NextTurnIn, int DroppedMessages,
            CognitiveMessage[] Inbox, CognitiveNote[] Memory, CognitiveNote[] Commitments) {
            public PersonalPlan PersonalPlan { get; init; }
            public EventWait WaitingFor { get; init; }
            public RecentProcess[] RecentProcess { get; init; }
        }

        double m_timeOffset;
        double Now => m_tools.CognitiveTime + m_timeOffset;

        public void SaveCheckpoint(string path) {
            if (m_pending != null) {
                throw new InvalidOperationException("Complete or abandon the frozen decision before saving.");
            }
            m_tools.SaveCognitiveCheckpoint(path, JsonSerializer.Serialize(CaptureState(), m_json));
        }

        internal Snapshot CaptureState() {
            CollectInbox();
            double now = Now;
            ActorTaskView lastTask = LatestOutcome();
            // Session references cannot be used after a world reload. Keep the actual received message.
            CognitiveMessage[] inbox = m_inbox.Select(message => message with {
                Value = m_tools.HasProduction ? message.Value : message.Value with { Source = null, OfferReference = null }
            }).ToArray();
            return new Snapshot(1, m_tools.CognitiveActorId, now, m_version, m_profile, m_goal, m_body, m_seenTask, lastTask,
                Math.Max(0, m_reviewAt - now), Math.Max(0, m_nextTurnAt - now), m_droppedMessages, inbox,
                m_memory.ToArray(), m_commitments.ToArray()) {
                PersonalPlan = m_plan, WaitingFor = m_wait, RecentProcess = m_recentProcess.ToArray()
            };
        }

        public bool RestoreCheckpoint() {
            if (m_version != 0 || m_pending != null || m_inbox.Count != 0) {
                return false;
            }
            string payload = m_tools.ReadCognitiveCheckpoint();
            if (payload == null) {
                return false;
            }
            Snapshot snapshot = JsonSerializer.Deserialize<Snapshot>(payload, m_json);
            if (snapshot == null || snapshot.Format != 1 || snapshot.ActorId != m_tools.CognitiveActorId) {
                return false;
            }
            if (!ValidSnapshot(snapshot)) {
                throw new InvalidDataException("Invalid cognitive checkpoint.");
            }
            if (!m_tools.RestoreCognitiveCheckpoint()) {
                return false;
            }

            RestoreState(snapshot);
            return true;
        }

        internal void RestoreState(Snapshot snapshot) {
            if (snapshot.ActorId != m_tools.CognitiveActorId || snapshot.Format != 1 || !ValidSnapshot(snapshot)
                || m_version != 0 || m_pending != null || m_inbox.Count != 0) {
                throw new InvalidDataException("Invalid or mismatched cognition checkpoint.");
            }
            m_timeOffset = snapshot.Time - m_tools.CognitiveTime;
            m_version = snapshot.Version;
            m_profile = snapshot.Profile;
            m_goal = snapshot.Goal;
            m_body = snapshot.Body;
            m_seenTask = snapshot.SeenTask;
            m_restoredOutcome = snapshot.LastTask;
            m_reviewAt = snapshot.Time + snapshot.ReviewIn;
            m_nextTurnAt = snapshot.Time + snapshot.NextTurnIn;
            m_droppedMessages = snapshot.DroppedMessages;
            m_inbox.AddRange(snapshot.Inbox);
            m_memory.AddRange(snapshot.Memory);
            m_commitments.AddRange(snapshot.Commitments);
            m_plan = snapshot.PersonalPlan;
            m_wait = snapshot.WaitingFor;
            m_recentProcess.Clear();
            m_recentProcess.AddRange(snapshot.RecentProcess ?? []);
        }

        static bool ValidSnapshot(Snapshot snapshot) => snapshot.Version >= 0
            && ValidText(snapshot.Profile, 320) && snapshot.Goal is not { Length: > 256 }
            && (snapshot.Version == 0 || snapshot.Body != null)
            && double.IsFinite(snapshot.Time) && snapshot.Time >= 0
            && double.IsFinite(snapshot.ReviewIn) && snapshot.ReviewIn is >= 0 and <= 120
            && double.IsFinite(snapshot.NextTurnIn) && snapshot.NextTurnIn is >= 0 and <= MinimumThinkInterval + 1e-8
            && snapshot.DroppedMessages >= 0
            && (snapshot.LastTask == null || IsTerminal(snapshot.LastTask))
            && snapshot.Inbox is { Length: <= InboxLimit }
            && snapshot.Inbox.All(message => message?.Value != null && Guid.TryParseExact(message.Id, "N", out _)
                && (message.Value.Source == null || TryPersonReference(message.Value.Source))
                && (message.Value.OfferReference == null || Guid.TryParseExact(message.Value.OfferReference, "N", out _))
                && message.Value.Text is not { Length: > 320 })
            && snapshot.Inbox.Select(message => message.Id).Distinct(StringComparer.Ordinal).Count() == snapshot.Inbox.Length
            && ValidNotes(snapshot.Memory, MemoryLimit) && ValidNotes(snapshot.Commitments, CommitmentLimit)
            && ValidPersonalState(snapshot.PersonalPlan, snapshot.WaitingFor, snapshot.RecentProcess);

        static bool TryPersonReference(string value) => value.StartsWith("person:", StringComparison.Ordinal)
            && int.TryParse(value.AsSpan(7), out int id) && id > 0;

        static bool ValidNotes(CognitiveNote[] notes, int limit) => notes != null && notes.Length <= limit
            && notes.All(note => note != null && Guid.TryParseExact(note.Id, "N", out _) && ValidText(note.Source, 64)
                && note.Kind is "body" or "observation" or "task" or "social" && ValidText(note.Text, 256) && double.IsFinite(note.At)
                && note.Evidence.ValueKind == JsonValueKind.Object)
            && notes.Select(note => note.Id).Distinct(StringComparer.Ordinal).Count() == notes.Length;

        ActorTaskView LatestOutcome() => m_tools.CognitiveLastTerminal is { } terminal ? AtCognitiveTime(terminal) : m_restoredOutcome;

        ActorTaskView AtCognitiveTime(ActorTaskView task) =>
            task with { LastProgressTime = task.LastProgressTime + m_timeOffset };
    }
}
