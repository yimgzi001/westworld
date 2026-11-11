using System.Text.Json;
using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    // One local installation owns the shared allowance while a founding world is running.
    internal sealed class ExperimentQuota : IDisposable {
        internal const long DefaultStorageLimit = 15_000_000_000;
        const long SaveHeadroom = 4_000_000;
        const long MaxRegionChunkGrowth = 530_000;

        sealed record RunUsage(string ExperimentId, long Revision, long Spent, long Unknown, long Reserved) {
            internal long Total => checked(Spent + Unknown + Reserved);

            internal static RunUsage From(CognitionLedgerData ledger, long revision) => new(ledger.ExperimentId, revision,
                ledger.Attempts.Where(attempt => attempt.State == "completed").Sum(attempt => attempt.Charge),
                ledger.Attempts.Where(attempt => attempt.State == "unknown").Sum(attempt => attempt.Reserved),
                ledger.Attempts.Where(attempt => attempt.State == "inFlight").Sum(attempt => attempt.Reserved));
        }

        sealed record Account(int Format, long FeeNanoUsd, long StorageBytes, Dictionary<string, RunUsage> Runs);
        sealed record Increase(long? FeeNanoUsd, long? StorageBytes);

        readonly string m_root;
        readonly string m_run;
        readonly string m_accountPath;
        readonly FileStream m_lock;
        readonly object m_sync = new();
        readonly string[] m_storageRoots;
        readonly string m_uploadFile;
        Account m_account;

        internal long FeeLimit => Volatile.Read(ref m_account).FeeNanoUsd;
        internal long StorageLimit => Volatile.Read(ref m_account).StorageBytes;
        internal long OtherFees { get; }
        internal long StorageUsed { get; private set; }
        internal long StorageReserve { get; private set; }
        internal long DiskFree { get; private set; }

        internal ExperimentQuota(CognitionRunLedger current, long feeLimit, long? storageLimit) {
            DirectoryInfo runDirectory = Directory.GetParent(current.DirectoryPath);
            m_run = runDirectory.Name;
            m_root = runDirectory.Parent.FullName;
            m_accountPath = Path.Combine(m_root, "quota-account.json");
            m_storageRoots = [m_root, Storage.GetSystemPath(WorldsManager.WorldsDirectoryName),
                Storage.GetSystemPath(ModsManager.LogPath), Storage.GetSystemPath("app:doc/Exports")];
            m_uploadFile = Storage.GetSystemPath(Storage.CombinePaths(ModsManager.DocPath, "WorldUpload.tmp"));
            m_lock = new FileStream(Path.Combine(m_root, "quota.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try {
                Account previous = File.Exists(m_accountPath) ? Read<Account>(m_accountPath) : null;
                if (previous != null && (previous.Format != 1 || previous.FeeNanoUsd <= 0 || previous.StorageBytes <= 0 || previous.Runs == null)) {
                    throw new InvalidDataException("Invalid Westworld quota account.");
                }
                Dictionary<string, RunUsage> runs = previous?.Runs ?? [];
                foreach (string directory in Directory.EnumerateDirectories(m_root)) {
                    string key = Path.GetFileName(directory);
                    string ledgerPath = Path.Combine(directory, "cognition", "ledger.json");
                    if (!Guid.TryParseExact(key, "N", out _) || !File.Exists(ledgerPath)) continue;
                    CognitionLedgerData ledger = key == m_run ? current.Data : Read<CognitionLedgerData>(ledgerPath);
                    if (ledger.Format != 1 || ledger.ExperimentId == null || ledger.Attempts == null || ledger.Revision < 0) {
                        throw new InvalidDataException("Invalid experiment ledger in the Westworld quota account.");
                    }
                    if (runs.TryGetValue(key, out RunUsage recorded)
                        && (ledger.ExperimentId != recorded.ExperimentId || ledger.Revision < recorded.Revision)) {
                        if (key == m_run) throw new InvalidDataException("The active experiment ledger is older than the quota account.");
                        continue;
                    }
                    runs[key] = RunUsage.From(ledger, ledger.Revision);
                }
                if (runs.ContainsKey(m_run) && !current.Exists) {
                    throw new InvalidDataException("The active experiment ledger is missing.");
                }
                OtherFees = runs.Where(entry => entry.Key != m_run).Sum(entry => entry.Value.Total);
                Increase requested = ReadIncrease();
                long fees = Math.Max(feeLimit, Math.Max(previous?.FeeNanoUsd ?? 0, requested?.FeeNanoUsd ?? 0));
                long initialStorage = previous?.StorageBytes ?? storageLimit ?? DefaultStorageLimit;
                long bytes = Math.Max(initialStorage, Math.Max(storageLimit ?? 0, requested?.StorageBytes ?? 0));
                Publish(new Account(1, fees, bytes, runs));
            }
            catch {
                m_lock.Dispose();
                throw;
            }
        }

        internal void Record(CognitionLedgerData ledger, long revision) {
            lock (m_sync) {
                Dictionary<string, RunUsage> runs = new(m_account.Runs) { [m_run] = RunUsage.From(ledger, revision) };
                Publish(m_account with { Runs = runs });
            }
        }

        internal void ReadIncreaseWhilePaused() {
            Increase requested = ReadIncrease();
            if (requested == null || (requested.FeeNanoUsd.GetValueOrDefault() <= FeeLimit
                && requested.StorageBytes.GetValueOrDefault() <= StorageLimit)) return;
            lock (m_sync) {
                Publish(m_account with {
                    FeeNanoUsd = Math.Max(m_account.FeeNanoUsd, requested.FeeNanoUsd.GetValueOrDefault()),
                    StorageBytes = Math.Max(m_account.StorageBytes, requested.StorageBytes.GetValueOrDefault())
                });
            }
        }

        Increase ReadIncrease() {
            string path = Path.Combine(m_root, "quota-limits.json");
            if (!File.Exists(path)) return null;
            try {
                Increase value = Read<Increase>(path);
                return value.FeeNanoUsd is <= 0 || value.StorageBytes is <= 0 ? null : value;
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or InvalidDataException) {
                // The operator may still be writing the file. Retry on the next paused poll.
                return null;
            }
        }

        void Publish(Account account) {
            string temporary = m_accountPath + ".tmp";
            using (FileStream file = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(file, account, CognitionRunLedger.Json);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, m_accountPath, overwrite: true);
            Volatile.Write(ref m_account, account);
        }

        static T Read<T>(string path) where T : class {
            using FileStream file = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(file, CognitionRunLedger.Json)
                ?? throw new InvalidDataException("Missing Westworld quota data.");
        }

        internal void Measure(Project project, string ledgerDirectory, int parallelism) {
            StorageUsed = checked(m_storageRoots.Distinct(StringComparer.OrdinalIgnoreCase).Sum(DirectoryBytes) + FileBytes(m_uploadFile));
            string world = Storage.GetSystemPath(project.FindSubsystem<SubsystemGameInfo>(true).DirectoryName);
            long projectBytes = FileBytes(Path.Combine(world, "Project.xml"));
            long ledgerBytes = FileBytes(Path.Combine(ledgerDirectory, "ledger.json"));
            int dirty = project.FindSubsystem<SubsystemTerrain>(true).Terrain.AllocatedChunks.Count(chunk => chunk.ModificationCounter > 0);
            StorageReserve = checked(SaveHeadroom + projectBytes * 3 + ledgerBytes + parallelism * 1_048_576L
                + dirty * MaxRegionChunkGrowth);
            DiskFree = m_storageRoots.Append(world).Select(path => Path.GetPathRoot(path))
                .Distinct(StringComparer.OrdinalIgnoreCase).Min(root => new DriveInfo(root).AvailableFreeSpace);
        }

        internal bool CanAdvance(long extra = 0) => StorageLimit - StorageUsed > checked(StorageReserve + extra)
            && DiskFree > checked(StorageReserve + extra);

        static long FileBytes(string path) {
            FileInfo file = new(path);
            return file.Exists ? file.Length : 0;
        }

        static long DirectoryBytes(string path) => Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint }).Sum(FileBytes)
            : 0;

        public void Dispose() => m_lock.Dispose();
    }
}
