using System.Text.Json;
namespace Westworld.Runtime.Core;

public sealed record ResourceTaskSpec(Guid WorldId,Guid TaskId,Guid RecipientId,int Value,int RequiredCount);
public sealed record ResourceTaskClaim(Guid RequestId,Guid ActorId);
public sealed record ResourceDeliveryIntent(Guid DeliveryId,Guid ActorId,Guid RecipientId,Guid SourceId,
 Guid OriginEvidenceId,int Value,int Count);
/// <summary>Host-verified native transfer, never a model assertion. Source is a tracked acquired lot.</summary>
public sealed record ResourceDeliveryProof(Guid NativeEventId,Guid DeliveryId,Guid SourceId,Guid OriginEvidenceId,
 Guid SenderId,Guid RecipientId,int Value,int SourceBefore,int SourceAfter,
 int SenderBefore,int SenderAfter,int RecipientBefore,int RecipientAfter);
public sealed record ResourceTaskCheckpoint(int Schema,string WorldCheckpoint,ResourceTaskSpec Spec,
 ResourceTaskClaim[] Claims,ResourceDeliveryIntent[] Intents,ResourceDeliveryProof[] Proofs);
public sealed record ResourceTaskStatus(Guid? Owner,int Delivered,int Required,bool Complete,bool NeedsReconciliation,Guid[] Pending);

