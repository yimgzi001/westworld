using System.Text.Json;
using System.Text.Json.Serialization;
namespace Westworld.Runtime.Core;

// Privileged host bookkeeping, never a model-visible observation or a substitute for native bodies.
public readonly record struct Position([property:JsonRequired] double X, [property:JsonRequired] double Y, [property:JsonRequired] double Z) {
    internal void Validate() {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Z) ||
            Math.Abs(X)>10_000_000 || Math.Abs(Y)>1_000_000 || Math.Abs(Z)>10_000_000)
            throw new ArgumentException("Invalid position within declared experimental envelope");
    }
}
public sealed record ActorState(Guid Id, string EntityKey, Position Position, long Revision, bool Active);
public sealed record SimulationInterest(Guid ActorId, Position Center);
public sealed record RenderView(Guid ViewId, Position Camera);
public sealed record WorldSnapshot(int Schema, Guid WorldId, ActorState[] Actors, Guid[] RetiredIds);

// One owning game thread. This layer stores native-validated bindings/positions only;
// it never creates entities, moves bodies, loads chunks or changes health/inventory.
public sealed partial class ActorRegistry {
    const int MaxActors=1000;
    readonly int ownerThread=Environment.CurrentManagedThreadId;
    readonly Dictionary<Guid,ActorState> actors=new();
    readonly Dictionary<Guid,RenderView> views=new();
    readonly HashSet<Guid> bound=new();
    readonly HashSet<Guid> retired=new();
    static readonly JsonSerializerOptions JsonOptions=new() {
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive=false,
        RespectRequiredConstructorParameters=true
    };
    public Guid WorldId {get;}
    // New runtime session after restore. Old commands must not target a restored session.
    public Guid SessionId {get;}=Guid.NewGuid();
    public ActorRegistry(Guid worldId) {
        if(worldId==Guid.Empty)throw new ArgumentException("World id required");
        WorldId=worldId;
    }
    void Owner() {if(Environment.CurrentManagedThreadId!=ownerThread)throw new InvalidOperationException("Game-thread ownership required");}
    static void Valid(ActorState a) {
        if(a is null || a.Id==Guid.Empty || string.IsNullOrWhiteSpace(a.EntityKey) || a.EntityKey.Length>128 ||
           a.EntityKey.Any(char.IsControl) || a.Revision<0)throw new ArgumentException("Invalid actor state");
        a.Position.Validate();
    }
    public void Add(Guid id,string nativeEntityKey,Position nativePosition) {
        Owner();var a=new ActorState(id,nativeEntityKey,nativePosition,0,true);Valid(a);
        if(actors.Count>=MaxActors || (actors.ContainsKey(id)||retired.Contains(id)) || actors.Values.Any(x=>x.EntityKey==nativeEntityKey))
            throw new InvalidOperationException("Duplicate identity/binding or capacity exceeded");
        actors.Add(id,a);bound.Add(id);healthUpdates.Add(id,0);healthVersion++;
    }
    public ActorState Read(Guid id) {Owner();return actors[id];}
    public void Update(Guid session,Guid id,long expectedRevision,Position nativePosition,bool active) {
        Owner();nativePosition.Validate();
        if(session!=SessionId || !bound.Contains(id))throw new InvalidOperationException("Wrong runtime session or unreconciled native binding");
        var a=actors[id];
        if(a.Revision!=expectedRevision)throw new InvalidOperationException("Stale actor revision");
        actors[id]=a with {Position=nativePosition,Active=active,Revision=checked(a.Revision+1)};
        healthUpdates[id]++;healthTotalUpdates++;healthVersion++;
    }
    public void Rebind(Guid session,Guid id,string nativeEntityKey,Position nativePosition) {
        Owner();nativePosition.Validate();var a=actors[id];
        if(session!=SessionId||bound.Contains(id)||a.EntityKey!=nativeEntityKey)throw new InvalidOperationException("Invalid native rebind");
        actors[id]=a with {Position=nativePosition,Revision=checked(a.Revision+1)};bound.Add(id);healthVersion++;
    }
    public void Remove(Guid id) {Owner();if(!actors.ContainsKey(id))throw new KeyNotFoundException("Unknown actor");if(retired.Count>=10_000)throw new InvalidOperationException("Retired identity capacity exceeded");actors.Remove(id);bound.Remove(id);retired.Add(id);healthUpdates.Remove(id);healthVersion++;}
    // Human presentation is separate and optional. No AI view is constructed by Add/Restore.
    public void SetView(Guid id,Position camera) {
        Owner();camera.Validate();if(id==Guid.Empty)throw new ArgumentException("View id required");
        if(!views.ContainsKey(id)&&views.Count>=1)throw new InvalidOperationException("At most one human view in this phase");
        views[id]=new(id,camera);
    }
    public void RemoveView(Guid id) {Owner();views.Remove(id);}
    public RenderView[] RenderViews() {Owner();return views.Values.OrderBy(x=>x.ViewId).ToArray();}
    public SimulationInterest[] SimulationInterests() {
        Owner();return actors.Values.Where(x=>x.Active&&bound.Contains(x.Id)).OrderBy(x=>x.Id).Select(x=>new SimulationInterest(x.Id,x.Position)).ToArray();
    }
    public WorldSnapshot Capture() {Owner();return new(1,WorldId,actors.Values.OrderBy(x=>x.Id).ToArray(),retired.Order().ToArray());}
    public string Serialize() {Owner();return JsonSerializer.Serialize(Capture(),JsonOptions);}
    public static ActorRegistry Restore(string json,Guid expectedWorldId) {
        if(json is null||json.Length>1_000_000)throw new FormatException("Snapshot size invalid");
        using var doc=JsonDocument.Parse(json,new JsonDocumentOptions {MaxDepth=8});
        NoDuplicates(doc.RootElement);
        var s=JsonSerializer.Deserialize<WorldSnapshot>(json,JsonOptions)??throw new FormatException("Snapshot missing");
        if(s.Schema!=1||s.WorldId!=expectedWorldId||s.Actors is null||s.Actors.Length>MaxActors||s.RetiredIds is null||s.RetiredIds.Length>10_000)
            throw new FormatException("Snapshot schema/world/capacity mismatch");
        var registry=new ActorRegistry(s.WorldId);
        // Fully validate the detached registry before returning any state to the host.
        foreach(var a in s.Actors){Valid(a);registry.Add(a.Id,a.EntityKey,a.Position);registry.actors[a.Id]=a;}
        foreach(var id in s.RetiredIds){if(id==Guid.Empty||registry.actors.ContainsKey(id)||!registry.retired.Add(id))throw new FormatException("Invalid retired identity");}
        registry.bound.Clear(); // Native saved entities must be reconciled by host before simulation resumes.
        return registry;
    }
    static void NoDuplicates(JsonElement e) {
        if(e.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject()){if(!names.Add(p.Name))throw new FormatException("Duplicate snapshot property");NoDuplicates(p.Value);}}
        else if(e.ValueKind==JsonValueKind.Array)foreach(var v in e.EnumerateArray())NoDuplicates(v);
    }
}
