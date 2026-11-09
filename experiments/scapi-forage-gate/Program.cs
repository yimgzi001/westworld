using Engine;
using Game;
using System.Diagnostics;
using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

internal static class ForageGate {
 static readonly string root=AppContext.BaseDirectory;static readonly Stopwatch clock=Stopwatch.StartNew();
 internal static string Mode="normal";static bool created,finished,passed;static Scenario scenario;static NativeActorSession session;static SubsystemUpdate updates;
 static void Log(object value){string s=JsonSerializer.Serialize(value);Console.WriteLine(s);File.AppendAllText(Path.Combine(root,"forage.jsonl"),s+Environment.NewLine);}
 static int Main(string[] args){
  if(args.Length<1||args.Length>2||args[0]!="--new-disposable-world"||!root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar)||Directory.Exists(Path.Combine(root,"doc"))||Directory.Exists(Path.Combine(root,"Mods")))return 2;
  Mode=args.Length==2?args[1]:"normal";if(Mode is not ("normal" or "cancel" or "disappeared"))return 2;
  Log(new{kind="start",scripted=true,models=0,mode=Mode});Window.Created+=()=>Window.Frame+=Frame;
  try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}return passed?0:1;
 }
 static void Frame(){try{
  if(finished)return;if(clock.Elapsed.TotalSeconds>90){Finish(false,"timeout");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){created=true;SettingsManager.VisibilityRange=32;var w=WorldsManager.CreateWorld(new WorldSettings{Name="Westworld local forage capability gate",Seed="actors-cloud-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});ScreensManager.SwitchScreen("GameLoading",w,null);}
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
internal sealed class ForagePolicy:IDecisionProvider {
 public async Task<HighLevelDecision> DecideAsync(ActorObservation o,CancellationToken c){
  await Task.Delay(300,c);
  var plant=o.Cells.FirstOrDefault(x=>x.Visibility=="known"&&x.Value is int v&&Terrain.ExtractContents(v)==PumpkinBlock.Index&&BasePumpkinBlock.GetSize(Terrain.ExtractData(v))==7);
  if(plant is null)throw new InvalidOperationException("No visible ripe pumpkin");
  return new(o.SessionId,o.ActorId,o.ObservationId,Guid.NewGuid(),"SatisfyHunger","ForageOne",-1,0,new(plant.X,plant.Y,plant.Z,plant.Value.Value));
 }
}
internal sealed class Scenario:IUpdateable,IDisposable {
 public float FloatUpdateOrder=>-14;readonly NativeActorSession session;readonly Guid actor;readonly ComponentPlayer native;readonly Action<object> log;readonly ScapiInterestBackend backend;readonly InterestCoordinator interests;readonly SubsystemTerrain terrain;
 readonly AsyncDecisionScheduler scheduler=new(new ForagePolicy(),TimeSpan.FromSeconds(2));
 int stage;double elapsed;int x,y,z;string lastStage="";HighLevelDecision decision;ForageResult done;long eatenBefore;
 public bool Done{get;private set;}public bool Passed{get;private set;}
 public Scenario(NativeActorSession s,Guid id,ComponentPlayer p,Action<object> log){session=s;actor=id;native=p;this.log=log;var project=p.Project;terrain=project.FindSubsystem<SubsystemTerrain>(true);backend=new ScapiInterestBackend(terrain,project.FindSubsystem<SubsystemSpawn>(true));interests=new InterestCoordinator(s.Registry,backend,new InterestRadii(16,32));interests.Sync();terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);}
 static void Require(bool condition,string why){if(!condition)throw new InvalidOperationException(why);}
 public void Update(float dt){if(Done)return;try{
  elapsed+=dt;interests.Sync();Require(native.GameWidget is null,"AI unexpectedly has viewport");
  if(stage==0&&elapsed>=3){
   var pos=native.ComponentBody.Position;int px=Terrain.ToCell(pos.X),pz=Terrain.ToCell(pos.Z);y=Terrain.ToCell(pos.Y);
   // Explicit native-world fixture: level support and one ripe plant; not natural discovery.
   for(int a=px-5;a<=px+5;a++)for(int b=pz-5;b<=pz+5;b++){terrain.ChangeCell(a,y-1,b,Terrain.MakeBlockValue(2));for(int c=y;c<=y+3;c++)terrain.ChangeCell(a,c,b,0);}
   native.ComponentBody.Position=new Vector3(px+0.5f,y,pz+0.5f);native.ComponentBody.Velocity=Vector3.Zero;
   var f=native.ComponentBody.Matrix.Forward;x=px+(MathF.Abs(f.X)>MathF.Abs(f.Z)?Math.Sign(f.X)*3:0);z=pz+(MathF.Abs(f.X)>MathF.Abs(f.Z)?0:Math.Sign(f.Z)*3);terrain.ChangeCell(x,y,z,Terrain.MakeBlockValue(PumpkinBlock.Index));
   var inventory=native.ComponentMiner.Inventory;for(int i=0;i<inventory.SlotsCount;i++)inventory.RemoveSlotItems(i,inventory.GetSlotCount(i));
   eatenBefore=native.PlayerStats.FoodItemsEaten;log(new{kind="fixture",artificial=true,inventoryCleared=true,plant=new{x,y,z},models=0});stage=1;
  }else if(stage==1&&elapsed>=4){
   var o=session.Observe(session.SessionId,actor);Require(o.Cells.Any(c=>c.X==x&&c.Y==y&&c.Z==z&&c.Visibility=="known"),"Fixture target is not actually visible");Require(scheduler.Request(o,out var error),"decision rejected: "+error);log(new{kind="observation",o.ObservationId,o.Position,o.Food,knownPlants=o.Cells.Where(c=>c.Value.HasValue&&Terrain.ExtractContents(c.Value.Value)==PumpkinBlock.Index).ToArray()});stage=2;
  }else if(stage==2&&scheduler.TryTake(actor,out var d,out var error)){
   Require(error is null&&d is not null,"policy failed: "+error);decision=d;var accepted=session.ApplyForageDecision(d);Require(session.ApplyForageDecision(d)==accepted,"duplicate acceptance unstable");log(new{kind="goal_accepted",accepted});if(ForageGate.Mode=="cancel")session.CancelForage(session.SessionId,actor,d.DecisionId);else if(ForageGate.Mode=="disappeared")terrain.ChangeCell(x,y,z,0);stage=3;
  }else if(stage==3){
   var result=session.ReadForage(session.SessionId,actor,decision.DecisionId);if(result.Stage!=lastStage){log(new{kind="stage",result});lastStage=result.Stage;}
   if(result.Status is "Succeeded" or "Blocked" or "Failed" or "Uncertain" or "Canceled"){
    log(new{kind="goal_terminal",result,blocks=native.PlayerStats.BlocksDug,itemsEaten=native.PlayerStats.FoodItemsEaten-eatenBefore});
    if(ForageGate.Mode=="normal")Require(result.Status=="Succeeded"&&result.Harvested&&result.Collected>=1&&result.FoodAfter>result.FoodBefore,"forage failed: "+result);else Require(result.Status==(ForageGate.Mode=="cancel"?"Canceled":"Blocked")&&!result.Harvested&&result.Collected==0&&native.PlayerStats.BlocksDug==0,"Interrupted goal changed world or wrong outcome");
    if(ForageGate.Mode=="normal")Require(Terrain.ExtractContents(terrain.Terrain.GetCellValue(x,y,z))==0&&native.PlayerStats.FoodItemsEaten==eatenBefore+1,"world/food postconditions missing");done=result;Require(session.ApplyForageDecision(decision)==result,"completed replay changed result");elapsed=0;stage=4;
   }
  }else if(stage==4&&elapsed>0.5){Require(native.PlayerStats.FoodItemsEaten==eatenBefore+(ForageGate.Mode=="normal"?1:0),"duplicate consumed twice");log(new{kind="verified",replayStable=true,health=native.ComponentHealth.Health,final=done});Done=true;Passed=true;}
 }catch(Exception e){Done=true;Passed=false;log(new{kind="scenario_error",error=e.ToString()});}}
 public void Dispose(){scheduler.Dispose();interests.Release();backend.Dispose();}
}
