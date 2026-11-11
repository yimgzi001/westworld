using Engine;

namespace Game.Westworld {
    public sealed partial class WorldCognitionScheduler {
        sealed record QuotaPause(string Reason, long Required);

        ExperimentQuota m_quota;
        QuotaPause m_quotaPause;
        bool m_quotaSaved;
        double m_nextQuotaCheck;
        double m_nextQuotaSave;
        bool m_quotaWriteFailed;

        long OtherExperimentFees => m_quota?.OtherFees ?? 0;
        long FeeLimit => m_quota?.FeeLimit ?? m_ledger.Data.Budget;
        long AvailableFees => FeeLimit - OtherExperimentFees - m_ledger.Spent - m_ledger.Unknown
            - m_observationRequests.Values.Sum(request => request.Attempt.Reserved);

        void CheckQuota() {
            if (m_quota == null || Time.RealTime < m_nextQuotaCheck) {
                return;
            }
            m_nextQuotaCheck = Time.RealTime + 1;
            try {
                if (m_quotaPause != null) {
                    m_quota.ReadIncreaseWhilePaused();
                }
                MeasureQuota();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                PauseForQuota("storage", 0);
                return;
            }
            if (!m_quota.CanAdvance()) {
                PauseForQuota("storage", 0);
            }
            else if (m_quotaSaved && !m_recoveryLedgerPending && !m_quotaWriteFailed && m_quotaPause is { } pause
                && (pause.Reason == "budget" ? AvailableFees >= pause.Required : m_quota.CanAdvance(pause.Required))) {
                m_quotaPause = null;
                m_quotaSaved = false;
                m_observationBackoff = default;
                Log.Information("Westworld quota pause ended at the saved simulation time.");
            }
        }

        void MeasureQuota() => m_quota.Measure(m_project, m_ledger.DirectoryPath, m_parallelism);

        void PauseForQuota(string reason, long required) {
            if (m_quotaPause != null) {
                return;
            }
            m_quotaPause = new QuotaPause(reason, required);
            m_quotaSaved = false;
            m_nextQuotaSave = 0;
            Log.Information($"Westworld paused at simulation time {m_time.GameTime:R}: {reason} allowance.");
            ComponentPlayer player = m_project.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers.FirstOrDefault();
            player?.ComponentGui.DisplaySmallMessage(reason == "budget" ? "已暂停：累计模型额度不足。" : "已暂停：存储余量不足。",
                Color.White, true, false);
        }

        void SaveQuotaPause() {
            if (m_quotaPause == null || m_quotaSaved || Time.RealTime < m_nextQuotaSave) {
                return;
            }
            m_nextQuotaSave = Time.RealTime + 2;
            m_quotaSaved = GameManager.TrySaveQuotaPause();
        }
    }
}
