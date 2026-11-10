using Engine;
using Game;
using Westworld.Runtime.Core;
using Westworld.Decisions.Local;
using System.Text.Json;

// Deterministic native gateway coverage, not model autonomy or a claim that every
// dynamic menu candidate has been exercised. Only run in a new disposable world.
internal sealed partial class ToolAgentScenario {
 string Matrix41Suite=>Environment.GetEnvironmentVariable("WESTWORLD_MATRIX41")??"baseline";
 bool Matrix40Enabled=>ToolAgentGate.Mode=="matrix_single";
 readonly Dictionary<Point3,int> matrix40Cells=[];
 readonly List<object> matrix40Failures=[]; bool matrix40Unsafe;
 bool matrix40PendingHead; double? matrix40ObserveDeadline; double matrix40ObserveRetryAt,matrix40StatusPollAt;
 sealed class Matrix40ObservationPending(string message):Exception(message);
 readonly Queue<(string Name,Func<object> Args,Action Verify)> matrix40Cases=[];
 int matrix40Phase, matrix40Checks; double matrix40Started; string matrix40Waiting;
 ToolReceipt matrix40Receipt; Action matrix40Verify; Vector3 matrix40MoveFrom;
 int matrix40X,matrix40Y,matrix40Z,matrix40WoodBefore,matrix40PlanksBefore,matrix40PlaceBefore; Point3 matrix40PlacedCell;
 string matrix40Case; bool matrix40Draining,matrix40CleanupUncertain; double matrix40DrainStarted,matrix40CancelStableAt; string matrix40CancelSnapshot; bool matrix40CancelTask; string matrix40Recipe; int matrix40RecipeCount;
 void Matrix40Check(bool value,string check){ToolAgentGate.Log(new{kind="matrix40_assertion",@case=matrix40Case,check,passed=value});if(!value)throw new InvalidOperationException(check);matrix40Checks++;}
 JsonElement Matrix40Read(string tool,object args){var r=Call(tool,args,false);if(r.State!="returned")throw new InvalidOperationException(tool+":"+r.Error);return r.Data;}
 void Matrix40Set(int x,int y,int z,int value){var t=p.Project.FindSubsystem<SubsystemTerrain>(true);var cell=new Point3(x,y,z);int before=t.Terrain.GetCellValue(x,y,z);matrix40Cells.TryAdd(cell,before);if(before!=value)t.ChangeCell(x,y,z,value);}
 object Matrix40State()=>new{position=new[]{p.ComponentBody.Position.X,p.ComponentBody.Position.Y,p.ComponentBody.Position.Z},inventory=Matrix40Read("read_inventory",new{}),self=Matrix40Read("read_self",new{}),task=Matrix40Read("task_status",new{})};
 string Matrix40Block(int x,int y,int z){var o=Snapshot().Observation;foreach(var b in o.GetProperty("visibleBlocks").EnumerateArray()){var c=b.GetProperty("cell");if(c.GetProperty("x").GetInt32()==x&&c.GetProperty("y").GetInt32()==y&&c.GetProperty("z").GetInt32()==z)return b.GetProperty("reference").GetString();}var coverage=o.TryGetProperty("coverage",out var coverageValue)?coverageValue:JsonSerializer.SerializeToElement(new{});ToolAgentGate.Log(new{kind="matrix40_observation_pending",@case=matrix40Case,coverage,note="Public snapshot only; no extra range or hidden target lookup."});throw new Matrix40ObservationPending("fixture_target_not_currently_visible");}
 string Matrix40PlaceTopFace(){
  var observation=Snapshot().Observation;
  if(!observation.TryGetProperty("focus",out var focus)||focus.ValueKind!=JsonValueKind.Object||!focus.TryGetProperty("kind",out var kind)||kind.GetString()!="block"||!focus.TryGetProperty("face",out var face)||!face.TryGetInt32(out int side)||side!=4||!focus.TryGetProperty("reference",out var reference)||reference.ValueKind!=JsonValueKind.String)
   throw new Matrix40ObservationPending("placement_requires_current_observed_top_face");
  var hit=focus.GetProperty("position");
  // The public ray hit and observed upward face determine the adjacent cell.
  // No hidden block lookup and no fabricated surface reference selects the target.
  matrix40PlacedCell=new(Terrain.ToCell(hit.GetProperty("x").GetSingle()),Terrain.ToCell(hit.GetProperty("y").GetSingle()+.001f),Terrain.ToCell(hit.GetProperty("z").GetSingle()));
  matrix40PlaceBefore=ExactCount(PlanksBlock.Index);
  ToolAgentGate.Log(new{kind="matrix40_place_target",source="public_focus_top_face",focus,expectedCell=new[]{matrix40PlacedCell.X,matrix40PlacedCell.Y,matrix40PlacedCell.Z},inventoryBefore=matrix40PlaceBefore});
  return reference.GetString();
 }
 void Matrix40Enqueue(string name,Func<object> args,Action verify=null)=>matrix40Cases.Enqueue((name,args,verify));
 void Matrix40Prepare(){
  var b=p.ComponentBody;matrix40X=Terrain.ToCell(b.Position.X);matrix40Y=Terrain.ToCell(b.Position.Y);matrix40Z=Terrain.ToCell(b.Position.Z);
  for(int x=matrix40X-5;x<=matrix40X+5;x++)for(int z=matrix40Z-5;z<=matrix40Z+5;z++){Matrix40Set(x,matrix40Y-1,z,DirtBlock.Index);for(int y=matrix40Y;y<=matrix40Y+3;y++)Matrix40Set(x,y,z,0);}
  b.Position=new(matrix40X+.5f,matrix40Y,matrix40Z+.5f);b.Velocity=Vector3.Zero;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2);
  var inv=p.ComponentMiner.Inventory;if(inv.GetSlotCount(8)!=0||inv.GetSlotCount(10)!=0)throw new InvalidOperationException("fixture_slots_not_empty");inv.AddSlotItems(8,OakWoodBlock.Index,2);matrix40WoodBefore=ExactCount(OakWoodBlock.Index);matrix40PlanksBefore=ExactCount(PlanksBlock.Index);
  ToolAgentGate.Log(new{kind="matrix40_fixture",artificial=true,worldEdited=true,positionEdited=true,inventoryInjected=true,injected=new{slot=8,value=OakWoodBlock.Index,count=2},modelApi=false,route="adapter_validated",note="Deterministic selected-tool menu validation before native dispatch; not model autonomy. Preparation Dismount remains native-only."});
  if(Matrix41Suite!="baseline"){
   ToolAgentGate.Log(new{kind="matrix41_suite",suite=Matrix41Suite,isolatedWorld=true,modelApi=false});
   switch(Matrix41Suite){
    case "queries": Matrix43EnqueueQueries(); break;
    case "cancel": Matrix43EnqueueCancellation(); break;
    case "navigation": Matrix43EnqueueNavigation(); break;
    case "mount": Matrix42EnqueueMount(); break;
    case "climb":
    case "sleep": Matrix42EnqueueBody(); break;
    case "interact": Matrix42EnqueueInteract(); break;
    case "equipment": Matrix41EnqueueEquipment(); break;
    case "items": Matrix41EnqueueItems(); break;
    case "combat": Matrix41EnqueueCombat(); break;
    case "furniture": Matrix41EnqueueFurniture(); break;
    default: throw new InvalidOperationException("unknown_matrix41_suite");
   }
   return;
  }
  foreach(string q in new[]{"read_self","read_environment","read_inventory","read_equipment","read_social"})Matrix40Enqueue(q,()=>new{});
  Matrix40Enqueue("read_map",()=>new{offset=0,limit=4});
  Matrix40Enqueue("act_SetPosture",()=>new{posture="crouching"});Matrix40Enqueue("act_SetPosture",()=>new{posture="standing"});
  Matrix40Enqueue("act_FaceTowards",()=>new{bearing=0,pitch=0,timeout=4});Matrix40Enqueue("act_Jump",()=>new{});
  Matrix40Enqueue("act_Wait",()=>new{duration=1});
  Matrix40Enqueue("act_MoveTo",()=>{matrix40MoveFrom=b.Position;return new{bearing=0,distance=1,range=.3,timeout=8};},()=>Matrix40Check((b.Position-matrix40MoveFrom).XZ.Length()>.4f,"move_actual_displacement"));
  Matrix40Enqueue("act_SelectItem",()=>new{slot=8,value=OakWoodBlock.Index});
  Matrix40Enqueue("act_TransferItems",()=>{var o=Matrix40Read("observe",new{});string own=o.GetProperty("inventoryReference").GetString();return new{source=own,source_slot=8,source_value=OakWoodBlock.Index,destination=own,destination_slot=10,destination_value=0,count=1};},()=>Matrix40Check(inv.GetSlotValue(10)==OakWoodBlock.Index&&inv.GetSlotCount(10)==1&&ExactCount(OakWoodBlock.Index)==matrix40WoodBefore,"transfer_exact_slots_and_conservation"));
  Matrix40Enqueue("find_recipes",()=>new{result_value=PlanksBlock.Index,offset=0,limit=4},()=>{var entries=Matrix40Read("find_recipes",new{result_value=PlanksBlock.Index,offset=0,limit=4});var recipe=entries.EnumerateArray().First(r=>r.GetProperty("recipe").GetProperty("resultValue").GetInt32()==PlanksBlock.Index);matrix40Recipe=recipe.GetProperty("reference").GetString();matrix40RecipeCount=recipe.GetProperty("recipe").GetProperty("resultCount").GetInt32();});
  Matrix40Enqueue("plan_craft",()=>new{workspace=Matrix40Read("observe",new{}).GetProperty("craftingReference").GetString(),recipe=matrix40Recipe,count=matrix40RecipeCount},()=>Matrix40Check(ExactCount(PlanksBlock.Index)==matrix40PlanksBefore+matrix40RecipeCount,"craft_actual_inventory_gain"));
  Matrix40Enqueue("act_Wait",()=>new{duration=.1},()=>Matrix40Set(matrix40X+3,matrix40Y,matrix40Z,DirtBlock.Index));
  Matrix40Enqueue("act_FaceTowards",()=>new{destination=Matrix40Block(matrix40X+3,matrix40Y,matrix40Z),timeout=4});
  Matrix40Enqueue("act_Dig",()=>new{target=Matrix40Block(matrix40X+3,matrix40Y,matrix40Z),timeout=8},()=>Matrix40Check(p.Project.FindSubsystem<SubsystemTerrain>(true).Terrain.GetCellContents(matrix40X+3,matrix40Y,matrix40Z)==0,"dig_actual_world_removal"));
  Matrix40Enqueue("act_FaceTowards",()=>new{bearing=0,pitch=-45,timeout=4});
  Matrix40Enqueue("act_Place",()=>{var slot=Enumerable.Range(0,inv.SlotsCount).First(s=>inv.GetSlotValue(s)==PlanksBlock.Index&&inv.GetSlotCount(s)>0);return new{target=Matrix40PlaceTopFace(),slot,value=PlanksBlock.Index};},()=>{Matrix40Check(p.Project.FindSubsystem<SubsystemTerrain>(true).Terrain.GetCellContents(matrix40PlacedCell.X,matrix40PlacedCell.Y,matrix40PlacedCell.Z)==PlanksBlock.Index,"place_actual_world_gain");Matrix40Check(ExactCount(PlanksBlock.Index)==matrix40PlaceBefore-1,"place_exact_inventory_consumption");});
  Matrix40Enqueue("act_Dig",()=>new{target=Matrix40Block(matrix40PlacedCell.X,matrix40PlacedCell.Y,matrix40PlacedCell.Z),timeout=8},()=>Matrix40Check(p.Project.FindSubsystem<SubsystemTerrain>(true).Terrain.GetCellContents(matrix40PlacedCell.X,matrix40PlacedCell.Y,matrix40PlacedCell.Z)==0,"placed_block_native_removed_before_settings"));
  Matrix40Enqueue("submit_plan",()=>new{steps=new[]{new{tool="act_Wait",arguments=new{duration=.2},retries=0,minimum_affected=0},new{tool="act_Wait",arguments=new{duration=.2},retries=0,minimum_affected=0}}});
  Matrix40Enqueue("submit_plan",()=>{matrix40CancelTask=true;return new{steps=new[]{new{tool="act_Wait",arguments=new{duration=3},retries=0,minimum_affected=0}}};});
  Matrix40EnqueueSettings();
 }
 (ToolDecisionPrompt Prompt,IReadOnlyList<ToolOffer> Offers) Matrix40Prompt(string tool,object args){
  capabilitySelection=new(new[]{tool},Array.Empty<string>());var snapshot=Snapshot();var prompt=Prompt(snapshot.Observation,snapshot.Inventory,out var offers);
  if(tool!="submit_plan")return(prompt,offers);
  var needed=JsonSerializer.SerializeToElement(args).GetProperty("steps").EnumerateArray().Select(s=>s.GetProperty("tool").GetString()).ToHashSet();
  for(int page=0;page<Math.Min(128,Math.Max(1,capabilityPlanPages));page++){
   var select=JsonSerializer.SerializeToElement(new{select_tool="submit_plan",plan_page=page});
   prompt.ParseResponse(JsonSerializer.Serialize(new{goal="Select finite plan schema page",steps=new[]{new{tool="read_capability_schema",arguments=select}}}));
   if(!HandleCapabilityDiscovery(new ToolDecisionStep("read_capability_schema",select)))throw new InvalidOperationException("matrix40_plan_page_selection_not_handled");
   snapshot=Snapshot();prompt=Prompt(snapshot.Observation,snapshot.Inventory,out offers);var plan=offers.SingleOrDefault(o=>o.Name=="submit_plan");if(plan==null)continue;
   var names=plan.Parameters.GetProperty("properties").GetProperty("steps").GetProperty("items").GetProperty("oneOf").EnumerateArray().Select(v=>v.GetProperty("properties").GetProperty("tool").GetProperty("const").GetString()).ToHashSet();
   if(needed.IsSubsetOf(names)){
    try{prompt.ParseResponse(JsonSerializer.Serialize(new{goal="Validate complete plan page arguments",steps=new[]{new{tool="submit_plan",arguments=args}}}));}
    catch(FormatException){continue;}
    ToolAgentGate.Log(new{kind="matrix40_plan_page_selected",page,needed});return(prompt,offers);
   }
  }
  throw new InvalidOperationException("matrix40_required_actions_not_on_a_single_plan_page");
 }
 bool Matrix40CanContinue(){if(Matrix41Suite!="baseline"||matrix40Phase!=3||matrix40Unsafe||gateway.RequiresReconciliation!=null||actions.IsActionRunning)return false;var a=Call("action_status",new{},false);var t=Call("task_status",new{},false);var n=Call("host_navigation_status",new{},false);return a.State=="returned"&&t.State=="returned"&&n.State=="returned"&&Capability39.CapabilityDispatchPolicy.KnownIdle(a.Data,true)&&Capability39.CapabilityDispatchPolicy.KnownIdle(t.Data)&&Capability39.CapabilityDispatchPolicy.KnownIdle(n.Data);}
 public void RequestMatrix40Stop(string reason){if(Done||matrix40Draining)return;Passed=false;Reason=reason;matrix40Draining=true;matrix40DrainStarted=MonotonicSeconds;try{if(navigation.Busy)Call("host_cancel_navigation",new{request_id=navigation.Snapshot.RequestId},false);Call("cancel_task",new{},false);Call("cancel_action",new{},false);}catch(Exception e){ToolAgentGate.Log(new{kind="matrix40_cancel_error",error=e.Message});}ToolAgentGate.Log(new{kind="matrix40_stop_requested",reason});}
 void TickCapabilityMatrix40(){
  if(matrix40Draining){try{var a=Call("action_status",new{},false);var t=Call("task_status",new{},false);if(a.State=="returned"&&t.State=="returned"&&!actions.IsActionRunning&&!navigation.Busy&&!navigation.OwnerQuarantined&&Capability39.CapabilityDispatchPolicy.KnownIdle(a.Data,true)&&Capability39.CapabilityDispatchPolicy.KnownIdle(t.Data)){matrix40Draining=false;Done=true;ToolAgentGate.Log(new{kind="matrix40_failure_drained",nativeIdle=true});return;}}catch(Exception e){ToolAgentGate.Log(new{kind="matrix40_drain_error",error=e.Message});}if(MonotonicSeconds-matrix40DrainStarted>7){matrix40CleanupUncertain=true;matrix40Draining=false;Done=true;Reason="matrix40_cleanup_uncertain";ToolAgentGate.Log(new{kind="cleanup_failure",reason=Reason,terrainRestored=false});}return;}
 try{
  if(TickCancellation43Stable())return;
  if(TickCapabilityNavigation43())return;
  if(matrix40CancelSnapshot!=null){if(MonotonicSeconds<matrix40CancelStableAt)return;var stable=Matrix40Read("task_status",new{});matrix40Unsafe=stable.GetRawText()!=matrix40CancelSnapshot||actions.IsActionRunning;Matrix40Check(!matrix40Unsafe,"cancelled_task_has_no_later_step_progress");matrix40CancelSnapshot=null;return;}

  if(matrix40Started==0)matrix40Started=MonotonicSeconds;if(MonotonicSeconds-matrix40Started>100){matrix40Unsafe=true;throw new TimeoutException("matrix40_wall_timeout");}
  if(matrix40Phase==0){if(p.ComponentBody.ParentBody is null){matrix40Phase=1;return;}if(p.Entity.FindComponent<ComponentRider>(true).m_isAnimating)return;matrix40Case="dismount";matrix40Receipt=Call("act_Dismount",new{},false);matrix40Waiting="action_status";matrix40Phase=1;return;}
  if(matrix40Waiting!=null){if(MonotonicSeconds<matrix40StatusPollAt)return;matrix40StatusPollAt=MonotonicSeconds+.05;var r=Call(matrix40Waiting,new{},false);if(r.State!="returned")throw new InvalidOperationException("status_unavailable");string s=Status(r.Data);if(s is "running" or "waiting" or "blocked" or "stopping")return;
   if(Matrix41Suite!="baseline"&&matrix40Waiting=="action_status"){
    matrix40Unsafe=!matrix40Receipt.Data.TryGetProperty("runId",out var issued)||!r.Data.TryGetProperty("runId",out var returned)||issued.ValueKind!=JsonValueKind.String||issued.GetString()!=returned.GetString();
    Matrix40Check(!matrix40Unsafe,"action_identity_matches");
   }
   if(matrix40Waiting=="task_status"){matrix40Unsafe=!Capability39.CapabilityDispatchPolicy.SameTask(matrix40Receipt.Data,r.Data);Matrix40Check(!matrix40Unsafe,"task_identity_matches");}
   bool acceptedMovingTargetFailure=Matrix41Suite=="combat"&&matrix40Case=="act_MeleeAttack"&&Combat41AcceptObservedFailure(r.Data);
   Matrix40Check(s==(matrix40CancelTask?"cancelled":"succeeded")||acceptedMovingTargetFailure,acceptedMovingTargetFailure?"native_terminal_expected_moving_target_rejection":"native_terminal_"+s);if(matrix40CancelTask){matrix40CancelSnapshot=r.Data.GetRawText();matrix40CancelStableAt=MonotonicSeconds+.5;}matrix40CancelTask=false;matrix40Waiting=null;matrix40Verify?.Invoke();matrix40Verify=null;ToolAgentGate.Log(new{kind="matrix40_after",@case=matrix40Case,state=Matrix40State()});return;}
  if(matrix40Phase==1){var terrain=p.Project.FindSubsystem<SubsystemTerrain>(true);int ax=Terrain.ToCell(p.ComponentBody.Position.X),az=Terrain.ToCell(p.ComponentBody.Position.Z);for(int x=ax-5;x<=ax+5;x++)for(int z=az-5;z<=az+5;z++)if(terrain.Terrain.GetChunkAtCell(x,z)?.State!=TerrainChunkState.Valid)return;Matrix40Prepare();matrix40Phase=2;return;}
  if(matrix40Phase==2){var t=p.Project.FindSubsystem<SubsystemTerrain>(true);for(int x=matrix40X-3;x<=matrix40X+3;x++)for(int z=matrix40Z-3;z<=matrix40Z+3;z++)if(t.Terrain.GetChunkAtCell(x,z)?.State!=TerrainChunkState.Valid)return;matrix40Phase=3;return;}
  if(matrix40Cases.Count==0){Matrix40Check(!actions.IsActionRunning&&!navigation.Busy&&!navigation.OwnerQuarantined,"native_owner_idle_at_end");Done=true;Passed=matrix40Failures.Count==0;Reason=Passed?"matrix40_single_verified":"matrix40_cases_failed";ToolAgentGate.Log(new{kind="matrix40_summary",passed=Passed,failures=matrix40Failures,checks=matrix40Checks,modelApi=false,route="adapter_validated",suite=Matrix41Suite,note="Only executed assertions and recorded effects are covered; not all family variants or model autonomy."});return;}
  if(matrix40ObserveDeadline.HasValue&&MonotonicSeconds<matrix40ObserveRetryAt)return;
  var item=matrix40Cases.Peek();matrix40PendingHead=true;matrix40Case=item.Name;var args=item.Args();ToolAgentGate.Log(new{kind="matrix40_before",@case=item.Name,arguments=args,state=Matrix40State()});var prepared=Matrix40Prompt(item.Name,args);var prompt=prepared.Prompt;var offered=prepared.Offers;prompt.ParseResponse(JsonSerializer.Serialize(new{goal="Matrix40 deterministic interface contract",steps=new[]{new{tool=item.Name,arguments=args}}}));ToolAgentGate.Log(new{kind="matrix40_adapter_validated",tool=item.Name,offered=offered.Select(t=>t.Name).ToArray(),capture=currentObservationId});matrix40Cases.Dequeue();matrix40PendingHead=false;matrix40ObserveDeadline=null;matrix40Receipt=Call(item.Name,args,false);Matrix40Check(matrix40Receipt.State=="returned","gateway_returned");matrix40Verify=item.Verify;
  if(item.Name is "plan_craft" or "submit_plan"){matrix40Waiting="task_status";if(matrix40CancelTask){var cancel=Call("cancel_task",new{},false);Matrix40Check(cancel.State=="returned","cancel_task_returned");}}
  else if(item.Name.StartsWith("act_"))matrix40Waiting="action_status";
  else{matrix40Verify?.Invoke();matrix40Verify=null;ToolAgentGate.Log(new{kind="matrix40_after",@case=item.Name,state=Matrix40State()});}
 }catch(Matrix40ObservationPending e)when(matrix40PendingHead){
  matrix40ObserveDeadline??=MonotonicSeconds+1;
  if(MonotonicSeconds<matrix40ObserveDeadline){matrix40ObserveRetryAt=MonotonicSeconds+.05;ToolAgentGate.Log(new{kind="matrix40_observation_retry",@case=matrix40Case,deadline=matrix40ObserveDeadline,dispatched=false});return;}
  Matrix40FailCase(e);
 }catch(Exception e){Matrix40FailCase(e);}}
 void Matrix40FailCase(Exception e){if(matrix40PendingHead){matrix40Cases.Dequeue();matrix40PendingHead=false;}matrix40ObserveDeadline=null;bool continuing=false;try{continuing=Matrix40CanContinue();}catch{}matrix40Failures.Add(new{@case=matrix40Case,error=e.Message});ToolAgentGate.Log(new{kind="matrix40_error",@case=matrix40Case,error=e.ToString(),checks=matrix40Checks,continuingWhenKnownIdle=continuing});if(continuing){matrix40Waiting=null;matrix40Verify=null;matrix40CancelTask=false;matrix40CancelSnapshot=null;return;}RequestMatrix40Stop("matrix40_single_failed");}
 void DisposeCapabilityMatrix40(){if(!Matrix40Enabled)return;if(matrix40CleanupUncertain||actions.IsActionRunning||navigation.Busy||navigation.OwnerQuarantined){ToolAgentGate.Log(new{kind="matrix40_restore_skipped",reason="owner_not_verified_idle",worldMustBeDisposed=true});return;}var status=Call("task_status",new{},false);if(status.State!="returned"||!Capability39.CapabilityDispatchPolicy.KnownIdle(status.Data)){ToolAgentGate.Log(new{kind="matrix40_restore_skipped",reason="task_not_verified_idle",worldMustBeDisposed=true});return;}var t=p.Project.FindSubsystem<SubsystemTerrain>(true);foreach(var cell in matrix40Cells)t.ChangeCell(cell.Key.X,cell.Key.Y,cell.Key.Z,cell.Value);}
}