/// <summary>Pure task accounting, not an executor or durable transaction store. Host must persist
/// Prepare in the SAME world checkpoint discipline before effect, and verify native proofs.
/// Restoration never emits actions. Uncertain pending transfers remain blocked until proved.</summary>
public sealed class ResourceDeliveryTask {
 const int Capacity=256;
 readonly ResourceTaskSpec spec;readonly int thread=Environment.CurrentManagedThreadId;
 readonly Dictionary<Guid,ResourceTaskClaim> claims=[];
 readonly Dictionary<Guid,ResourceDeliveryIntent> intents=[];
 readonly Dictionary<Guid,ResourceDeliveryProof> proofs=[];
 readonly HashSet<Guid> nativeEvents=[];
 Guid? owner;int delivered;bool restoring;string? restoreCheckpoint;
 public ResourceDeliveryTask(ResourceTaskSpec spec){
  ArgumentNullException.ThrowIfNull(spec);
  if(spec.WorldId==Guid.Empty||spec.TaskId==Guid.Empty||spec.RecipientId==Guid.Empty||spec.Value<0||spec.RequiredCount<=0||spec.RequiredCount>1000000)
   throw new ArgumentException("Invalid task scope");this.spec=spec;
 }
 void Owner(){if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("Owner thread required");}
 void Writable(){Owner();if(restoring)throw new InvalidOperationException("reconcile_world_checkpoint_before_task_writes");}
 public ResourceTaskStatus Status {get{Owner();return new(owner,delivered,spec.RequiredCount,delivered==spec.RequiredCount,restoring,intents.Keys.Where(x=>!proofs.ContainsKey(x)).ToArray());}}
 public ResourceTaskClaim Claim(Guid requestId,Guid actorId){
  Writable();if(requestId==Guid.Empty||actorId==Guid.Empty||actorId==spec.RecipientId)throw new ArgumentException("Invalid claim");
  var claim=new ResourceTaskClaim(requestId,actorId);
  if(claims.TryGetValue(requestId,out var prior)){if(prior!=claim)throw new InvalidOperationException("Claim identity conflict");return prior;}
  if(owner is Guid existing&&existing!=actorId)throw new InvalidOperationException("Already claimed");
  if(claims.Count>=Capacity)throw new InvalidOperationException("Claim capacity");owner=actorId;claims.Add(requestId,claim);return claim;
 }
 public ResourceDeliveryIntent Prepare(ResourceDeliveryIntent intent){
  Writable();ArgumentNullException.ThrowIfNull(intent);
  if(intents.TryGetValue(intent.DeliveryId,out var prior)){if(prior!=intent)throw new InvalidOperationException("Delivery identity conflict");return prior;}
  if(owner!=intent.ActorId||intent.DeliveryId==Guid.Empty||intent.SourceId==Guid.Empty||intent.OriginEvidenceId==Guid.Empty
   ||intent.RecipientId!=spec.RecipientId||intent.Value!=spec.Value||intent.Count<=0||intent.Count>spec.RequiredCount-delivered)
   throw new ArgumentException("Invalid delivery scope or quantity");
  if(intents.Count>=Capacity)throw new InvalidOperationException("Delivery capacity");
  if(intents.Keys.Any(x=>!proofs.ContainsKey(x)))throw new InvalidOperationException("Reconcile pending transfer before another delivery");
  intents.Add(intent.DeliveryId,intent);return intent;
 }
 public ResourceDeliveryProof Accept(ResourceDeliveryProof proof){Writable();return AcceptCore(proof);}
 ResourceDeliveryProof AcceptCore(ResourceDeliveryProof proof){
  ArgumentNullException.ThrowIfNull(proof);
  if(proofs.TryGetValue(proof.DeliveryId,out var prior)){if(prior!=proof)throw new InvalidOperationException("Proof identity conflict");return prior;}
  if(!intents.TryGetValue(proof.DeliveryId,out var i)||proof.NativeEventId==Guid.Empty||nativeEvents.Contains(proof.NativeEventId)
   ||proof.SourceId!=i.SourceId||proof.OriginEvidenceId!=i.OriginEvidenceId||proof.SenderId!=i.ActorId
   ||proof.RecipientId!=i.RecipientId||proof.Value!=i.Value)throw new ArgumentException("Unbound native proof");
  // Exact integer conservation; no position/name matching, model claims, or inferred pickup.
  long moved=(long)proof.SourceBefore-proof.SourceAfter;
  if(moved<=0||moved>i.Count||proof.SourceBefore<0||proof.SourceAfter<0||proof.SenderBefore<0||proof.SenderAfter<0||proof.RecipientBefore<0||proof.RecipientAfter<0
   ||(long)proof.SenderBefore-proof.SenderAfter!=moved
   ||(long)proof.RecipientAfter-proof.RecipientBefore!=moved)throw new ArgumentException("Native transfer not conserved");
  // A source lot cannot regain quantity just because a new delivery ID was chosen.
  var previous=proofs.Values.LastOrDefault(p=>p.SourceId==proof.SourceId);
  if(previous is not null&&(previous.OriginEvidenceId!=proof.OriginEvidenceId||previous.SourceAfter!=proof.SourceBefore))
   throw new ArgumentException("Source lot continuity unknown");
  if(delivered+moved>spec.RequiredCount)throw new InvalidOperationException("Task overdelivery");
  proofs.Add(proof.DeliveryId,proof);nativeEvents.Add(proof.NativeEventId);delivered+=(int)moved;return proof;
 }
 public string Capture(string worldCheckpoint){
  Owner();ValidateCheckpoint(worldCheckpoint);
  if(restoring)throw new InvalidOperationException("Unreconciled restored task cannot be checkpointed");
  return JsonSerializer.Serialize(new ResourceTaskCheckpoint(1,worldCheckpoint,spec,claims.Values.ToArray(),intents.Values.ToArray(),proofs.Values.ToArray()));
 }
 static void ValidateCheckpoint(string s){if(string.IsNullOrWhiteSpace(s)||s.Length>160)throw new ArgumentException("World checkpoint identity required");}
 public static ResourceDeliveryTask Restore(string json,Guid expectedWorld,string expectedCheckpoint){
  ValidateCheckpoint(expectedCheckpoint);if(json is null||json.Length>500000)throw new FormatException("Task checkpoint size");
  using(var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=12}))NoDuplicateFields(doc.RootElement);
  var saved=JsonSerializer.Deserialize<ResourceTaskCheckpoint>(json)??throw new FormatException("Missing task snapshot");
  if(saved.Schema!=1||saved.WorldCheckpoint!=expectedCheckpoint||saved.Spec is null||saved.Spec.WorldId!=expectedWorld
   ||saved.Claims is null||saved.Intents is null||saved.Proofs is null||saved.Claims.Length>Capacity||saved.Intents.Length>Capacity||saved.Proofs.Length>Capacity)
   throw new FormatException("Mismatched task/world checkpoint");
  if(saved.Claims.Any(x=>x is null)||saved.Intents.Any(x=>x is null)||saved.Proofs.Any(x=>x is null))throw new FormatException("Null snapshot entry");
  var task=new ResourceDeliveryTask(saved.Spec);
  foreach(var c in saved.Claims){if(task.claims.ContainsKey(c.RequestId))throw new FormatException("Duplicate saved claim");task.Claim(c.RequestId,c.ActorId);}
  var remaining=saved.Proofs.ToDictionary(p=>p.DeliveryId);
  foreach(var i in saved.Intents){if(task.intents.ContainsKey(i.DeliveryId))throw new FormatException("Duplicate saved delivery");task.Prepare(i);if(remaining.Remove(i.DeliveryId,out var proof))task.Accept(proof);}
  if(remaining.Count!=0)throw new FormatException("Orphan saved proof");
  task.restoring=true;task.restoreCheckpoint=expectedCheckpoint;return task;
 }
 /// <summary>Caller has loaded and verified the exact paired native world checkpoint and receipts.
 /// Empty evidence cannot clear a pending transfer. No retry or item movement is issued here.</summary>
 public void Reconcile(string loadedWorldCheckpoint,IReadOnlyList<ResourceDeliveryProof> nativeReceipts){
  Owner();if(!restoring||loadedWorldCheckpoint!=restoreCheckpoint)throw new InvalidOperationException("Wrong restored checkpoint");
  ArgumentNullException.ThrowIfNull(nativeReceipts);
  foreach(var p in nativeReceipts)AcceptCore(p);
  if(intents.Keys.Any(x=>!proofs.ContainsKey(x)))throw new InvalidOperationException("Native transfer outcome remains unknown; do not replay");
  restoring=false;
 }
 static void NoDuplicateFields(JsonElement e){
  if(e.ValueKind==JsonValueKind.Object){var keys=new HashSet<string>();foreach(var p in e.EnumerateObject()){if(!keys.Add(p.Name))throw new FormatException("Duplicate snapshot field");NoDuplicateFields(p.Value);}}
  else if(e.ValueKind==JsonValueKind.Array)foreach(var x in e.EnumerateArray())NoDuplicateFields(x);
 }
}
