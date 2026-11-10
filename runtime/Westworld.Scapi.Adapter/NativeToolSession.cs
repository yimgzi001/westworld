using System.Text.Json;
using Game;
using Game.Westworld;
using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

// One existing ActorTools/AtomicActions owner, all calls on its game thread. Models
// get JSON observations and schemas, never this privileged adapter or native objects.
public sealed class NativeToolSession {
 static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComponentPlayer,NativeToolSession> owners=new();
 readonly ComponentPlayer player;HostNavigationToolRouter? navigation;AtomicActions? actionOwner;
 readonly ActorTools tools;readonly ToolCallLedger ledger;readonly HashSet<string> allowed;
 public Guid SessionId {get;}=Guid.NewGuid(); public Guid ActorId {get;}
 public long? RequiresReconciliation=>ledger.RequiresReconciliation;
 public void Reconcile(long uncertainSequence,long evidenceReadSequence)=>ledger.Reconcile(uncertainSequence,evidenceReadSequence);
 public long NextSequence=>ledger.NextSequence;
 public NativeToolSession(Guid actor,ComponentPlayer player,ActorTools tools,IEnumerable<string>? allowedTools=null){
  ArgumentNullException.ThrowIfNull(player);ArgumentNullException.ThrowIfNull(tools);
  if(!tools.IsBoundTo(player.Entity))throw new ArgumentException("Tool owner does not match native player");
  ActorId=actor;this.player=player;this.tools=tools;ledger=new(SessionId,actor);
  var native=tools.GetTools().Select(t=>t.Name).ToHashSet(StringComparer.Ordinal);
  allowed=allowedTools?.ToHashSet(StringComparer.Ordinal)??native;
  if(!allowed.IsSubsetOf(native))throw new ArgumentException("Unknown or unavailable native tool");
  lock(owners){if(owners.TryGetValue(player,out _))throw new InvalidOperationException("Native actor already has a tool session");owners.Add(player,this);}
 }
 public IReadOnlyList<ActorToolDefinition> GetTools(){_=ledger.NextSequence;return tools.GetTools().Where(t=>allowed.Contains(t.Name)).Concat(navigation is null?Array.Empty<ActorToolDefinition>():HostNavigationToolRouter.Definitions().Select(d=>new ActorToolDefinition(d.Name,d.Description,d.Parameters))).ToArray();}
 public static bool IsReadOnly(string name)=>name.StartsWith("read_",StringComparison.Ordinal)||name.StartsWith("find_",StringComparison.Ordinal)||name is "observe" or "action_status" or "task_status" or "host_navigation_status";
 public static bool IsStopOnly(string name)=>name is "cancel_action" or "cancel_task" or "host_cancel_navigation";
 public ToolReceipt Read(long sequence)=>ledger.Read(sequence);
 public ToolReceipt Invoke(ToolRequest request){
  if(request.ReadOnly!=IsReadOnly(request.Tool)||request.StopOnly!=IsStopOnly(request.Tool))throw new ArgumentException("Read-only classification mismatch");
  return ledger.Invoke(request,(name,args)=>{
  if(HostNavigationToolRouter.IsHostTool(name)){
   if(navigation is null)return HostNavigationToolRouter.Rejected("navigation_not_registered");
   if(name=="host_navigate_local"&&navigation.OwnerQuarantined)return HostNavigationToolRouter.Rejected("navigation_owner_quarantined");
   if(name=="host_navigate_local"&&!navigation.Busy){
    var task=tools.Invoke("task_status",JsonSerializer.SerializeToElement(new{}));
    bool idle=task.Ok&&task.Data.ValueKind==JsonValueKind.Object&&task.Data.TryGetProperty("status",out var state)&&state.ValueKind==JsonValueKind.String&&state.GetString() is "idle" or "succeeded" or "failed" or "cancelled" or "skipped";
    if(actionOwner!.IsActionRunning||!idle)return HostNavigationToolRouter.Rejected("native_owner_busy_or_unknown");
   }
   return navigation.Invoke(name,args);
  }
  if(!IsReadOnly(name)&&navigation?.OwnerQuarantined==true)return HostNavigationToolRouter.Rejected("navigation_owner_quarantined");
  if(navigation?.Busy==true&&!IsReadOnly(name))return HostNavigationToolRouter.Rejected(IsStopOnly(name)?"use_host_cancel_navigation":"navigation_busy");
  if(!allowed.Contains(name))return HostNavigationToolRouter.Rejected("tool_not_offered");
  var result=tools.Invoke(name,args);return new(result.Ok,result.Data,result.Error);
 });}
 public void AttachNavigation(BoundedLocalNavigation bounded,AtomicActions existingOwner){
  _=ledger.NextSequence;ArgumentNullException.ThrowIfNull(bounded);ArgumentNullException.ThrowIfNull(existingOwner);
  if(navigation is not null)throw new InvalidOperationException("Navigation already registered");
  if(!tools.IsBoundToOwner(existingOwner)||!bounded.Matches(player,existingOwner))throw new ArgumentException("Navigation owner/player mismatch");
  navigation=new HostNavigationToolRouter(player,existingOwner,new BoundedControl(player,existingOwner,bounded));actionOwner=existingOwner;
 }
 sealed class BoundedControl(ComponentPlayer player,AtomicActions owner,BoundedLocalNavigation bounded):IHostNavigationControl {
  public bool Matches(object p,object a)=>ReferenceEquals(player,p)&&ReferenceEquals(owner,a)&&bounded.Matches(player,owner);
  public bool Busy=>bounded.Busy;
  public bool OwnerQuarantined=>bounded.OwnerQuarantined;
  public Guid RequestId=>bounded.Snapshot.RequestId;
  public JsonElement ReadStatus(){var s=bounded.Snapshot;return JsonSerializer.SerializeToElement(new{request_id=s.RequestId,status=s.Status.ToString().ToLowerInvariant(),reason=s.Reason,busy=s.Busy,owner_quarantined=bounded.OwnerQuarantined,segments=s.Segments,path=s.Path,remaining=s.RequestId==Guid.Empty?(double?)null:s.Remaining,readiness=s.Readiness,start_failure_readiness=s.StartFailureReadiness,elapsed_seconds=s.ElapsedSeconds,budget_seconds=s.BudgetSeconds,completion_meaning=s.CompletionMeaning});}
  public void Start(Guid request,double bearing,double distance,double maxSeconds)=>bounded.Start(request,bearing,distance,maxSeconds);
  public void Cancel()=>bounded.Cancel("host_cancel_navigation");
 }
 public ToolReceipt Call(string name,object arguments)=>Invoke(new(SessionId,ActorId,NextSequence,name,JsonSerializer.SerializeToElement(arguments),IsReadOnly(name),IsStopOnly(name)));
}
