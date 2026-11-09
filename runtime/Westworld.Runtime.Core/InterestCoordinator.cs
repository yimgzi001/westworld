namespace Westworld.Runtime.Core;

public readonly record struct InterestRadii(float FullReady, float Content) {
    public void Validate(){if(!float.IsFinite(FullReady)||!float.IsFinite(Content)||FullReady<16||Content<FullReady||Content>256)throw new ArgumentException("Interest radii outside experimental bounds");}
}
public interface IInterestBackend {
    // Requests only: no guarantee native terrain processing has completed.
    // Idempotent per actor. A throwing Upsert may have applied partially; Remove must also
    // tolerate an unknown id so the coordinator can retain and release attempted requests.
    void Upsert(Guid actorId,Position center,InterestRadii radii);
    void Remove(Guid actorId);
    void Publish();
}
// Host calls once on its game thread after reconciling native positions. Never reads render views.
public sealed class InterestCoordinator {
    readonly ActorRegistry registry;readonly IInterestBackend backend;readonly InterestRadii radii;
    readonly HashSet<Guid> submitted=new();readonly int owner=Environment.CurrentManagedThreadId;
    public InterestCoordinator(ActorRegistry registry,IInterestBackend backend,InterestRadii radii){ArgumentNullException.ThrowIfNull(registry);ArgumentNullException.ThrowIfNull(backend);radii.Validate();this.registry=registry;this.backend=backend;this.radii=radii;}
    void Owner(){if(Environment.CurrentManagedThreadId!=owner)throw new InvalidOperationException("Game-thread ownership required");}
    public void Sync(){Owner();var interests=registry.SimulationInterests();var active=interests.Select(x=>x.ActorId).ToHashSet();
        foreach(var id in submitted.Where(id=>!active.Contains(id)).ToArray()){backend.Remove(id);submitted.Remove(id);}
        foreach(var x in interests){submitted.Add(x.ActorId);backend.Upsert(x.ActorId,x.Center,radii);}
        backend.Publish();
    }
    public void Release(){Owner();foreach(var id in submitted.ToArray()){backend.Remove(id);submitted.Remove(id);}backend.Publish();}
}
