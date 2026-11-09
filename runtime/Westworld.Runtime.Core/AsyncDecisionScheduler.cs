using System.Diagnostics;
namespace Westworld.Runtime.Core;

public sealed record HighLevelDecision(Guid SessionId, Guid ActorId, Guid ObservationId,
    Guid DecisionId, string Goal, string Skill, int Slot, int ExpectedValue, CellTarget? Target = null);
public interface IDecisionProvider {
    Task<HighLevelDecision> DecideAsync(ActorObservation observation, CancellationToken cancellationToken);
}

// Host-owned mailbox. No native objects cross this boundary. Taking a decision does not execute it.
public sealed class AsyncDecisionScheduler : IDisposable {
    sealed record Outcome(HighLevelDecision? Decision, string? Error, long Finished);
    sealed class Job {
        public required ActorObservation Snapshot;
        public required CancellationTokenSource Cancellation;
        public required Task<Outcome> Work;
        public required long Started;
        public Task? CancellationWork;
        public bool Retired, Ready;
        public HighLevelDecision? Decision;
        public string? Error;
    }
    readonly IDecisionProvider provider;
    readonly int owner = Environment.CurrentManagedThreadId, maxConcurrent, capacity;
    readonly TimeSpan deadline;
    readonly bool allowSurvey;
    readonly Func<int,bool>? supportedSurveyHarvest;
    readonly Dictionary<Guid, Job> mailboxes = new();
    readonly List<Job> running = new();
    bool disposed;
    public AsyncDecisionScheduler(IDecisionProvider provider, TimeSpan deadline, int maxConcurrent = 2, int capacity = 64, bool allowSurvey = false, Func<int,bool>? supportedSurveyHarvest = null) {
        if (allowSurvey && supportedSurveyHarvest == null) throw new ArgumentException("Survey requires a trusted supported-harvest predicate");
        this.allowSurvey = allowSurvey; this.supportedSurveyHarvest = supportedSurveyHarvest;
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        if (deadline <= TimeSpan.Zero || deadline > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(deadline));
        if (maxConcurrent < 1 || maxConcurrent > 2 || capacity < maxConcurrent || capacity > 1024) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.deadline = deadline; this.maxConcurrent = maxConcurrent; this.capacity = capacity;
    }
    void Owner() {
        if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("Game thread required");
        if (disposed) throw new ObjectDisposedException(nameof(AsyncDecisionScheduler));
    }
    // Cancellation callbacks are provider code, so never run/await them on the game thread.
    static void Cancel(Job job) { job.CancellationWork ??= CancelAsync(job.Cancellation); }
    static async Task CancelAsync(CancellationTokenSource source) {
        try { await source.CancelAsync().ConfigureAwait(false); } catch { /* Provider cancellation failure does not unblock capacity. */ }
    }
    static async Task<Outcome> Invoke(IDecisionProvider provider, ActorObservation snapshot, CancellationToken token) {
        try { return new(await provider.DecideAsync(snapshot, token).ConfigureAwait(false), null, Stopwatch.GetTimestamp()); }
        catch (OperationCanceledException) { return new(null, "provider_canceled", Stopwatch.GetTimestamp()); }
        catch { return new(null, "provider_failed", Stopwatch.GetTimestamp()); }
    }
    void Pump() {
        long now = Stopwatch.GetTimestamp();
        foreach (var job in running.ToArray()) {
            if (job.Work.IsCompleted && (job.CancellationWork == null || job.CancellationWork.IsCompleted)) {
                var outcome = job.Work.GetAwaiter().GetResult(); // Invoke captures provider failures; task is already complete.
                running.Remove(job);
                job.Cancellation.Dispose();
                if (job.Retired) continue;
                job.Ready = true;
                if (Stopwatch.GetElapsedTime(job.Started, outcome.Finished) >= deadline) { job.Error = "deadline_exceeded"; continue; }
                var d = outcome.Decision;
                if (outcome.Error != null) { job.Error = outcome.Error; continue; }
                if (d == null || d.SessionId != job.Snapshot.SessionId || d.ActorId != job.Snapshot.ActorId || d.ObservationId != job.Snapshot.ObservationId || d.DecisionId == Guid.Empty) {
                    job.Error = "invalid_decision_scope"; continue;
                }
                if (string.IsNullOrWhiteSpace(d.Goal) || d.Goal.Length > 512 || string.IsNullOrWhiteSpace(d.Skill) || d.Skill.Length > 64 || d.Skill.Any(char.IsControl) || d.Slot < -1 || d.Slot > 4095) {
                    job.Error = "invalid_decision_fields"; continue;
                }
                if (!ValidTarget(d, job.Snapshot)) { job.Error = "invalid_decision_target"; continue; }
                job.Decision = d;
            } else if (!job.Retired && Stopwatch.GetElapsedTime(job.Started, now) >= deadline) {
                job.Retired = true; job.Ready = true; job.Error = "deadline_exceeded"; Cancel(job);
            }
        }
    }
    bool ValidTarget(HighLevelDecision decision, ActorObservation snapshot) {
        if (decision.Skill == "Survey") {
            if (!allowSurvey || supportedSurveyHarvest == null || decision.Goal != "FindFood" || decision.Slot != -1 || decision.ExpectedValue != 0 || decision.Target != null
                || snapshot.Health <= 0 || snapshot.Health > 1 || snapshot.Food < 0 || snapshot.Food >= 0.8f) return false;
            if (snapshot.Inventory.Any(s => s.CanEat && s.Count > 0)) return false;
            // Host-supplied pure capability predicate. Unknown cells never reach it.
            try { return !snapshot.Cells.Any(c => c.Visibility == "known" && c.Value.HasValue && supportedSurveyHarvest(c.Value.Value)); }
            catch { return false; }
        }
        if (decision.Skill != "ForageOne") return decision.Target == null;
        if (decision.Goal != "SatisfyHunger" || decision.Target == null || decision.Slot != -1 || decision.ExpectedValue != 0) return false;
        var target = decision.Target;
        try { target.Validate(); } catch (ArgumentOutOfRangeException) { return false; }
        var matches = snapshot.Cells.Where(c => c.X == target.X && c.Y == target.Y && c.Z == target.Z).ToArray();
        return matches.Length == 1 && matches[0].Visibility == "known" && matches[0].Value.HasValue && matches[0].Value == target.ExpectedValue;
    }
    public bool Request(ActorObservation snapshot, out string? error) {
        Owner(); Pump(); ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SessionId == Guid.Empty || snapshot.ActorId == Guid.Empty || snapshot.ObservationId == Guid.Empty || !double.IsFinite(snapshot.Time) || !float.IsFinite(snapshot.Health) || !float.IsFinite(snapshot.Food) || snapshot.Inventory == null || snapshot.Cells == null || snapshot.Inventory.Length > 4096 || snapshot.Cells.Length > 4096 || snapshot.Inventory.Any(x => x == null) || snapshot.Cells.Any(x => x == null)) throw new ArgumentException("Invalid observation");
        snapshot.Position.Validate();
        if (mailboxes.ContainsKey(snapshot.ActorId) || running.Any(j => j.Snapshot.ActorId == snapshot.ActorId)) { error = "actor_busy"; return false; }
        if (running.Count >= maxConcurrent) { error = "concurrency_full"; return false; }
        if (mailboxes.Count >= capacity) { error = "mailbox_full"; return false; }
        // Elements are immutable records; copying arrays detaches mutable container ownership.
        var copy = snapshot with { Inventory = snapshot.Inventory.ToArray(), Cells = snapshot.Cells.ToArray() };
        var providerCopy = copy with { Inventory = copy.Inventory.ToArray(), Cells = copy.Cells.ToArray() };
        var cts = new CancellationTokenSource(); long started = Stopwatch.GetTimestamp();
        var job = new Job { Snapshot = copy, Cancellation = cts, Started = started,
            Work = Task.Run(() => Invoke(provider, providerCopy, cts.Token)) };
        mailboxes.Add(snapshot.ActorId, job); running.Add(job); error = null; return true;
    }
    public bool TryTake(Guid actor, out HighLevelDecision? decision, out string? error) {
        Owner(); Pump(); decision = null; error = null;
        if (!mailboxes.TryGetValue(actor, out var job) || !job.Ready) return false;
        mailboxes.Remove(actor); decision = job.Decision; error = job.Error; return true;
    }
    static async Task ReleaseAfterCompletion(Job job) {
        await job.Work.ConfigureAwait(false);
        if (job.CancellationWork != null) await job.CancellationWork.ConfigureAwait(false);
        job.Cancellation.Dispose();
    }
    public void Dispose() {
        if (disposed) return; Owner(); disposed = true;
        foreach (var job in running) { job.Retired = true; Cancel(job); _ = ReleaseAfterCompletion(job); }
        mailboxes.Clear(); running.Clear();
    }
}
