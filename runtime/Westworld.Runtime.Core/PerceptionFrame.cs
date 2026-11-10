using System.Collections.ObjectModel;

namespace Westworld.Runtime.Core;

// BindingRevision is the native binding generation, NOT ActorRegistry's per-update Revision.
public readonly record struct PerceptionScope(Guid WorldId, Guid SessionId, Guid ActorId, long BindingRevision) {
    internal void Validate() {
        if (WorldId == Guid.Empty || SessionId == Guid.Empty || ActorId == Guid.Empty || BindingRevision < 0)
            throw new ArgumentException("World, runtime session, actor and binding generation required");
    }
}

public readonly record struct PerceptionCellKey(int X, int Y, int Z);
public enum PerceptionKnowledge { Current, Historical, Unknown, Absent, Expired }

/// <summary>Evidence, never an action reference or permission. Current means observed in the
/// owning frame, not current forever. ReadAt demotes expired values without rewriting history.</summary>
public sealed class PerceptionCellEvidence {
    public PerceptionCellKey Cell { get; }
    public PerceptionKnowledge Knowledge { get; }
    public int? Value { get; }
    public Guid? SourceObservationId { get; }
    public double? ObservedAt { get; }
    internal PerceptionCellEvidence(PerceptionCellKey cell, PerceptionKnowledge knowledge,
        int? value = null, Guid? source = null, double? observedAt = null) {
        Cell = cell; Knowledge = knowledge; Value = value; SourceObservationId = source; ObservedAt = observedAt;
    }
    internal PerceptionCellEvidence Expired() => new(Cell, PerceptionKnowledge.Expired, Value, SourceObservationId, ObservedAt);
}

/// <summary>Minimal immutable TERRAIN frame over existing runtime observation/memory DTOs.
/// Does not capture the world, generate refs, certify visibility, infer eye pose, normalize
/// native values or replace LocalObservationMemory. Trusted host supplies one actual capture.
/// All times use ActorObservation.Time's monotonic observation clock, never gameTime mixed in.</summary>
public sealed class PerceptionFrame {
    public const int MaximumCells = 4096;
    readonly ReadOnlyDictionary<PerceptionCellKey, PerceptionCellEvidence> evidence;
    public PerceptionScope Scope { get; }
    public Guid ObservationId { get; }
    public long Sequence { get; }
    public double CapturedAt { get; }
    public Position Position { get; }
    public float Health { get; }
    public double EvidenceLifetimeSeconds { get; }
    public string ValueSemantics { get; }
    public IReadOnlyList<CellObservation> Cells { get; }
    public IReadOnlyList<LocalObservedCell> RetainedCells { get; }
    public IReadOnlyDictionary<PerceptionCellKey, PerceptionCellEvidence> Evidence => evidence;

    PerceptionFrame(PerceptionScope scope, long sequence, ActorObservation observation,
        CellObservation[] cells, LocalObservedCell[] retained, double lifetime, string semantics,
        Dictionary<PerceptionCellKey, PerceptionCellEvidence> facts) {
        Scope = scope; ObservationId = observation.ObservationId; Sequence = sequence; CapturedAt = observation.Time;
        Position = observation.Position; Health = observation.Health; EvidenceLifetimeSeconds = lifetime;
        ValueSemantics = semantics; Cells = Array.AsReadOnly(cells); RetainedCells = Array.AsReadOnly(retained);
        evidence = new(facts);
    }

