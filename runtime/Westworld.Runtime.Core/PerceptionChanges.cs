namespace Westworld.Runtime.Core;

public enum PerceptionChangeKind {
    FirstObserved, ObservedAgain, ObservedValueChanged, NotCurrentlyObserved,
    KnowledgeExpired, KnowledgeEvicted, HistoricalMemoryUpdated
}

/// <summary>One immutable subject entry, optionally coalescing several transitions. Kinds and
/// Occurrences retain the fact that intermediate changes happened; Previous/Current are endpoints.
/// Not a lossless event log and never evidence that a world object was destroyed or collected.</summary>
public sealed class PerceptionChange {
    public PerceptionCellKey Cell { get; }
    public Guid? FromObservationId { get; }
    public Guid ToObservationId { get; }
    public long FirstSequence { get; }
    public long LastSequence { get; }
    public PerceptionCellEvidence Previous { get; }
    public PerceptionCellEvidence Current { get; }
    public IReadOnlyList<PerceptionChangeKind> Kinds { get; }
    public int Occurrences { get; }
    internal PerceptionChange(PerceptionCellKey cell, Guid? from, Guid to, long first, long last,
        PerceptionCellEvidence previous, PerceptionCellEvidence current,
        IEnumerable<PerceptionChangeKind> kinds, int occurrences) {
        Cell = cell; FromObservationId = from; ToObservationId = to; FirstSequence = first; LastSequence = last;
        Previous = previous; Current = current; Kinds = Array.AsReadOnly(kinds.Distinct().ToArray()); Occurrences = occurrences;
    }
    internal PerceptionChange Merge(PerceptionChange next) => new(Cell, FromObservationId, next.ToObservationId,
        FirstSequence, next.LastSequence, Previous, next.Current, Kinds.Concat(next.Kinds), checked(Occurrences + next.Occurrences));
}

public sealed class PerceptionChangeBatch {
    public PerceptionScope Scope { get; }
    public Guid? FromObservationId { get; }
    public Guid? ToObservationId { get; }
    public long? ToSequence { get; }
    public double? ToCapturedAt { get; }
    public double? EvidenceLifetimeSeconds { get; }
    public bool BaselinePresent => FromObservationId.HasValue;
    public IReadOnlyList<PerceptionChange> Entries { get; }
    public long OmittedTransitions { get; }
    public long SkippedFrames { get; }
    public bool ResyncRequired => OmittedTransitions != 0 || SkippedFrames != 0 || !BaselinePresent;
    internal PerceptionChangeBatch(PerceptionScope scope, Guid? from, PerceptionFrame? current,
        IEnumerable<PerceptionChange> entries, long omitted, long skipped) {
        Scope = scope; FromObservationId = from; ToObservationId = current?.ObservationId; ToSequence = current?.Sequence;
        ToCapturedAt = current?.CapturedAt; EvidenceLifetimeSeconds = current?.EvidenceLifetimeSeconds;
        Entries = Array.AsReadOnly(entries.ToArray()); OmittedTransitions = omitted; SkippedFrames = skipped;
    }
}

/// <summary>Actor-private, game-thread-only TERRAIN change journal, not another world memory.
/// Retains only the last immutable frame plus a bounded coalescing mailbox and replay IDs.
/// Trusted source captures are required. No model calls, world reads, target ranking or action refs.
/// New world/session/binding requires a new journal. TakePending is not model delivery/ack.</summary>
public sealed class PerceptionChangeJournal {
    readonly PerceptionScope scope;
    readonly int capacity, maximumFrames, owner = Environment.CurrentManagedThreadId;
    readonly Dictionary<Guid, double> observations = [];
    Dictionary<PerceptionCellKey, PerceptionChange> pending = [];
    PerceptionFrame? last;
    Guid? pendingBaseline;
    long omitted, skipped;
    public PerceptionChangeJournal(PerceptionScope scope, int capacity = 64, int maximumFrames = 4096) {
        scope.Validate();
        if (capacity is < 1 or > 256 || maximumFrames is < 1 or > 4096) throw new ArgumentOutOfRangeException();
        this.scope = scope; this.capacity = capacity; this.maximumFrames = maximumFrames;
    }
    void Owner() { if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("Game thread required"); }
    public int PendingCount { get { Owner(); return pending.Count; } }
    public int FrameCount { get { Owner(); return observations.Count; } }
    public PerceptionFrame? Latest { get { Owner(); return last; } }

