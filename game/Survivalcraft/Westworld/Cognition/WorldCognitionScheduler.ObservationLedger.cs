using System.Diagnostics;
using System.Text.Json;

namespace Game.Westworld {
    public sealed partial class WorldCognitionScheduler {
        sealed record ObservationPublication(CognitionLedgerData Data, Action[] Confirmed, Task<long> Write);

        CognitionLedgerData m_observationData;
        readonly List<Action> m_observationConfirmations = [];
        ObservationPublication m_observationPublication;
        Task<string> m_observationRecovery;

        bool ObservationLedgerPending => m_observationPublication != null || !ReferenceEquals(m_observationData, m_ledger.Data);

        void StageObservationLedger(CognitionLedgerData data, Action confirmed = null) {
            m_observationData = data;
            if (confirmed != null) {
                m_observationConfirmations.Add(confirmed);
            }
        }

        void WriteObservationBatch() {
            if (m_observationPublication != null || ReferenceEquals(m_observationData, m_ledger.Data)) {
                return;
            }
            CognitionLedgerData data = m_observationData;
            Action[] confirmed = m_observationConfirmations.ToArray();
            m_observationConfirmations.Clear();
            m_observationPublication = new ObservationPublication(data, confirmed, Task.Run(() => WriteObservationSnapshot(data)));
        }

        long WriteObservationSnapshot(CognitionLedgerData data) {
            long started = Stopwatch.GetTimestamp();
            CheckStorage();
            m_ledger.WriteSnapshot(data);
            return Stopwatch.GetTimestamp() - started;
        }

        void ConfirmObservationBatch() {
            if (m_observationPublication is not { Write.IsCompleted: true } publication) {
                return;
            }
            long elapsed = publication.Write.GetAwaiter().GetResult();
            m_ledger.Publish(publication.Data);
            m_observationMetrics?.BackgroundWrite(elapsed);
            m_observationPublication = null;
            foreach (Action confirmed in publication.Confirmed) {
                confirmed();
            }
        }

        // Recover financial history only. Observation has no matching world/actor checkpoint to replay.
        public Task<string> RecoverObservationLedgerAsync() {
            if (m_disposed || m_project != null || m_observationRecovery != null
                || m_ledger.Data.Batch != null || m_ledger.Data.Attempts.Length == 0) {
                throw new InvalidOperationException("Open an existing observation ledger without starting a world.");
            }
            CognitionLedgerData original = m_ledger.Data;
            bool interrupted = original.Attempts.Any(attempt => attempt.State == "inFlight");
            CognitionAttempt[] attempts = original.Attempts.Select(attempt => attempt.State == "inFlight"
                ? attempt with { State = "unknown", Error = "send_outcome_unknown" } : attempt).ToArray();
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
            _ = Task.Run(async () => {
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
        }
    }
}
