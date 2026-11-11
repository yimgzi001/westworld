using System.Diagnostics;
using System.Text.Json;

namespace Game.Westworld {
    public sealed partial class WorldCognitionScheduler {
        sealed record ObservationPublication(CognitionLedgerData Data, Action[] Confirmed, Task<long> Write);

        CognitionLedgerData m_observationData;
        readonly List<Action> m_observationConfirmations = [];
        ObservationPublication m_observationPublication;
        Task<string> m_observationRecovery;
        double m_nextObservationWrite;

        bool ObservationLedgerPending => m_observationPublication != null || !ReferenceEquals(m_observationData, m_ledger.Data);

        void StageObservationLedger(CognitionLedgerData data, Action confirmed = null) {
            m_observationData = data;
            if (confirmed != null) {
                m_observationConfirmations.Add(confirmed);
            }
        }

        void WriteObservationBatch() {
            if (m_observationPublication != null || ReferenceEquals(m_observationData, m_ledger.Data)
                || m_quotaWriteFailed && Engine.Time.RealTime < m_nextObservationWrite) {
                return;
            }
            CognitionLedgerData data = m_observationData;
            Action[] confirmed = m_observationConfirmations.ToArray();
            m_observationConfirmations.Clear();
            m_observationPublication = new ObservationPublication(data, confirmed, Task.Run(() => WriteObservationSnapshot(data)));
        }

        long WriteObservationSnapshot(CognitionLedgerData data) {
            long started = Stopwatch.GetTimestamp();
            if (m_quota == null) CheckStorage();
            long revision = m_ledger.WriteSnapshot(data);
            m_quota?.Record(data, revision);
            return Stopwatch.GetTimestamp() - started;
        }

        void ConfirmObservationBatch() {
            if (m_observationPublication is not { Write.IsCompleted: true } publication) {
                return;
            }
            long elapsed;
            try {
                elapsed = publication.Write.GetAwaiter().GetResult();
            }
            catch (Exception error) when (m_quota != null && error is (IOException or UnauthorizedAccessException)) {
                // No send callback has been acknowledged. Retry the same reservations and settlements.
                m_observationConfirmations.InsertRange(0, publication.Confirmed);
                m_observationPublication = null;
                m_quotaWriteFailed = true;
                m_nextObservationWrite = Engine.Time.RealTime + 2;
                PauseForQuota("storage", 0);
                Engine.Log.Warning("Westworld could not publish the shared usage ledger: " + error.GetType().Name);
                return;
            }
            m_quotaWriteFailed = false;
            m_ledger.Publish(publication.Data);
            m_observationMetrics?.BackgroundWrite(elapsed);
            m_observationPublication = null;
            foreach (Action confirmed in publication.Confirmed) {
                confirmed();
            }
        }

        static CognitionAttempt[] RecoverInterruptedAttempts(CognitionAttempt[] attempts) => attempts.Select(attempt =>
            attempt.State == "inFlight" ? attempt with { State = "unknown", Error = "send_outcome_unknown" } : attempt).ToArray();

        // Allows reconciling the ledger independently of loading an observation world.
        public Task<string> RecoverObservationLedgerAsync() {
            if (m_disposed || m_project != null || m_observationRecovery != null || m_ledger.Data.Attempts.Length == 0) {
                throw new InvalidOperationException("Open an existing observation ledger without starting a world.");
            }
            CognitionLedgerData original = m_ledger.Data;
            bool interrupted = original.Attempts.Any(attempt => attempt.State == "inFlight");
            CognitionAttempt[] attempts = RecoverInterruptedAttempts(original.Attempts);
            m_observationRecovery = Task.Run(() => {
                if (interrupted) {
                    WriteObservationSnapshot(original with { Attempts = attempts });
                }
                return JsonSerializer.Serialize(new {
                    attempts = attempts.Length,
                    completed = attempts.Count(a => a.State == "completed"),
                    spent = attempts.Where(a => a.State == "completed").Sum(a => a.Charge),
                    unknown = attempts.Where(a => a.State == "unknown").Sum(a => a.Reserved)
                }, CognitionRunLedger.Json);
            });
            return m_observationRecovery;
        }

        void FinishObservationLedger(Task previous, CognitionLedgerData final = null) {
            Task closing = Task.Run(async () => {
                try {
                    if (previous != null) {
                        await previous.ConfigureAwait(false);
                    }
                    if (final != null) {
                        WriteObservationSnapshot(final);
                    }
                }
                catch (Exception error) {
                    Engine.Log.Error("Observation ledger could not close cleanly: " + error.GetType().Name);
                }
                finally {
                    m_ledger.Dispose();
                }
            });
            if (m_nativeObservation) {
                // A normal close can immediately reopen the same world or exit the process.
                closing.GetAwaiter().GetResult();
            }
        }
    }
}
