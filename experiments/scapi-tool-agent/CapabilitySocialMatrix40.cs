using Engine;
using Game;
using Game.Westworld;
using System.Diagnostics;
using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;
using Capability39;
using Capability39.SocialTasks;
using Westworld.Decisions.Local;

// Standalone fixture. Do not construct ToolAgentScenario for either of these actors.
internal sealed class CapabilitySocialMatrix40 : IUpdateable, IDisposable {
 public float FloatUpdateOrder => -14;
 public bool Done {get;private set;} public bool Passed {get;private set;}
 public string Reason {get;private set;}="pending";
 readonly ComponentPlayer[] ps; readonly AtomicActions[] actions; readonly NativeToolSession[] ports;
 readonly NativeActorSession senses; readonly ScapiInterestBackend backend; readonly InterestCoordinator interests;
 readonly SubsystemTerrain terrain; readonly Dictionary<Point3,int> original=[]; readonly Stopwatch clock=Stopwatch.StartNew();
 string stopReason; double stopStarted; bool idleVerified; bool completedScenario;
 int phase,checks,value,slot,senderBefore,recipientBefore; string targetA,targetB,offer; double expiresWait; bool disposed;
 readonly bool social42=Environment.GetEnvironmentVariable("WESTWORLD_SOCIAL42")=="1";
 readonly Guid socialWorld=Guid.NewGuid();
 readonly List<JsonElement>[] mailboxes=[new(),new()];
 const string Speech="矩阵通信";
 double GameTime=>ps[0].Project.FindSubsystem<SubsystemTime>(true).GameTime;
 public CapabilitySocialMatrix40(ComponentPlayer[] players){
  if(players.Length!=2||players.Any(p=>!p.PlayerData.IsExternalActor)||ReferenceEquals(players[0],players[1]))throw new ArgumentException("Two distinct external fixture actors required");
  ps=players;actions=ps.Select(p=>new AtomicActions(p.Entity)).ToArray();ports=ps.Select((p,i)=>new NativeToolSession(Guid.NewGuid(),p,new ActorTools(actions[i],true))).ToArray();
  terrain=ps[0].Project.FindSubsystem<SubsystemTerrain>(true);senses=new(ps[0].Project,Guid.NewGuid(),actionsEnabled:false);
  for(int i=0;i<2;i++)senses.Bind(ports[i].ActorId,ps[i]);ps[0].Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(senses);
  backend=new(terrain,ps[0].Project.FindSubsystem<SubsystemSpawn>(true));interests=new(senses.Registry,backend,new InterestRadii(32,48));interests.Sync();
  foreach(var p in ps)terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);
  Log(new{kind="matrix40_social_start",artificialFixture=true,inventoryInjected=false,healthEdited=false,model=false,social42,route=social42?"adapter_validated_with_explicit_native_negative_controls":"native_only",actors=ports.Select(x=>new{x.ActorId,x.SessionId})});
 }
 static void Log(object o)=>ToolAgentGate.Log(o);
 void Check(bool ok,string label){Log(new{kind="matrix40_assert",label,passed=ok,phase});if(!ok)throw new InvalidOperationException(label);checks++;}
 ToolReceipt RawCall(int actor,string tool,object args){
  var r=ports[actor].Call(tool,args);Log(new{kind="matrix40_receipt",actor,tool,arguments=args,receipt=r});
  if(social42&&r.State=="returned"){
   if(tool=="read_social"&&r.Data.ValueKind==JsonValueKind.Array){foreach(var e in r.Data.EnumerateArray())mailboxes[actor].Add(e.Clone());while(mailboxes[actor].Count>64)mailboxes[actor].RemoveAt(0);}
   if(tool=="act_RespondToOffer"&&Successful(r)){var a=JsonSerializer.SerializeToElement(args);var reference=a.GetProperty("offer").GetString();mailboxes[actor].RemoveAll(e=>e.TryGetProperty("offerReference",out var f)&&f.GetString()==reference);}
  }
  return r;
 }
 static bool IsSocialWrite(string tool)=>tool is "act_Speak" or "act_OfferItem" or "act_RespondToOffer";
 ToolReceipt Call(int actor,string tool,object args){
  if(social42&&IsSocialWrite(tool))ValidateSocial42(actor,tool,args,false);
  return RawCall(actor,tool,args);
 }
 // Negative raw calls are intentional native rejection probes, never adapter-approved actions.
 ToolReceipt NegativeResponse(int actor,object args,string label){
  if(social42){ValidateSocial42(actor,"act_RespondToOffer",args,true);Log(new{kind="social42_native_negative_probe",actor,label,route="native_only_negative_control"});}
  return RawCall(actor,"act_RespondToOffer",args);
 }
 void ValidateSocial42(int actor,string tool,object args,bool mustBeUnavailable){
  var empty=JsonSerializer.SerializeToElement(new{});
  var observation=RawCall(actor,"observe",new{look_distance=5,object_distance=5,limit=100,block_limit=32});
  var inventory=RawCall(actor,"read_inventory",new{});
  var task=RawCall(actor,"task_status",new{});var action=RawCall(actor,"action_status",new{});
  var inbox=RawCall(actor,"read_social",new{});
  if(new[]{observation,inventory,task,action,inbox}.Any(r=>r.State!="returned"))throw new InvalidOperationException("social42_capture_read_failed");
  bool idle=CapabilityDispatchPolicy.KnownIdle(task.Data)&&CapabilityDispatchPolicy.KnownIdle(action.Data,true)&&!actions[actor].IsActionRunning;
  if(!idle)throw new InvalidOperationException("social42_owner_not_idle");
  double now=observation.Data.GetProperty("gameTime").GetDouble();
  mailboxes[actor].RemoveAll(e=>e.TryGetProperty("gameTime",out var t)&&now-t.GetDouble()>=SocialTaskFamily.NativeOfferLifetimeSeconds);
  var events=mailboxes[actor].GroupBy(e=>e.TryGetProperty("offerReference",out var f)&&f.ValueKind==JsonValueKind.String?f.GetString():e.GetRawText()).Select(g=>g.Last()).ToArray();
  var mailbox=JsonSerializer.SerializeToElement(events);
  var scope=new SocialCaptureId(socialWorld,ports[actor].SessionId,ports[actor].ActorId,Guid.NewGuid());
  var reads=new[]{new CapabilityRead(scope,"observe",empty,observation.Data),new CapabilityRead(scope,"read_inventory",empty,inventory.Data),new CapabilityRead(scope,"read_social",empty,mailbox)};
  var facts=CapabilityFacts.Build(scope,reads,ports[actor].GetTools().Select(t=>t.Name).ToArray(),idle);
  if(facts.Social is null)throw new InvalidOperationException("social42_social_facts_missing");
  var family=SocialTaskFamily.Build(scope,facts.Social);
  var candidates=family.Entries.Where(e=>e.Descriptor.Name!="submit_plan").Select(e=>new CapabilityMenuCandidate(e.Descriptor.Name,e.State.ActorSupported,e.State.Facts,e.State.Reason,e.Offer,e.Schema));
  var menu=new CapabilityMenu(scope.Session,scope.Actor,scope.Capture,CapabilityCatalog.All,Array.Empty<CapabilityMenuCandidate>(),candidates);
  var view=menu.Select(scope.Session,scope.Actor,scope.Capture,new(new[]{tool},Array.Empty<string>()));
  if(mustBeUnavailable){
   Check(!view.Offers.Any(o=>o.Name==tool),"social42_stale_offer_not_admitted");
   Log(new{kind="social42_adapter_rejected",actor,tool,capture=scope.Capture,reason="no_current_received_offer",nativeDispatched=false});return;
  }
  var context=ToolResultProjection.BuildContext(JsonSerializer.SerializeToElement(new{observation=observation.Data,inventory=inventory.Data,social=mailbox}),Array.Empty<JsonElement>());
  if(context.GetProperty("projectionStatus").GetString()!="projected")throw new InvalidOperationException("social42_public_context_budget");
  var prompt=new ToolDecisionPrompt(scope.Session,scope.Actor,scope.Capture,context,view.Offers);
  var parsed=prompt.ParseResponse(JsonSerializer.Serialize(new{goal="Deterministic two actor social menu contract",steps=new[]{new{tool,arguments=args}}}));
  if(parsed.Steps.Count!=1||parsed.Steps[0].Tool!=tool)throw new InvalidOperationException("social42_parsed_action_mismatch");
  Log(new{kind="social42_adapter_validated",actor,tool,session=scope.Session,actorId=scope.Actor,capture=scope.Capture,offered=view.Offers.Select(o=>o.Name),route="adapter_validated",modelApi=false});
 }
 static string Status(ToolReceipt r)=>r.Data.ValueKind==JsonValueKind.Object&&r.Data.TryGetProperty("status",out var s)?s.GetString():null;
 static bool Successful(ToolReceipt r)=>r.State=="returned"&&Status(r)=="succeeded";
 int Count(int actor)=>Enumerable.Range(0,ps[actor].ComponentMiner.Inventory.SlotsCount).Where(i=>ps[actor].ComponentMiner.Inventory.GetSlotValue(i)==value).Sum(ps[actor].ComponentMiner.Inventory.GetSlotCount);
 void AuditInventory(string label){Log(new{kind="matrix40_inventory",label,value,first=Count(0),second=Count(1),firstReceipt=Call(0,"read_inventory",new{}),secondReceipt=Call(1,"read_inventory",new{})});}
 void Reconcile(int actor){if(ports[actor].RequiresReconciliation is long seq){var r=Call(actor,"read_inventory",new{});Check(r.State=="returned"&&!actions[actor].IsActionRunning,"failed call reconciled only with idle and inventory");ports[actor].Reconcile(seq,r.Sequence);}}
 void Set(int x,int y,int z,int v){var c=new Point3(x,y,z);original.TryAdd(c,terrain.Terrain.GetCellValue(x,y,z));terrain.ChangeCell(x,y,z,v);}
 bool Ready(){foreach(var p in ps){int x=Terrain.ToCell(p.ComponentBody.Position.X),z=Terrain.ToCell(p.ComponentBody.Position.Z);for(int a=x-4;a<=x+4;a++)for(int b=z-4;b<=z+4;b++)if(terrain.Terrain.GetChunkAtCell(a,b)?.State!=TerrainChunkState.Valid)return false;}return true;}
 void Fixture(){var p=ps[0].ComponentBody.Position;int x=Terrain.ToCell(p.X),y=Terrain.ToCell(p.Y),z=Terrain.ToCell(p.Z);for(int a=x-4;a<=x+4;a++)for(int c=z-4;c<=z+4;c++){Set(a,y-1,c,DirtBlock.Index);for(int b=y;b<=y+3;b++)Set(a,b,c,0);}
  for(int i=0;i<2;i++){var b=ps[i].ComponentBody;b.Position=new(x+.5f+i*1.5f,y,z+.5f);b.Velocity=Vector3.Zero;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,i==0?-MathF.PI/2:MathF.PI/2);}
  Log(new{kind="matrix40_fixture",terrainEdited=true,positionEdited=true,inventoryInjected=false,note="Disposable world only. Flat floor and adjacent actors are artificial, not autonomous navigation."});
 }
 string OtherReference(int actor){var r=Call(actor,"observe",new{look_distance=5,object_distance=5,limit=100,block_limit=32});Check(r.State=="returned","public observation returned");var pos=ps[1-actor].ComponentBody.Position;
  var candidates=r.Data.GetProperty("entities").EnumerateArray().Where(e=>{var p=e.GetProperty("position");return Math.Abs(p.GetProperty("x").GetSingle()-pos.X)<.1f&&Math.Abs(p.GetProperty("y").GetSingle()-pos.Y)<.1f&&Math.Abs(p.GetProperty("z").GetSingle()-pos.Z)<.1f;}).ToArray();Check(candidates.Length==1,"other actor has exactly one actual public reference");return candidates[0].GetProperty("reference").GetString();}
 string ReceivedOffer(int actor){var r=Call(actor,"read_social",new{});Check(r.State=="returned","own social inbox returned");var entries=r.Data.EnumerateArray().Where(x=>x.TryGetProperty("offerReference",out var o)&&o.ValueKind==JsonValueKind.String).ToArray();Check(entries.Length==1,"one actual received offer reference");return entries[0].GetProperty("offerReference").GetString();}
 void OfferFromSecond(){targetA=OtherReference(1);var inv=ps[1].ComponentMiner.Inventory;slot=Enumerable.Range(0,inv.SlotsCount).First(i=>inv.GetSlotValue(i)==value&&inv.GetSlotCount(i)>0);Check(Successful(Call(1,"act_OfferItem",new{target=targetA,slot,value,count=1})),"second actor offer succeeded");offer=ReceivedOffer(0);}
 public void RequestStop(string reason){if(Done||stopReason!=null)return;stopReason=reason;stopStarted=clock.Elapsed.TotalSeconds;Log(new{kind="matrix40_stop_requested",reason,phase});}
 bool Drain(){
  bool idle=true;
  for(int i=0;i<2;i++){Call(i,"cancel_task",new{});Call(i,"cancel_action",new{});var task=Call(i,"task_status",new{});var action=Call(i,"action_status",new{});bool taskIdle=task.State=="returned"&&Status(task) is "idle" or "succeeded" or "failed" or "cancelled" or "skipped";bool actionIdle=action.State=="returned"&&(action.Data.ValueKind==JsonValueKind.Null||Status(action) is "succeeded" or "failed" or "cancelled" or "rejected");idle&=taskIdle&&actionIdle&&!actions[i].IsActionRunning;}
  if(idle){idleVerified=true;Done=true;Passed=completedScenario;Reason=completedScenario?"two_actor_social_matrix_verified":stopReason;Log(new{kind="matrix40_social_result",passed=Passed,checks,phase,reason=Reason,idleVerified=true,elapsed=clock.Elapsed.TotalSeconds});return true;}
  if(clock.Elapsed.TotalSeconds-stopStarted>=7){Done=true;Passed=false;Reason="cleanup_uncertain";Log(new{kind="matrix40_social_result",passed=false,checks,phase,reason=Reason,idleVerified=false,stopReason});}
  return Done;
 }
 public void Update(float dt){if(Done)return;try{
  if(stopReason!=null){Drain();return;}
  if(clock.Elapsed.TotalSeconds>100){RequestStop("social_matrix_wall_timeout");return;}interests.Sync();
  if(phase==0){if(ps.Any(p=>p.Entity.FindComponent<ComponentRider>(true).m_isAnimating))return;for(int i=0;i<2;i++)if(ps[i].ComponentBody.ParentBody is not null)Call(i,"act_Dismount",new{});phase=1;return;}
  if(phase==1){if(actions.Any(a=>a.IsActionRunning))return;if(!Ready())return;Check(ps.All(p=>p.ComponentBody.ParentBody is null),"both actors dismounted");Fixture();phase=2;return;}
  if(phase==2){if(!Ready())return;targetB=OtherReference(0);targetA=OtherReference(1);var inv=ps[0].ComponentMiner.Inventory;slot=Enumerable.Range(0,inv.SlotsCount).First(i=>inv.GetSlotCount(i)>0);value=inv.GetSlotValue(slot);senderBefore=Count(0);recipientBefore=Count(1);AuditInventory("baseline");var r=Call(0,"act_Speak",new{text=Speech,mode="normal",target=targetB});Check(r.State=="returned","speech admitted");phase=3;return;}
  if(phase==3){if(actions[0].IsActionRunning)return;Check(Successful(Call(0,"action_status",new{})),"speech completed");var inbox=Call(1,"read_social",new{});Check(inbox.State=="returned"&&string.Concat(inbox.Data.EnumerateArray().Where(e=>e.TryGetProperty("text",out var t)&&t.ValueKind==JsonValueKind.String).Select(e=>e.GetProperty("text").GetString())).Contains(Speech),"recipient heard actual speech");targetB=OtherReference(0);Check(Successful(Call(0,"act_OfferItem",new{target=targetB,slot,value,count=1})),"offer admitted");Check(Count(0)==senderBefore&&Count(1)==recipientBefore,"offer alone transfers nothing");offer=ReceivedOffer(1);phase=4;return;}
  if(phase==4){Check(Successful(Call(1,"act_RespondToOffer",new{offer,accept=true})),"received offer accepted");Check(Count(0)==senderBefore-1&&Count(1)==recipientBefore+1,"actual transfer conserves item across two inventories");AuditInventory("accepted");var r=NegativeResponse(1,new{offer,accept=true},"consumed_offer_replay");Check(!Successful(r)&&!actions[1].IsActionRunning,"consumed offer replay rejected");Check(Count(0)+Count(1)==senderBefore+recipientBefore,"replay no duplicate items");AuditInventory("consumed_offer_replay_rejected");Reconcile(1);Call(0,"read_social",new{});Call(1,"read_social",new{});OfferFromSecond();phase=5;return;}
  if(phase==5){Check(Successful(Call(0,"act_RespondToOffer",new{offer,accept=false})),"decline succeeds");Check(Count(0)==senderBefore-1&&Count(1)==recipientBefore+1,"decline transfers nothing");AuditInventory("declined");Call(0,"read_social",new{});Call(1,"read_social",new{});OfferFromSecond();expiresWait=GameTime;Log(new{kind="matrix40_expiry_wait",stage="start",gameTime=expiresWait,waitStartedGameTime=expiresWait,elapsedGameSeconds=0d,requiredGameSeconds=16});phase=6;return;}
  if(phase==6){if(GameTime-expiresWait<16)return;Log(new{kind="matrix40_expiry_wait",stage="end",gameTime=GameTime,waitStartedGameTime=expiresWait,elapsedGameSeconds=GameTime-expiresWait,requiredGameSeconds=16});var r=NegativeResponse(0,new{offer,accept=true},"expired_offer");Check(!Successful(r)&&!actions[0].IsActionRunning,"expired actual offer rejected");Check(Count(0)==senderBefore-1&&Count(1)==recipientBefore+1,"expiry transfers nothing");AuditInventory("expired");Reconcile(0);Check(ps.All(p=>p.GameWidget is null),"AI actors have no viewport");completedScenario=true;RequestStop("scenario_complete");}
 }catch(Exception e){Log(new{kind="matrix40_social_error",checks,phase,error=e.ToString(),elapsed=clock.Elapsed.TotalSeconds});if(stopReason==null)RequestStop("scenario_error:"+e.Message);else if(clock.Elapsed.TotalSeconds-stopStarted>=7){Done=true;Passed=false;Reason="cleanup_uncertain";Log(new{kind="matrix40_social_result",passed=false,reason=Reason,idleVerified=false});}}}
 public void Dispose(){if(disposed)return;disposed=true;var u=ps[0].Project.FindSubsystem<SubsystemUpdate>(true);u.RemoveUpdateable(this);
  if(!idleVerified){Log(new{kind="matrix40_social_cleanup",terrainRestored=false,idleVerified=false,reason="cleanup_uncertain",worldMustBeDisposed=true});throw new InvalidOperationException("cleanup_uncertain: no verified idle; terrain intentionally not restored");}
  for(int i=0;i<2;i++)u.RemoveUpdateable(actions[i]);u.RemoveUpdateable(senses);senses.Dispose();foreach(var c in original)terrain.ChangeCell(c.Key.X,c.Key.Y,c.Key.Z,c.Value);interests.Release();backend.Dispose();Log(new{kind="matrix40_social_cleanup",terrainRestored=true,idleVerified=true,worldMustBeDisposed=true,note="External actor/session registration lives until this disposable world is unloaded; do not reuse actors."});}
}
