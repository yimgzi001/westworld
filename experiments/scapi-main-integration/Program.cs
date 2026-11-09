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
  if(args.Length is <1 or >2||args[0] is not ("fixture" or "terraced" or "natural" or "terraced" or "blocked" or "obstacle" or "reload")||!Root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar))return 2;Mode=args[0];if(args.Length==2){if(args[1].Length>64||!args[1].All(c=>char.IsLetterOrDigit(c)||c=='-'))return 2;Seed=args[1];}
  if(Mode!="reload"&&(Directory.Exists(Path.Combine(Root,"doc"))||Directory.Exists(Path.Combine(Root,"Mods"))))return 2;
  if(Mode=="reload"&&!File.Exists(Path.Combine(Root,"integration.scworld")))return 2;
  Log(new{kind="start",mode=Mode,seed=Seed,fixture=Mode is "fixture" or "terraced" or "blocked" or "obstacle",models=Mode=="reload"?0:1});Window.Created+=()=>Window.Frame+=Frame;
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
 readonly LlamaProcessProvider model;readonly AsyncDecisionScheduler decisions;readonly LocalExploration exploration;readonly LocalObservationMemory localMemory;long routeStarted;double routeMaxAge;ExplorationChoice route;ObservedStepRoute stepRoute;ObservedAlignmentRoute alignment;AtomicActionRun alignmentRun;NavigationSettling settling;readonly NavigationLookPlan lookPlan=new(16);double navTime,totalHorizontalPath;Vector3 previousMeasured;bool havePrevious;long alignmentStarted;string lastAlignedCell;int proofRefreshes;HashSet<(int X,int Y,int Z)> routeAir=new();AtomicActionRun routeRun;bool initialModelChecked,obstacleInjected;int routeMoves,routeArrivals,routeFailures,blockedFailures,obstacleStops;readonly bool reload;readonly float restoreFood;readonly long initialEaten;readonly int initialItems;
 int stage,scan,moves;double elapsed;CellTarget target;AtomicActionRun face;string targetRef;int foodSlot,foodValue;Guid restoredTask;Vector3 original;
 public bool Done{get;private set;}public bool Passed{get;private set;}public bool CheckpointReady { get { if(stage!=7)return false;var task=Call("task_status");return task.GetProperty("status").GetString()=="waiting"&&task.GetProperty("currentStep").GetInt32()==1; } }
 public Scenario(ComponentPlayer p,Action<object> log,bool reload){this.p=p;this.log=log;this.reload=reload;original=p.ComponentBody.Position;terrain=p.Project.FindSubsystem<SubsystemTerrain>(true);senses=new NativeActorSession(p.Project,Guid.NewGuid(),actionsEnabled:false,observationAbove:3);senses.Bind(actor,p);p.Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(senses);backend=new ScapiInterestBackend(terrain,p.Project.FindSubsystem<SubsystemSpawn>(true));interests=new InterestCoordinator(senses.Registry,backend,new InterestRadii(16,32));interests.Sync();terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);
  bool rejected=false;try{senses.Submit(new(senses.SessionId,actor,Guid.NewGuid(),Guid.NewGuid(),0,0.2f,0.1f));}catch(InvalidOperationException e){rejected=e.Message.StartsWith("Observation-only");}Require(rejected,"read-only observer accepted a competing movement command");
  localMemory=new LocalObservationMemory(senses.SessionId,actor);exploration=new LocalExploration(senses.SessionId,actor);actions=new AtomicActions(p.Entity);tools=new ActorTools(actions,false);initialEaten=p.PlayerStats.FoodItemsEaten;restoreFood=p.ComponentVitalStats.Food;initialItems=Enumerable.Range(0,p.ComponentMiner.Inventory.SlotsCount).Sum(p.ComponentMiner.Inventory.GetSlotCount);
  var bodySize=p.ComponentBody.BoundingBox.Max-p.ComponentBody.BoundingBox.Min;Require(bodySize.X<=.50001f&&bodySize.Z<=.50001f&&bodySize.Y<=1.90001f,"route projection requires ordinary player body");
  var local=Path.GetFullPath(Path.Combine(IntegrationGate.Root,"..","local-model"));Func<int,bool> harvest=v=>Terrain.ExtractContents(v)==PumpkinBlock.Index&&BasePumpkinBlock.GetSize(Terrain.ExtractData(v))==7;bool survey=IntegrationGate.Mode is "natural" or "terraced" or "blocked" or "obstacle";model=new(Path.Combine(local,"llama-b12211","llama-completion"),Path.Combine(local,"Qwen3.5-4B-Q4_K_M.gguf"),harvest,allowSurvey:survey);decisions=new(model,TimeSpan.FromSeconds(65),1,allowSurvey:survey,supportedSurveyHarvest:harvest);
  if(reload){Require(p.Project.FindSubsystem<SubsystemTime>(true).GameTime==0,"restore must precede first world update");Require(tools.RestoreTaskCheckpoint(),"checkpoint did not bind");var status=Call("task_status");restoredTask=status.GetProperty("taskId").GetGuid();log(new{kind="restored",task=status,food=restoreFood,entity=p.Entity.Id,external=p.PlayerData.IsExternalActor,views=p.Project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count});stage=90;}
  log(new{kind="bound",controlOwner="main AtomicActions/ActorTools only",capabilities=actions.GetCapabilities().Count,completeVitalStats=p.ComponentVitalStats is not null,views=p.Project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count});
 }
 static void Require(bool c,string why){if(!c)throw new InvalidOperationException(why);}
 JsonElement Call(string name,object args=null){using var d=JsonDocument.Parse(tools.InvokeJson(name,JsonSerializer.Serialize(args??new{})));Require(d.RootElement.GetProperty("ok").GetBoolean(),name+" rejected: "+d.RootElement);return d.RootElement.GetProperty("data").Clone();}
 static bool Terminal(JsonElement task)=>task.GetProperty("status").GetString() is "succeeded" or "failed" or "cancelled";
 public void Save(){tools.SaveTaskCheckpoint("app:integration.scworld");log(new{kind="checkpoint",task=Call("task_status"),food=p.ComponentVitalStats.Food,eaten=p.PlayerStats.FoodItemsEaten,inventory=Call("read_inventory")});}
 public void Update(float dt){if(Done)return;try{
  elapsed+=dt;navTime+=dt;if(havePrevious)totalHorizontalPath+=(p.ComponentBody.Position-previousMeasured).XZ.Length();previousMeasured=p.ComponentBody.Position;havePrevious=true;interests.Sync();Require(p.GameWidget is null&&p.ComponentHealth.Health>0,"native actor lost viewless survival state");
  if(stage==0&&elapsed>=3){
   original=p.ComponentBody.Position;
   var owned=p.ComponentMiner.Inventory;for(int i=0;i<owned.SlotsCount;i++)owned.RemoveSlotItems(i,owned.GetSlotCount(i));log(new{kind="initial_condition",starterInventoryCleared=true,worldResourcesPlaced=IntegrationGate.Mode is "fixture" or "terraced"});
   if(IntegrationGate.Mode=="fixture"){
    int x=Terrain.ToCell(original.X),y=Terrain.ToCell(original.Y),z=Terrain.ToCell(original.Z);for(int a=x-5;a<=x+5;a++)for(int b=z-5;b<=z+5;b++){terrain.ChangeCell(a,y-1,b,Terrain.MakeBlockValue(2));for(int c=y;c<=y+3;c++)terrain.ChangeCell(a,c,b,0);}p.ComponentBody.Position=new Vector3(x+0.5f,y,z+0.5f);p.ComponentBody.Velocity=Vector3.Zero;
    var forward=p.ComponentBody.Matrix.Forward;int tx=x+(MathF.Abs(forward.X)>MathF.Abs(forward.Z)?Math.Sign(forward.X)*3:0),tz=z+(MathF.Abs(forward.X)>MathF.Abs(forward.Z)?0:Math.Sign(forward.Z)*3);terrain.ChangeCell(tx,y,tz,Terrain.MakeBlockValue(PumpkinBlock.Index));
    var inventory=p.ComponentMiner.Inventory;for(int i=0;i<inventory.SlotsCount;i++)inventory.RemoveSlotItems(i,inventory.GetSlotCount(i));log(new{kind="artificial_fixture",nativePlant=true,inventoryCleared=true});
   }
   if(IntegrationGate.Mode=="terraced"){
    int x=Terrain.ToCell(original.X),y=Terrain.ToCell(original.Y),z=Terrain.ToCell(original.Z);
    for(int a=x-4;a<=x+4;a++)for(int b=z-4;b<=z+5;b++)for(int c=y-3;c<=y+5;c++)terrain.ChangeCell(a,c,b,3);
    for(int c=y;c<=y+4;c++)terrain.ChangeCell(x,c,z,0);
    for(int b=z;b<=z+3;b++)for(int c=y+1;c<=y+5;c++)terrain.ChangeCell(x+1,c,b,0);
    terrain.ChangeCell(x+1,y,z+3,DirtBlock.Index);terrain.ChangeCell(x+1,y+1,z+3,Terrain.MakeBlockValue(PumpkinBlock.Index));
    p.ComponentBody.Position=new(x+.5f,y,z+.68f);p.ComponentBody.Velocity=Vector3.Zero;
    log(new{kind="terraced_fixture",source="artificial full-chain positive control, not general food strategy",expected="align -> observe -> up-step -> observe resource -> native harvest/pickup/eat",placedFood=true});
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
   }original=p.ComponentBody.Position;previousMeasured=original;totalHorizontalPath=0;stage=1;
  }else if(stage==1&&elapsed>=4){
   if(p.ComponentBody.ParentBody is not null){log(new{kind="navigation_mode_unavailable",mode="mounted",parentTemplate=p.ComponentBody.ParentBody.Entity.ValuesDictionary.DatabaseObject.Name,reason="land Survey requires ground actor; no observed safe dismount policy yet",actualParent=true});Done=true;Passed=false;return;}
   var o=senses.Observe(senses.SessionId,actor);var window=localMemory.Observe(o);
   var visible=actions.Queries.FindVisibleObjects(5,24);log(new{kind="food_environment_observed",o.ObservationId,o.Time,entities=visible.Entities.Select(e=>new{e.EntityId,e.Name,position=e.Position.ToString(),e.Distance}),drops=visible.Pickables.Select(d=>new{d.Value,d.Count,d.Distance}),meaning="true local evidence for general food strategy; current executable plant chain remains pumpkin only"});
   if(o.Food<.8f&&o.Inventory.Any(i=>i.CanEat&&i.Count>0)){Require(decisions.Request(o,out var inventoryError),inventoryError);log(new{kind="inventory_food_available",o.ObservationId,items=o.Inventory.Where(i=>i.CanEat&&i.Count>0).ToArray(),meaning="actual native inventory; prefer eating over continuing exploration"});stage=18;return;}
   var found=o.Cells.FirstOrDefault(c=>c.Visibility=="known"&&c.Value.HasValue&&Terrain.ExtractContents(c.Value.Value)==PumpkinBlock.Index&&BasePumpkinBlock.GetSize(Terrain.ExtractData(c.Value.Value))==7);
   if(found is not null){target=new(found.X,found.Y,found.Z,found.Value.Value);Require(decisions.Request(o,out var error),error);log(new{kind="resource_observed",observation=o.ObservationId,target,moves,scan,natural=IntegrationGate.Mode=="natural"});stage=3;}
   else if(IntegrationGate.Mode is "natural" or "terraced" or "blocked" or "obstacle"){
    if(!initialModelChecked){Require(decisions.Request(o,out var error),error);stage=13;}
    else if(routeMoves>=64){log(new{kind="bounded_search_exhausted",routeMoves,scan,meaning="bounded safe exploration did not find supported food"});Done=true;Passed=false;}
    else {
     var projection=LocalRouteCandidates.Project(window,1,RouteGroundPolicy.ObservedFullCube);var steps=ObservedStepRoutes.Project(window);var candidates=projection.Candidates.Concat(steps.Select(s=>new ExplorationCandidate(s.Id,s.Destination,true))).ToArray();var centre=LocalNavigationAlignment.Project(window);string cellKey=$"{Math.Floor(o.Position.X)},{Math.Floor(o.Position.Y)},{Math.Floor(o.Position.Z)}";
     bool raisedKnown=window.Cells.Any(c=>c.Y==(int)Math.Round(o.Position.Y)&&(c.Value&1023) is 1 or 2 or 3 or 4 or 5 or 8&&Math.Abs(c.X-Math.Floor(o.Position.X))+Math.Abs(c.Z-Math.Floor(o.Position.Z))==1);
     if(centre is not null&&lastAlignedCell!=cellKey&&(raisedKnown||candidates.Length==0)){if(centre.MaximumAge>1.5){RefreshProof(centre.Evidence);return;}alignment=centre;lastAlignedCell=cellKey;alignmentStarted=Stopwatch.GetTimestamp();alignmentRun=actions.Start(new MoveTo(new((float)centre.Destination.X,(float)centre.Destination.Y,(float)centre.Destination.Z),.035f,3));log(new{kind="alignment_started",alignment,raisedKnown});stage=16;return;}
     var selection=exploration.Select(new(senses.SessionId,actor,o.ObservationId,o.Time,o.Position,candidates),o.Time);
     log(new{kind="local_route_observed",o.ObservationId,known=o.Cells.Count(c=>c.Visibility=="known"),safeCandidates=candidates.Length,stepCandidates=steps.Count,groundPolicy=projection.GroundPolicy.ToString(),historyCount=window.Cells.Count(c=>!c.IsCurrent),maximumAge=projection.MaximumAge,reason=selection.Reason,observedAt=o.Time,origin=o.Position,knownCells=o.Cells.Where(c=>c.Visibility=="known").ToArray()});
     if(selection.Choice is {} choice){
      if(choice.PriorVisits>0&&scan<16){var eye=p.ComponentCreatureModel.EyePosition;var look=lookPlan.Next(window,new(eye.X,eye.Y,eye.Z),MissingNavigationCells(window));
       if(look.Look is {} request){exploration.Report(choice.ChoiceId,o.Time,o.Position,ExplorationOutcome.Interrupted);scan++;face=actions.Start(new FaceTowards(new((float)request.Target.X,(float)request.Target.Y,(float)request.Target.Z),4,(float)Math.Clamp(request.PitchRadians*180/Math.PI,-75,75)));log(new{kind="active_look_before_backtracking",look});stage=17;return;}}
      var selectedStep=steps.FirstOrDefault(s=>s.Id==choice.EvidenceId);var proof=selectedStep?.Evidence??projection.Provenance[choice.EvidenceId];if(proof.Max(c=>c.Age)>(selectedStep is null?2:1.25)){exploration.Report(choice.ChoiceId,o.Time,o.Position,ExplorationOutcome.Interrupted);RefreshProof(proof);return;}proofRefreshes=0;route=choice;routeStarted=Stopwatch.GetTimestamp();stepRoute=selectedStep;routeMaxAge=proof.Max(c=>c.Age);routeAir=proof.Where(c=>(c.Value&1023)==0).Select(c=>(c.X,c.Y,c.Z)).ToHashSet();routeMoves++;scan=0;
      if(IntegrationGate.Mode=="obstacle"&&!obstacleInjected){obstacleInjected=true;int tx=Terrain.ToCell((float)route.Destination.X),ty=Terrain.ToCell((float)route.Destination.Y),tz=Terrain.ToCell((float)route.Destination.Z);terrain.ChangeCell(tx,ty,tz,Terrain.MakeBlockValue(2));terrain.ChangeCell(tx,ty+1,tz,Terrain.MakeBlockValue(2));log(new{kind="dynamic_obstacle_injected",tx,ty,tz,meaning="test-only world changes after snapshot, no food inserted"});}
      var destination=new Vector3((float)route.Destination.X,(float)route.Destination.Y,(float)route.Destination.Z);routeRun=actions.Start(stepRoute is null?new MoveTo(destination,.25f,4):new StepTo(destination));log(new{kind="local_route_started",route,routeKind=stepRoute?.Kind.ToString()??"Flat",routeMoves,proofCells=proof.Count,provenance=proof});stage=14;}
     else if(scan++<16){var eye=p.ComponentCreatureModel.EyePosition;var look=lookPlan.Next(window,new(eye.X,eye.Y,eye.Z),MissingNavigationCells(window));log(new{kind="active_look",look});
      if(look.Look is {} request){float pitch=(float)Math.Clamp(request.PitchRadians*180/Math.PI,-75,75);face=actions.Start(new FaceTowards(new((float)request.Target.X,(float)request.Target.Y,(float)request.Target.Z),4,pitch));stage=17;}
      else{log(new{kind="no_safe_route",routeMoves,scan,reason=look.Reason});Done=true;Passed=IntegrationGate.Mode=="blocked"&&routeMoves==0;}}
     else{log(new{kind="no_safe_route",routeMoves,scan,meaning="unknown terrain is not permission to walk"});Done=true;Passed=IntegrationGate.Mode=="blocked"&&routeMoves==0;}
    }
   }
   else if(scan++<8){Call("act_FaceTowards",new{bearing=45,pitch=-20,timeout=4});stage=2;}
   else if(moves<4){moves++;scan=0;Call("submit_plan",new{steps=new[]{new{tool="act_MoveTo",arguments=new{bearing=0,distance=4,range=0.4,timeout=8},retries=1}}});stage=12;}
   else {log(new{kind="bounded_search_exhausted",moves,scan,position=new[]{p.ComponentBody.Position.X,p.ComponentBody.Position.Y,p.ComponentBody.Position.Z},meaning="no supported food actually perceived; no fabricated success"});Done=true;Passed=false;}
  }else if(stage==2){var status=Call("action_status");if(status.ValueKind!=JsonValueKind.Null&&status.GetProperty("status").GetString()!="running"){log(new{kind="scan_completed",status,look=new[]{p.ComponentLocomotion.LookAngles.X,p.ComponentLocomotion.LookAngles.Y},immersion=p.ComponentBody.ImmersionFactor,verticalSpeed=p.ComponentBody.Velocity.Y,embeddedInIce=p.ComponentBody.IsEmbeddedInIce,stunTime=p.ComponentLocomotion.StunTime,disableInputMovement=p.ComponentLocomotion.DisableInputMovement,lookSpeed=p.ComponentLocomotion.LookSpeed,hasParentBody=p.ComponentBody.ParentBody is not null,parentTemplate=p.ComponentBody.ParentBody?.Entity.ValuesDictionary.DatabaseObject.Name});stage=1;}}
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
   StepEnvelopeCheck stepCheck=null;
   if(stepRoute is not null){stepCheck=ObservedStepGuard.Check(stepRoute,new(bounds.Min.X,bounds.Min.Y,bounds.Min.Z),new(bounds.Max.X,bounds.Max.Y,bounds.Max.Z),p.ComponentBody.IsSmoothRiseEnabled,p.ComponentBody.MaxSmoothRiseHeight);withinProof=stepCheck.Inside;}
   bool expired=routeMaxAge+Stopwatch.GetElapsedTime(routeStarted).TotalSeconds>3;
   bool movementLimit=stepRoute is null?(cross>.1||along<-.1||along>1.1||Math.Abs(actual.Y-route.Origin.Y)>.2):(cross>.25||along<-.2||along>1.3);
   bool violation=expired||!withinProof||movementLimit||p.ComponentBody.ImmersionFactor>.1;
   if(violation){if(routeRun.Result.Status==AtomicActionStatus.Running)actions.Cancel(routeRun);log(new{kind="route_safety_stop",routeKind=stepRoute?.Kind.ToString()??"Flat",stepCheck,expired,withinProof,cross,along,vertical=actual.Y-route.Origin.Y,immersion=p.ComponentBody.ImmersionFactor});}
   if(routeRun.Result.Status==AtomicActionStatus.Running)return;
   var o=senses.Observe(senses.SessionId,actor);var result=routeRun.Result;var outcome=expired?ExplorationOutcome.Interrupted:!violation&&result.Status==AtomicActionStatus.Succeeded?ExplorationOutcome.Arrived:ExplorationOutcome.Blocked;
   exploration.Report(route.ChoiceId,o.Time,o.Position,outcome);if(outcome==ExplorationOutcome.Arrived)routeArrivals++;else routeFailures++;if(result.Failure==AtomicActionFailure.Blocked)blockedFailures++;if(IntegrationGate.Mode=="obstacle"&&routeMoves==1&&!expired&&(result.Failure==AtomicActionFailure.Blocked||violation))obstacleStops++;log(new{kind="local_route_result",choice=route.ChoiceId,routeKind=stepRoute?.Kind.ToString()??"Flat",stepCheck,status=result.Status.ToString(),failure=result.Failure.ToString(),position=o.Position,outcome=outcome.ToString(),routeMoves,totalHorizontalPath,netHorizontal=(p.ComponentBody.Position-original).XZ.Length()});stage=1;if(outcome!=ExplorationOutcome.Arrived)BeginSettling("route_interrupted");if(IntegrationGate.Mode=="obstacle"&&obstacleStops>=1&&routeArrivals>=1){log(new{kind="obstacle_recovery_verified",obstacleStops,blockedFailures,routeFailures,routeArrivals,routeMoves});Done=true;Passed=true;}
  }
  else if(stage==18&&decisions.TryTake(actor,out var meal,out var mealError)){
   Require(mealError is null&&meal?.Skill=="EatOne","inventory decision did not select EatOne: "+mealError);
   var current=senses.Observe(senses.SessionId,actor);Require(current.Inventory.Any(i=>i.Slot==meal.Slot&&i.Value==meal.ExpectedValue&&i.Count>0&&i.CanEat),"food changed during decision");
   log(new{kind="food_strategy_selected",strategy="EatInventory",decision=meal,metrics=model.LastMetrics,observation=current.ObservationId});
   Call("submit_plan",new{steps=new object[]{new{tool="act_Wait",arguments=new{duration=.1}},new{tool="act_Eat",arguments=new{slot=meal.Slot,value=meal.ExpectedValue,count=1},minimum_affected=1}}});stage=7;
  }
  else if(stage==17&&face.Result.Status!=AtomicActionStatus.Running){log(new{kind="active_look_result",status=face.Result.Status.ToString(),failure=face.Result.Failure.ToString()});stage=1;}
  else if(stage==16){var box=p.ComponentBody.BoundingBox;var air=alignment.Evidence.Where(c=>(c.Value&1023)==0).Select(c=>(c.X,c.Y,c.Z)).ToHashSet();bool inside=true;
   for(int x=(int)Math.Floor(box.Min.X+1e-5);x<=(int)Math.Floor(box.Max.X-1e-5)&&inside;x++)for(int z=(int)Math.Floor(box.Min.Z+1e-5);z<=(int)Math.Floor(box.Max.Z-1e-5)&&inside;z++)for(int y=(int)Math.Floor(box.Min.Y+1e-5);y<=(int)Math.Floor(box.Max.Y-1e-5);y++)if(!air.Contains((x,y,z))){inside=false;break;}
   bool expired=alignment.MaximumAge+Stopwatch.GetElapsedTime(alignmentStarted).TotalSeconds>3;
   if((!inside||expired||p.ComponentBody.ImmersionFactor>.1)&&alignmentRun.Result.Status==AtomicActionStatus.Running)actions.Cancel(alignmentRun);
   if(alignmentRun.Result.Status!=AtomicActionStatus.Running){log(new{kind="alignment_result",status=alignmentRun.Result.Status.ToString(),failure=alignmentRun.Result.Failure.ToString(),inside,expired,actual=p.ComponentBody.Position.ToString()});BeginSettling("alignment_finished");}
  }
  else if(stage==15){var v=p.ComponentBody.Velocity;var state=settling.Update(new(navTime,p.ComponentHealth.Health>0,p.ComponentBody.StandingOnValue.HasValue,p.ComponentBody.ParentBody is not null,p.ComponentBody.ImmersionFactor,v.X,v.Y,v.Z));
   if(state.Status!=NavigationSettlingStatus.Waiting){log(new{kind="navigation_settled",state,position=p.ComponentBody.Position.ToString()});if(state.Status==NavigationSettlingStatus.Settled)stage=1;else{Done=true;Passed=false;}}
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
 void RefreshProof(IReadOnlyList<LocalObservedCell> proof){
  if(proofRefreshes++>=8){log(new{kind="proof_refresh_exhausted",proofRefreshes});Done=true;Passed=false;return;}
  var cell=proof.OrderByDescending(c=>c.Age).First();var target=new Vector3(cell.X+.5f,cell.Y+.5f,cell.Z+.5f);var d=target-p.ComponentCreatureModel.EyePosition;float pitch=Math.Clamp(MathF.Atan2(d.Y,d.XZ.Length())*180/MathF.PI,-75,75);
  face=actions.Start(new FaceTowards(target,4,pitch));log(new{kind="proof_refresh",cell,proofRefreshes,meaning="reobserve old real evidence before starting; not extending TTL"});stage=17;
 }
 void BeginSettling(string reason){settling=new NavigationSettling(navTime);stage=15;log(new{kind="navigation_stopping",reason,navTime});}
 static IReadOnlyList<NavigationMissingCell> MissingNavigationCells(LocalObservationWindow w){
  int fx=(int)Math.Floor(w.Position.X),fy=(int)Math.Round(w.Position.Y),fz=(int)Math.Floor(w.Position.Z);var needed=new List<NavigationMissingCell>();
  int minX=(int)Math.Floor(w.Position.X-.25+1e-5),maxX=(int)Math.Floor(w.Position.X+.25-1e-5),minZ=(int)Math.Floor(w.Position.Z-.25+1e-5),maxZ=(int)Math.Floor(w.Position.Z+.25-1e-5);
  for(int x=minX;x<=maxX;x++)for(int z=minZ;z<=maxZ;z++){needed.Add(new(x,fy-1,z,NavigationLookStage.CurrentSupport));for(int y=fy;y<=fy+1;y++)needed.Add(new(x,y,z,NavigationLookStage.CurrentBody));}
  bool upSeen=false;var known=w.Cells.ToDictionary(c=>(c.X,c.Y,c.Z));
  foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)}) {
   int x=fx+dx,z=fz+dz;bool raised=known.TryGetValue((x,fy,z),out var riser)&&(riser.Value&1023) is 1 or 2 or 3 or 4 or 5 or 8;
   bool lower=known.TryGetValue((x,fy-1,z),out var floor)&&(floor.Value&1023)==0;
   if(raised){upSeen=true;for(int y=fy+1;y<=fy+3;y++)needed.Add(new(x,y,z,NavigationLookStage.HeadClearance));}
   else {needed.Add(new(x,lower?fy-2:fy-1,z,NavigationLookStage.ForwardSupport));for(int y=lower?fy-1:fy;y<=fy+1;y++)needed.Add(new(x,y,z,NavigationLookStage.ForwardSupport));}
  }
  if(upSeen)for(int y=fy+2;y<=fy+3;y++)needed.Add(new(fx,y,fz,NavigationLookStage.HeadClearance));
  return needed.Where(c=>c.Y>=0&&c.Y<256&&Math.Pow(c.X+.5-w.Position.X,2)+Math.Pow(c.Y+.5-w.Position.Y,2)+Math.Pow(c.Z+.5-w.Position.Z,2)<=25).DistinctBy(c=>(c.X,c.Y,c.Z)).ToArray();
 }
 public void Dispose(){log(new{kind="path_metrics",totalHorizontalPath,netHorizontal=(p.ComponentBody.Position-original).XZ.Length(),meaning="sum of actual per-update horizontal body displacement; includes passive drift, not command lengths"});if(alignmentRun?.Result.Status==AtomicActionStatus.Running)actions.Cancel(alignmentRun);if(routeRun?.Result.Status==AtomicActionStatus.Running)actions.Cancel(routeRun);if(face?.Result.Status==AtomicActionStatus.Running)actions.Cancel(face);p.Project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(this);decisions.Dispose();p.Project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(senses);senses.Dispose();interests.Release();backend.Dispose();}
}
