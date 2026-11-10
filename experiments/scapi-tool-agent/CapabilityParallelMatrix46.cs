using Engine;
using Game;
using Game.Westworld;
using System.Diagnostics;
using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;
using Capability39;
using Capability39.Cooperation;
using Capability39.SocialTasks;
using Westworld.Decisions.Local;

// Standalone fixture. Do not construct ToolAgentScenario for either of these actors.
internal sealed class CapabilityParallelMatrix46 : IUpdateable, IDisposable {
 public float FloatUpdateOrder => -14;
 public bool Done {get;private set;} public bool Passed {get;private set;}
 public string Reason {get;private set;}="pending";
 readonly ComponentPlayer[] ps; readonly AtomicActions[] actions; readonly NativeToolSession[] ports;
 readonly NativeActorSession senses; readonly ScapiInterestBackend backend; readonly InterestCoordinator interests;
 readonly SubsystemTerrain terrain; readonly Dictionary<Point3,int> original=[]; readonly Stopwatch clock=Stopwatch.StartNew();
 string stopReason; double stopStarted; bool idleVerified; bool completedScenario;
 int phase,checks; bool disposed; double nextEvidenceSample;
 readonly bool social42=true;
 readonly Guid socialWorld=Guid.NewGuid();
 readonly List<JsonElement>[] mailboxes;
 readonly Dictionary<long,Guid>[] dispatchIds;
 double GameTime=>ps[0].Project.FindSubsystem<SubsystemTime>(true).GameTime;
 public CapabilityParallelMatrix46(ComponentPlayer[] players){
  if(players.Length is not (2 or 4)||players.Any(p=>!p.PlayerData.IsExternalActor)||players.Distinct().Count()!=players.Length)throw new ArgumentException("Two or four distinct external fixture actors required");
  if(players.Any(p=>!ReferenceEquals(p.Project,players[0].Project))||players.Select(p=>p.Entity).Distinct().Count()!=players.Length)throw new ArgumentException("Same project and distinct entities required");
  ps=players;origins=new Vector3[ps.Length];runIds=new string[ps.Length];bindings=new CoopBinding45[ps.Length];mailboxes=ps.Select(_=>new List<JsonElement>()).ToArray();dispatchIds=ps.Select(_=>new Dictionary<long,Guid>()).ToArray();actions=ps.Select(p=>new AtomicActions(p.Entity)).ToArray();ports=ps.Select((p,i)=>new NativeToolSession(Guid.NewGuid(),p,new ActorTools(actions[i],true))).ToArray();
  terrain=ps[0].Project.FindSubsystem<SubsystemTerrain>(true);senses=new(ps[0].Project,Guid.NewGuid(),actionsEnabled:false);
  for(int i=0;i<ps.Length;i++)senses.Bind(ports[i].ActorId,ps[i]);ps[0].Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(senses);
  backend=new(terrain,ps[0].Project.FindSubsystem<SubsystemSpawn>(true));interests=new(senses.Registry,backend,new InterestRadii(32,48));interests.Sync();
  foreach(var p in ps)terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);
  Log(new{kind="parallel46_scope",worldId=socialWorld,actualActors=ps.Length,actors=ports.Select(x=>new{actorId=x.ActorId,sessionId=x.SessionId}),modelApi=false});
  Log(new{kind="parallel46_start",artificialFixture=true,inventoryInjected=false,healthEdited=false,model=false,social42,route=social42?"adapter_validated_with_explicit_native_negative_controls":"native_only",actors=ports.Select(x=>new{x.ActorId,x.SessionId})});
 }
 static void Log(object o)=>ToolAgentGate.Log(o);
 void Check(bool ok,string label){Log(new{kind="parallel46_assert",label,passed=ok,phase});if(!ok)throw new InvalidOperationException(label);checks++;}
 ToolReceipt RawCall(int actor,string tool,object args){
  var request=Guid.NewGuid();var r=ports[actor].Call(tool,args);dispatchIds[actor][r.Sequence]=request;CooperativeEvidence45.ValidateIdentity(new(socialWorld,ports[actor].SessionId,ports[actor].ActorId,request),new(socialWorld,r.SessionId,r.ActorId,request));Log(new{kind="parallel46_receipt",actor,requestId=request,actorId=ports[actor].ActorId,sessionId=ports[actor].SessionId,tool,arguments=args,receipt=r,gameTime=GameTime});
  if(social42&&r.State=="returned"){
   if(tool=="read_social"&&r.Data.ValueKind==JsonValueKind.Array){foreach(var e in r.Data.EnumerateArray())mailboxes[actor].Add(e.Clone());while(mailboxes[actor].Count>64)mailboxes[actor].RemoveAt(0);}
   if(tool=="act_RespondToOffer"&&Successful(r)){var a=JsonSerializer.SerializeToElement(args);var reference=a.GetProperty("offer").GetString();mailboxes[actor].RemoveAll(e=>e.TryGetProperty("offerReference",out var f)&&f.GetString()==reference);}
  }
  return r;
 }
 static bool IsSocialWrite(string tool)=>tool is "act_Speak" or "act_OfferItem" or "act_RespondToOffer";
 ToolReceipt Call(int actor,string tool,object args){
  if(tool.StartsWith("act_")||tool=="cancel_action")ValidateSocial42(actor,tool,args,false);
  return RawCall(actor,tool,args);
 }
 void ValidateSocial42(int actor,string tool,object args,bool mustBeUnavailable){
  var empty=JsonSerializer.SerializeToElement(new{});
  var observation=RawCall(actor,"observe",new{look_distance=16,object_distance=16,limit=100,block_limit=32});
  var inventory=RawCall(actor,"read_inventory",new{});
  var task=RawCall(actor,"task_status",new{});var action=RawCall(actor,"action_status",new{});
  var inbox=RawCall(actor,"read_social",new{});
  if(new[]{observation,inventory,task,action,inbox}.Any(r=>r.State!="returned"))throw new InvalidOperationException("parallel46_capture_read_failed");
  bool idle=CapabilityDispatchPolicy.KnownIdle(task.Data)&&CapabilityDispatchPolicy.KnownIdle(action.Data,true)&&!actions[actor].IsActionRunning;
  if(!idle&&tool!="cancel_action")throw new InvalidOperationException("parallel46_owner_not_idle");
  double now=observation.Data.GetProperty("gameTime").GetDouble();
  mailboxes[actor].RemoveAll(e=>e.TryGetProperty("gameTime",out var t)&&now-t.GetDouble()>=SocialTaskFamily.NativeOfferLifetimeSeconds);
  var events=mailboxes[actor].GroupBy(e=>e.TryGetProperty("offerReference",out var f)&&f.ValueKind==JsonValueKind.String?f.GetString():e.GetRawText()).Select(g=>g.Last()).ToArray();
  var mailbox=JsonSerializer.SerializeToElement(events);
  var scope=new SocialCaptureId(socialWorld,ports[actor].SessionId,ports[actor].ActorId,Guid.NewGuid());
  var reads=new[]{new CapabilityRead(scope,"observe",empty,observation.Data),new CapabilityRead(scope,"read_inventory",empty,inventory.Data),new CapabilityRead(scope,"read_social",empty,mailbox)};
  var facts=CapabilityFacts.Build(scope,reads,ports[actor].GetTools().Select(t=>t.Name).ToArray(),idle);
  if(facts.Social is null)throw new InvalidOperationException("parallel46_social_facts_missing");
  var family=SocialTaskFamily.Build(scope,facts.Social);
  var candidates=family.Entries.Where(e=>e.Descriptor.Name!="submit_plan").Select(e=>new CapabilityMenuCandidate(e.Descriptor.Name,e.State.ActorSupported,e.State.Facts,e.State.Reason,e.Offer,e.Schema));
  var enriched=System.Text.Json.Nodes.JsonNode.Parse(inventory.Data.GetRawText())!;
  foreach(var row in enriched["slots"]!.AsArray())if(row!["count"]!.GetValue<int>()>0){var item=RawCall(actor,"read_item",new{value=row["value"]!.GetValue<int>()});if(item.State=="returned"&&item.Data.TryGetProperty("nutrition",out var nutrition))row["nutrition"]=System.Text.Json.Nodes.JsonNode.Parse(nutrition.GetRawText());}
  var native=NativeToolOffers.Build(ports[actor].GetTools(),observation.Data,inventory.Data,Array.Empty<JsonElement>(),visibleEatInventory:JsonSerializer.SerializeToElement(enriched));
  native=native.Select(o=>{if(o.Name!="act_MoveTo")return o;var parameters=System.Text.Json.Nodes.JsonNode.Parse(o.Parameters.GetRawText())!;parameters["properties"]!["timeout"]!["maximum"]=15;return o with{Parameters=JsonSerializer.SerializeToElement(parameters)};}).ToArray();
  var menu=new CapabilityMenu(scope.Session,scope.Actor,scope.Capture,CapabilityCatalog.All,native.Select(o=>new CapabilityMenuCandidate(o.Name,true,FactState.Available,"same_actor_capture",o)),candidates);
  var view=menu.Select(scope.Session,scope.Actor,scope.Capture,new(new[]{tool},Array.Empty<string>()));
  if(mustBeUnavailable){
   Check(!view.Offers.Any(o=>o.Name==tool),"parallel46_stale_offer_not_admitted");
   Log(new{kind="parallel46_adapter_rejected",actor,tool,capture=scope.Capture,reason="no_current_received_offer",nativeDispatched=false});return;
  }
  var context=ToolResultProjection.BuildContext(JsonSerializer.SerializeToElement(new{observation=observation.Data,inventory=JsonSerializer.SerializeToElement(enriched),social=mailbox}),Array.Empty<JsonElement>());
  if(context.GetProperty("projectionStatus").GetString()!="projected")throw new InvalidOperationException("parallel46_public_context_budget");
  var prompt=new ToolDecisionPrompt(scope.Session,scope.Actor,scope.Capture,context,view.Offers);
  var parsed=prompt.ParseResponse(JsonSerializer.Serialize(new{goal="Deterministic multi actor parallel movement menu contract",steps=new[]{new{tool,arguments=args}}}));
  if(parsed.Steps.Count!=1||parsed.Steps[0].Tool!=tool)throw new InvalidOperationException("parallel46_parsed_action_mismatch");
  Log(new{kind="parallel46_adapter_validated",actor,tool,session=scope.Session,actorId=scope.Actor,capture=scope.Capture,offered=view.Offers.Select(o=>o.Name),route="adapter_validated",modelApi=false});
 }
 static string Status(ToolReceipt r)=>r.Data.ValueKind==JsonValueKind.Object&&r.Data.TryGetProperty("status",out var s)?s.GetString():null;
 static bool Successful(ToolReceipt r)=>r.State=="returned"&&Status(r)=="succeeded";
 void Set(int x,int y,int z,int v){var c=new Point3(x,y,z);original.TryAdd(c,terrain.Terrain.GetCellValue(x,y,z));terrain.ChangeCell(x,y,z,v);}
 bool Ready(){foreach(var p in ps){int x=Terrain.ToCell(p.ComponentBody.Position.X),z=Terrain.ToCell(p.ComponentBody.Position.Z);for(int a=x-4;a<=x+4;a++)for(int b=z-4;b<=z+4;b++)if(terrain.Terrain.GetChunkAtCell(a,b)?.State!=TerrainChunkState.Valid)return false;}return true;}
 const int FixtureOffsetZ=12;
 bool FixtureStartReady(){var p=ps[0].ComponentBody.Position;int x=Terrain.ToCell(p.X),z=Terrain.ToCell(p.Z)+FixtureOffsetZ;for(int i=0;i<ps.Length;i++)for(int dx=-4;dx<=4;dx++)for(int dz=-4;dz<=4;dz++)if(terrain.Terrain.GetChunkAtCell(x+dx,z+3*i+dz)?.State!=TerrainChunkState.Valid)return false;return true;}
 void Fixture(){var p=ps[0].ComponentBody.Position;int x=Terrain.ToCell(p.X),y=Terrain.ToCell(p.Y),z=Terrain.ToCell(p.Z)+FixtureOffsetZ;for(int a=x-4;a<=x+30;a++)for(int c=z-4;c<=z+3*ps.Length+2;c++){Set(a,y-1,c,DirtBlock.Index);for(int b=y;b<=y+3;b++)Set(a,b,c,0);}
  for(int i=0;i<ps.Length;i++){var b=ps[i].ComponentBody;b.Position=new(x+.5f,y,z+.5f+i*3f);b.Velocity=Vector3.Zero;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2);}
  Log(new{kind="parallel46_fixture",offsetFromSpawnZ=FixtureOffsetZ,naturalEntitiesRemoved=false,terrainEdited=true,positionEdited=true,inventoryInjected=false,note="Disposable world only. Flat parallel lanes and initial positions artificial; three meter lane separation; no later teleport, velocity or physics edits."});
 }
 public void RequestStop(string reason){if(Done||stopReason!=null)return;stopReason=reason;stopStarted=clock.Elapsed.TotalSeconds;Log(new{kind="parallel46_stop_requested",reason,phase});}
 bool Drain(){
  bool idle=true;
  for(int i=0;i<ps.Length;i++){Call(i,"cancel_task",new{});Call(i,"cancel_action",new{});var task=Call(i,"task_status",new{});var action=Call(i,"action_status",new{});bool taskIdle=task.State=="returned"&&Status(task) is "idle" or "succeeded" or "failed" or "cancelled" or "skipped";bool actionIdle=action.State=="returned"&&(action.Data.ValueKind==JsonValueKind.Null||Status(action) is "succeeded" or "failed" or "cancelled" or "rejected");idle&=taskIdle&&actionIdle&&!actions[i].IsActionRunning;}
  if(idle){idleVerified=true;Done=true;Passed=completedScenario;Reason=completedScenario?"multi_actor_parallel_movement_verified":stopReason;Log(new{kind="parallel46_summary",modelApi=false,passed=Passed,checks,phase,reason=Reason,idleVerified=true,elapsed=clock.Elapsed.TotalSeconds});return true;}
  if(clock.Elapsed.TotalSeconds-stopStarted>=7){Done=true;Passed=false;Reason="cleanup_uncertain";Log(new{kind="parallel46_summary",modelApi=false,passed=false,checks,phase,reason=Reason,idleVerified=false,stopReason});}
  return Done;
 }
 object MoveArgs(int actor){var obs=RawCall(actor,"observe",new{look_distance=16,object_distance=16,limit=100,block_limit=32});var focus=obs.Data.GetProperty("focus");Check(focus.ValueKind==JsonValueKind.Object&&focus.GetProperty("kind").GetString()=="block"&&focus.GetProperty("distance").GetSingle()>6,"actual_far_ground_focus");Log(new{kind="parallel46_current_target",actor,focus,menuBranch="native_observed_destination_timeout15",modelApi=false});return new{destination=focus.GetProperty("reference").GetString(),range=.3,timeout=15};}
 readonly Vector3[] origins=new Vector3[0]; readonly string[] runIds=new string[0];
 Vector3 cancelA,cancelB,settleA; double settleAt; double startedAt,firstOverlap=-1,lastOverlap=-1,maxOverlapGap; int overlapSamples,round; readonly CoopBinding45[] bindings=new CoopBinding45[0]; readonly List<ParallelPair46> runningSamples=[]; ParallelPair46 beforeCancel,afterCancel;
 ParallelPair46 Pair(ToolReceipt[] rows){ParallelSample46 At(int i){var p=ps[i].ComponentBody.Position;return new(new(socialWorld,rows[i].SessionId,rows[i].ActorId,bindings[i].Request),Guid.Parse(Run(rows[i])),GameTime,Status(rows[i]),new(p.X,p.Y,p.Z));}return new(At(0),At(1));}
 string Run(ToolReceipt r)=>r.Data.ValueKind==JsonValueKind.Object&&r.Data.TryGetProperty("runId",out var id)?id.GetString():null;
 static float[] Position(Vector3 v)=>[v.X,v.Y,v.Z];
 float Distance(int actor)=>(ps[actor].ComponentBody.Position-origins[actor]).XZ.Length();
 void Effect(string name,bool passed,object evidence){Log(new{kind="parallel46_effect",name,passed,evidence,gameTime=GameTime,modelApi=false});Check(passed,name);}
 ToolReceipt[] Sample(string label){var rows=Enumerable.Range(0,ps.Length).Select(i=>RawCall(i,"action_status",new{})).ToArray();
  Log(new{kind="parallel46_sample",label,round,gameTime=GameTime,rows=rows.Select((r,i)=>new{actor=i,actorId=ports[i].ActorId,sessionId=ports[i].SessionId,runId=Run(r),status=Status(r),position=Position(ps[i].ComponentBody.Position),origin=Position(origins[i]),horizontalDisplacement=Distance(i),receipt=r}),modelApi=false});return rows;}
 void BeginRound(int index){round=index;firstOverlap=lastOverlap=-1;maxOverlapGap=0;overlapSamples=0;runningSamples.Clear();startedAt=GameTime;
  for(int i=0;i<ps.Length;i++){origins[i]=ps[i].ComponentBody.Position;var r=Call(i,"act_MoveTo",MoveArgs(i));Check(r.State=="returned"&&Status(r)=="running","move_admitted_running");runIds[i]=Run(r);bindings[i]=new(socialWorld,r.SessionId,r.ActorId,dispatchIds[i][r.Sequence]);}
  Check(runIds.All(x=>Guid.TryParse(x,out _))&&runIds.Distinct().Count()==ps.Length,"distinct_move_run_ids");
  Log(new{kind="parallel46_round_start",round,gameTime=GameTime,runIds,origins=origins.Select(Position),artificialFixture=true,modelApi=false});
 }
 bool Overlap(ToolReceipt[] rows){bool both=Enumerable.Range(0,ps.Length).All(i=>Status(rows[i])=="running"&&Run(rows[i])==runIds[i]&&actions[i].IsActionRunning);if(both){if(firstOverlap<0)firstOverlap=GameTime;if(lastOverlap>=0)maxOverlapGap=Math.Max(maxOverlapGap,GameTime-lastOverlap);lastOverlap=GameTime;overlapSamples++;runningSamples.Add(Pair(rows));}return both;}
 public void Update(float dt){if(Done)return;try{
  if(stopReason!=null){Drain();return;}if(clock.Elapsed.TotalSeconds>100){RequestStop("parallel_timeout");return;}interests.Sync();
  // Throttle audit polling only; native actions/physics retain their own update loop.
  if(phase is 4 or 5 or 6 or 7 or 10){if(GameTime<nextEvidenceSample)return;nextEvidenceSample=GameTime+.05;}
  if(phase==0){if(ps.Any(p=>p.Entity.FindComponent<ComponentRider>(true).m_isAnimating))return;for(int i=0;i<ps.Length;i++)if(ps[i].ComponentBody.ParentBody is not null)Call(i,"act_Dismount",new{});phase=1;return;}
  if(phase==1){if(actions.Any(a=>a.IsActionRunning)||!Ready()||!FixtureStartReady())return;Check(ps.All(p=>p.ComponentBody.ParentBody is null),"both_dismounted");Fixture();phase=2;return;}
  if(phase==2){if(!Ready())return;for(int i=0;i<ps.Length;i++)Call(i,"act_FaceTowards",new{bearing=0,pitch=-8,timeout=4});phase=3;return;}
  if(phase==3){if(actions.Any(a=>a.IsActionRunning)||!Ready())return;
   Effect("same_world_distinct_actors",ps.All(p=>ReferenceEquals(p.Project,ps[0].Project))&&ports.Select(p=>p.ActorId).Distinct().Count()==ps.Length&&ports.Select(p=>p.SessionId).Distinct().Count()==ps.Length&&ps.All(p=>p.GameWidget is null),new{worldId=socialWorld,actors=ports.Select(p=>new{p.ActorId,p.SessionId}),noViewports=true});BeginRound(1);phase=4;return;}
  if(phase==4){var rows=Sample("parallel_moving");Overlap(rows);if(actions.Any(a=>a.IsActionRunning))return;
   var verifiedWindow=ParallelEvidence46.ValidateRunningWindow(runningSamples);Effect("parallel_success",Enumerable.Range(0,ps.Length).All(i=>Successful(rows[i])&&Run(rows[i])==runIds[i]&&Distance(i)>3)&&overlapSamples>=2&&lastOverlap>firstOverlap,new{receipts=rows,runIds,allDisplacements=Enumerable.Range(0,ps.Length).Select(Distance),verifiedWindow,displacementA=Distance(0),displacementB=Distance(1),overlapSamples,firstOverlap,lastOverlap,maxOverlapGap,startedAt,endedAt=GameTime});for(int i=0;i<ps.Length;i++)Call(i,"act_FaceTowards",new{bearing=0,pitch=-8,timeout=4});phase=8;return;}
  if(phase==8){if(actions.Any(a=>a.IsActionRunning)||!Ready())return;BeginRound(2);phase=5;return;}
  if(phase==5){var rows=Sample("before_cancel");bool both=Overlap(rows);if(!both){Check(actions.All(a=>a.IsActionRunning),"round2_both_remain_running_until_cancel");return;}if(Distance(0)<.2f||Distance(1)<.2f||overlapSamples<2)return;
   beforeCancel=Pair(rows);cancelA=ps[0].ComponentBody.Position;cancelB=ps[1].ComponentBody.Position;var cancel=Call(0,"cancel_action",new{});Check(cancel.State=="returned","cancel_A_returned");Log(new{kind="parallel46_cancel_dispatched",cancelReceipt=cancel,gameTime=GameTime,runIds});settleA=ps[0].ComponentBody.Position;settleAt=GameTime;phase=7;return;}
  if(phase==7){var after=Sample("next_tick_after_cancel_A");Check(Enumerable.Range(1,ps.Length-1).All(i=>Run(after[i])==runIds[i]&&Status(after[i]) is "running" or "succeeded"),"all_other_move_owners_unchanged");Check(Run(after[0])==runIds[0]&&Status(after[0])=="cancelled"&&Run(after[1])==runIds[1]&&Status(after[1])=="running","cancelled_A_and_running_B_during_settle");if((ps[0].ComponentBody.Position-settleA).Length()>.01f){settleA=ps[0].ComponentBody.Position;settleAt=GameTime;return;}if(GameTime-settleAt<.2)return;afterCancel=Pair(after);
   Effect("cancel_isolation_running",Run(after[0])==runIds[0]&&Status(after[0])=="cancelled"&&Run(after[1])==runIds[1]&&Status(after[1])=="running"&&actions[1].IsActionRunning,new{receipts=after,runIds,cancelPositionA=Position(cancelA),cancelPositionB=Position(cancelB),displacementA=Distance(0),displacementB=Distance(1)});phase=6;return;}
  if(phase==6){var rows=Sample("after_cancel_A_B_continues");Check(Run(rows[0])==runIds[0]&&Status(rows[0])=="cancelled"&&!actions[0].IsActionRunning,"A_stays_cancelled");if(actions.Skip(1).Any(a=>a.IsActionRunning))return;
   var finalPair=Pair(rows);ParallelEvidence46.ValidateCancellationIsolation(beforeCancel,afterCancel,finalPair.B,finalPair.A,.03);float aDrift=(ps[0].ComponentBody.Position-cancelA).XZ.Length(),bFurther=(ps[1].ComponentBody.Position-cancelB).XZ.Length();
   Effect("cancel_isolation_completed",Enumerable.Range(1,ps.Length-1).All(i=>Successful(rows[i])&&Run(rows[i])==runIds[i]&&Distance(i)>3)&&bFurther>.4f,new{receipts=rows,runIds,displacementA=Distance(0),displacementB=Distance(1),aDriftAfterCancel=aDrift,settledDrift=(ps[0].ComponentBody.Position-settleA).Length(),settleAt,bDisplacementAfterCancel=bFurther,endedAt=GameTime});Call(0,"cancel_action",new{});Call(0,"act_FaceTowards",new{bearing=0,pitch=-8,timeout=4});phase=9;return;}
  if(phase==9){if(actions[0].IsActionRunning||!Ready())return;origins[0]=ps[0].ComponentBody.Position;var oldRunId=runIds[0];var move=Call(0,"act_MoveTo",MoveArgs(0));runIds[0]=Run(move);Check(runIds[0]!=oldRunId,"recovery_new_run");Log(new{kind="parallel46_recovery_start",actor=0,actorId=ports[0].ActorId,sessionId=ports[0].SessionId,oldRunId,newRunId=runIds[0],origin=Position(origins[0]),gameTime=GameTime,receipt=move});phase=10;return;}
  if(phase==10){if(actions[0].IsActionRunning)return;var rows=Sample("A_recovered");Effect("cancelled_actor_recovered",Successful(rows[0])&&Run(rows[0])==runIds[0]&&Distance(0)>3&&actions.Skip(1).All(a=>!a.IsActionRunning),new{receipt=rows[0],runId=runIds[0],displacementA=Distance(0),otherOwnersIdle=true});completedScenario=true;RequestStop("scenario_complete");}
 }catch(Exception e){Log(new{kind="parallel46_error",phase,error=e.ToString()});if(stopReason==null)RequestStop("scenario_error:"+e.Message);else if(clock.Elapsed.TotalSeconds-stopStarted>=7){Done=true;Passed=false;Reason="cleanup_uncertain";}}}
 public void Dispose(){if(disposed)return;disposed=true;var u=ps[0].Project.FindSubsystem<SubsystemUpdate>(true);u.RemoveUpdateable(this);
  if(!idleVerified){Log(new{kind="parallel46_cleanup",terrainRestored=false,idleVerified=false,reason="cleanup_uncertain",worldMustBeDisposed=true});throw new InvalidOperationException("cleanup_uncertain: no verified idle; terrain intentionally not restored");}
  for(int i=0;i<ps.Length;i++)u.RemoveUpdateable(actions[i]);u.RemoveUpdateable(senses);senses.Dispose();foreach(var c in original)terrain.ChangeCell(c.Key.X,c.Key.Y,c.Key.Z,c.Value);interests.Release();backend.Dispose();Log(new{kind="parallel46_cleanup",terrainRestored=true,idleVerified=true,worldMustBeDisposed=true,note="External actor/session registration lives until this disposable world is unloaded; do not reuse actors."});}
}
