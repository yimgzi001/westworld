using Engine;
using System.Text.Json;

// Host navigation travels through NativeToolSession -> HostNavigationToolRouter
// -> the already-bound BoundedLocalNavigation and sole native action owner.
// This fixture does not invoke ActorTools with a fictitious host action.
internal sealed partial class ToolAgentScenario {
 string matrix43NavigationPending;
 Guid matrix43NavigationRequest,matrix43PreviousNavigationRequest;
 Vector3 matrix43NavigationFrom,matrix43CancelledAt;
 double matrix43NavigationDeadline,matrix43NavigationPollAt,matrix43NavigationStableUntil;
 JsonElement Matrix43NavigationRead()=>Matrix40Read("host_navigation_status",new{});
 static Guid Matrix43Request(JsonElement status)=>status.TryGetProperty("request_id",out var id)&&id.TryGetGuid(out var value)?value:Guid.Empty;
 void Matrix43AssertOwner(JsonElement status){
  Matrix40Check(matrix43NavigationRequest!=Guid.Empty&&Matrix43Request(status)==matrix43NavigationRequest,"navigation43_exact_host_request_identity");
  Matrix40Check(!status.GetProperty("owner_quarantined").GetBoolean(),"navigation43_native_owner_not_quarantined");
 }
 void Matrix43BeginWait(string phase){
  var status=matrix40Receipt.Data;Guid request=Matrix43Request(status);
  Matrix40Check(request!=Guid.Empty&&request!=matrix43PreviousNavigationRequest,"navigation43_new_nonempty_host_request");
  Matrix40Check(Status(status) is "running" or "arrived","navigation43_start_admitted_not_only_gateway_returned");
  matrix43PreviousNavigationRequest=request;matrix43NavigationRequest=request;matrix43NavigationPending=phase;matrix43NavigationDeadline=MonotonicSeconds+12;matrix43NavigationPollAt=0;
  ToolAgentGate.Log(new{kind="matrix43_host_navigation_start",phase,request,origin=new[]{matrix43NavigationFrom.X,matrix43NavigationFrom.Y,matrix43NavigationFrom.Z},status,route="NativeToolSession.HostNavigationToolRouter.BoundedLocalNavigation",modelApi=false});
 }
 void Matrix43EnqueueNavigation(){
  Matrix40Enqueue("act_SetPosture",()=>new{posture="standing"});
  Matrix40Enqueue("act_FaceTowards",()=>new{bearing=0,pitch=0,timeout=4});
  Matrix40Enqueue("act_Wait",()=>new{duration=.3});
  Matrix40Enqueue("host_navigation_status",()=>new{},()=>{
   var status=matrix40Receipt.Data;Matrix40Check(!status.GetProperty("busy").GetBoolean()&&!status.GetProperty("owner_quarantined").GetBoolean(),"navigation43_initial_host_idle");
  });
  Matrix40Enqueue("host_navigate_local",()=>{matrix43NavigationFrom=p.ComponentBody.Position;return new{bearing=90,distance=1.5,max_seconds=8};},()=>Matrix43BeginWait("arrival"));
  Matrix40Enqueue("host_navigation_status",()=>new{},()=>{
   var status=matrix40Receipt.Data;Matrix43AssertOwner(status);Matrix40Check(Status(status)=="arrived"&&!status.GetProperty("busy").GetBoolean(),"navigation43_public_arrived_receipt");
  });
  Matrix40Enqueue("host_navigate_local",()=>{matrix43NavigationFrom=p.ComponentBody.Position;return new{bearing=90,distance=3,max_seconds=8};},()=>Matrix43BeginWait("cancel_after_progress"));
  Matrix40Enqueue("host_navigation_status",()=>new{},()=>{
   var status=matrix40Receipt.Data;Matrix43AssertOwner(status);Matrix40Check(Status(status)=="running"&&status.GetProperty("busy").GetBoolean(),"navigation43_public_running_before_cancel");
  });
  Matrix40Enqueue("host_cancel_navigation",()=>new{request_id=matrix43NavigationRequest},()=>{
   Matrix43AssertOwner(matrix40Receipt.Data);Matrix40Check(Status(matrix40Receipt.Data) is "stopping" or "cancelled","navigation43_cancel_admitted_for_exact_request");
   matrix43NavigationPending="cancellation";matrix43NavigationDeadline=MonotonicSeconds+7;matrix43NavigationPollAt=0;matrix43NavigationStableUntil=0;
  });
  Matrix40Enqueue("host_navigation_status",()=>new{},()=>{
   var status=matrix40Receipt.Data;Matrix43AssertOwner(status);Matrix40Check(Status(status)=="cancelled"&&!status.GetProperty("busy").GetBoolean()&&!actions.IsActionRunning,"navigation43_public_cancelled_and_native_idle");
  });
 }
 // Central calls navigation.Update() once per frame, then this hook INSIDE the
 // matrix try block, before queue dispatch. Failure/draining paths skip this hook.
 bool TickCapabilityNavigation43(){
  if(matrix43NavigationPending==null)return false;
  if(MonotonicSeconds>matrix43NavigationDeadline)throw new TimeoutException("navigation43_host_wait_deadline");
  if(MonotonicSeconds<matrix43NavigationPollAt)return true;matrix43NavigationPollAt=MonotonicSeconds+.05;
  var status=Matrix43NavigationRead();Matrix43AssertOwner(status);string state=Status(status);bool busy=status.GetProperty("busy").GetBoolean();
  if(matrix43NavigationPending=="arrival"){
   if(state is "running" or "stopping")return true;
   Matrix40Check(state=="arrived"&&!busy&&!actions.IsActionRunning,"navigation43_arrived_is_native_settled");
   double displacement=(p.ComponentBody.Position-matrix43NavigationFrom).XZ.Length();
   Matrix40Check(displacement>.8&&status.GetProperty("remaining").GetDouble()<=.4,"navigation43_arrival_actual_displacement_and_remaining");
   ToolAgentGate.Log(new{kind="matrix43_host_navigation_arrived",request=matrix43NavigationRequest,status,displacement,position=new[]{p.ComponentBody.Position.X,p.ComponentBody.Position.Y,p.ComponentBody.Position.Z}});matrix43NavigationPending=null;return false;
  }
  if(matrix43NavigationPending=="cancel_after_progress"){
   Matrix40Check(state=="running"&&busy,"navigation43_cancel_probe_still_running");
   double displacement=(p.ComponentBody.Position-matrix43NavigationFrom).XZ.Length();if(displacement<.2)return true;
   ToolAgentGate.Log(new{kind="matrix43_host_navigation_pre_cancel",request=matrix43NavigationRequest,displacement,status});matrix43NavigationPending=null;return false;
  }
  if(state=="stopping")return true;
  Matrix40Check(state=="cancelled"&&!busy&&!actions.IsActionRunning,"navigation43_cancelled_is_native_settled");
  if(matrix43NavigationStableUntil==0){matrix43CancelledAt=p.ComponentBody.Position;matrix43NavigationStableUntil=MonotonicSeconds+.5;return true;}
  if(MonotonicSeconds<matrix43NavigationStableUntil)return true;
  double drift=(p.ComponentBody.Position-matrix43CancelledAt).XZ.Length();Matrix40Check(drift<=.1,"navigation43_no_resumed_motion_half_second_after_cancel");
  ToolAgentGate.Log(new{kind="matrix43_host_navigation_stopped",request=matrix43NavigationRequest,status,windowSeconds=.5,drift,modelApi=false});matrix43NavigationPending=null;return false;
 }
}
