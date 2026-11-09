using Engine;
using Game;
using System.Diagnostics;
using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

internal static class IntentGate {
 static readonly string root=AppContext.BaseDirectory;static readonly Stopwatch clock=Stopwatch.StartNew();
 static bool created,finished,passed;static Scenario scenario;static NativeActorSession session;static SubsystemUpdate updates;
 static void Log(object value){string s=JsonSerializer.Serialize(value);Console.WriteLine(s);File.AppendAllText(Path.Combine(root,"intent.jsonl"),s+Environment.NewLine);}
 static int Main(string[] args){
  if(args.Length!=1||args[0]!="--new-disposable-world"||!root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar)||Directory.Exists(Path.Combine(root,"doc"))||Directory.Exists(Path.Combine(root,"Mods")))return 2;
  Log(new{kind="start",scripted=true,models=0});Window.Created+=()=>Window.Frame+=Frame;
  try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}return passed?0:1;
 }
 static void Frame(){try{
  if(finished)return;if(clock.Elapsed.TotalSeconds>90){Finish(false,"timeout");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){created=true;SettingsManager.VisibilityRange=32;var w=WorldsManager.CreateWorld(new WorldSettings{Name="Westworld native intent gate",Seed="actors-cloud-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});ScreensManager.SwitchScreen("GameLoading",w,null);}
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
internal sealed class Scenario:IUpdateable,IDisposable {
 public float FloatUpdateOrder=>-14;
 readonly NativeActorSession session;readonly Guid actor;readonly ComponentPlayer native;readonly Action<object> log;readonly ScapiInterestBackend backend;readonly InterestCoordinator interests;readonly Stopwatch wall=Stopwatch.StartNew();
 double elapsed,acceptedAt;int stage=-3;CellObservation target;Point3 wallCell;int oldWall;bool wallSet;Position cancelPosition;MoveIntent move,cancel;SkillResult completed;SlotObservation[] initialInventory;
 public bool Done{get;private set;}public bool Passed{get;private set;}
 public Scenario(NativeActorSession s,Guid id,ComponentPlayer player,Action<object> log){session=s;actor=id;native=player;this.log=log;var project=player.Project;backend=new ScapiInterestBackend(project.FindSubsystem<SubsystemTerrain>(true),project.FindSubsystem<SubsystemSpawn>(true));interests=new InterestCoordinator(s.Registry,backend,new InterestRadii(16,32));interests.Sync();project.FindSubsystem<SubsystemTerrain>(true).TerrainUpdater.RemoveUpdateLocation(player.PlayerData.PlayerIndex);}
 static void Require(bool condition,string why){if(!condition)throw new InvalidOperationException(why);}
 static bool Reject(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
 public void Update(float dt){if(Done)return;try{
  elapsed+=dt;interests.Sync();Require(native.Project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count==0,"Unexpected AI viewport");
  var terrain=native.Project.FindSubsystem<SubsystemTerrain>(true);
  if(stage==-3&&elapsed>=3){
   var o=session.Observe(session.SessionId,actor);var eye=native.ComponentCreatureModel.EyePosition;
   target=o.Cells.Where(c=>c.Value.HasValue&&Terrain.ExtractContents(c.Value.Value)==0).OrderByDescending(c=>Vector3.Distance(eye,new Vector3(c.X+0.5f,c.Y+0.5f,c.Z+0.5f))).First(c=>{var mid=(eye+new Vector3(c.X+0.5f,c.Y+0.5f,c.Z+0.5f))*0.5f;return Vector3.Distance(eye,new Vector3(c.X+0.5f,c.Y+0.5f,c.Z+0.5f))>2.2f&&(Terrain.ToCell(mid.X)!=Terrain.ToCell(eye.X)||Terrain.ToCell(mid.Z)!=Terrain.ToCell(eye.Z))&&(Terrain.ToCell(mid.X)!=c.X||Terrain.ToCell(mid.Z)!=c.Z);});
   var center=(eye+new Vector3(target.X+0.5f,target.Y+0.5f,target.Z+0.5f))*0.5f;wallCell=new Point3(Terrain.ToCell(center.X),Terrain.ToCell(center.Y),Terrain.ToCell(center.Z));oldWall=terrain.Terrain.GetCellValue(wallCell.X,wallCell.Y,wallCell.Z);terrain.ChangeCell(wallCell.X,wallCell.Y,wallCell.Z,GraniteBlock.Index);wallSet=true;log(new{kind="fixture_wall",target,wall=new[]{wallCell.X,wallCell.Y,wallCell.Z},testSetupNotAgentAction=true});stage=-2;
  }else if(stage==-2&&terrain.Terrain.GetChunkAtCell(wallCell.X,wallCell.Z)?.State==TerrainChunkState.Valid){
   var o=session.Observe(session.SessionId,actor);var hidden=o.Cells.Single(c=>c.X==target.X&&c.Y==target.Y&&c.Z==target.Z);Require(hidden.Value is null&&hidden.Visibility=="unknown","Opaque wall leaked target value");log(new{kind="wall_occludes",hidden});terrain.ChangeCell(wallCell.X,wallCell.Y,wallCell.Z,oldWall);wallSet=false;stage=-1;
  }else if(stage==-1&&terrain.Terrain.GetChunkAtCell(wallCell.X,wallCell.Z)?.State==TerrainChunkState.Valid){
   var o=session.Observe(session.SessionId,actor);Require(o.Cells.Single(c=>c.X==target.X&&c.Y==target.Y&&c.Z==target.Z).Value is int restored&&Terrain.ExtractContents(restored)==Terrain.ExtractContents(target.Value.Value),"Restored LOS did not restore known target");log(new{kind="wall_restored",passed=true});stage=0;
  }else if(stage==0&&elapsed>=3){
   var o=session.Observe(session.SessionId,actor);initialInventory=o.Inventory;
   Require(o.Cells.Length==100&&o.Cells.Any(c=>c.Value.HasValue)&&o.Cells.Any(c=>!c.Value.HasValue)&&o.Cells.All(c=>(c.Visibility=="unknown")==!c.Value.HasValue),"Observation disclosure policy mismatch");
   log(new{kind="observation",o.ActorId,o.ObservationId,o.Position,o.Health,o.Food,known=o.Cells.Count(c=>c.Value.HasValue),unknown=o.Cells.Count(c=>!c.Value.HasValue),cells=o.Cells});
   move=new(session.SessionId,actor,Guid.NewGuid(),o.ObservationId,0,0.4f,0.6f);
   Require(Reject(()=>session.Submit(move with {SessionId=Guid.NewGuid()})),"Stale session accepted");
   var accepted=session.Submit(move);Require(accepted.Status=="Accepted","Submit claimed completion");
   Require(session.Submit(move)==accepted,"Repeated submission changed result");
   Require(Reject(()=>session.Submit(move with {Forward=0.3f})),"ID payload conflict accepted");
   Require(Reject(()=>session.Submit(move with {ActionId=Guid.NewGuid(),ObservationId=Guid.NewGuid()})),"Unknown observation accepted");
   log(new{kind="accepted",accepted,duplicateStable=true,conflictRejected=true});acceptedAt=wall.Elapsed.TotalSeconds;stage=1;
  }else if(stage==1){
   var status=session.Read(session.SessionId,actor,move.ActionId);
   if(status.Status is "Succeeded" or "Blocked" or "Failed"){
    Require(status.Status=="Succeeded"&&status.ForwardProgress>0.01&&status.HorizontalDistance<3,"Native directional move failed");completed=status;log(new{kind="movement_completed",result=status});stage=2;
   }
  }else if(stage==2&&wall.Elapsed.TotalSeconds-acceptedAt>3.3){
   Require(session.Submit(move)==completed,"Terminal replay was not idempotent");
   Require(Reject(()=>session.Submit(move with {ActionId=Guid.NewGuid()})),"Stale observation accepted");
   var o=session.Observe(session.SessionId,actor);cancel=new(session.SessionId,actor,Guid.NewGuid(),o.ObservationId,0.4f,0,0.6f);session.Submit(cancel);log(new{kind="terminal_replay",terminalReplay=true,staleObservationRejected=true});stage=3;
  }else if(stage==3){
   var status=session.Read(session.SessionId,actor,cancel.ActionId);
   if(status.Status=="Running"&&Vector2.Distance(native.ComponentBody.Position.XZ,new Vector2((float)status.Start.X,(float)status.Start.Z))>0.03f){
    var pos=native.ComponentBody.Position;cancelPosition=new Position(pos.X,pos.Y,pos.Z);var stopped=session.Cancel(session.SessionId,actor,cancel.ActionId);Require(stopped.Status=="Stopping"&&native.ComponentInput.PlayerInput.Move.LengthSquared()==0,"Active cancellation did not clear consumed input");log(new{kind="active_cancel_requested",cancelPosition,stopped});stage=4;
   }else if(status.Status is "Failed" or "Blocked" or "Succeeded")throw new InvalidOperationException("Did not observe active movement before cancel");
  }else if(stage==4){
   var status=session.Read(session.SessionId,actor,cancel.ActionId);
   if(status.Status is "Canceled" or "Failed"){
    Require(status.Status=="Canceled"&&status.HorizontalDistance<3&&Vector2.Distance(new Vector2((float)status.End.X,(float)status.End.Z),new Vector2((float)cancelPosition.X,(float)cancelPosition.Z))<0.3f,"Canceled action moved or failed");var o=session.Observe(session.SessionId,actor);Require(o.Inventory.SequenceEqual(initialInventory),"Movement changed inventory");Require(native.ComponentHealth.Health==1,"Health changed");
    Require(Reject(()=>session.Read(Guid.NewGuid(),actor,move.ActionId)),"Old session query accepted");log(new{kind="cancel_completed",result=status,inventoryUnchanged=true,health=native.ComponentHealth.Health});native.Project.RemoveEntity(native.Entity,true);stage=5;
   }
  }else if(stage==5){
   Require(session.Registry.SimulationInterests().Length==0,"Removed actor left activity center");Require(Reject(()=>session.Observe(session.SessionId,actor)),"Removed actor still observable");Require(session.Submit(move)==completed,"Retired actor terminal replay lost");log(new{kind="removed_binding",interests=0,observationRejected=true,terminalReplay=true});Done=true;Passed=true;
  }
 }catch(Exception e){Done=true;Passed=false;log(new{kind="scenario_error",error=e.ToString()});}}
 public void Dispose(){if(wallSet)native.Project.FindSubsystem<SubsystemTerrain>(true).ChangeCell(wallCell.X,wallCell.Y,wallCell.Z,oldWall);interests.Release();backend.Dispose();}
}
