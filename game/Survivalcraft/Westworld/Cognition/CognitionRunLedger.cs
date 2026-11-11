using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Westworld {
    internal sealed record CognitionAttempt(string Id, [property: JsonPropertyName("batchId")] string TurnId, int ActorId,
        long Reserved, string State,
        DateTimeOffset SentAt, DateTimeOffset? ReceivedAt = null, long Charge = 0, string Model = null, string RequestId = null,
        int? InputTokens = null, int? OutputTokens = null, string Error = null, int? HttpStatus = null, Guid? PrecedingTask = null,
        string Decision = null);
    internal sealed record CognitionLedgerData(int Format, string ExperimentId, long Budget, long InputPrice, long OutputPrice,
        string PriceVersion, string SourceIdentity, int Parallelism, CognitionAttempt[] Attempts,
        [property: JsonPropertyName("batch")] JsonElement? LegacyBatch = null, long Revision = 0);

    // Each publication is one durable replacement under an exclusive run lock.
    internal sealed class CognitionRunLedger : IDisposable {
        internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
        };
        readonly FileStream m_lock;
        readonly string m_path;
        long m_writtenRevision;
        internal string DirectoryPath { get; }
        internal bool Exists => File.Exists(m_path);
        internal CognitionLedgerData Data { get; private set; }

        internal CognitionRunLedger(string directory, long budget, long inputPrice, long outputPrice, string priceVersion, string sourceIdentity, int parallelism) {
            DirectoryPath = directory;
            Directory.CreateDirectory(directory);
            m_path = Path.Combine(directory, "ledger.json");
            m_lock = new FileStream(Path.Combine(directory, "ledger.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try {
                Data = File.Exists(m_path) ? JsonSerializer.Deserialize<CognitionLedgerData>(File.ReadAllText(m_path), Json)
                    : new CognitionLedgerData(1, Guid.NewGuid().ToString("N"), budget, inputPrice, outputPrice, priceVersion, sourceIdentity, parallelism, []);
                if (Data == null || Data.Format != 1 || Data.InputPrice != inputPrice || Data.OutputPrice != outputPrice
                    || Data.PriceVersion != priceVersion || Data.SourceIdentity != sourceIdentity || Data.Parallelism != parallelism
                    || Data.Attempts == null) {
                    throw new InvalidDataException("Incompatible cognition ledger.");
                }
                if (Data.LegacyBatch != null) {
                    throw new InvalidDataException("This ledger belongs to the retired frozen scheduler.");
                }
                m_writtenRevision = Data.Revision;
            }
            catch {
                m_lock.Dispose();
                throw;
            }
        }

        // Observation writes on one worker and publishes the acknowledged snapshot on the game thread.
        internal long WriteSnapshot(CognitionLedgerData data) {
            long revision = checked(m_writtenRevision + 1);
            string temporary = m_path + ".tmp";
            using (FileStream file = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(file, data with { Revision = revision }, Json);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, m_path, overwrite: true);
            m_writtenRevision = revision;
            return revision;
        }

        internal void Publish(CognitionLedgerData data) => Data = data;

        internal long Spent => Data.Attempts.Where(a => a.State == "completed").Sum(a => a.Charge);
        internal long Unknown => Data.Attempts.Where(a => a.State == "unknown").Sum(a => a.Reserved);

        public void Dispose() => m_lock.Dispose();
    }
}
