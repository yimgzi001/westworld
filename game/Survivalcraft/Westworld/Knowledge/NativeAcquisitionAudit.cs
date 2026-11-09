using System.Diagnostics;
using System.Runtime.CompilerServices;
namespace Game.Westworld;

/// <summary>Host-only immutable acquisition facts. Never expose this audit through actor perception.</summary>
public sealed record NativeAcquisitionFact(Guid SourceId, Guid ActorId, int Value, int SourceBefore,
 int SourceAfter, int? InventoryBefore, int? InventoryAfter, string Method, double MonotonicSeconds, double GameTime);
public static class NativeAcquisitionAudit {
 sealed class Identity { public readonly Guid Id=Guid.NewGuid(); }
 static readonly ConditionalWeakTable<object,Identity> identities=new();
 static long listenerFailures;
 public static Guid IdentityFor(object instance){ArgumentNullException.ThrowIfNull(instance);return identities.GetValue(instance,_=>new Identity()).Id;}
 public static event Action<NativeAcquisitionFact> Recorded;
 public static long ListenerFailures=>Interlocked.Read(ref listenerFailures);
 public static int? InventoryCount(IInventory inventory,int value){try{if(inventory is null)return null;int n=0;for(int i=0;i<inventory.SlotsCount;i++)if(inventory.GetSlotValue(i)==value)n=checked(n+inventory.GetSlotCount(i));return n;}catch{return null;}}
 public static void Record(object source,object actor,int value,int sourceBefore,int sourceAfter,int? inventoryBefore,int? inventoryAfter,string method,double gameTime){
  try{Publish(new(IdentityFor(source),IdentityFor(actor),value,sourceBefore,sourceAfter,inventoryBefore,inventoryAfter,method,Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency,gameTime));}catch{Interlocked.Increment(ref listenerFailures);}
 }
 public static void Publish(NativeAcquisitionFact fact){
  if(fact is null)return;var handlers=Recorded;if(handlers is null)return;
  foreach(Action<NativeAcquisitionFact> handler in handlers.GetInvocationList())try{handler(fact);}catch{Interlocked.Increment(ref listenerFailures);}
 }
}
