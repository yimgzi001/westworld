using Engine;
using Game;
using Game.Westworld;
using System.Diagnostics;
using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;
using Westworld.Decisions.Local;

internal static class IntegrationGate {
 internal static readonly string Root=AppContext.BaseDirectory;static readonly Stopwatch wall=Stopwatch.StartNew();
 internal static string Mode;internal static string Seed="actors-cloud-20261109";static bool created,finished,passed;static Scenario scenario;static SubsystemUpdate updates;
 internal static void Log(object value){var json=JsonSerializer.Serialize(value);Console.WriteLine(json);File.AppendAllText(Path.Combine(Root,"integration.jsonl"),json+"\n");}
 static int Main(string[] args){
  if(args.Length is <1 or >2||args[0] is not ("fixture" or "natural" or "blocked" or "obstacle" or "reload")||!Root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar))return 2;Mode=args[0];if(args.Length==2){if(args[1].Length>64||!args[1].All(c=>char.IsLetterOrDigit(c)||c=='-'))return 2;Seed=args[1];}
  if(Mode!="reload"&&(Directory.Exists(Path.Combine(Root,"doc"))||Directory.Exists(Path.Combine(Root,"Mods"))))return 2;
  if(Mode=="reload"&&!File.Exists(Path.Combine(Root,"integration.scworld")))return 2;
  Log(new{kind="start",mode=Mode,seed=Seed,fixture=Mode is "fixture" or "blocked" or "obstacle",models=Mode=="reload"?0:1});Window.Created+=()=>Window.Frame+=Frame;
  try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}return passed?0:1;
 }
 static void Frame(){try{
  if(finished)return;if(wall.Elapsed.TotalSeconds>150){Finish(false,"finite scenario timeout");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){
   created=true;SettingsManager.VisibilityRange=32;
   if(Mode=="reload"){
    using var input=File.OpenRead(Path.Combine(Root,"integration.scworld"));var directory=WorldsManager.ImportWorld(input);var world=WorldsManager.GetWorldInfo(directory);
    GameManager.LoadProject(world,((GameScreen)ScreensManager.m_screens["Game"]).Children.Find<GamesWidget>("GamesWidget",true));
    updates=GameManager.Project.FindSubsystem<SubsystemUpdate>(true);var p=GameManager.Project.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers.Single();
    scenario=new Scenario(p,Log,true);updates.AddUpdateable(scenario);ScreensManager.SwitchScreen("Game");
   }else{var w=WorldsManager.CreateWorld(new WorldSettings{Name="Westworld integrated native task gate",Seed=Seed,GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});ScreensManager.SwitchScreen("GameLoading",w,null);}
  }
  if(GameManager.Project is null)return;updates??=GameManager.Project.FindSubsystem<SubsystemUpdate>(true);if(updates.UpdateFailureCount!=0)throw new InvalidOperationException(updates.LastUpdateFailure);
  var players=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);
  if(players.PlayersData.Count==0){players.AddPlayerData(new PlayerData(GameManager.Project){Name="IntegratedActor",IsExternalActor=true});ScreensManager.SwitchScreen("Game");}
  if(scenario is null&&players.ComponentPlayers.Count==1&&players.PlayersData[0].IsReadyForPlaying){scenario=new Scenario(players.ComponentPlayers[0],Log,false);updates.AddUpdateable(scenario);}
  if(scenario?.CheckpointReady==true){scenario.Save();Finish(true,"checkpoint published at safe task boundary");}
  else if(scenario?.Done==true)Finish(scenario.Passed,"scenario finished");
 }catch(Exception e){Log(new{kind="failure",error=e.ToString()});Finish(false,"exception");}}
 static void Finish(bool ok,string reason){if(finished)return;finished=true;passed=ok;Log(new{kind="result",passed,reason,seconds=wall.Elapsed.TotalSeconds,updateFailures=updates?.UpdateFailureCount});scenario?.Dispose();Window.Close();}
}
internal sealed class Scenario:IUpdateable,IDisposable {
 public float FloatUpdateOrder=>-14;readonly ComponentPlayer p;readonly Action<object> log;readonly AtomicActions actions;readonly ActorTools tools;readonly NativeActorSession senses;readonly Guid actor=Guid.NewGuid();readonly ScapiInterestBackend backend;readonly InterestCoordinator interests;readonly SubsystemTerrain terrain;
 readonly LlamaProcessProvider model;readonly AsyncDecisionScheduler decisions;readonly LocalExploration exploration;readonly LocalObservationMemory localMemory;long routeStarted;double routeMaxAge;ExplorationChoice route;HashSet<(int X,int Y,int Z)> routeAir=new();AtomicActionRun routeRun;bool initialModelChecked,obstacleInjected;int routeMoves,routeArrivals,routeFailures,blockedFailures,obstacleStops;readonly bool reload;readonly float restoreFood;readonly long initialEaten;readonly int initialItems;
 int stage,scan,moves;double elapsed;CellTarget target;AtomicActionRun face;string targetRef;int foodSlot,foodValue;Guid restoredTask;Vector3 original;
 public bool Done{get;private set;}public bool Passed{get;private set;}public bool CheckpointReady { get { if(stage!=7)return false;var task=Call("task_status");return task.GetProperty("status").GetString()=="waiting"&&task.GetProperty("currentStep").GetInt32()==1; } }
 public Scenario(ComponentPlayer p,Action<object> log,bool reload){this.p=p;this.log=log;this.reload=reload;terrain=p.Project.FindSubsystem<SubsystemTerrain>(true);senses=new NativeActorSession(p.Project,Guid.NewGuid(),actionsEnabled:false);senses.Bind(actor,p);p.Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(senses);backend=new ScapiInterestBackend(terrain,p.Project.FindSubsystem<SubsystemSpawn>(true));interests=new InterestCoordinator(senses.Registry,backend,new InterestRadii(16,32));interests.Sync();terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);
  bool rejected=false;try{senses.Submit(new(senses.SessionId,actor,Guid.NewGuid(),Guid.NewGuid(),0,0.2f,0.1f));}catch(InvalidOperationException e){rejected=e.Message.StartsWith("Observation-only");}Require(rejected,"read-only observer accepted a competing movement command");
  localMemory=new LocalObservationMemory(senses.SessionId,actor);exploration=new LocalExploration(senses.SessionId,actor);actions=new AtomicActions(p.Entity);tools=new ActorTools(actions,false);initialEaten=p.PlayerStats.FoodItemsEaten;restoreFood=p.ComponentVitalStats.Food;initialItems=Enumerable.Range(0,p.ComponentMiner.Inventory.SlotsCount).Sum(p.ComponentMiner.Inventory.GetSlotCount);
  var bodySize=p.ComponentBody.BoundingBox.Max-p.ComponentBody.BoundingBox.Min;Require(bodySize.X<=.50001f&&bodySize.Z<=.50001f&&bodySize.Y<=1.90001f,"route projection requires ordinary player body");
  var local=Path.GetFullPath(Path.Combine(IntegrationGate.Root,"..","local-model"));Func<int,bool> harvest=v=>Terrain.ExtractContents(v)==PumpkinBlock.Index&&BasePumpkinBlock.GetSize(Terrain.ExtractData(v))==7;bool survey=IntegrationGate.Mode is "natural" or "blocked" or "obstacle";model=new(Path.Combine(local,"llama-b12211","llama-completion"),Path.Combine(local,"Qwen3.5-4B-Q4_K_M.gguf"),harvest,allowSurvey:survey);decisions=new(model,TimeSpan.FromSeconds(65),1,allowSurvey:survey,supportedSurveyHarvest:harvest);
  if(reload){Require(p.Project.FindSubsystem<SubsystemTime>(true).GameTime==0,"restore must precede first world update");Require(tools.RestoreTaskCheckpoint(),"checkpoint did not bind");var status=Call("task_status");restoredTask=status.GetProperty("taskId").GetGuid();log(new{kind="restored",task=status,food=restoreFood,entity=p.Entity.Id,external=p.PlayerData.IsExternalActor,views=p.Project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count});stage=90;}
  log(new{kind="bound",controlOwner="main AtomicActions/ActorTools only",capabilities=actions.GetCapabilities().Count,completeVitalStats=p.ComponentVitalStats is not null,views=p.Project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count});
 }
 static void Require(bool c,string why){if(!c)throw new InvalidOperationException(why);}
 JsonElement Call(string name,object args=null){using var d=JsonDocument.Parse(tools.InvokeJson(name,JsonSerializer.Serialize(args??new{})));Require(d.RootElement.GetProperty("ok").GetBoolean(),name+" rejected: "+d.RootElement);return d.RootElement.GetProperty("data").Clone();}
 static bool Terminal(JsonElement task)=>task.GetProperty("status").GetString() is "succeeded" or "failed" or "cancelled";
 public void Save(){tools.SaveTaskCheckpoint("app:integration.scworld");log(new{kind="checkpoint",task=Call("task_status"),food=p.ComponentVitalStats.Food,eaten=p.PlayerStats.FoodItemsEaten,inventory=Call("read_inventory")});}
 public void Update(float dt){if(Done)return;try{
  elapsed+=dt;interests.Sync();Require(p.GameWidget is null&&p.ComponentHealth.Health>0,"native actor lost viewless survival state");
  if(stage==0&&elapsed>=3){
   original=p.ComponentBody.Position;
   var owned=p.ComponentMiner.Inventory;for(int i=0;i<owned.SlotsCount;i++)owned.RemoveSlotItems(i,owned.GetSlotCount(i));log(new{kind="initial_condition",starterInventoryCleared=true,worldResourcesPlaced=IntegrationGate.Mode=="fixture"});
   if(IntegrationGate.Mode=="fixture"){
    int x=Terrain.ToCell(original.X),y=Terrain.ToCell(original.Y),z=Terrain.ToCell(original.Z);for(int a=x-5;a<=x+5;a++)for(int b=z-5;b<=z+5;b++){terrain.ChangeCell(a,y-1,b,Terrain.MakeBlockValue(2));for(int c=y;c<=y+3;c++)terrain.ChangeCell(a,c,b,0);}p.ComponentBody.Position=new Vector3(x+0.5f,y,z+0.5f);p.ComponentBody.Velocity=Vector3.Zero;
    var forward=p.ComponentBody.Matrix.Forward;int tx=x+(MathF.Abs(forward.X)>MathF.Abs(forward.Z)?Math.Sign(forward.X)*3:0),tz=z+(MathF.Abs(forward.X)>MathF.Abs(forward.Z)?0:Math.Sign(forward.Z)*3);terrain.ChangeCell(tx,y,tz,Terrain.MakeBlockValue(PumpkinBlock.Index));
    var inventory=p.ComponentMiner.Inventory;for(int i=0;i<inventory.SlotsCount;i++)inventory.RemoveSlotItems(i,inventory.GetSlotCount(i));log(new{kind="artificial_fixture",nativePlant=true,inventoryCleared=true});
   }
   if(IntegrationGate.Mode=="obstacle"){
    int x=Terrain.ToCell(original.X),y=Terrain.ToCell(original.Y),z=Terrain.ToCell(original.Z);
    for(int a=x-5;a<=x+5;a++)for(int b=z-5;b<=z+5;b++){terrain.ChangeCell(a,y-1,b,Terrain.MakeBlockValue(2));for(int c=y;c<=y+3;c++)terrain.ChangeCell(a,c,b,0);}
    p.ComponentBody.Position=new Vector3(x+.5f,y,z+.5f);p.ComponentBody.Velocity=Vector3.Zero;log(new{kind="obstacle_fixture",placedFood=false,flatKnownGround=true});
   }
   if(IntegrationGate.Mode=="blocked"){
    int x=Terrain.ToCell(original.X),y=Terrain.ToCell(original.Y),z=Terrain.ToCell(original.Z);
    for(int a=x-3;a<=x+3;a++)for(int b=z-3;b<=z+3;b++){terrain.ChangeCell(a,y-1,b,Terrain.MakeBlockValue(2));for(int c=y;c<=y+2;c++)terrain.ChangeCell(a,c,b,(Math.Abs(a-x)==1||Math.Abs(b-z)==1)?Terrain.MakeBlockValue(2):0);}
    p.ComponentBody.Position=new Vector3(x+.5f,y,z+.5f);p.ComponentBody.Velocity=Vector3.Zero;
    log(new{kind="blocked_fixture",placedFood=false,closedStoneRing=true});
   }stage=1;
  }else if(stage==1&&elapsed>=4){
   var o=senses.Observe(senses.SessionId,actor);var window=localMemory.Observe(o);var found=o.Cells.FirstOrDefault(c=>c.Visibility=="known"&&c.Value.HasValue&&Terrain.ExtractContents(c.Value.Value)==PumpkinBlock.Index&&BasePumpkinBlock.GetSize(Terrain.ExtractData(c.Value.Value))==7);
   if(found is not null){target=new(found.X,found.Y,found.Z,found.Value.Value);Require(decisions.Request(o,out var error),error);log(new{kind="resource_observed",observation=o.ObservationId,target,moves,scan,natural=IntegrationGate.Mode=="natural"});stage=3;}
   else if(IntegrationGate.Mode is "natural" or "blocked" or "obstacle"){
    if(!initialModelChecked){Require(decisions.Request(o,out var error),error);stage=13;}
    else if(routeMoves>=32){log(new{kind="bounded_search_exhausted",routeMoves,scan,meaning="bounded safe exploration did not find supported food"});Done=true;Passed=false;}
    else {
     var projection=LocalRouteCandidates.Project(window,1,RouteGroundPolicy.ObservedFullCube);var selection=exploration.Select(new(senses.SessionId,actor,o.ObservationId,o.Time,o.Position,projection.Candidates.ToArray()),o.Time);
     log(new{kind="local_route_observed",o.ObservationId,known=o.Cells.Count(c=>c.Visibility=="known"),safeCandidates=projection.Candidates.Count,groundPolicy=projection.GroundPolicy.ToString(),historyCount=window.Cells.Count(c=>!c.IsCurrent),maximumAge=projection.MaximumAge,reason=selection.Reason,observedAt=o.Time,origin=o.Position,knownCells=o.Cells.Where(c=>c.Visibility=="known").ToArray()});
     if(selection.Choice is {} choice){route=choice;routeStarted=Stopwatch.GetTimestamp();routeMaxAge=projection.Provenance[choice.EvidenceId].Max(c=>c.Age);routeAir=projection.Evidence[choice.EvidenceId].Where(c=>c.Value.HasValue&&Terrain.ExtractContents(c.Value.Value)==0).Select(c=>(c.X,c.Y,c.Z)).ToHashSet();routeMoves++;scan=0;
      if(IntegrationGate.Mode=="obstacle"&&!obstacleInjected){obstacleInjected=true;int tx=Terrain.ToCell((float)route.Destination.X),ty=Terrain.ToCell((float)route.Destination.Y),tz=Terrain.ToCell((float)route.Destination.Z);terrain.ChangeCell(tx,ty,tz,Terrain.MakeBlockValue(2));terrain.ChangeCell(tx,ty+1,tz,Terrain.MakeBlockValue(2));log(new{kind="dynamic_obstacle_injected",tx,ty,tz,meaning="test-only world changes after snapshot, no food inserted"});}
      routeRun=actions.Start(new MoveTo(new Vector3((float)route.Destination.X,(float)route.Destination.Y,(float)route.Destination.Z),0.25f,4));log(new{kind="local_route_started",route,routeMoves,proofCells=projection.Evidence[choice.EvidenceId].Count,provenance=projection.Provenance[choice.EvidenceId]});stage=14;}
     else if(scan++<8){Call("act_FaceTowards",new{bearing=45,pitch=-40,timeout=4});stage=2;}
     else{log(new{kind="no_safe_route",routeMoves,scan,meaning="unknown terrain is not permission to walk"});Done=true;Passed=IntegrationGate.Mode=="blocked"&&routeMoves==0;}
    }
   }
   else if(scan++<8){Call("act_FaceTowards",new{bearing=45,pitch=-20,timeout=4});stage=2;}
   else if(moves<4){moves++;scan=0;Call("submit_plan",new{steps=new[]{new{tool="act_MoveTo",arguments=new{bearing=0,distance=4,range=0.4,timeout=8},retries=1}}});stage=12;}
   else {log(new{kind="bounded_search_exhausted",moves,scan,position=new[]{p.ComponentBody.Position.X,p.ComponentBody.Position.Y,p.ComponentBody.Position.Z},meaning="no supported food actually perceived; no fabricated success"});Done=true;Passed=false;}
  }else if(stage==2){var status=Call("action_status");if(status.ValueKind!=JsonValueKind.Null&&status.GetProperty("status").GetString()!="running"){log(new{kind="scan_completed",status,look=new[]{p.ComponentLocomotion.LookAngles.X,p.ComponentLocomotion.LookAngles.Y},immersion=p.ComponentBody.ImmersionFactor,verticalSpeed=p.ComponentBody.Velocity.Y});stage=1;}}
  else if(stage==13&&decisions.TryTake(actor,out var idle,out var idleError)){
   Require(idleError is null&&idle?.Skill is "Survey" or "Wait","unexpected no-resource model response: "+idleError+"; "+model.LastFailure+"; "+model.LastResponse);log(new{kind="model_search_goal",decision=idle,metrics=model.LastMetrics,policy="model chooses Survey/FindFood; bounded local skill selects evidence-supported short routes"});if(idle.Skill=="Wait"){Done=true;Passed=false;log(new{kind="model_waited",meaning="no host search overrides model Wait"});}else{initialModelChecked=true;stage=1;}
  }
  else if(stage==14){
   var actual=p.ComponentBody.Position;
   double dx=route.Destination.X-route.Origin.X,dz=route.Destination.Z-route.Origin.Z,length=Math.Sqrt(dx*dx+dz*dz);
   double cross=Math.Abs((actual.X-route.Origin.X)*dz-(actual.Z-route.Origin.Z)*dx)/length;
   double along=((actual.X-route.Origin.X)*dx+(actual.Z-route.Origin.Z)*dz)/(length*length);
   var bounds=p.ComponentBody.BoundingBox;bool withinProof=true;
   for(int x=(int)Math.Floor(bounds.Min.X+1e-5);x<=(int)Math.Floor(bounds.Max.X-1e-5)&&withinProof;x++)for(int z=(int)Math.Floor(bounds.Min.Z+1e-5);z<=(int)Math.Floor(bounds.Max.Z-1e-5)&&withinProof;z++)for(int y=(int)Math.Floor(bounds.Min.Y+1e-5);y<=(int)Math.Floor(bounds.Max.Y-1e-5);y++)if(!routeAir.Contains((x,y,z))){withinProof=false;break;}
   bool expired=routeMaxAge+Stopwatch.GetElapsedTime(routeStarted).TotalSeconds>3;bool violation=expired||!withinProof||cross>0.1||along<-.1||along>1.1||Math.Abs(actual.Y-route.Origin.Y)>0.2||p.ComponentBody.ImmersionFactor>0.1;
   if(violation){if(routeRun.Result.Status==AtomicActionStatus.Running)actions.Cancel(routeRun);log(new{kind="route_safety_stop",expired,withinProof,cross,along,vertical=actual.Y-route.Origin.Y,immersion=p.ComponentBody.ImmersionFactor});}
   if(routeRun.Result.Status==AtomicActionStatus.Running)return;
   var o=senses.Observe(senses.SessionId,actor);var result=routeRun.Result;var outcome=expired?ExplorationOutcome.Interrupted:!violation&&result.Status==AtomicActionStatus.Succeeded?ExplorationOutcome.Arrived:ExplorationOutcome.Blocked;
   exploration.Report(route.ChoiceId,o.Time,o.Position,outcome);if(outcome==ExplorationOutcome.Arrived)routeArrivals++;else routeFailures++;if(result.Failure==AtomicActionFailure.Blocked)blockedFailures++;if(IntegrationGate.Mode=="obstacle"&&routeMoves==1&&!expired&&(result.Failure==AtomicActionFailure.Blocked||violation))obstacleStops++;log(new{kind="local_route_result",choice=route.ChoiceId,status=result.Status.ToString(),failure=result.Failure.ToString(),position=o.Position,outcome=outcome.ToString(),routeMoves});stage=1;if(IntegrationGate.Mode=="obstacle"&&obstacleStops>=1&&routeArrivals>=1){log(new{kind="obstacle_recovery_verified",obstacleStops,blockedFailures,routeFailures,routeArrivals,routeMoves});Done=true;Passed=true;}
  }
  else if(stage==12){var task=Call("task_status");if(Terminal(task)){log(new{kind="exploration_task",task});stage=1;}}
  else if(stage==3&&decisions.TryTake(actor,out var d,out var error)){
   Require(error is null&&d?.Skill=="ForageOne"&&d.Target==target,"model did not select observed forage: "+error);var current=senses.Observe(senses.SessionId,actor);Require(current.Cells.Any(c=>c.X==target.X&&c.Y==target.Y&&c.Z==target.Z&&c.Value==target.ExpectedValue&&c.Visibility=="known"),"target changed during inference");
   var shape=BlocksManager.Blocks[Terrain.ExtractContents(target.ExpectedValue)].GetCustomCollisionBoxes(terrain,target.ExpectedValue).First().Center();face=actions.Start(new FaceTowards(new Vector3(target.X,target.Y,target.Z)+shape));log(new{kind="model_decision",decision=d,currentObservation=current.ObservationId,metrics=model.LastMetrics});stage=4;
  }else if(stage==4&&face.Result.Status!=AtomicActionStatus.Running){
   Require(face.Result.Status==AtomicActionStatus.Succeeded,"3D facing failed: "+face.Result);var o=Call("observe",new{look_distance=5,object_distance=5,limit=24});var focus=o.GetProperty("focus");var eyeFocus=actions.Queries.LookAhead(5);log(new{kind="aim_verified",observation=o,eye=new[]{p.ComponentCreatureModel.EyePosition.X,p.ComponentCreatureModel.EyePosition.Y,p.ComponentCreatureModel.EyePosition.Z},look=new[]{p.ComponentLocomotion.LookAngles.X,p.ComponentLocomotion.LookAngles.Y},cell=eyeFocus?.Cell.ToString()});Require(eyeFocus?.Cell==new Point3(target.X,target.Y,target.Z)&&eyeFocus.BlockValue.HasValue&&Terrain.ReplaceLight(eyeFocus.BlockValue.Value,0)==Terrain.ReplaceLight(target.ExpectedValue,0),"focused resource is not the selected target");Require(focus.ValueKind==JsonValueKind.Object&&Terrain.ExtractContents(focus.GetProperty("blockValue").GetInt32())==PumpkinBlock.Index,"main eye ray did not observe pumpkin after actual facing");targetRef=focus.GetProperty("reference").GetString();
   Call("submit_plan",new{steps=new object[]{new{tool="act_MoveTo",arguments=new{destination=targetRef,range=1.5,timeout=12},retries=1},new{tool="act_Dig",arguments=new{target=targetRef,timeout=15},minimum_affected=1}}});log(new{kind="shared_plan_started",observation=o});stage=5;
  }else if(stage==5){var task=Call("task_status");if(Terminal(task)){log(new{kind="harvest_plan_result",task});Require(task.GetProperty("status").GetString()=="succeeded"&&p.PlayerStats.BlocksDug==1,"reused main harvest task failed");Call("act_MoveTo",new{bearing=0,distance=1,range=0.3,timeout=5});elapsed=0;stage=6;}}
  else if(stage==6){var status=Call("action_status");if(status.ValueKind==JsonValueKind.Null||status.GetProperty("status").GetString()=="running")return;
   var inv=p.ComponentMiner.Inventory;int slot=Enumerable.Range(0,inv.SlotsCount).FirstOrDefault(i=>inv.GetSlotCount(i)>0&&Terrain.ExtractContents(inv.GetSlotValue(i))==PumpkinBlock.Index,-1);
   if(slot<0){Require(elapsed<6,"natural pickup not observed");return;}foodSlot=slot;foodValue=inv.GetSlotValue(slot);log(new{kind="native_pickup_confirmed",slot,value=foodValue,count=inv.GetSlotCount(slot)});
   Call("submit_plan",new{steps=new object[]{new{tool="act_Wait",arguments=new{duration=0.1}},new{tool="act_Eat",arguments=new{slot,value=foodValue,count=1},minimum_affected=1}}});stage=7;
  }
  else if(stage==90){var task=Call("task_status");if(Terminal(task)){Require(task.GetProperty("status").GetString()=="succeeded"&&task.GetProperty("taskId").GetGuid()==restoredTask&&p.PlayerStats.FoodItemsEaten==initialEaten+1&&p.ComponentVitalStats.Food>restoreFood&&Enumerable.Range(0,p.ComponentMiner.Inventory.SlotsCount).Sum(p.ComponentMiner.Inventory.GetSlotCount)==initialItems-1,"restore meal outcome failed");Require(!tools.RestoreTaskCheckpoint(),"checkpoint replay accepted");log(new{kind="restored_meal_verified",task,foodBefore=restoreFood,foodAfter=p.ComponentVitalStats.Food,eaten=p.PlayerStats.FoodItemsEaten-initialEaten,itemsBefore=initialItems,itemsAfter=Enumerable.Range(0,p.ComponentMiner.Inventory.SlotsCount).Sum(p.ComponentMiner.Inventory.GetSlotCount)});Done=true;Passed=true;}}
 }catch(Exception e){Done=true;Passed=false;log(new{kind="scenario_error",error=e.ToString()});}}
 public void Dispose(){decisions.Dispose();p.Project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(senses);senses.Dispose();interests.Release();backend.Dispose();}
}
