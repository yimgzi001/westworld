using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Westworld {
    internal sealed record CognitionMember(int ActorId, string TurnId, string Input, string Tools, long PerAttempt,
        string Decision = null, DateTimeOffset? RetryAt = null);
    internal sealed record CognitionBatch(string Id, double Time, CognitionMember[] Members, bool Reserved = false, string Fault = null);
    internal sealed record CognitionAttempt(string Id, string BatchId, int ActorId, int Number, long Reserved, string State,
        DateTimeOffset SentAt, DateTimeOffset? ReceivedAt = null, long Charge = 0, string Model = null, string RequestId = null,
        int? InputTokens = null, int? OutputTokens = null, string Error = null, int? HttpStatus = null, Guid? PrecedingTask = null);
    internal sealed record CognitionLedgerData(int Format, string ExperimentId, long Budget, long InputPrice, long OutputPrice,
        string PriceVersion, string SourceIdentity, int Parallelism, CognitionBatch Batch, CognitionAttempt[] Attempts);

    // Each publication is one durable replacement under an exclusive run lock.
    internal sealed class CognitionRunLedger : IDisposable {
        internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
        };
        readonly FileStream m_lock;
        readonly string m_path;
        internal string DirectoryPath { get; }
        internal CognitionLedgerData Data { get; private set; }

        internal CognitionRunLedger(string directory, long budget, long inputPrice, long outputPrice, string priceVersion, string sourceIdentity, int parallelism) {
            DirectoryPath = directory;
            Directory.CreateDirectory(directory);
            m_path = Path.Combine(directory, "ledger.json");
            m_lock = new FileStream(Path.Combine(directory, "ledger.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try {
                Data = File.Exists(m_path) ? JsonSerializer.Deserialize<CognitionLedgerData>(File.ReadAllText(m_path), Json)
                    : new CognitionLedgerData(1, Guid.NewGuid().ToString("N"), budget, inputPrice, outputPrice, priceVersion, sourceIdentity, parallelism, null, []);
                if (Data == null || Data.Format != 1 || Data.InputPrice != inputPrice || Data.OutputPrice != outputPrice
                    || Data.PriceVersion != priceVersion || Data.SourceIdentity != sourceIdentity || Data.Parallelism != parallelism
                    || Data.Attempts == null) {
                    throw new InvalidDataException("Incompatible cognition ledger.");
                }
            }
            catch {
                m_lock.Dispose();
                throw;
            }
        }

        internal void Save(CognitionLedgerData data) {
            WriteSnapshot(data);
            Publish(data);
        }

        // Observation writes on one worker and publishes the acknowledged snapshot on the game thread.
        internal void WriteSnapshot(CognitionLedgerData data) {
            string temporary = m_path + ".tmp";
            using (FileStream file = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(file, data, Json);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, m_path, overwrite: true);
        }

        internal void Publish(CognitionLedgerData data) => Data = data;

        internal long Spent => Data.Attempts.Where(a => a.State == "completed").Sum(a => a.Charge);
        internal long Unknown => Data.Attempts.Where(a => a.State == "unknown").Sum(a => a.Reserved);

        internal CognitionAttempt[] Attempts(CognitionBatch batch) =>
            Data.Attempts.Where(attempt => attempt.BatchId == batch.Id).ToArray();

        internal long Outstanding(CognitionBatch batch) {
            CognitionAttempt[] attempts = Attempts(batch);
            return batch.Members.Where(member => member.Decision == null).Sum(member =>
                checked(member.PerAttempt * (2 - attempts.Count(a => a.ActorId == member.ActorId && a.State != "inFlight"))));
        }

        public void Dispose() => m_lock.Dispose();
    }
}
