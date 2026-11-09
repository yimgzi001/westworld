using System.Diagnostics;
using System.Text.Json;

namespace Game.Westworld {
    public sealed partial class WorldCognitionScheduler {
        sealed class ObservationMetrics {
            readonly long m_started = Stopwatch.GetTimestamp();
            readonly HashSet<int> m_dispatched = [];
            readonly Dictionary<int, (Guid TaskId, long Time)> m_tasks = [];
            readonly List<long> m_initialWait = [];
            readonly List<long> m_lookAheadWait = [];
            readonly List<long> m_scheduler = [];
            readonly List<long> m_backgroundLedger = [];
            int m_tasksOffered;

            internal void TaskChanged(int actorId, Guid taskId, bool started) {
                if (started) {
                    m_tasks[actorId] = (taskId, Stopwatch.GetTimestamp());
                    m_tasksOffered++;
                }
                else if (m_tasks.TryGetValue(actorId, out var task) && task.TaskId == taskId) {
                    m_tasks.Remove(actorId);
                }
            }

            internal void Dispatched(int actorId, Guid? precedingTask) {
                long now = Stopwatch.GetTimestamp();
                if (m_dispatched.Add(actorId) && precedingTask == null) {
                    m_initialWait.Add(now - m_started);
                }
                if (precedingTask is Guid taskId && m_tasks.TryGetValue(actorId, out var task) && task.TaskId == taskId) {
                    m_lookAheadWait.Add(now - task.Time);
                }
            }

            internal void BackgroundWrite(long elapsed) => m_backgroundLedger.Add(elapsed);

            internal void Frame(long elapsed) => m_scheduler.Add(elapsed);

            internal string Snapshot() => JsonSerializer.Serialize(new {
                frames = m_scheduler.Count, ledgerWrites = m_backgroundLedger.Count, tasksOffered = m_tasksOffered,
                initialQueue = Summarize(m_initialWait), lookAheadQueue = Summarize(m_lookAheadWait),
                schedulerPerFrame = Summarize(m_scheduler),
                ledgerPerFrame = new { count = m_scheduler.Count, totalMs = 0, p50Ms = 0, p95Ms = 0, maxMs = 0 },
                backgroundLedgerBatch = Summarize(m_backgroundLedger)
            }, CognitionRunLedger.Json);

            static object Summarize(List<long> samples) {
                long[] ordered = samples.Order().ToArray();
                double Percentile(double fraction) => ordered.Length == 0 ? 0
                    : Milliseconds(ordered[(int)Math.Ceiling(ordered.Length * fraction) - 1]);
                return new {
                    count = ordered.Length, totalMs = Milliseconds(ordered.Sum()),
                    p50Ms = Percentile(.50), p95Ms = Percentile(.95), maxMs = Percentile(1)
                };
            }

            static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
        }

        ObservationMetrics m_observationMetrics;

        public string ObservationMetricsJson() => m_observationMetrics?.Snapshot() ?? "{}";

    }
}