    public static PerceptionFrame FromObservation(PerceptionScope scope, long sequence, ActorObservation observation,
        LocalObservationWindow? memory = null, double evidenceLifetimeSeconds = 3,
        string valueSemantics = "native_encoded_value_v1") {
        scope.Validate(); ArgumentNullException.ThrowIfNull(observation);
        if (sequence < 1 || observation.SessionId != scope.SessionId || observation.ActorId != scope.ActorId
            || observation.ObservationId == Guid.Empty || !ValidTime(observation.Time) || !float.IsFinite(observation.Health))
            throw new ArgumentException("Invalid observation scope, version or time");
        observation.Position.Validate();
        if (observation.Position.Y < 0 || observation.Position.Y > 255)
            throw new ArgumentException("Unsupported observation height");
        if (!double.IsFinite(evidenceLifetimeSeconds) || evidenceLifetimeSeconds <= 0 || evidenceLifetimeSeconds > 3)
            throw new ArgumentOutOfRangeException(nameof(evidenceLifetimeSeconds));
        if (string.IsNullOrWhiteSpace(valueSemantics) || valueSemantics.Length > 64 || valueSemantics.Any(char.IsControl))
            throw new ArgumentException("Explicit bounded value semantics required");
        if (observation.Cells is null || observation.Cells.Length > MaximumCells)
            throw new ArgumentException("Bounded current cells required");
        var cells = observation.Cells.ToArray();
        var facts = new Dictionary<PerceptionCellKey, PerceptionCellEvidence>();
        foreach (var cell in cells) {
            if (cell is null || cell.Visibility is not ("known" or "unknown")
                || (cell.Visibility == "known" ? !cell.Value.HasValue || cell.Value < 0 : cell.Value.HasValue))
                throw new ArgumentException("Unknown cannot contain a value; known requires one");
            var key = Key(cell.X, cell.Y, cell.Z);
            var fact = cell.Visibility == "known"
                ? new PerceptionCellEvidence(key, PerceptionKnowledge.Current, cell.Value, observation.ObservationId, observation.Time)
                : new PerceptionCellEvidence(key, PerceptionKnowledge.Unknown);
            if (!facts.TryAdd(key, fact)) throw new ArgumentException("Duplicate current cell");
        }

        var retained = memory?.Cells.ToArray() ?? [];
        if (memory is not null) {
            if (memory.SessionId != scope.SessionId || memory.ActorId != scope.ActorId
                || memory.ObservationId != observation.ObservationId || memory.Time != observation.Time
                || memory.Position != observation.Position || memory.Health != observation.Health)
                throw new ArgumentException("Memory must belong to this exact actor/capture/time/position");
            if (retained.Length > MaximumCells) throw new ArgumentException("Bounded memory required");
            var seen = new HashSet<PerceptionCellKey>();
            foreach (var cell in retained) {
                if (cell is null || cell.Value < 0 || cell.SourceObservationId == Guid.Empty
                    || !ValidTime(cell.ObservedAt) || cell.ObservedAt > observation.Time
                    || !double.IsFinite(cell.Age) || cell.Age < 0 || cell.Age > evidenceLifetimeSeconds
                    || Math.Abs(observation.Time - cell.ObservedAt - cell.Age) > 1e-6
                    || cell.IsCurrent != (cell.SourceObservationId == observation.ObservationId))
                    throw new ArgumentException("Invalid retained provenance, time or freshness");
                var key = Key(cell.X, cell.Y, cell.Z);
                if (!seen.Add(key)) throw new ArgumentException("Duplicate retained cell");
                facts.TryGetValue(key, out var current);
                if (cell.IsCurrent) {
                    if (cell.Age != 0 || cell.ObservedAt != observation.Time
                        || current?.Knowledge != PerceptionKnowledge.Current || current.Value != cell.Value)
                        throw new ArgumentException("Memory current cell must match the same current capture");
                } else {
                    if (current?.Knowledge == PerceptionKnowledge.Current)
                        throw new ArgumentException("Historical memory cannot overwrite a current cell");
                    facts[key] = new(key, PerceptionKnowledge.Historical, cell.Value, cell.SourceObservationId, cell.ObservedAt);
                }
            }
        }
        if (facts.Count > MaximumCells) throw new ArgumentException("Combined evidence bound exceeded");
        // Arrays and dictionaries are detached. Existing record cells have no mutable members.
        return new(scope, sequence, observation, cells, retained, evidenceLifetimeSeconds, valueSemantics, facts);
    }

    public PerceptionCellEvidence ReadAt(PerceptionCellKey cell, double observationClockNow) {
        Key(cell.X, cell.Y, cell.Z);
        if (!ValidTime(observationClockNow) || observationClockNow < CapturedAt)
            throw new ArgumentException("Monotonic observation-clock time required");
        if (!evidence.TryGetValue(cell, out var fact)) return new(cell, PerceptionKnowledge.Absent);
        return fact.ObservedAt is double time && observationClockNow - time > EvidenceLifetimeSeconds ? fact.Expired() : fact;
    }

    internal static bool ValidTime(double time) => double.IsFinite(time) && time >= 0 && time <= 1e12;
    internal static PerceptionCellKey Key(int x, int y, int z) {
        if (Math.Abs((long)x) > 10_000_000 || Math.Abs((long)z) > 10_000_000 || y is < 0 or > 255)
            throw new ArgumentException("Cell outside declared observation envelope");
        return new(x, y, z);
    }
}
