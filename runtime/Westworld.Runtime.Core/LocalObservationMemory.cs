namespace Westworld.Runtime.Core;

public sealed record LocalObservedCell(int X,int Y,int Z,int Value,Guid SourceObservationId,double ObservedAt,double Age,bool IsCurrent);
// Immutable short-lived evidence window, not a new visual observation. No eye pose is
// available in ActorObservation; Position is the latest body position only.
public sealed class LocalObservationWindow {
    public Guid SessionId {get;} public Guid ActorId {get;} public Guid ObservationId {get;}
    public double Time {get;} public Position Position {get;} public float Health {get;}
    public IReadOnlyList<LocalObservedCell> Cells {get;}
    internal LocalObservationWindow(ActorObservation o,IEnumerable<LocalObservedCell> cells) {
        SessionId=o.SessionId;ActorId=o.ActorId;ObservationId=o.ObservationId;Time=o.Time;Position=o.Position;Health=o.Health;
        Cells=Array.AsReadOnly(cells.ToArray());
    }
}

/// <summary>Actor-private trusted-sensor evidence, bounded in time, space and memory.
/// Unknown never refreshes a known cell. Historical evidence is explicitly labelled.
/// This does not certify that a remembered block has not changed since observation.</summary>
public sealed class LocalObservationMemory {
    readonly Guid session,actor; readonly int capacity,owner=Environment.CurrentManagedThreadId;
    readonly double ttl,radius; double last=-1;
    readonly Dictionary<(int,int,int),LocalObservedCell> cells=new();
    readonly HashSet<Guid> recentIds=new(); readonly Queue<(Guid Id,double Time)> ids=new();
    public int Count=>cells.Count;
    public LocalObservationMemory(Guid sessionId,Guid actorId,int capacity=1024,double ttlSeconds=3,double radius=5) {
        if(sessionId==Guid.Empty||actorId==Guid.Empty)throw new ArgumentException("Scope required");
        if(capacity<1||capacity>4096||!double.IsFinite(ttlSeconds)||ttlSeconds<=0||ttlSeconds>3||!double.IsFinite(radius)||radius<=0||radius>5)throw new ArgumentOutOfRangeException();
        session=sessionId;actor=actorId;this.capacity=capacity;ttl=ttlSeconds;this.radius=radius;
    }
    public LocalObservationWindow Observe(ActorObservation o) {
        if(Environment.CurrentManagedThreadId!=owner)throw new InvalidOperationException("Game thread required");
        ArgumentNullException.ThrowIfNull(o);
        if(o.SessionId!=session||o.ActorId!=actor||o.ObservationId==Guid.Empty||!double.IsFinite(o.Time)||o.Time<0||o.Time<last||recentIds.Contains(o.ObservationId))throw new ArgumentException("Invalid scope, time or replay");
        var p=o.Position;
        if(!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!double.IsFinite(p.Z)||Math.Abs(p.X)>10_000_000||Math.Abs(p.Z)>10_000_000||p.Y<0||p.Y>255||!float.IsFinite(o.Health)||o.Cells is null||o.Cells.Length>4096)throw new ArgumentException("Invalid observation");
        var seen=new HashSet<(int,int,int)>();
        foreach(var c in o.Cells) {
            if(c is null||!seen.Add((c.X,c.Y,c.Z))||Math.Abs((long)c.X)>10_000_000||Math.Abs((long)c.Z)>10_000_000||c.Y<0||c.Y>255||c.Visibility is not ("known" or "unknown")||(c.Visibility=="known"? !c.Value.HasValue||c.Value<0:c.Value.HasValue))throw new ArgumentException("Invalid cells");
        }
        // Validate completely before changing retained evidence.
        last=o.Time;
        foreach(var k in cells.Where(kv=>o.Time-kv.Value.ObservedAt>ttl||!Near(kv.Value.X,kv.Value.Y,kv.Value.Z,p)).Select(kv=>kv.Key).ToArray())cells.Remove(k);
        foreach(var c in o.Cells)if(c.Visibility=="known"&&Near(c.X,c.Y,c.Z,p))cells[(c.X,c.Y,c.Z)]=new(c.X,c.Y,c.Z,c.Value!.Value,o.ObservationId,o.Time,0,true);
        while(cells.Count>capacity) {
            var key=cells.OrderBy(kv=>kv.Value.ObservedAt).ThenByDescending(kv=>Distance(kv.Value.X,kv.Value.Y,kv.Value.Z,p)).ThenBy(kv=>kv.Key).First().Key;cells.Remove(key);
        }
        while(ids.Count>0&&(o.Time-ids.Peek().Time>ttl||ids.Count>=4096))recentIds.Remove(ids.Dequeue().Id);
        ids.Enqueue((o.ObservationId,o.Time));recentIds.Add(o.ObservationId);
        return new(o,cells.Values.OrderBy(c=>c.X).ThenBy(c=>c.Y).ThenBy(c=>c.Z).Select(c=>c with {Age=o.Time-c.ObservedAt,IsCurrent=c.SourceObservationId==o.ObservationId}));
    }
    // Distance to cell centre, not an omniscient query of its geometry.
    bool Near(int x,int y,int z,Position p)=>Distance(x,y,z,p)<=radius*radius+1e-9;
    static double Distance(int x,int y,int z,Position p)=>Math.Pow(x+.5-p.X,2)+Math.Pow(y+.5-p.Y,2)+Math.Pow(z+.5-p.Z,2);
}
