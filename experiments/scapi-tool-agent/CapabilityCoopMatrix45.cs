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
internal sealed class CapabilityCoopMatrix45 : IUpdateable, IDisposable {
 public float FloatUpdateOrder => -14;
 public bool Done {get;private set;} public bool Passed {get;private set;}
 public string Reason {get;private set;}="pending";
 readonly ComponentPlayer[] ps; readonly AtomicActions[] actions; readonly NativeToolSession[] ports;
 readonly NativeActorSession senses; readonly ScapiInterestBackend backend; readonly InterestCoordinator interests;
 readonly SubsystemTerrain terrain; readonly Dictionary<Point3,int> original=[]; readonly Stopwatch clock=Stopwatch.StartNew();
 string stopReason; double stopStarted; bool idleVerified; bool completedScenario;
 int phase,checks,value,slot; string targetB,offer; bool disposed;
 readonly bool social42=true;
 readonly Guid socialWorld=Guid.NewGuid();
 readonly List<JsonElement>[] mailboxes=[new(),new()];
 readonly Dictionary<long,Guid>[] dispatchIds=[new(),new()];
 double GameTime=>ps[0].Project.FindSubsystem<SubsystemTime>(true).GameTime;
 public CapabilityCoopMatrix45(ComponentPlayer[] players){
  if(players.Length!=2||players.Any(p=>!p.PlayerData.IsExternalActor)||ReferenceEquals(players[0],players[1]))throw new ArgumentException("Two distinct external fixture actors required");
  if(!ReferenceEquals(players[0].Project,players[1].Project)||ReferenceEquals(players[0].Entity,players[1].Entity))throw new ArgumentException("Same project and distinct entities required");
  ps=players;actions=ps.Select(p=>new AtomicActions(p.Entity)).ToArray();ports=ps.Select((p,i)=>new NativeToolSession(Guid.NewGuid(),p,new ActorTools(actions[i],true))).ToArray();
  terrain=ps[0].Project.FindSubsystem<SubsystemTerrain>(true);senses=new(ps[0].Project,Guid.NewGuid(),actionsEnabled:false);
  for(int i=0;i<2;i++)senses.Bind(ports[i].ActorId,ps[i]);ps[0].Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(senses);
  backend=new(terrain,ps[0].Project.FindSubsystem<SubsystemSpawn>(true));interests=new(senses.Registry,backend,new InterestRadii(32,48));interests.Sync();
  foreach(var p in ps)terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);
  Log(new{kind="coop45_scope",worldId=socialWorld,actors=ports.Select(x=>new{actorId=x.ActorId,sessionId=x.SessionId}),modelApi=false});
  Log(new{kind="coop45_start",artificialFixture=true,inventoryInjected=false,healthEdited=false,model=false,social42,route=social42?"adapter_validated_with_explicit_native_negative_controls":"native_only",actors=ports.Select(x=>new{x.ActorId,x.SessionId})});
 }
 static void Log(object o)=>ToolAgentGate.Log(o);
 void Check(bool ok,string label){Log(new{kind="coop45_assert",label,passed=ok,phase});if(!ok)throw new InvalidOperationException(label);checks++;}
 ToolReceipt RawCall(int actor,string tool,object args){
  var request=Guid.NewGuid();var r=ports[actor].Call(tool,args);dispatchIds[actor][r.Sequence]=request;CooperativeEvidence45.ValidateIdentity(new(socialWorld,ports[actor].SessionId,ports[actor].ActorId,request),new(socialWorld,r.SessionId,r.ActorId,request));Log(new{kind="coop45_receipt",actor,requestId=request,actorId=ports[actor].ActorId,sessionId=ports[actor].SessionId,tool,arguments=args,receipt=r,gameTime=GameTime});
  if(social42&&r.State=="returned"){
   if(tool=="read_social"&&r.Data.ValueKind==JsonValueKind.Array){foreach(var e in r.Data.EnumerateArray())mailboxes[actor].Add(e.Clone());while(mailboxes[actor].Count>64)mailboxes[actor].RemoveAt(0);}
   if(tool=="act_RespondToOffer"&&Successful(r)){var a=JsonSerializer.SerializeToElement(args);var reference=a.GetProperty("offer").GetString();mailboxes[actor].RemoveAll(e=>e.TryGetProperty("offerReference",out var f)&&f.GetString()==reference);}
  }
  return r;
 }
 static bool IsSocialWrite(string tool)=>tool is "act_Speak" or "act_OfferItem" or "act_RespondToOffer";
 ToolReceipt Call(int actor,string tool,object args){
  if(tool.StartsWith("act_"))ValidateSocial42(actor,tool,args,false);
  return RawCall(actor,tool,args);
 }
 void ValidateSocial42(int actor,string tool,object args,bool mustBeUnavailable){
  var empty=JsonSerializer.SerializeToElement(new{});
  var observation=RawCall(actor,"observe",new{look_distance=5,object_distance=5,limit=100,block_limit=32});
  var inventory=RawCall(actor,"read_inventory",new{});
  var task=RawCall(actor,"task_status",new{});var action=RawCall(actor,"action_status",new{});
  var inbox=RawCall(actor,"read_social",new{});
  if(new[]{observation,inventory,task,action,inbox}.Any(r=>r.State!="returned"))throw new InvalidOperationException("coop45_capture_read_failed");
  bool idle=CapabilityDispatchPolicy.KnownIdle(task.Data)&&CapabilityDispatchPolicy.KnownIdle(action.Data,true)&&!actions[actor].IsActionRunning;
  if(!idle)throw new InvalidOperationException("coop45_owner_not_idle");
  double now=observation.Data.GetProperty("gameTime").GetDouble();
  mailboxes[actor].RemoveAll(e=>e.TryGetProperty("gameTime",out var t)&&now-t.GetDouble()>=SocialTaskFamily.NativeOfferLifetimeSeconds);
  var events=mailboxes[actor].GroupBy(e=>e.TryGetProperty("offerReference",out var f)&&f.ValueKind==JsonValueKind.String?f.GetString():e.GetRawText()).Select(g=>g.Last()).ToArray();
  var mailbox=JsonSerializer.SerializeToElement(events);
  var scope=new SocialCaptureId(socialWorld,ports[actor].SessionId,ports[actor].ActorId,Guid.NewGuid());
  var reads=new[]{new CapabilityRead(scope,"observe",empty,observation.Data),new CapabilityRead(scope,"read_inventory",empty,inventory.Data),new CapabilityRead(scope,"read_social",empty,mailbox)};
  var facts=CapabilityFacts.Build(scope,reads,ports[actor].GetTools().Select(t=>t.Name).ToArray(),idle);
  if(facts.Social is null)throw new InvalidOperationException("coop45_social_facts_missing");
  var family=SocialTaskFamily.Build(scope,facts.Social);
  var candidates=family.Entries.Where(e=>e.Descriptor.Name!="submit_plan").Select(e=>new CapabilityMenuCandidate(e.Descriptor.Name,e.State.ActorSupported,e.State.Facts,e.State.Reason,e.Offer,e.Schema));
  var enriched=System.Text.Json.Nodes.JsonNode.Parse(inventory.Data.GetRawText())!;
  foreach(var row in enriched["slots"]!.AsArray())if(row!["count"]!.GetValue<int>()>0){var item=RawCall(actor,"read_item",new{value=row["value"]!.GetValue<int>()});if(item.State=="returned"&&item.Data.TryGetProperty("nutrition",out var nutrition))row["nutrition"]=System.Text.Json.Nodes.JsonNode.Parse(nutrition.GetRawText());}
  var native=NativeToolOffers.Build(ports[actor].GetTools(),observation.Data,inventory.Data,Array.Empty<JsonElement>(),visibleEatInventory:JsonSerializer.SerializeToElement(enriched));
  var menu=new CapabilityMenu(scope.Session,scope.Actor,scope.Capture,CapabilityCatalog.All,native.Select(o=>new CapabilityMenuCandidate(o.Name,true,FactState.Available,"same_actor_capture",o)),candidates);
  var view=menu.Select(scope.Session,scope.Actor,scope.Capture,new(new[]{tool},Array.Empty<string>()));
  if(mustBeUnavailable){
   Check(!view.Offers.Any(o=>o.Name==tool),"coop45_stale_offer_not_admitted");
   Log(new{kind="coop45_adapter_rejected",actor,tool,capture=scope.Capture,reason="no_current_received_offer",nativeDispatched=false});return;
  }
  var context=ToolResultProjection.BuildContext(JsonSerializer.SerializeToElement(new{observation=observation.Data,inventory=JsonSerializer.SerializeToElement(enriched),social=mailbox}),Array.Empty<JsonElement>());
  if(context.GetProperty("projectionStatus").GetString()!="projected")throw new InvalidOperationException("coop45_public_context_budget");
  var prompt=new ToolDecisionPrompt(scope.Session,scope.Actor,scope.Capture,context,view.Offers);
  var parsed=prompt.ParseResponse(JsonSerializer.Serialize(new{goal="Deterministic two actor social menu contract",steps=new[]{new{tool,arguments=args}}}));
  if(parsed.Steps.Count!=1||parsed.Steps[0].Tool!=tool)throw new InvalidOperationException("coop45_parsed_action_mismatch");
  Log(new{kind="coop45_adapter_validated",actor,tool,session=scope.Session,actorId=scope.Actor,capture=scope.Capture,offered=view.Offers.Select(o=>o.Name),route="adapter_validated",modelApi=false});
 }
 static string Status(ToolReceipt r)=>r.Data.ValueKind==JsonValueKind.Object&&r.Data.TryGetProperty("status",out var s)?s.GetString():null;
 static bool Successful(ToolReceipt r)=>r.State=="returned"&&Status(r)=="succeeded";
 int Count(int actor)=>Enumerable.Range(0,ps[actor].ComponentMiner.Inventory.SlotsCount).Where(i=>ps[actor].ComponentMiner.Inventory.GetSlotValue(i)==value).Sum(ps[actor].ComponentMiner.Inventory.GetSlotCount);
 void Set(int x,int y,int z,int v){var c=new Point3(x,y,z);original.TryAdd(c,terrain.Terrain.GetCellValue(x,y,z));terrain.ChangeCell(x,y,z,v);}
 bool Ready(){foreach(var p in ps){int x=Terrain.ToCell(p.ComponentBody.Position.X),z=Terrain.ToCell(p.ComponentBody.Position.Z);for(int a=x-4;a<=x+4;a++)for(int b=z-4;b<=z+4;b++)if(terrain.Terrain.GetChunkAtCell(a,b)?.State!=TerrainChunkState.Valid)return false;}return true;}
 void Fixture(){var p=ps[0].ComponentBody.Position;int x=Terrain.ToCell(p.X),y=Terrain.ToCell(p.Y),z=Terrain.ToCell(p.Z);for(int a=x-4;a<=x+4;a++)for(int c=z-4;c<=z+4;c++){Set(a,y-1,c,DirtBlock.Index);for(int b=y;b<=y+3;b++)Set(a,b,c,0);}
  for(int i=0;i<2;i++){var b=ps[i].ComponentBody;b.Position=new(x+.5f+i*1.5f,y,z+.5f);b.Velocity=Vector3.Zero;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,i==0?-MathF.PI/2:MathF.PI/2);}
  Log(new{kind="coop45_fixture",terrainEdited=true,positionEdited=true,inventoryInjected=false,note="Disposable world only. Flat floor and adjacent actors are artificial, not autonomous navigation."});
 }
 string OtherReference(int actor){var r=Call(actor,"observe",new{look_distance=5,object_distance=5,limit=100,block_limit=32});Check(r.State=="returned","public observation returned");var pos=ps[1-actor].ComponentBody.Position;
  var candidates=r.Data.GetProperty("entities").EnumerateArray().Where(e=>{var p=e.GetProperty("position");return Math.Abs(p.GetProperty("x").GetSingle()-pos.X)<.1f&&Math.Abs(p.GetProperty("y").GetSingle()-pos.Y)<.1f&&Math.Abs(p.GetProperty("z").GetSingle()-pos.Z)<.1f;}).ToArray();Check(candidates.Length==1,"other actor has exactly one actual public reference");return candidates[0].GetProperty("reference").GetString();}
 string ReceivedOffer(int actor){var r=Call(actor,"read_social",new{});Check(r.State=="returned","own social inbox returned");var entries=r.Data.EnumerateArray().Where(x=>x.TryGetProperty("offerReference",out var o)&&o.ValueKind==JsonValueKind.String).ToArray();Check(entries.Length==1,"one actual received offer reference");return entries[0].GetProperty("offerReference").GetString();}
 public void RequestStop(string reason){if(Done||stopReason!=null)return;stopReason=reason;stopStarted=clock.Elapsed.TotalSeconds;Log(new{kind="coop45_stop_requested",reason,phase});}
 bool Drain(){
  bool idle=true;
  for(int i=0;i<2;i++){Call(i,"cancel_task",new{});Call(i,"cancel_action",new{});var task=Call(i,"task_status",new{});var action=Call(i,"action_status",new{});bool taskIdle=task.State=="returned"&&Status(task) is "idle" or "succeeded" or "failed" or "cancelled" or "skipped";bool actionIdle=action.State=="returned"&&(action.Data.ValueKind==JsonValueKind.Null||Status(action) is "succeeded" or "failed" or "cancelled" or "rejected");idle&=taskIdle&&actionIdle&&!actions[i].IsActionRunning;}
  if(idle){idleVerified=true;Done=true;Passed=completedScenario;Reason=completedScenario?"two_actor_cooperation_verified":stopReason;Log(new{kind="coop45_summary",modelApi=false,passed=Passed,checks,phase,reason=Reason,idleVerified=true,elapsed=clock.Elapsed.TotalSeconds});return true;}
  if(clock.Elapsed.TotalSeconds-stopStarted>=7){Done=true;Passed=false;Reason="cleanup_uncertain";Log(new{kind="coop45_summary",modelApi=false,passed=false,checks,phase,reason=Reason,idleVerified=false,stopReason});}
  return Done;
 }
 int events; float foodBefore; long statsBefore; string waitRun,eatRun,pickupRun; double waitStart,eatStart,eatEnd,waitObserved,dropCreatedAt,dropCreatedWall; bool subscribed;
 CoopBinding45 waitBinding,eatBinding; CoopActionEvidence45 eatEvidence;
 CoopInventory45 InventoryEvidence()=>new(socialWorld,ports[0].ActorId,ports[1].ActorId,value,Count(0),Count(1));
 CoopInventory45 deliveredBefore,eatingBefore;
 int WorldCount()=>ps[0].Project.FindSubsystem<SubsystemPickables>(true).Pickables.Where(d=>!d.ToRemove&&d.Value==value).Sum(d=>d.Count);
 void State(string label)=>Log(new{kind="coop45_state",label,value,countA=Count(0),countB=Count(1),worldCount=WorldCount(),gameTime=GameTime,hostAuditOnly=true});
 void Effect(string name,bool passed,object evidence){Log(new{kind="coop45_effect",name,passed,evidence,gameTime=GameTime,modelApi=false});Check(passed,name);}
 void Eaten(int v){if(v==value)events++;}
 string Run(ToolReceipt r)=>r.Data.GetProperty("runId").GetString();
 public void Update(float dt){if(Done)return;try{
  if(stopReason!=null){Drain();return;}if(clock.Elapsed.TotalSeconds>100){RequestStop("coop_timeout");return;}interests.Sync();
  if(phase==0){if(ps.Any(p=>p.Entity.FindComponent<ComponentRider>(true).m_isAnimating))return;for(int i=0;i<2;i++)if(ps[i].ComponentBody.ParentBody is not null)Call(i,"act_Dismount",new{});phase=1;return;}
  if(phase==1){if(actions.Any(a=>a.IsActionRunning)||!Ready())return;Check(ps.All(p=>p.ComponentBody.ParentBody is null),"dismounted");Fixture();phase=19;return;}
  if(phase==19){if(!Ready())return;Call(0,"act_FaceTowards",new{bearing=0,pitch=-75,timeout=4});phase=21;return;}
  if(phase==21){if(actions[0].IsActionRunning)return;Check(Successful(RawCall(0,"action_status",new{})),"pickup_face_down_succeeded");phase=2;return;}
  if(phase==2){if(!Ready())return;Effect("same_world_distinct_actors",ReferenceEquals(ps[0].Project,ps[1].Project)&&ports[0].ActorId!=ports[1].ActorId&&ports[0].SessionId!=ports[1].SessionId&&ps.All(p=>p.GameWidget is null),new{worldId=socialWorld,actors=ports.Select(p=>new{p.ActorId,p.SessionId}),noViewports=ps.All(p=>p.GameWidget is null)});
   value=CookedMeatBlock.Index;Check(Count(0)==0&&Count(1)==0&&WorldCount()==0,"no_initial_cooked_meat");State("baseline");
   var drop=ps[0].Project.FindSubsystem<SubsystemPickables>(true).AddPickable(value,1,ps[0].ComponentBody.Position+new Vector3(.25f,ps[0].ComponentBody.StanceBoxSize.Y*.5f,0),Vector3.Zero,null);
   Log(new{kind="coop45_food_fixture",artificialFixture=true,worldDropInjected=true,inventoryInjected=false,healthEdited=false,hungerEdited=false,value,count=1,position=new[]{drop.Position.X,drop.Position.Y,drop.Position.Z},modelApi=false});State("world_drop_created");dropCreatedAt=GameTime;dropCreatedWall=clock.Elapsed.TotalSeconds;phase=20;return;}
  if(phase==20){
   if(Count(0)!=0||Count(1)!=0){State("automatic_acquisition_before_explicit_pickup");throw new InvalidOperationException("native_auto_pickup_won_race_not_explicit_pickup");}
   var obs=RawCall(0,"observe",new{look_distance=5,object_distance=5,limit=100,block_limit=32});var refs=obs.Data.GetProperty("pickables").EnumerateArray().Where(e=>e.GetProperty("value").GetInt32()==value).ToArray();
   Log(new{kind="coop45_pickup_visibility",gameTime=GameTime,elapsedGameSeconds=GameTime-dropCreatedAt,elapsedWallSeconds=clock.Elapsed.TotalSeconds-dropCreatedWall,publicMatches=refs.Length,autoPickupCooldownSeconds=.5,modelApi=false});
   if(refs.Length==0&&GameTime-dropCreatedAt<.45&&clock.Elapsed.TotalSeconds-dropCreatedWall<2)return;Check(refs.Length==1,"food_drop_public_reference");
   var pick=Call(0,"act_PickUp",new{target=refs[0].GetProperty("reference").GetString(),count=1});Check(pick.State=="returned","pickup_admitted");pickupRun=Run(pick);phase=3;return;}
  if(phase==3){if(actions[0].IsActionRunning)return;var picked=RawCall(0,"action_status",new{});Effect("acquisition",Successful(picked)&&Run(picked)==pickupRun&&picked.Data.GetProperty("action").GetString()=="PickUp"&&picked.Data.GetProperty("affectedCount").GetInt32()==1&&Count(0)==1&&Count(1)==0&&WorldCount()==0,new{receipt=picked,inventoryA=Count(0),inventoryB=Count(1),worldCount=WorldCount(),source="artificial_world_drop_explicit_pickup"});State("acquired");Call(0,"act_FaceTowards",new{bearing=0,pitch=0,timeout=4});phase=22;return;}
  if(phase==22){if(actions[0].IsActionRunning)return;Check(Successful(RawCall(0,"action_status",new{})),"offer_face_recipient_succeeded");
   targetB=OtherReference(0);var inv=RawCall(0,"read_inventory",new{}).Data;slot=inv.GetProperty("slots").EnumerateArray().Single(e=>e.GetProperty("value").GetInt32()==value&&e.GetProperty("count").GetInt32()==1).GetProperty("slot").GetInt32();
   Check(Successful(Call(0,"act_OfferItem",new{target=targetB,slot,value,count=1})),"offer_succeeded");Check(Count(0)==1&&Count(1)==0,"offer_transfers_nothing");offer=ReceivedOffer(1);phase=4;return;}
  if(phase==4){deliveredBefore=InventoryEvidence();var accepted=Call(1,"act_RespondToOffer",new{offer,accept=true});CooperativeEvidence45.ValidateDelivery(deliveredBefore,InventoryEvidence(),1);Effect("delivery",Successful(accepted)&&Count(0)==0&&Count(1)==1,new{receipt=accepted,beforeA=1,beforeB=0,afterA=Count(0),afterB=Count(1),transferred=1});State("delivered");
   eatingBefore=InventoryEvidence();foodBefore=ps[1].ComponentVitalStats.Food;statsBefore=ps[1].PlayerStats.FoodItemsEaten;Check(foodBefore<.98f,"native_hunger_available");ps[1].ComponentVitalStats.FoodEaten+=Eaten;subscribed=true;
   waitStart=GameTime;var wait=Call(0,"act_Wait",new{duration=8});waitRun=Run(wait);waitBinding=new(socialWorld,wait.SessionId,wait.ActorId,dispatchIds[0][wait.Sequence]);Check(actions[0].IsActionRunning,"A_wait_started");
   var inv=RawCall(1,"read_inventory",new{}).Data;slot=inv.GetProperty("slots").EnumerateArray().Single(e=>e.GetProperty("value").GetInt32()==value&&e.GetProperty("count").GetInt32()==1).GetProperty("slot").GetInt32();eatStart=GameTime;var eat=Call(1,"act_Eat",new{slot,value,count=1});eatRun=Run(eat);eatBinding=new(socialWorld,eat.SessionId,eat.ActorId,dispatchIds[1][eat.Sequence]);phase=5;return;}
  if(phase==5){if(actions[1].IsActionRunning)return;eatEnd=GameTime;var eaten=RawCall(1,"action_status",new{});var waiting=RawCall(0,"action_status",new{});CooperativeEvidence45.ValidateEating(eatingBefore,InventoryEvidence(),ports[1].ActorId,eaten.Data.GetProperty("affectedCount").GetInt32());eatEvidence=new(eatBinding,"act_Eat",Guid.Parse(eatRun),eatStart,eatEnd,Status(eaten),eaten.Data.GetProperty("affectedCount").GetInt32());waitObserved=GameTime;Effect("eating",Successful(eaten)&&Run(eaten)==eatRun&&Count(1)==0&&Count(0)==0&&events==1&&ps[1].PlayerStats.FoodItemsEaten==statsBefore+1&&ps[1].ComponentVitalStats.Food>foodBefore,new{eatReceipt=eaten,foodBefore,foodAfter=ps[1].ComponentVitalStats.Food,statisticBefore=statsBefore,statisticAfter=ps[1].PlayerStats.FoodItemsEaten,nativeEvents=events,beforeCount=1,afterCount=Count(1)});
   Effect("overlap",actions[0].IsActionRunning&&Status(waiting)=="running"&&Run(waiting)==waitRun,new{waitRunId=waitRun,eatRunId=eatRun,waitStart,eatStart,eatEnd,waitObservedRunningAt=GameTime,runningWaitReceipt=waiting,eatReceipt=eaten});State("eaten");phase=6;return;}
  if(phase==6){if(actions[0].IsActionRunning)return;var waited=RawCall(0,"action_status",new{});CooperativeEvidence45.ValidateOverlap(new(waitBinding,"act_Wait",Guid.Parse(waitRun),waitStart,GameTime,Status(waited),waited.Data.GetProperty("affectedCount").GetInt32()),eatEvidence,waitObserved);Effect("independent_progress",Successful(waited)&&Run(waited)==waitRun&&waitRun!=eatRun,new{waitReceipt=waited,waitRunId=waitRun,eatRunId=eatRun,waitStart,eatStart,eatEnd,waitEnd=GameTime});completedScenario=true;RequestStop("scenario_complete");}
 }catch(Exception e){Log(new{kind="coop45_error",phase,error=e.ToString()});if(stopReason==null)RequestStop("scenario_error:"+e.Message);else if(clock.Elapsed.TotalSeconds-stopStarted>=7){Done=true;Passed=false;Reason="cleanup_uncertain";}}}
 public void Dispose(){if(disposed)return;disposed=true;if(subscribed){ps[1].ComponentVitalStats.FoodEaten-=Eaten;subscribed=false;}var u=ps[0].Project.FindSubsystem<SubsystemUpdate>(true);u.RemoveUpdateable(this);
  if(!idleVerified){Log(new{kind="coop45_cleanup",terrainRestored=false,idleVerified=false,reason="cleanup_uncertain",worldMustBeDisposed=true});throw new InvalidOperationException("cleanup_uncertain: no verified idle; terrain intentionally not restored");}
  for(int i=0;i<2;i++)u.RemoveUpdateable(actions[i]);u.RemoveUpdateable(senses);senses.Dispose();foreach(var c in original)terrain.ChangeCell(c.Key.X,c.Key.Y,c.Key.Z,c.Value);interests.Release();backend.Dispose();Log(new{kind="coop45_cleanup",terrainRestored=true,idleVerified=true,worldMustBeDisposed=true,note="External actor/session registration lives until this disposable world is unloaded; do not reuse actors."});}
}