    public void Observe(PerceptionFrame frame) {
        Owner(); ArgumentNullException.ThrowIfNull(frame);
        if (frame.Scope != scope) throw new ArgumentException("Cross-world/session/actor/binding frame rejected");
        if (last is not null && (frame.Sequence <= last.Sequence || frame.CapturedAt < last.CapturedAt
            || frame.ValueSemantics != last.ValueSemantics || frame.EvidenceLifetimeSeconds != last.EvidenceLifetimeSeconds))
            throw new ArgumentException("Out-of-order frame or changed evidence contract");
        if (observations.ContainsKey(frame.ObservationId)) throw new ArgumentException("Replayed snapshot/version");
        if (observations.Count >= maximumFrames) throw new InvalidOperationException("Journal frame bound reached; establish a new explicit baseline");
        foreach (var fact in frame.Evidence.Values.Where(f => f.Knowledge == PerceptionKnowledge.Historical)) {
            if (observations.TryGetValue(fact.SourceObservationId!.Value, out double captured) && captured != fact.ObservedAt)
                throw new ArgumentException("Retained source snapshot changed its capture time");
            if (last is not null && last.Evidence.TryGetValue(fact.Cell, out var prior)
                && prior.SourceObservationId == fact.SourceObservationId
                && (prior.Value != fact.Value || prior.ObservedAt != fact.ObservedAt))
                throw new ArgumentException("Retained source snapshot changed its cell evidence");
        }

        var delta = Diff(last, frame);
        var next = new Dictionary<PerceptionCellKey, PerceptionChange>(pending);
        long nextOmitted = omitted;
        long nextSkipped = checked(skipped + (last is null ? 0 : frame.Sequence - last.Sequence - 1));
        foreach (var change in delta) {
            if (next.TryGetValue(change.Cell, out var existing)) next[change.Cell] = existing.Merge(change);
            else if (next.Count < capacity) next.Add(change.Cell, change);
            else nextOmitted = checked(nextOmitted + 1);
        }
        // All validation/diff/coalescing completes before mutating journal state.
        pending = next; omitted = nextOmitted; skipped = nextSkipped;
        observations.Add(frame.ObservationId, frame.CapturedAt); last = frame;
    }

    public PerceptionChangeBatch PeekPending() {
        Owner();
        return new(scope, pendingBaseline, last, pending.Values.OrderBy(e => e.FirstSequence)
            .ThenBy(e => e.Cell.X).ThenBy(e => e.Cell.Y).ThenBy(e => e.Cell.Z), omitted, skipped);
    }
    public PerceptionChangeBatch TakePending() {
        var batch = PeekPending(); pending = []; omitted = 0; skipped = 0; pendingBaseline = last?.ObservationId; return batch;
    }

    static IEnumerable<PerceptionChange> Diff(PerceptionFrame? previous, PerceptionFrame current) {
        var keys = (previous?.Evidence.Keys ?? Enumerable.Empty<PerceptionCellKey>()).Concat(current.Evidence.Keys)
            .Distinct().OrderBy(k => k.X).ThenBy(k => k.Y).ThenBy(k => k.Z);
        foreach (var key in keys) {
            var before = previous is null ? new PerceptionCellEvidence(key, PerceptionKnowledge.Absent)
                : previous.ReadAt(key, previous.CapturedAt);
            var after = current.ReadAt(key, current.CapturedAt);
            PerceptionChangeKind? kind = null;
            if (after.Knowledge == PerceptionKnowledge.Current) {
                if (before.Value.HasValue && before.Value != after.Value) kind = PerceptionChangeKind.ObservedValueChanged;
                else if (before.Knowledge != PerceptionKnowledge.Current)
                    kind = before.Value.HasValue ? PerceptionChangeKind.ObservedAgain : PerceptionChangeKind.FirstObserved;
            } else if (before.Knowledge == PerceptionKnowledge.Current) {
                kind = PerceptionChangeKind.NotCurrentlyObserved;
            } else if (after.Knowledge == PerceptionKnowledge.Historical) {
                if (before.Value != after.Value || before.SourceObservationId != after.SourceObservationId || before.ObservedAt != after.ObservedAt)
                    kind = PerceptionChangeKind.HistoricalMemoryUpdated;
            } else if (before.Knowledge == PerceptionKnowledge.Historical) {
                kind = current.CapturedAt - before.ObservedAt!.Value > current.EvidenceLifetimeSeconds
                    ? PerceptionChangeKind.KnowledgeExpired : PerceptionChangeKind.KnowledgeEvicted;
            }
            if (kind is not null) yield return new(key, previous?.ObservationId, current.ObservationId,
                current.Sequence, current.Sequence, before, after, [kind.Value], 1);
        }
    }
}
