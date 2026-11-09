namespace Westworld.Runtime.Core;

// KnownSafe is supplied by the trusted local sensor: both destination AND approach must
// be checked. This pure core cannot authenticate a GUID or prove physical traversability.
public sealed record ExplorationCandidate(Guid EvidenceId, Position Destination, bool KnownSafe);
public sealed record ExplorationObservation(Guid SessionId, Guid ActorId, Guid ObservationId,
    double Time, Position Origin, ExplorationCandidate[] Candidates);
public sealed record ExplorationChoice(Guid ChoiceId, Guid SessionId, Guid ActorId, Guid ObservationId,
    Guid EvidenceId, Position Origin, Position Destination, int PriorVisits, string Reason);
public sealed record ExplorationSelection(ExplorationChoice? Choice, string Reason);
public enum ExplorationOutcome { Arrived, Blocked, TimedOut, Interrupted }

// Actor-private, bounded local heuristic, not a pathfinder or global map. Game-thread only.
public sealed class LocalExploration {
    readonly record struct Cell(int X, int Y, int Z);
    readonly record struct Edge(Cell From, Cell To);
    sealed record Visit(int Count, long Sequence);
    sealed record Failure(double Until, long Sequence);
    readonly Guid session, actor;
    readonly int owner = Environment.CurrentManagedThreadId, capacity;
    readonly double backoff;
    readonly Dictionary<Cell, Visit> visits = new();
    readonly Dictionary<Edge, Failure> failures = new();
    double lastTime;
    long sequence;
    Cell? lastCell;
    ExplorationChoice? pending;
    public LocalExploration(Guid sessionId, Guid actorId, int capacity = 128, double failureBackoffSeconds = 10) {
        if (sessionId == Guid.Empty || actorId == Guid.Empty) throw new ArgumentException("Actor and session required");
        if (capacity < 4 || capacity > 4096 || !double.IsFinite(failureBackoffSeconds) || failureBackoffSeconds <= 0 || failureBackoffSeconds > 300)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        session = sessionId; actor = actorId; this.capacity = capacity; backoff = failureBackoffSeconds;
    }
    void Owner() { if (Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("Game thread required"); }
    void Time(double now) {
        if (!double.IsFinite(now) || now < 0 || now > 1e12 || now < lastTime) throw new ArgumentException("Monotonic finite observation-clock time required");
    }
    static Cell Key(Position p) => new((int)Math.Floor(p.X), (int)Math.Floor(p.Y), (int)Math.Floor(p.Z));
    static double Distance(Position a, Position b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
    public int MemoryCount { get { Owner(); return visits.Count + failures.Count; } }
    public bool HasPendingChoice { get { Owner(); return pending != null; } }
    void Advance(double now) {
        lastTime = now;
        foreach (var e in failures.Where(e => e.Value.Until <= now).Select(e => e.Key).ToArray()) failures.Remove(e);
    }
    void MakeRoom() {
        if (visits.Count + failures.Count < capacity) return;
        // Retain blocked edges ahead of visit history. If only edges remain, the earliest
        // expiring edge is forgotten; this is a bounded heuristic, not a permanent ban.
        if (visits.Count != 0) visits.Remove(visits.MinBy(e => e.Value.Sequence).Key);
        else failures.Remove(failures.OrderBy(e => e.Value.Until).ThenBy(e => e.Value.Sequence).First().Key);
    }
    void VisitPosition(Position p) {
        var cell = Key(p);
        if (lastCell == cell) return; // polling in place is not another visit
        lastCell = cell;
        if (visits.TryGetValue(cell, out var old)) visits[cell] = new(Math.Min(old.Count + 1, 1_000_000), ++sequence);
        else { MakeRoom(); visits.Add(cell, new(1, ++sequence)); }
    }
    public ExplorationSelection Select(ExplorationObservation observation, double now) {
        Owner(); Time(now); ArgumentNullException.ThrowIfNull(observation);
        if (observation.SessionId != session || observation.ActorId != actor || observation.ObservationId == Guid.Empty)
            throw new ArgumentException("Observation scope mismatch");
        if (!double.IsFinite(observation.Time) || observation.Time < 0 || observation.Time > now || now - observation.Time > 2)
            throw new ArgumentException("Fresh local observation required");
        observation.Origin.Validate();
        if (observation.Candidates == null || observation.Candidates.Length > 32) throw new ArgumentException("At most 32 local candidates");
        var candidates = observation.Candidates.ToArray();
        var evidence = new HashSet<Guid>(); var destinations = new HashSet<Cell>();
        foreach (var c in candidates) {
            if (c == null || c.EvidenceId == Guid.Empty || !evidence.Add(c.EvidenceId)) throw new ArgumentException("Invalid or duplicate evidence");
            c.Destination.Validate();
            if (!destinations.Add(Key(c.Destination)) || Distance(observation.Origin, c.Destination) > 4 + 1e-6)
                throw new ArgumentException("Duplicate or nonlocal candidate");
        }
        // No invalid request above mutates memory/time or displaces the pending choice.
        Advance(now);
        if (pending != null) return new(null, "actor_busy");
        VisitPosition(observation.Origin);
        var from = Key(observation.Origin);
        var safe = candidates.Where(c => c.KnownSafe && Key(c.Destination) != from && Distance(observation.Origin, c.Destination) >= 1 - 1e-6).ToArray();
        if (safe.Length == 0) return new(null, "no_known_safe_local_candidate");
        var available = safe.Where(c => !failures.ContainsKey(new(from, Key(c.Destination)))).ToArray();
        if (available.Length == 0) return new(null, "all_safe_edges_in_backoff");
        var best = available.OrderBy(c => visits.GetValueOrDefault(Key(c.Destination))?.Count ?? 0)
            .ThenByDescending(c => Distance(observation.Origin, c.Destination))
            .ThenBy(c => c.Destination.X).ThenBy(c => c.Destination.Y).ThenBy(c => c.Destination.Z).First();
        int count = visits.GetValueOrDefault(Key(best.Destination))?.Count ?? 0;
        pending = new(Guid.NewGuid(), session, actor, observation.ObservationId, best.EvidenceId,
            observation.Origin, best.Destination, count, "lowest_visits_then_farthest_known_safe; blocked_edges_excluded");
        return new(pending, "selected");
    }
    public void Report(Guid choiceId, double now, Position actualPosition, ExplorationOutcome outcome) {
        Owner(); Time(now); actualPosition.Validate();
        if (pending == null || pending.ChoiceId != choiceId || !Enum.IsDefined(outcome)) throw new ArgumentException("Unknown choice or outcome");
        if (outcome == ExplorationOutcome.Arrived && Distance(actualPosition, pending.Destination) > 0.4 + 1e-6)
            throw new ArgumentException("Arrival lacks positional evidence");
        var choice = pending;
        Advance(now); VisitPosition(actualPosition);
        if (outcome is ExplorationOutcome.Blocked or ExplorationOutcome.TimedOut) {
            var edge = new Edge(Key(choice.Origin), Key(choice.Destination));
            if (!failures.ContainsKey(edge)) MakeRoom();
            failures[edge] = new(now + backoff, ++sequence);
        }
        pending = null;
    }
}
