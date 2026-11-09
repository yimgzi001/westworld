using Engine;
using Game;
using System.Diagnostics;
using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

internal static class SurfaceVisibilityGate {
 static readonly string root=AppContext.BaseDirectory;static readonly Stopwatch clock=Stopwatch.StartNew();
 static bool created,finished,passed;static Scenario scenario;static NativeActorSession session;static SubsystemUpdate updates;
 static void Log(object value){string s=JsonSerializer.Serialize(value);Console.WriteLine(s);File.AppendAllText(Path.Combine(root,"surface.jsonl"),s+Environment.NewLine);}
 static int Main(string[] args){
  if(args.Length!=1||args[0]!="--new-disposable-world"||!root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar)||Directory.Exists(Path.Combine(root,"doc"))||Directory.Exists(Path.Combine(root,"Mods")))return 2;
  Log(new{kind="start",scripted=true,models=0});Window.Created+=()=>Window.Frame+=Frame;
  try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}return passed?0:1;
 }
 static void Frame(){try{
  if(finished)return;if(clock.Elapsed.TotalSeconds>60){Finish(false,"timeout");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){created=true;SettingsManager.VisibilityRange=32;var w=WorldsManager.CreateWorld(new WorldSettings{Name="Westworld surface visibility gate",Seed="actors-cloud-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});ScreensManager.SwitchScreen("GameLoading",w,null);}
  if(GameManager.Project is null)return;
  updates??=GameManager.Project.FindSubsystem<SubsystemUpdate>(true);if(updates.UpdateFailureCount!=0)throw new InvalidOperationException(updates.LastUpdateFailure);
  var players=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);
  if(players.PlayersData.Count==0){players.AddPlayerData(new PlayerData(GameManager.Project){Name="IntentActor",IsExternalActor=true});ScreensManager.SwitchScreen("Game");}
  if(scenario is null&&players.ComponentPlayers.Count==1&&players.PlayersData[0].IsReadyForPlaying){
   var p=players.ComponentPlayers[0];session=new NativeActorSession(GameManager.Project,Guid.NewGuid(),actionsEnabled:false);var id=Guid.NewGuid();session.Bind(id,p);scenario=new Scenario(session,id,p,Log);updates.AddUpdateable(session);updates.AddUpdateable(scenario);
  }
  if(scenario?.Done==true)Finish(scenario.Passed,"scenario_finished");
 }catch(Exception e){Log(new{kind="failure",error=e.ToString()});Finish(false,"exception");}}
 static void Finish(bool success,string reason){if(finished)return;finished=true;passed=success;try{Log(new{kind="result",passed,reason,seconds=clock.Elapsed.TotalSeconds,updateFailures=updates?.UpdateFailureCount});session?.Dispose();scenario?.Dispose();}finally{Window.Close();}}
}
internal sealed class Scenario:IUpdateable,IDisposable {
 public float FloatUpdateOrder=>-14;
 readonly NativeActorSession session;readonly Guid actor;readonly ComponentPlayer player;readonly Action<object> log;readonly ScapiInterestBackend backend;readonly InterestCoordinator interests;readonly SubsystemTerrain terrain;readonly Game.Westworld.AtomicActions actions;
 readonly Dictionary<(int,int,int),int> originals=[];int x,y,z,stage,checks;double elapsed;Game.Westworld.AtomicActionRun facing;
 public bool Done{get;private set;}public bool Passed{get;private set;}
 public Scenario(NativeActorSession s,Guid id,ComponentPlayer p,Action<object> audit){session=s;actor=id;player=p;log=audit;terrain=p.Project.FindSubsystem<SubsystemTerrain>(true);backend=new(terrain,p.Project.FindSubsystem<SubsystemSpawn>(true));interests=new(s.Registry,backend,new InterestRadii(16,32));interests.Sync();terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);actions=new(p.Entity);}
 void Set(int a,int b,int c,int value){if(!originals.ContainsKey((a,b,c)))originals[(a,b,c)]=terrain.Terrain.GetCellValue(a,b,c);terrain.ChangeCell(a,b,c,value);}
 void Check(bool yes,string label){if(!yes)throw new InvalidOperationException(label);checks++;log(new{kind="assertion",label,passed=true});}
 bool Loaded()=>Enumerable.Range(x-4,9).All(a=>Enumerable.Range(z-4,9).All(c=>terrain.Terrain.GetChunkAtCell(a,c)?.State==TerrainChunkState.Valid));
 CellObservation Cell(ActorObservation o,int a,int b,int c)=>o.Cells.Single(v=>v.X==a&&v.Y==b&&v.Z==c);
 ActorObservation Observe(string label){var o=session.Observe(session.SessionId,actor);var eye=player.ComponentCreatureModel.EyePosition;var f=player.ComponentCreatureModel.EyeRotation.GetForwardVector();log(new{kind="observation",label,o,eye=new[]{eye.X,eye.Y,eye.Z},forward=new[]{f.X,f.Y,f.Z}});return o;}
 public void Update(float dt){if(Done)return;try{elapsed+=dt;interests.Sync();if(elapsed>40)throw new TimeoutException("surface scenario cap");
 if(stage==0&&elapsed>=2){var p=player.ComponentBody.Position;x=Terrain.ToCell(p.X);y=Terrain.ToCell(p.Y);z=Terrain.ToCell(p.Z);
  for(int a=x-4;a<=x+4;a++)for(int c=z-4;c<=z+4;c++){Set(a,y-1,c,DirtBlock.Index);for(int b=y;b<=y+4;b++)Set(a,b,c,0);}
  player.ComponentBody.Position=new(x+.5f,y,z+.5f);player.ComponentBody.Velocity=Vector3.Zero;
  log(new{kind="artificial_fixture",flatFloor=true,origin=new[]{x,y,z},target=new[]{x+3,y-1,z},wallPlane=x+1,inventoryInjection=false,fixtureTeleport=true,observationInjection=false});stage=1;
 }else if(stage==1&&elapsed>=3&&Loaded()&&player.ComponentBody.Velocity.Length()<.1f){
  var eye=player.ComponentCreatureModel.EyePosition;var top=VoxelSurfaceVisibility.SupportTop(x+3,y-1,z);var t=new Vector3((float)top.X,(float)top.Y,(float)top.Z);var delta=t-eye;float pitch=MathF.Asin(delta.Y/delta.Length())*180/MathF.PI;
  facing=actions.Start(new Game.Westworld.FaceTowards(t,4,pitch));stage=2;
 }else if(stage==2&&facing.Result.Status!=Game.Westworld.AtomicActionStatus.Running){
  Check(facing.Result.Status==Game.Westworld.AtomicActionStatus.Succeeded,"native view aimed at floor top");var o=Observe("open_floor");
  Check(Cell(o,x+3,y-1,z).Value is int v&&Terrain.ExtractContents(v)==DirtBlock.Index,"far floor actual top visible");
  Check(Cell(o,x-3,y-1,z).Value is null,"behind FOV unknown");Check(Cell(o,x+4,y-1,z+4).Value is null,"outside five metre center radius unknown");
  var e=player.ComponentCreatureModel.EyePosition;var center=new Position(x+3.5,y-.5,z+.5);bool centerClear=VoxelSurfaceVisibility.LineOfSight(new(e.X,e.Y,e.Z),center,(a,b,c)=>{if(terrain.Terrain.GetChunkAtCell(a,c)?.State!=TerrainChunkState.Valid)return null;int value=terrain.Terrain.GetCellValue(a,b,c);return !BlocksManager.Blocks[Terrain.ExtractContents(value)].IsTransparent_(value);});
  Check(!centerClear,"native terrain center ray blocked while surface observed");
  for(int b=y;b<=y+3;b++)for(int c=z-3;c<=z+3;c++)Set(x+1,b,c,GraniteBlock.Index);stage=3;
 }else if(stage==3&&Loaded()){
  var o=Observe("complete_wall");Check(Cell(o,x+3,y-1,z).Value is null,"complete wall hides every target sample");
  for(int b=y;b<=y+3;b++)for(int c=z-3;c<=z+3;c++)Set(x+1,b,c,0);stage=4;
 }else if(stage==4&&Loaded()){
  var o=Observe("wall_removed");Check(Cell(o,x+3,y-1,z).Value is int v&&Terrain.ExtractContents(v)==DirtBlock.Index,"removing wall restores actual top visibility");
  Check(player.Project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count==0,"zero AI viewports");Check(o.Cells.All(c=>(c.Value is null)==(c.Visibility=="unknown")),"unknown values never disclosed");
  log(new{kind="surface_gate_complete",checks,nativeCornerSeamCovered=false,models=0});Done=Passed=true;
 }
 }catch(Exception e){Done=true;Passed=false;log(new{kind="scenario_error",checks,error=e.ToString()});}}
 public void Dispose(){if(facing?.Result.Status==Game.Westworld.AtomicActionStatus.Running)actions.Cancel(facing);player.Project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(actions);player.Project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(this);foreach(var pair in originals)terrain.ChangeCell(pair.Key.Item1,pair.Key.Item2,pair.Key.Item3,pair.Value);interests.Release();backend.Dispose();}
}
