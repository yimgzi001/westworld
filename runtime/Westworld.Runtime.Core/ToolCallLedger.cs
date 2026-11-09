using System.Text.Json;
namespace Westworld.Runtime.Core;

public sealed record ToolRequest(Guid SessionId,Guid ActorId,long Sequence,string Tool,JsonElement Arguments,bool ReadOnly=false,bool StopOnly=false);
public sealed record ToolBackendResult(bool Ok,JsonElement Data,string? Error=null);
public sealed record ToolReceipt(Guid SessionId,Guid ActorId,long Sequence,string Tool,string State,JsonElement Data,string? Error);

// Ordered, bounded in-memory admission ledger, NOT crash-durable transaction storage.
// The (session,actor,sequence) tuple is the action identity. An evicted sequence is
// never executed again. Reconnect reads receipts/current native state before new work.
public sealed class ToolCallLedger {
 readonly Guid session,actor;readonly int capacity,owner=Environment.CurrentManagedThreadId;
 readonly Dictionary<long,(string Tool,string Arguments,bool ReadOnly,bool StopOnly,ToolReceipt Receipt)> receipts=new();
 readonly Queue<long> order=new();long highest;bool invoking;long? uncertain;
 public long? RequiresReconciliation {get {Owner();return uncertain;}}
 public long NextSequence { get { Owner();return checked(highest+1); } }
 public ToolCallLedger(Guid session,Guid actor,int capacity=256){if(session==Guid.Empty||actor==Guid.Empty||capacity<1||capacity>4096)throw new ArgumentException("Invalid ledger scope or capacity");this.session=session;this.actor=actor;this.capacity=capacity;}
 void Owner(){if(Environment.CurrentManagedThreadId!=owner)throw new InvalidOperationException("Owner/game thread required");}
 public ToolReceipt Read(long sequence){Owner();return receipts.TryGetValue(sequence,out var r)?r.Receipt:throw new InvalidOperationException(sequence<=highest?"receipt_expired_reconcile_native_state":"request_not_admitted");}
 public void Reconcile(long uncertainSequence,long evidenceReadSequence){
  Owner();if(invoking||uncertain!=uncertainSequence||evidenceReadSequence<=uncertainSequence||!receipts.TryGetValue(evidenceReadSequence,out var proof)||!proof.ReadOnly||proof.Receipt.State!="returned")throw new InvalidOperationException("A later successful native observation is required");
  uncertain=null;
 }
 public ToolReceipt Invoke(ToolRequest request,Func<string,JsonElement,ToolBackendResult> backend){
  Owner();ArgumentNullException.ThrowIfNull(request);ArgumentNullException.ThrowIfNull(backend);
  if(invoking)throw new InvalidOperationException("Reentrant tool submission forbidden");
  if(request.SessionId!=session||request.ActorId!=actor)throw new ArgumentException("Tool scope mismatch");
  if(request.Sequence<=0||string.IsNullOrWhiteSpace(request.Tool)||request.Tool.Length>80||request.Arguments.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Invalid tool envelope");
  if(request.ReadOnly&&request.StopOnly)throw new ArgumentException("Read-only and stop-only are distinct");
  string args=request.Arguments.GetRawText();if(args.Length>32768)throw new ArgumentException("Tool payload exceeds bound");
  if(receipts.TryGetValue(request.Sequence,out var old)){
   if(old.Tool!=request.Tool||old.Arguments!=args||old.ReadOnly!=request.ReadOnly||old.StopOnly!=request.StopOnly)throw new InvalidOperationException("Sequence payload conflict");
   return old.Receipt;
  }
  if(request.Sequence<=highest)throw new InvalidOperationException("receipt_expired_reconcile_native_state");
  if(request.Sequence!=checked(highest+1))throw new InvalidOperationException("Sequence gap");
  if(uncertain.HasValue&&!request.ReadOnly&&!request.StopOnly)throw new InvalidOperationException("reconcile_before_another_write");
  // Record admission before calling a backend that could partially affect the world.
  highest=request.Sequence;invoking=true;ToolReceipt receipt;
  try{
   var result=backend(request.Tool,request.Arguments.Clone());
   if(!result.Ok&&!request.ReadOnly)uncertain=request.Sequence;
   receipt=new(session,actor,request.Sequence,request.Tool,result.Ok?"returned":"native_error",result.Data.Clone(),result.Error);
  }catch(Exception e){
   // The caller must observe/reconcile, not resubmit the same command under a new ID
   // merely because this response cannot establish whether native effects occurred.
   if(!request.ReadOnly)uncertain=request.Sequence;
   receipt=new(session,actor,request.Sequence,request.Tool,"uncertain",JsonSerializer.SerializeToElement((object?)null),e.GetType().Name);
  }finally{invoking=false;}
  receipts.Add(request.Sequence,(request.Tool,args,request.ReadOnly,request.StopOnly,receipt));order.Enqueue(request.Sequence);
  while(order.Count>capacity)receipts.Remove(order.Dequeue());
  return receipt;
 }
}
