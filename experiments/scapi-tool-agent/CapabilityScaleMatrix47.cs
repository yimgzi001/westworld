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
internal sealed class CapabilityScaleMatrix47 : IUpdateable, IDisposable {
 public float FloatUpdateOrder => -14;
 public bool Done {get;private set;} public bool Passed {get;private set;}
 public string Reason {get;private set;}="pending";
 static readonly InterestRadii FixtureRadii=new(16,24);
 readonly ComponentPlayer[] ps; readonly AtomicActions[] actions; readonly NativeToolSession[] ports;
 readonly NativeActorSession senses; readonly ScapiInterestBackend backend; readonly InterestCoordinator interests;
 readonly SubsystemTerrain terrain; readonly Dictionary<Point3,int> original=[]; readonly Stopwatch clock=Stopwatch.StartNew();
 string stopReason; double stopStarted; bool idleVerified; bool completedScenario;
 int phase,checks; bool disposed;bool cleanupErrorLogged;double nextDrain;
 readonly bool social42=true;
 readonly Guid socialWorld=Guid.NewGuid();
 readonly List<JsonElement>[] mailboxes;
 readonly Dictionary<long,Guid>[] dispatchIds;
 double GameTime=>ps[0].Project.FindSubsystem<SubsystemTime>(true).GameTime;
 public CapabilityScaleMatrix47(ComponentPlayer[] players){
  // Pure fixture preflight before creating action owners, sessions or terrain leases.
  FixtureRadii.Validate();new InterestRadii(32,48).Validate();
  if(FixtureRadii.FullReady<Math.Sqrt(8*8+3*3))throw new InvalidOperationException("Fixture interest does not cover edit/readiness extent");
  if(players.Length<2||players.Length>SubsystemPlayers.MaxExternalActors||players.Any(p=>!p.PlayerData.IsExternalActor)||players.Distinct().Count()!=players.Length)throw new ArgumentException("Between 2 and the existing external actor limit required");
  if(players.Any(p=>!ReferenceEquals(p.Project,players[0].Project))||players.Select(p=>p.Entity).Distinct().Count()!=players.Length)throw new ArgumentException("Same project and distinct entities required");
  ps=players;actionSteady=new bool[ps.Length];operation=new string[ps.Length];successes=new int[ps.Length];failures=new int[ps.Length];steadySuccesses=new int[ps.Length];travel=new double[ps.Length];steadyTravel=new double[ps.Length];origins=new Vector3[ps.Length];runIds=new string[ps.Length];bindings=new CoopBinding45[ps.Length];mailboxes=ps.Select(_=>new List<JsonElement>()).ToArray();dispatchIds=ps.Select(_=>new Dictionary<long,Guid>()).ToArray();actions=ps.Select(p=>new AtomicActions(p.Entity)).ToArray();ports=ps.Select((p,i)=>new NativeToolSession(Guid.NewGuid(),p,new ActorTools(actions[i],true))).ToArray();
  terrain=ps[0].Project.FindSubsystem<SubsystemTerrain>(true);senses=new(ps[0].Project,socialWorld,actionsEnabled:false);
  for(int i=0;i<ps.Length;i++)senses.Bind(ports[i].ActorId,ps[i]);ps[0].Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(senses);
  backend=new(terrain,ps[0].Project.FindSubsystem<SubsystemSpawn>(true));interests=new(senses.Registry,backend,new InterestRadii(32,48));interests.Sync();
  foreach(var p in ps)terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);
  Log(new{kind="batch47_scope",suite="scale",worldId=socialWorld,actualActors=ps.Length,actors=ports.Select(x=>new{actorId=x.ActorId,sessionId=x.SessionId}),modelApi=false});
  Log(new{kind="scale47_start",artificialFixture=true,inventoryInjected=false,healthEdited=false,model=false,social42,route=social42?"adapter_validated_with_explicit_native_negative_controls":"native_only",actors=ports.Select(x=>new{x.ActorId,x.SessionId})});
 }
 static void Log(object o)=>ToolAgentGate.Log(o);
 void Check(bool ok,string label){Log(new{kind="scale47_assert",label,passed=ok,phase});if(!ok)throw new InvalidOperationException(label);checks++;}
 ToolReceipt RawCall(int actor,string tool,object args){
  var request=Guid.NewGuid();var r=ports[actor].Call(tool,args);dispatchIds[actor][r.Sequence]=request;CooperativeEvidence45.ValidateIdentity(new(socialWorld,ports[actor].SessionId,ports[actor].ActorId,request),new(socialWorld,r.SessionId,r.ActorId,request));Log(new{kind="scale47_receipt",actor,requestId=request,actorId=ports[actor].ActorId,sessionId=ports[actor].SessionId,tool,arguments=args,receipt=r,gameTime=GameTime});
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
  var observation=RawCall(actor,"observe",new{look_distance=5,object_distance=5,limit=8,block_limit=16});
  var inventory=RawCall(actor,"read_inventory",new{});
  var task=RawCall(actor,"task_status",new{});var action=RawCall(actor,"action_status",new{});
  var inbox=RawCall(actor,"read_social",new{});
  if(new[]{observation,inventory,task,action,inbox}.Any(r=>r.State!="returned"))throw new InvalidOperationException("scale47_capture_read_failed");
  bool idle=CapabilityDispatchPolicy.KnownIdle(task.Data)&&CapabilityDispatchPolicy.KnownIdle(action.Data,true)&&!actions[actor].IsActionRunning;
  if(!idle&&tool!="cancel_action")throw new InvalidOperationException("scale47_owner_not_idle");
  double now=observation.Data.GetProperty("gameTime").GetDouble();
  mailboxes[actor].RemoveAll(e=>e.TryGetProperty("gameTime",out var t)&&now-t.GetDouble()>=SocialTaskFamily.NativeOfferLifetimeSeconds);
  var events=mailboxes[actor].GroupBy(e=>e.TryGetProperty("offerReference",out var f)&&f.ValueKind==JsonValueKind.String?f.GetString():e.GetRawText()).Select(g=>g.Last()).ToArray();
  var mailbox=JsonSerializer.SerializeToElement(events);
  var scope=new SocialCaptureId(socialWorld,ports[actor].SessionId,ports[actor].ActorId,Guid.NewGuid());
  var reads=new[]{new CapabilityRead(scope,"observe",empty,observation.Data),new CapabilityRead(scope,"read_inventory",empty,inventory.Data),new CapabilityRead(scope,"read_social",empty,mailbox)};
  var facts=CapabilityFacts.Build(scope,reads,ports[actor].GetTools().Select(t=>t.Name).ToArray(),idle);
  if(facts.Social is null)throw new InvalidOperationException("scale47_social_facts_missing");
  var family=SocialTaskFamily.Build(scope,facts.Social);
  var candidates=family.Entries.Where(e=>e.Descriptor.Name!="submit_plan").Select(e=>new CapabilityMenuCandidate(e.Descriptor.Name,e.State.ActorSupported,e.State.Facts,e.State.Reason,e.Offer,e.Schema));
  var enriched=System.Text.Json.Nodes.JsonNode.Parse(inventory.Data.GetRawText())!;
  foreach(var row in enriched["slots"]!.AsArray())if(row!["count"]!.GetValue<int>()>0){var item=RawCall(actor,"read_item",new{value=row["value"]!.GetValue<int>()});if(item.State=="returned"&&item.Data.TryGetProperty("nutrition",out var nutrition))row["nutrition"]=System.Text.Json.Nodes.JsonNode.Parse(nutrition.GetRawText());}
  var native=NativeToolOffers.Build(ports[actor].GetTools(),observation.Data,inventory.Data,Array.Empty<JsonElement>(),visibleEatInventory:JsonSerializer.SerializeToElement(enriched));
  var menu=new CapabilityMenu(scope.Session,scope.Actor,scope.Capture,CapabilityCatalog.All,native.Select(o=>new CapabilityMenuCandidate(o.Name,true,FactState.Available,"same_actor_capture",o)),candidates);
  var view=menu.Select(scope.Session,scope.Actor,scope.Capture,new(new[]{tool},Array.Empty<string>()));
  if(mustBeUnavailable){
   Check(!view.Offers.Any(o=>o.Name==tool),"scale47_stale_offer_not_admitted");
   Log(new{kind="scale47_adapter_rejected",actor,tool,capture=scope.Capture,reason="no_current_received_offer",nativeDispatched=false});return;
  }
  var context=ToolResultProjection.BuildContext(JsonSerializer.SerializeToElement(new{observation=observation.Data,inventory=JsonSerializer.SerializeToElement(enriched),social=mailbox}),Array.Empty<JsonElement>());
  if(context.GetProperty("projectionStatus").GetString()!="projected")throw new InvalidOperationException("scale47_public_context_budget");
  var prompt=new ToolDecisionPrompt(scope.Session,scope.Actor,scope.Capture,context,view.Offers);
  var parsed=prompt.ParseResponse(JsonSerializer.Serialize(new{goal="Deterministic multi actor parallel movement menu contract",steps=new[]{new{tool,arguments=args}}}));
  if(parsed.Steps.Count!=1||parsed.Steps[0].Tool!=tool)throw new InvalidOperationException("scale47_parsed_action_mismatch");
  Log(new{kind="scale47_adapter_validated",actor,tool,session=scope.Session,actorId=scope.Actor,capture=scope.Capture,offered=view.Offers.Select(o=>o.Name),route="adapter_validated",modelApi=false});
 }
 static string Status(ToolReceipt r)=>r.Data.ValueKind==JsonValueKind.Object&&r.Data.TryGetProperty("status",out var s)?s.GetString():null;
 static bool Successful(ToolReceipt r)=>r.State=="returned"&&Status(r)=="succeeded";
 void Set(int x,int y,int z,int v){var c=new Point3(x,y,z);original.TryAdd(c,terrain.Terrain.GetCellValue(x,y,z));terrain.ChangeCell(x,y,z,v);}
 bool Ready(){foreach(var p in ps){int x=Terrain.ToCell(p.ComponentBody.Position.X),z=Terrain.ToCell(p.ComponentBody.Position.Z);for(int a=x-4;a<=x+4;a++)for(int b=z-4;b<=z+4;b++)if(terrain.Terrain.GetChunkAtCell(a,b)?.State!=TerrainChunkState.Valid)return false;}return true;}
 Vector3[] lanePositions;Guid[] fixtureLeases;
 void PrimeFixture(){if(lanePositions is not null)return;var p=ps[0].ComponentBody.Position;int columns=(int)Math.Ceiling(Math.Sqrt(ps.Length)),rows=(int)Math.Ceiling((double)ps.Length/columns);lanePositions=Enumerable.Range(0,ps.Length).Select(i=>new Vector3(Terrain.ToCell(p.X)-columns*6+.5f+(i%columns)*12,Terrain.ToCell(p.Y),Terrain.ToCell(p.Z)+12+.5f+(i/columns)*3)).ToArray();fixtureLeases=lanePositions.Select(_=>Guid.NewGuid()).ToArray();for(int i=0;i<ps.Length;i++)backend.Upsert(fixtureLeases[i],new Westworld.Runtime.Core.Position(lanePositions[i].X,lanePositions[i].Y,lanePositions[i].Z),FixtureRadii);backend.Publish();Log(new{kind="scale47_fixture_preload",actualActors=ps.Length,columns,rows,positions=lanePositions.Select(Position),artificialFixture=true,temporaryInterestLeases=true,modelApi=false});}
 Vector3 Lane(int i)=>lanePositions[i];
 bool FixtureStartReady(){for(int i=0;i<ps.Length;i++){var p=Lane(i);for(int dx=-4;dx<=8;dx++)for(int dz=-3;dz<=3;dz++)if(terrain.Terrain.GetChunkAtCell(Terrain.ToCell(p.X)+dx,Terrain.ToCell(p.Z)+dz)?.State!=TerrainChunkState.Valid)return false;}return true;}
 void Fixture(){var positions=Enumerable.Range(0,ps.Length).Select(Lane).ToArray();foreach(var p in positions){int x=Terrain.ToCell(p.X),y=Terrain.ToCell(p.Y),z=Terrain.ToCell(p.Z);for(int a=x-3;a<=x+8;a++)for(int c=z-2;c<=z+2;c++){Set(a,y-1,c,DirtBlock.Index);for(int b=y;b<=y+3;b++)Set(a,b,c,0);}}
  for(int i=0;i<ps.Length;i++){var b=ps[i].ComponentBody;b.Position=positions[i];b.Velocity=Vector3.Zero;b.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2);}
  foreach(var id in fixtureLeases)backend.Remove(id);interests.Sync();backend.Publish();
  Log(new{kind="scale47_fixture",actualActors=ps.Length,positions=positions.Select(Position),naturalEntitiesRemoved=false,terrainEdited=true,positionEdited=true,inventoryInjected=false,artificialFixture=true,note="Grid of independent four-meter shuttle lanes; initial placement only. Native movement, turning and physics unchanged."});
 }
 public void RequestStop(string reason){if(Done||stopReason!=null)return;stopReason=reason;stopStarted=clock.Elapsed.TotalSeconds;Log(new{kind="scale47_stop_requested",reason,phase});}
 bool Drain(){
  bool idle=true;
  for(int i=0;i<ps.Length;i++){RawCall(i,"cancel_task",new{});RawCall(i,"cancel_action",new{});var task=RawCall(i,"task_status",new{});var action=RawCall(i,"action_status",new{});bool taskIdle=task.State=="returned"&&Status(task) is "idle" or "succeeded" or "failed" or "cancelled" or "skipped";bool actionIdle=action.State=="returned"&&(action.Data.ValueKind==JsonValueKind.Null||Status(action) is "succeeded" or "failed" or "cancelled" or "rejected");idle&=taskIdle&&actionIdle&&!actions[i].IsActionRunning;}
  if(idle){idleVerified=true;Done=true;Passed=completedScenario;Reason=completedScenario?"multi_actor_scale_window_verified":stopReason;Log(new{kind="batch47_summary",suite="scale",modelApi=false,passed=Passed,actualActors=ps.Length,checks,phase,reason=Reason,idleVerified=true,elapsed=clock.Elapsed.TotalSeconds,successes,failures,steadySuccesses,travel,steadyTravel});return true;}
  if(clock.Elapsed.TotalSeconds-stopStarted>=7){Done=true;Passed=false;Reason="cleanup_uncertain";Log(new{kind="batch47_summary",suite="scale",modelApi=false,passed=false,checks,phase,reason=Reason,idleVerified=false,stopReason});}
  return Done;
 }
 readonly Vector3[] origins; readonly string[] runIds; readonly CoopBinding45[] bindings;
 readonly bool[] actionSteady;readonly string[] operation;readonly int[] successes,failures,steadySuccesses;readonly double[] travel,steadyTravel;
 Dictionary<Guid,long> healthSteadyBaseline;
 double warmStarted,steadyStarted,steadyGameStarted,nextPoll;bool steady;bool stopDispatch;
 string Run(ToolReceipt r)=>r.Data.ValueKind==JsonValueKind.Object&&r.Data.TryGetProperty("runId",out var id)?id.GetString():null;
 static float[] Position(Vector3 v)=>[v.X,v.Y,v.Z];
 void Window(string label){var health=senses.Registry.CaptureHealthPage(senses.SessionId,limit:128);var expected=ports.Select(p=>p.ActorId).ToHashSet();Check(health.WorldId==socialWorld&&health.SessionId==senses.SessionId&&health.TotalActors==ps.Length&&health.ActiveBoundActors==ps.Length&&health.NextOffset is null&&health.Actors.Select(a=>a.ActorId).ToHashSet().SetEquals(expected),"native_registry_health_matches_real_cohort");
  if(label=="steady_start")healthSteadyBaseline=health.Actors.ToDictionary(a=>a.ActorId,a=>a.UpdateCount);
  if(label=="steady_end")Check(healthSteadyBaseline is not null&&health.Actors.All(a=>a.UpdateCount>healthSteadyBaseline[a.ActorId]),"all_registry_actors_updated_during_steady");
  Log(new{kind="scale47_health",phase=label,health,steadyBaseline=healthSteadyBaseline,gameTime=GameTime,monotonicSeconds=clock.Elapsed.TotalSeconds,hostAuditOnly=true,note="Registry update counts are not completed actions or scheduling fairness",modelApi=false});
  Log(new{kind="batch47_window",suite="scale",phase=label,wallUtc=DateTimeOffset.UtcNow,monotonicSeconds=clock.Elapsed.TotalSeconds,gameTime=GameTime,actualActors=ps.Length,durationBasis="monotonic_wall",warmupSeconds=10,steadySeconds=30,modelApi=false});}
 void Dispatch(int i,bool turn){actionSteady[i]=steady;origins[i]=ps[i].ComponentBody.Position;operation[i]=turn?"act_FaceTowards":"act_MoveTo";var r=turn?Call(i,operation[i],new{bearing=180,pitch=0,timeout=4}):Call(i,operation[i],new{bearing=0,distance=4,range=.3,timeout=4});Check(r.State=="returned"&&Status(r) is "running" or "succeeded","native_cycle_dispatch");runIds[i]=Run(r);bindings[i]=new(socialWorld,r.SessionId,r.ActorId,dispatchIds[i][r.Sequence]);Log(new{kind="scale47_dispatch",actor=i,actorId=r.ActorId,sessionId=r.SessionId,requestId=bindings[i].Request,tool=operation[i],runId=runIds[i],origin=Position(origins[i]),gameTime=GameTime,monotonicSeconds=clock.Elapsed.TotalSeconds,window=steady?"steady":"warmup"});}
 void Poll(){for(int i=0;i<ps.Length;i++){if(runIds[i] is null)continue;var r=RawCall(i,"action_status",new{});Check(Run(r)==runIds[i],"cycle_run_identity");if(actions[i].IsActionRunning)continue;
   bool success=Successful(r);float moved=(ps[i].ComponentBody.Position-origins[i]).XZ.Length();bool move=operation[i]=="act_MoveTo";bool actual=success&&(!move||moved>3);
   if(actual){successes[i]++;if(actionSteady[i])steadySuccesses[i]++;if(move){travel[i]+=moved;if(actionSteady[i])steadyTravel[i]+=moved;}}else failures[i]++;
   Log(new{kind="scale47_progress",actor=i,actorId=r.ActorId,sessionId=r.SessionId,requestId=bindings[i].Request,tool=operation[i],runId=runIds[i],receipt=r,passed=actual,startedInSteady=actionSteady[i],origin=Position(origins[i]),position=Position(ps[i].ComponentBody.Position),horizontalDisplacement=moved,successes=successes[i],failures=failures[i],steadySuccesses=steadySuccesses[i],travel=travel[i],steadyTravel=steadyTravel[i],gameTime=GameTime,monotonicSeconds=clock.Elapsed.TotalSeconds,window=steady?"steady":"warmup"});
   runIds[i]=null;if(!actual){RequestStop("native_cycle_failed_actor_"+i);return;}if(!stopDispatch)Dispatch(i,move);
  }}
 public void Update(float dt){if(Done)return;try{
  if(stopReason!=null){if(clock.Elapsed.TotalSeconds<nextDrain)return;nextDrain=clock.Elapsed.TotalSeconds+.1;Drain();return;}if(clock.Elapsed.TotalSeconds>110){RequestStop("scale_wall_timeout");return;}interests.Sync();
  if(phase==0){if(ps.Any(p=>p.Entity.FindComponent<ComponentRider>(true).m_isAnimating))return;for(int i=0;i<ps.Length;i++)if(ps[i].ComponentBody.ParentBody is not null)Call(i,"act_Dismount",new{});phase=1;return;}
  if(phase==1){if(actions.Any(a=>a.IsActionRunning)||!Ready())return;PrimeFixture();if(!FixtureStartReady())return;Check(ps.All(p=>p.ComponentBody.ParentBody is null),"all_dismounted");Fixture();phase=2;return;}
  if(phase==2){if(!Ready())return;Check(ps.All(p=>p.GameWidget is null),"all_ai_no_viewport");warmStarted=clock.Elapsed.TotalSeconds;Window("warmup_start");for(int i=0;i<ps.Length;i++)Dispatch(i,false);phase=3;return;}
  if(phase==3){if(!steady&&clock.Elapsed.TotalSeconds-warmStarted>=10){steady=true;steadyStarted=clock.Elapsed.TotalSeconds;steadyGameStarted=GameTime;Window("steady_start");}
   if(steady&&clock.Elapsed.TotalSeconds-steadyStarted>=30){stopDispatch=true;Window("steady_end");Log(new{kind="scale47_window_effect",passed=GameTime>steadyGameStarted,gameTimeStart=steadyGameStarted,gameTimeEnd=GameTime,wallStart=steadyStarted,wallEnd=clock.Elapsed.TotalSeconds,actualActors=ps.Length,steadySuccesses,steadyTravel,failures,modelApi=false});Check(GameTime>steadyGameStarted&&steadySuccesses.All(n=>n>=2)&&steadyTravel.All(n=>n>3)&&failures.All(n=>n==0),"each_actor_steady_native_progress");completedScenario=true;RequestStop("window_complete");return;}
   if(clock.Elapsed.TotalSeconds<nextPoll)return;nextPoll=clock.Elapsed.TotalSeconds+.1;Poll();
   Log(new{kind="scale47_heartbeat",gameTime=GameTime,monotonicSeconds=clock.Elapsed.TotalSeconds,window=steady?"steady":"warmup",actualActors=ps.Length,running=actions.Count(a=>a.IsActionRunning),successes,failures,steadySuccesses,travel,steadyTravel,modelApi=false});
  }
 }catch(Exception e){if(stopReason==null||!cleanupErrorLogged)Log(new{kind="scale47_error",phase,error=e.ToString()});if(stopReason!=null)cleanupErrorLogged=true;if(stopReason==null)RequestStop("scenario_error:"+e.Message);else if(clock.Elapsed.TotalSeconds-stopStarted>=7){Done=true;Passed=false;Reason="cleanup_uncertain";}}}
 public void Dispose(){if(disposed)return;disposed=true;var u=ps[0].Project.FindSubsystem<SubsystemUpdate>(true);u.RemoveUpdateable(this);
  if(!idleVerified){Log(new{kind="scale47_cleanup",terrainRestored=false,idleVerified=false,reason="cleanup_uncertain",worldMustBeDisposed=true});throw new InvalidOperationException("cleanup_uncertain: no verified idle; terrain intentionally not restored");}
  for(int i=0;i<ps.Length;i++)u.RemoveUpdateable(actions[i]);u.RemoveUpdateable(senses);senses.Dispose();foreach(var c in original)terrain.ChangeCell(c.Key.X,c.Key.Y,c.Key.Z,c.Value);interests.Release();backend.Dispose();Log(new{kind="scale47_cleanup",terrainRestored=true,idleVerified=true,worldMustBeDisposed=true,note="External actor/session registration lives until this disposable world is unloaded; do not reuse actors."});}
}
