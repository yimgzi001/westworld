namespace ScapiDualSmoke;

public readonly record struct MoveVector(float X, float Y, float Z);
public enum ActionKind { Move, Stop }
public sealed record ActionRequest(Guid Session, int PlayerId, long RequestId, ActionKind Kind,
    double IssuedAtSeconds, double ExpiresAtSeconds, double DurationSeconds, MoveVector Move);
public sealed record ActionEvent(int PlayerId, long RequestId, string Status, string Reason);
public sealed record TickResult(IReadOnlyDictionary<int, MoveVector> Intents, IReadOnlyList<ActionEvent> Events);

// In-process intent mailbox only. No game references, I/O, dynamic code or background game mutations.
// Submit and Tick must use the same monotonic clock. Tick belongs to one game thread.
public sealed class ActionQueue {
    sealed class Slot {
        public long LastId;
        public ActionRequest? Pending, Active;
        public double Ends;
    }
    readonly object gate = new();
    readonly Guid session;
    readonly Dictionary<int, Slot> slots;
    readonly List<ActionEvent> events = new();
    double lastTick = double.NegativeInfinity;
    public ActionQueue(Guid session, IEnumerable<int> players) {
        if (session == Guid.Empty) throw new ArgumentException("Empty session", nameof(session));
        this.session = session;
        slots = players.Distinct().ToDictionary(id => id, _ => new Slot());
        if (slots.Count == 0 || slots.Keys.Any(id => id < 0)) throw new ArgumentException("Invalid players", nameof(players));
    }
    void Event(ActionRequest r, string status, string reason) => events.Add(new(r.PlayerId, r.RequestId, status, reason));
    bool Reject(ActionRequest r, string why) { Event(r, "rejected", why); return false; }
    static bool Finite(double v) => double.IsFinite(v);
    public bool Submit(ActionRequest r, double nowSeconds) {
        ArgumentNullException.ThrowIfNull(r);
        lock (gate) {
            if (r.Session != session) return Reject(r, "wrong_session");
            if (!slots.TryGetValue(r.PlayerId, out var slot)) return Reject(r, "unknown_player");
            if (r.RequestId <= 0 || r.RequestId <= slot.LastId) return Reject(r, "replayed_id");
            // A producer samples its clock before acquiring gate; Tick may win the lock.
            // Its older sample is not a clock rewind, but must not revive an expired request.
            double effectiveNow = Math.Max(nowSeconds, lastTick);
            if (!Finite(nowSeconds) || !Finite(r.IssuedAtSeconds) ||
                !Finite(r.ExpiresAtSeconds) || r.IssuedAtSeconds > nowSeconds ||
                r.ExpiresAtSeconds <= effectiveNow || r.ExpiresAtSeconds <= r.IssuedAtSeconds)
                return Reject(r, "invalid_or_expired_time");
            if (r.Kind != ActionKind.Move && r.Kind != ActionKind.Stop) return Reject(r, "unknown_action");
            if (!Finite(r.DurationSeconds) || !float.IsFinite(r.Move.X) || !float.IsFinite(r.Move.Y) || !float.IsFinite(r.Move.Z))
                return Reject(r, "nonfinite_intent");
            if (r.Kind == ActionKind.Move && (r.DurationSeconds <= 0 || r.DurationSeconds > .4 ||
                (double)r.Move.X*r.Move.X + (double)r.Move.Y*r.Move.Y + (double)r.Move.Z*r.Move.Z > 1))
                return Reject(r, "invalid_move");
            if (r.Kind == ActionKind.Stop && (r.DurationSeconds != 0 || r.Move != default)) return Reject(r, "invalid_stop");
            if (r.Kind == ActionKind.Move && (slot.Pending != null || slot.Active != null)) return Reject(r, "busy");
            slot.LastId = r.RequestId;
            if (r.Kind == ActionKind.Stop) {
                if (slot.Pending is not null) Event(slot.Pending, "completed", "stopped_before_start");
                if (slot.Active is not null) Event(slot.Active, "completed", "stopped");
                slot.Active = null; // logical cancellation only; no game mutation here
            }
            slot.Pending = r;
            Event(r, "accepted", "queued");
            return true;
        }
    }
    public TickResult Tick(double nowSeconds) {
        lock (gate) {
            if (!Finite(nowSeconds) || nowSeconds < lastTick) {
                foreach (var slot in slots.Values) {
                    if (slot.Pending is not null) Event(slot.Pending, "rejected", "invalid_tick_clock");
                    if (slot.Active is not null) Event(slot.Active, "completed", "invalid_tick_clock");
                    slot.Pending = slot.Active = null;
                }
                return Result(); // fail closed; a later valid tick can accept new requests
            }
            lastTick = nowSeconds;
            foreach (var slot in slots.Values) {
                if (slot.Active is not null && nowSeconds >= slot.Ends) {
                    Event(slot.Active, "completed", "duration_or_deadline"); slot.Active = null;
                }
                if (slot.Pending is not { } request) continue;
                slot.Pending = null;
                // Stop remains effective even if its queue deadline elapsed before the next tick.
                if (request.Kind == ActionKind.Stop) { slot.Active = null; Event(request, "completed", "stopped"); }
                else if (nowSeconds >= request.ExpiresAtSeconds) Event(request, "rejected", "expired_before_start");
                else { slot.Active = request; slot.Ends = Math.Min(nowSeconds + request.DurationSeconds, request.ExpiresAtSeconds); }
            }
            return Result();
        }
    }
    TickResult Result() {
        var intents = slots.ToDictionary(p => p.Key, p => p.Value.Active?.Move ?? default);
        var batch = events.ToArray(); events.Clear();
        return new(intents, batch);
    }
}
