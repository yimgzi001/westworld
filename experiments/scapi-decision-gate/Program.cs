using Engine;
using Game;
using System.Diagnostics;
using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

internal static class DecisionGate {
 static readonly string root=AppContext.BaseDirectory;static readonly Stopwatch clock=Stopwatch.StartNew();
 static bool created,finished,passed;static Scenario scenario;static NativeActorSession session;static SubsystemUpdate updates;
 static void Log(object value){string s=JsonSerializer.Serialize(value);Console.WriteLine(s);File.AppendAllText(Path.Combine(root,"decision.jsonl"),s+Environment.NewLine);}
 static int Main(string[] args){
  if(args.Length!=1||args[0]!="--new-disposable-world"||!root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar)||Directory.Exists(Path.Combine(root,"doc"))||Directory.Exists(Path.Combine(root,"Mods")))return 2;
  Log(new{kind="start",scripted=true,models=0});Window.Created+=()=>Window.Frame+=Frame;
  try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}return passed?0:1;
 }
 static void Frame(){try{
  if(finished)return;if(clock.Elapsed.TotalSeconds>90){Finish(false,"timeout");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){created=true;SettingsManager.VisibilityRange=32;var w=WorldsManager.CreateWorld(new WorldSettings{Name="Westworld async hunger decision gate",Seed="actors-cloud-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});ScreensManager.SwitchScreen("GameLoading",w,null);}
  if(GameManager.Project is null)return;
  updates??=GameManager.Project.FindSubsystem<SubsystemUpdate>(true);if(updates.UpdateFailureCount!=0)throw new InvalidOperationException(updates.LastUpdateFailure);
  var players=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);
  if(players.PlayersData.Count==0){players.AddPlayerData(new PlayerData(GameManager.Project){Name="IntentActor",IsExternalActor=true});ScreensManager.SwitchScreen("Game");}
  if(scenario is null&&players.ComponentPlayers.Count==1&&players.PlayersData[0].IsReadyForPlaying){
   var p=players.ComponentPlayers[0];session=new NativeActorSession(GameManager.Project,Guid.NewGuid());var id=Guid.NewGuid();session.Bind(id,p);scenario=new Scenario(session,id,p,Log);updates.AddUpdateable(session);updates.AddUpdateable(scenario);
  }
  if(scenario?.Done==true)Finish(scenario.Passed,"scenario_finished");
 }catch(Exception e){Log(new{kind="failure",error=e.ToString()});Finish(false,"exception");}}
 static void Finish(bool success,string reason){if(finished)return;finished=true;passed=success;try{Log(new{kind="result",passed,reason,seconds=clock.Elapsed.TotalSeconds,updateFailures=updates?.UpdateFailureCount});session?.Dispose();scenario?.Dispose();}finally{Window.Close();}}
}
// Deliberately a deterministic delayed substitute, not a language model.
internal sealed class DelayedHungerPolicy:IDecisionProvider {
 public async Task<HighLevelDecision> DecideAsync(ActorObservation o,CancellationToken cancellation){
  await Task.Delay(1200,cancellation);
  var food=o.Inventory.Where(i=>i.CanEat&&i.Count>0&&i.Nutrition>0).OrderByDescending(i=>i.Nutrition).FirstOrDefault();
  return o.Food<0.8f&&food is not null
   ?new(o.SessionId,o.ActorId,o.ObservationId,Guid.NewGuid(),"SatisfyHunger","EatOne",food.Slot,food.Value)
   :new(o.SessionId,o.ActorId,o.ObservationId,Guid.NewGuid(),"Observe","Wait",-1,0);
 }
}
internal sealed class Scenario:IUpdateable,IDisposable {
 public float FloatUpdateOrder=>-14;
 readonly NativeActorSession session;readonly Guid actor;readonly ComponentPlayer native;readonly Action<object> log;readonly ScapiInterestBackend backend;readonly InterestCoordinator interests;
 readonly AsyncDecisionScheduler scheduler=new(new DelayedHungerPolicy(),TimeSpan.FromSeconds(2));readonly Stopwatch wall=Stopwatch.StartNew();
 int stage,pendingTicks;double elapsed,requestTime,maxRequestMilliseconds;Position start;float initialFood;long initialEaten;double progressWhileWaiting;MoveIntent move;HighLevelDecision decision;EatResult eaten;
 public bool Done{get;private set;}public bool Passed{get;private set;}
 public Scenario(NativeActorSession s,Guid id,ComponentPlayer p,Action<object> log){session=s;actor=id;native=p;this.log=log;var project=p.Project;backend=new ScapiInterestBackend(project.FindSubsystem<SubsystemTerrain>(true),project.FindSubsystem<SubsystemSpawn>(true));interests=new InterestCoordinator(s.Registry,backend,new InterestRadii(16,32));interests.Sync();project.FindSubsystem<SubsystemTerrain>(true).TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);}
 static void Require(bool value,string why){if(!value)throw new InvalidOperationException(why);}
 void Request(ActorObservation o){var sw=Stopwatch.StartNew();Require(scheduler.Request(o,out var error),"Decision request rejected: "+error);maxRequestMilliseconds=Math.Max(maxRequestMilliseconds,sw.Elapsed.TotalMilliseconds);requestTime=wall.Elapsed.TotalSeconds;}
 public void Update(float dt){if(Done)return;try{
  elapsed+=dt;interests.Sync();Require(native.GameWidget is null,"Unexpected AI view");
  if(stage==0&&elapsed>=3){
   var o=session.Observe(session.SessionId,actor);Require(o.Food<0.8f&&o.Inventory.Any(i=>i.CanEat),"Native initial hunger/food fixture absent");
   start=o.Position;initialFood=o.Food;initialEaten=native.PlayerStats.FoodItemsEaten;
   move=new(session.SessionId,actor,Guid.NewGuid(),o.ObservationId,0,0.4f,0.6f);session.Submit(move);Request(o);
   log(new{kind="decision_requested",provider="deterministic delayed substitute",models=0,food=o.Food,candidates=o.Inventory.Where(i=>i.CanEat).ToArray(),skillContinues="bounded native move"});stage=1;
  }else if(stage==1){
   pendingTicks++;progressWhileWaiting=Math.Max(progressWhileWaiting,Vector2.Distance(native.ComponentBody.Position.XZ,new Vector2((float)start.X,(float)start.Z)));
   if(scheduler.TryTake(actor,out var d,out var error)){
    Require(error is null&&d is not null,"Decision failed: "+error);decision=d;Require(decision.Goal=="SatisfyHunger"&&decision.Skill=="EatOne","Policy did not select observed survival need");
    log(new{kind="decision_received",decision,pendingTicks,progressWhileWaiting,latency=wall.Elapsed.TotalSeconds-requestTime,maxRequestMilliseconds,foodWhileWaiting=native.ComponentVitalStats.Food});stage=2;
   }
  }else if(stage==2){
   var status=session.Read(session.SessionId,actor,move.ActionId);
   if(status.Status=="Succeeded"){
    Require(pendingTicks>10&&progressWhileWaiting>0.1&&native.ComponentVitalStats.Food<initialFood,"Game did not continue during decision");
    var accepted=session.ApplyDecision(decision);Require(accepted is not null&&accepted.Status=="Accepted","Eat must be accepted before executing");Require(session.ApplyDecision(decision)==accepted,"Duplicate decision changed acceptance");log(new{kind="eat_accepted",accepted,duplicateStable=true});stage=3;
   }else if(status.Status is "Failed" or "Blocked")throw new InvalidOperationException("Background movement did not succeed");
  }else if(stage==3){
   var result=session.ReadEat(session.SessionId,actor,decision.DecisionId);
   if(result.Status!="Accepted"&&result.Status!="Executing"){
    Require(result.Status=="Succeeded"&&result.CountAfter==result.CountBefore-1&&result.FoodAfter>result.FoodBefore&&result.ItemsEatenAfter==initialEaten+1,"Native eating postconditions failed: "+result);
    eaten=result;Require(session.ApplyDecision(decision)==result,"Completed eat replay changed result");Require(native.ComponentMiner.Inventory.GetSlotCount(decision.Slot)==result.CountAfter&&native.PlayerStats.FoodItemsEaten==initialEaten+1,"Replay consumed again");
    log(new{kind="eat_completed",result,replayConsumedAgain=false});Request(session.Observe(session.SessionId,actor));stage=4;
   }
  }else if(stage==4){
   if(scheduler.TryTake(actor,out var d,out var error)){
    Require(error is null&&d is not null&&d.Goal=="Observe"&&d.Skill=="Wait","Satisfied state did not change policy decision");Require(session.ApplyDecision(d) is null,"Wait unexpectedly executed food skill");Require(native.PlayerStats.FoodItemsEaten==initialEaten+1&&native.ComponentMiner.Inventory.GetSlotCount(decision.Slot)==eaten.CountAfter,"Unexpected repeat food consumption");
    log(new{kind="satisfied_wait",decision=d,food=native.ComponentVitalStats.Food,consumedItems=native.PlayerStats.FoodItemsEaten-initialEaten,health=native.ComponentHealth.Health,maxRequestMilliseconds});Done=true;Passed=true;
   }
  }
 }catch(Exception e){Done=true;Passed=false;log(new{kind="scenario_error",error=e.ToString()});}}
 public void Dispose(){scheduler.Dispose();interests.Release();backend.Dispose();}
}
