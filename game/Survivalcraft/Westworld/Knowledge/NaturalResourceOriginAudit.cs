using System.Diagnostics;
namespace Game.Westworld;

/// <summary>Host-only drop provenance, not perception or proof of natural generation/actor causation.</summary>
public sealed record NaturalResourceOriginFact(Guid EventId, Guid SourceId, Guid WorldId, string Kind,
 int Value, int Count, int? CellX, int? CellY, int? CellZ, int? OriginalBlockValue,
 Guid? OriginEntityId, Guid? OwnerEntityId, double CreationGameTime, double GameTime, double MonotonicSeconds);

/// <summary>Records the actual object returned by native drop creation after mod callbacks.
/// No mutable world objects are published. Subscriber failures cannot abort the native operation.</summary>
public static class NaturalResourceOriginAudit {
 static long recordFailures,listenerFailures;
 public static long RecordFailures=>Interlocked.Read(ref recordFailures);
 public static long ListenerFailures=>Interlocked.Read(ref listenerFailures);
 public static event Action<NaturalResourceOriginFact> Recorded;
 public static void RecordBlock(Pickable drop,SubsystemTerrain terrain,int x,int y,int z,int originalValue){
  try {
   ArgumentNullException.ThrowIfNull(drop);ArgumentNullException.ThrowIfNull(terrain);
   Record(drop,terrain.Project,"block_drop",drop.Value,drop.Count,x,y,z,originalValue,null,drop.OwnerEntity,
    drop.CreationTime,terrain.Project.FindSubsystem<SubsystemGameInfo>(true).TotalElapsedGameTime);
  } catch { Interlocked.Increment(ref recordFailures); }
 }
 public static void RecordCreature(Pickable drop,ComponentLoot loot){
  try {
   ArgumentNullException.ThrowIfNull(drop);ArgumentNullException.ThrowIfNull(loot);
   Record(drop,loot.Project,"creature_loot",drop.Value,drop.Count,null,null,null,null,loot.Entity,drop.OwnerEntity,
    drop.CreationTime,loot.m_subsystemGameInfo.TotalElapsedGameTime);
  } catch { Interlocked.Increment(ref recordFailures); }
 }
 // Pure capture seam; wrappers above read actual native objects within the protected boundary.
 internal static void Record(object source,object world,string kind,int value,int count,
  int? x,int? y,int? z,int? originalValue,object originEntity,object ownerEntity,double creationTime,double gameTime){
  try {
   ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(world);
   if(count<=0||!double.IsFinite(creationTime)||!double.IsFinite(gameTime)||creationTime<0||gameTime<creationTime
    ||(kind!="block_drop"&&kind!="creature_loot")
    ||(kind=="block_drop"&&(!x.HasValue||!y.HasValue||!z.HasValue||!originalValue.HasValue||originEntity!=null))
    ||(kind=="creature_loot"&&(x.HasValue||y.HasValue||z.HasValue||originalValue.HasValue||originEntity==null)))
    throw new ArgumentException("Incomplete native provenance");
   Publish(new(Guid.NewGuid(),NativeAcquisitionAudit.IdentityFor(source),NativeAcquisitionAudit.IdentityFor(world),
    kind,value,count,x,y,z,originalValue,originEntity is null?null:NativeAcquisitionAudit.IdentityFor(originEntity),
    ownerEntity is null?null:NativeAcquisitionAudit.IdentityFor(ownerEntity),creationTime,gameTime,
    Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency));
  } catch { Interlocked.Increment(ref recordFailures); }
 }
 internal static void Publish(NaturalResourceOriginFact fact){
  var handlers=Recorded;if(handlers is null)return;
  foreach(Action<NaturalResourceOriginFact> listener in handlers.GetInvocationList())
   try{listener(fact);}catch{Interlocked.Increment(ref listenerFailures);}
 }
}
