// Compile the actual NativeToolSession source against deterministic inert stand-ins.
// These tests exercise routing only, not Survivalcraft physics or binding implementation.
using System.Text.Json;
namespace Game {public sealed class ComponentPlayer {public object Entity {get;}=new();}}
namespace Game.Westworld {
 public sealed class AtomicActions {public bool IsActionRunning;}
 public sealed record ActorToolDefinition(string Name,string Description,JsonElement Parameters);
 public sealed record ActorToolResult(bool Ok,JsonElement Data,string? Error=null);
 public sealed class ActorTools(object entity,AtomicActions owner){
  public string? TaskStatus="idle";public int Writes;public bool TaskReadFails;
  public bool IsBoundTo(object x)=>ReferenceEquals(entity,x);public bool IsBoundToOwner(AtomicActions x)=>ReferenceEquals(owner,x);
  public IReadOnlyList<ActorToolDefinition> GetTools()=>new[]{"act_MoveTo","plan_craft","read_inventory","task_status","cancel_action","cancel_task"}.Select(x=>new ActorToolDefinition(x,"",JsonSerializer.SerializeToElement(new{}))).ToArray();
  public ActorToolResult Invoke(string name,JsonElement args){if(name=="task_status")return new(!TaskReadFails,JsonSerializer.SerializeToElement(new{status=TaskStatus}));if(name.StartsWith("act_")||name=="plan_craft")Writes++;return new(true,JsonSerializer.SerializeToElement(new{status="succeeded"}));}
 }
}
namespace Westworld.Scapi.Adapter {
 public enum BoundedNavigationStatus {Idle,Running,Stopping,Arrived,Failed,Cancelled}
 public sealed record BoundedNavigationSnapshot(Guid RequestId,BoundedNavigationStatus Status,string Reason,bool Busy,int Segments,double Path,double Remaining);
 public sealed class BoundedLocalNavigation(Game.ComponentPlayer player,Game.Westworld.AtomicActions owner){
  public bool OwnerQuarantined{get;set;}
  Guid request;public bool Busy{get;private set;}public int Starts,Cancels;BoundedNavigationStatus status;
  public bool Matches(Game.ComponentPlayer p,Game.Westworld.AtomicActions a)=>ReferenceEquals(player,p)&&ReferenceEquals(owner,a);
  public BoundedNavigationSnapshot Snapshot=>new(request,status,"fixture",Busy,0,0,999);
  public void Start(Guid r,double b,double d,double s){request=r;Starts++;Busy=true;status=BoundedNavigationStatus.Running;}
  public void Cancel(string why){Cancels++;status=BoundedNavigationStatus.Stopping;}
 }
}
static class NativeChecks {
 public static int Run(){int n=0;void C(bool ok){if(!ok)throw new Exception("native route check "+n);n++;}void R(Action a){try{a();}catch(ArgumentException){n++;return;}catch(InvalidOperationException){n++;return;}throw new Exception("expected rejection");}
  var p=new Game.ComponentPlayer();var owner=new Game.Westworld.AtomicActions();var tools=new Game.Westworld.ActorTools(p.Entity,owner);var session=new Westworld.Scapi.Adapter.NativeToolSession(Guid.NewGuid(),p,tools);var nav=new Westworld.Scapi.Adapter.BoundedLocalNavigation(p,owner);
  R(()=>session.AttachNavigation(new(new Game.ComponentPlayer(),owner),owner));R(()=>session.AttachNavigation(nav,new()));
  session.AttachNavigation(nav,owner);R(()=>session.AttachNavigation(nav,owner));R(()=>new Westworld.Scapi.Adapter.NativeToolSession(Guid.NewGuid(),p,tools));
  C(session.GetTools().Count==9);C(session.Call("host_navigation_status",new{}).Data.GetProperty("remaining").ValueKind==JsonValueKind.Null);
  owner.IsActionRunning=true;C(session.Call("host_navigate_local",new{bearing=90,distance=3}).Data.GetProperty("status").GetString()=="rejected"&&nav.Starts==0);owner.IsActionRunning=false;
  foreach(var status in new string?[]{null,"unknown","running","waiting","blocked"}){tools.TaskStatus=status;C(session.Call("host_navigate_local",new{bearing=90,distance=3}).Data.GetProperty("status").GetString()=="rejected"&&nav.Starts==0);}
  tools.TaskStatus="idle";tools.TaskReadFails=true;C(session.Call("host_navigate_local",new{bearing=90,distance=3}).Data.GetProperty("status").GetString()=="rejected");tools.TaskReadFails=false;
  long seq=session.NextSequence;var req=new Westworld.Runtime.Core.ToolRequest(session.SessionId,session.ActorId,seq,"host_navigate_local",JsonSerializer.SerializeToElement(new{bearing=90,distance=3}));session.Invoke(req);session.Invoke(req);C(nav.Starts==1);
  C(session.Call("act_MoveTo",new{}).Data.GetProperty("status").GetString()=="rejected"&&tools.Writes==0);C(session.Call("plan_craft",new{}).Data.GetProperty("status").GetString()=="rejected"&&tools.Writes==0);C(session.Call("read_inventory",new{}).State=="returned");
  var before=nav.Snapshot.RequestId;C(session.Call("host_cancel_navigation",new{request_id=Guid.NewGuid()}).Data.GetProperty("status").GetString()=="rejected"&&nav.Cancels==0);C(session.Call("host_cancel_navigation",new{request_id=before}).Data.GetProperty("status").GetString()=="stopping"&&nav.Cancels==1);
  C(session.Call("cancel_action",new{}).Data.GetProperty("reason").GetString()=="use_host_cancel_navigation");
  C(session.Call("cancel_task",new{}).Data.GetProperty("reason").GetString()=="use_host_cancel_navigation");
  nav.OwnerQuarantined=true;
  C(session.Call("host_navigation_status",new{}).Data.GetProperty("owner_quarantined").GetBoolean());
  C(session.Call("host_navigate_local",new{bearing=90,distance=1}).Data.GetProperty("reason").GetString()=="navigation_owner_quarantined");
  C(session.Call("act_MoveTo",new{}).Data.GetProperty("reason").GetString()=="navigation_owner_quarantined");
  C(session.Call("cancel_action",new{}).Data.GetProperty("reason").GetString()=="navigation_owner_quarantined");
  C(session.Call("read_inventory",new{}).State=="returned");
  C(session.Call("host_cancel_navigation",new{request_id=before}).State=="returned");
  C(!session.RequiresReconciliation.HasValue);return n;
 }
}
