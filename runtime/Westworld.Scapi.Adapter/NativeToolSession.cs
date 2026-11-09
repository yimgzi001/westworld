using System.Text.Json;
using Game;
using Game.Westworld;
using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

// One existing ActorTools/AtomicActions owner, all calls on its game thread. Models
// get JSON observations and schemas, never this privileged adapter or native objects.
public sealed class NativeToolSession {
 static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComponentPlayer,NativeToolSession> owners=new();
 readonly ActorTools tools;readonly ToolCallLedger ledger;readonly HashSet<string> allowed;
 public Guid SessionId {get;}=Guid.NewGuid(); public Guid ActorId {get;}
 public long? RequiresReconciliation=>ledger.RequiresReconciliation;
 public void Reconcile(long uncertainSequence,long evidenceReadSequence)=>ledger.Reconcile(uncertainSequence,evidenceReadSequence);
 public long NextSequence=>ledger.NextSequence;
 public NativeToolSession(Guid actor,ComponentPlayer player,ActorTools tools,IEnumerable<string>? allowedTools=null){
  ArgumentNullException.ThrowIfNull(player);ArgumentNullException.ThrowIfNull(tools);
  if(!tools.IsBoundTo(player.Entity))throw new ArgumentException("Tool owner does not match native player");
  ActorId=actor;this.tools=tools;ledger=new(SessionId,actor);
  var native=tools.GetTools().Select(t=>t.Name).ToHashSet(StringComparer.Ordinal);
  allowed=allowedTools?.ToHashSet(StringComparer.Ordinal)??native;
  if(!allowed.IsSubsetOf(native))throw new ArgumentException("Unknown or unavailable native tool");
  lock(owners){if(owners.TryGetValue(player,out _))throw new InvalidOperationException("Native actor already has a tool session");owners.Add(player,this);}
 }
 public IReadOnlyList<ActorToolDefinition> GetTools(){_=ledger.NextSequence;return tools.GetTools().Where(t=>allowed.Contains(t.Name)).ToArray();}
 public static bool IsReadOnly(string name)=>name.StartsWith("read_",StringComparison.Ordinal)||name.StartsWith("find_",StringComparison.Ordinal)||name is "observe" or "action_status" or "task_status";
 public static bool IsStopOnly(string name)=>name is "cancel_action" or "cancel_task";
 public ToolReceipt Read(long sequence)=>ledger.Read(sequence);
 public ToolReceipt Invoke(ToolRequest request){
  if(request.ReadOnly!=IsReadOnly(request.Tool)||request.StopOnly!=IsStopOnly(request.Tool))throw new ArgumentException("Read-only classification mismatch");
  return ledger.Invoke(request,(name,args)=>{
  if(!allowed.Contains(name))return new(false,JsonSerializer.SerializeToElement((object?)null),"tool_not_offered");
  var result=tools.Invoke(name,args);return new(result.Ok,result.Data,result.Error);
 });}
 public ToolReceipt Call(string name,object arguments)=>Invoke(new(SessionId,ActorId,NextSequence,name,JsonSerializer.SerializeToElement(arguments),IsReadOnly(name),IsStopOnly(name)));
}
