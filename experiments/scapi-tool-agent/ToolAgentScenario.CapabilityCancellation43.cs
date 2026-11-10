using Engine;
using System.Text.Json;
using Westworld.Runtime.Core;

// Explicit menu-dispatched cancellation contracts in a disposable native world.
// Long Wait is intentionally cancellable; the task's second Wait must never start.
// This does not claim cancellation of an active locomotion action.
internal sealed partial class ToolAgentScenario {
 ToolReceipt cancel43Issued;
 string cancel43StatusTool,cancel43IdKey,cancel43Terminal;
 double cancel43StableUntil;
 Vector3 cancel43Position;
 bool cancel43StablePending;
 ToolReceipt Cancel43Dispatch(string tool,object args){
  var prepared=Matrix40Prompt(tool,args);
  prepared.Prompt.ParseResponse(JsonSerializer.Serialize(new{goal="Stage43 explicit cancellation contract",steps=new[]{new{tool,arguments=args}}}));
  ToolAgentGate.Log(new{kind="matrix40_adapter_validated",tool,offered=prepared.Offers.Select(x=>x.Name).ToArray(),capture=currentObservationId,suite="cancel"});
  var r=Call(tool,args,false);Matrix40Check(r.State=="returned","cancel43_dispatch_returned_"+tool);return r;
 }
 void Cancel43Identity(JsonElement data){
  Matrix40Check(cancel43Issued.Data.TryGetProperty(cancel43IdKey,out var issued)&&issued.ValueKind==JsonValueKind.String&&
   data.TryGetProperty(cancel43IdKey,out var actual)&&actual.ValueKind==JsonValueKind.String&&
   issued.GetString()==actual.GetString()&&issued.GetString()!=Guid.Empty.ToString(),"cancel43_same_"+cancel43IdKey);
 }
 void Cancel43Running(){
  var state=matrix40Receipt.Data;Cancel43Identity(state);
  Matrix40Check(Status(state) is "running" or "waiting" or "blocked","cancel43_observed_running_before_cancel");
  if(cancel43StatusTool=="task_status")Matrix40Check(state.GetProperty("completedSteps").GetInt32()==0,"cancel43_task_second_step_not_started");
 }
 void Cancel43Cancelled(){
  var state=matrix40Receipt.Data;Cancel43Identity(state);
  Matrix40Check(Status(state)=="cancelled","cancel43_native_cancelled");
  Matrix40Check(!actions.IsActionRunning,"cancel43_native_owner_stopped");
  if(cancel43StatusTool=="task_status")Matrix40Check(state.GetProperty("completedSteps").GetInt32()==0,"cancel43_no_completed_task_steps");
  cancel43Terminal=state.GetRawText();cancel43Position=p.ComponentBody.Position;
  cancel43StableUntil=MonotonicSeconds+.6;cancel43StablePending=true;
  ToolAgentGate.Log(new{kind="cancellation43_window_started",statusTool=cancel43StatusTool,identity=state.GetProperty(cancel43IdKey),terminal=state,position=new[]{cancel43Position.X,cancel43Position.Y,cancel43Position.Z},observationSeconds=.6,modelApi=false});
 }
 bool TickCancellation43Stable(){
  if(!cancel43StablePending)return false;
  if(MonotonicSeconds<cancel43StableUntil)return true;
  // Re-enter the same explicit dynamic menu after actual game updates elapsed.
  var terminal=Cancel43Dispatch(cancel43StatusTool,new{}).Data;Cancel43Identity(terminal);
  Matrix40Check(terminal.GetRawText()==cancel43Terminal,"cancel43_terminal_and_step_progress_unchanged");
  Matrix40Check(!actions.IsActionRunning,"cancel43_no_native_action_restarted");
  var position=p.ComponentBody.Position;float drift=(position-cancel43Position).Length();
  Matrix40Check(drift<.03f,"cancel43_actual_position_stable");
  ToolAgentGate.Log(new{kind="cancellation43_stable_effect",statusTool=cancel43StatusTool,terminal,drift,position=new[]{position.X,position.Y,position.Z},noLaterStepProgress=true,modelApi=false});
  cancel43StablePending=false;return true;
 }
 void Matrix43EnqueueCancellation(){
  Matrix40Enqueue("act_Wait",()=>new{duration=.5}); // Native settling before the observation window.
  Matrix40Enqueue("read_self",()=>new{},()=>{
   cancel43StatusTool="action_status";cancel43IdKey="runId";
   cancel43Issued=Cancel43Dispatch("act_Wait",new{duration=10});
  });
  Matrix40Enqueue("action_status",()=>new{},Cancel43Running);
  Matrix40Enqueue("cancel_action",()=>new{},()=>Matrix40Check(matrix40Receipt.Data.ValueKind==JsonValueKind.True,"cancel43_action_cancel_accepted"));
  Matrix40Enqueue("action_status",()=>new{},Cancel43Cancelled);
  Matrix40Enqueue("read_self",()=>new{},()=>{
   cancel43StatusTool="task_status";cancel43IdKey="taskId";
   cancel43Issued=Cancel43Dispatch("submit_plan",new{steps=new object[]{
    new{tool="act_Wait",arguments=new{duration=10},retries=0,minimum_affected=0},
    new{tool="act_Wait",arguments=new{duration=2},retries=0,minimum_affected=0}
   }});
  });
  Matrix40Enqueue("task_status",()=>new{},Cancel43Running);
  Matrix40Enqueue("cancel_task",()=>new{},()=>Matrix40Check(matrix40Receipt.Data.ValueKind==JsonValueKind.True,"cancel43_task_cancel_accepted"));
  Matrix40Enqueue("task_status",()=>new{},Cancel43Cancelled);
 }
}
