using Engine;
using Game;
using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

// Install only on the native world game thread after actor/native bindings are reconciled.
// No views or player entities are constructed. This is an integration seam, not a complete host.
public sealed class ScapiInterestBackend : IInterestBackend,IDisposable {
    readonly TerrainUpdater updater;readonly SubsystemSpawn spawn;
    readonly Dictionary<Guid,(int Key,Vector3 Center)> leases=new();
    readonly Func<IEnumerable<Vector3>> provider;
    Vector3[] publishedCenters=Array.Empty<Vector3>();
    public bool IsPublished {get;private set;}
    readonly int owner=Environment.CurrentManagedThreadId;int nextKey=-1;bool disposed;
    public ScapiInterestBackend(SubsystemTerrain terrain,SubsystemSpawn spawn){
        ArgumentNullException.ThrowIfNull(terrain);ArgumentNullException.ThrowIfNull(spawn);
        if(terrain.Project is null||terrain.Project!=spawn.Project)throw new ArgumentException("Subsystem world mismatch");
        if(spawn.SimulationCentersProvider is not null)throw new InvalidOperationException("Existing simulation-center provider");
        updater=terrain.TerrainUpdater??throw new ArgumentException("Terrain updater not initialized");this.spawn=spawn;
        provider=()=>{Owner();return publishedCenters.ToArray();};
    }
    void Owner(){if(Environment.CurrentManagedThreadId!=owner)throw new InvalidOperationException("Game-thread ownership required");if(disposed)throw new ObjectDisposedException(nameof(ScapiInterestBackend));}
    int Allocate(){
        // Native camera keys are player indices. Negative keys use a separate, collision-checked namespace.
        while(updater.m_updateParameters.Locations.ContainsKey(nextKey)||updater.m_pendingLocations.ContainsKey(nextKey))nextKey=checked(nextKey-1);
        int key=nextKey;nextKey=checked(nextKey-1);return key;
    }
    public void Upsert(Guid actorId,Position center,InterestRadii radii){
        Owner();radii.Validate();if(actorId==Guid.Empty||!double.IsFinite(center.X)||!double.IsFinite(center.Y)||!double.IsFinite(center.Z)||Math.Abs(center.X)>10_000_000||Math.Abs(center.Y)>1_000_000||Math.Abs(center.Z)>10_000_000)throw new ArgumentException("Invalid interest");
        var native=new Vector3((float)center.X,(float)center.Y,(float)center.Z);
        int key=leases.TryGetValue(actorId,out var lease)?lease.Key:Allocate();
        leases[actorId]=(key,native); // Track the owned key before a possibly partially applied native request.
        updater.SetUpdateLocation(key,new Vector2(native.X,native.Z),radii.FullReady,radii.Content);
    }
    public void Remove(Guid actorId){Owner();if(leases.TryGetValue(actorId,out var lease)){updater.RemoveUpdateLocation(lease.Key);leases.Remove(actorId);}}
    public void Publish(){
        Owner();
        if(spawn.SimulationCentersProvider is not null&&!ReferenceEquals(spawn.SimulationCentersProvider,provider))throw new InvalidOperationException("Simulation-center provider ownership changed");
        publishedCenters=leases.Values.Select(x=>x.Center).ToArray();spawn.SimulationCentersProvider=provider;IsPublished=true;
    }
    public void Dispose(){if(disposed)return;Owner();foreach(var id in leases.Keys.ToArray())Remove(id);
        if(ReferenceEquals(spawn.SimulationCentersProvider,provider))spawn.SimulationCentersProvider=null;
        publishedCenters=Array.Empty<Vector3>();IsPublished=false;disposed=true;
    }
}
