using Engine;
using Game;
using System.Diagnostics;
using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

// Native terrain adapter experiment, NOT five living actors or an AI controller.
internal static class InterestSmoke {
 static readonly Stopwatch clock=Stopwatch.StartNew();
 static readonly string root=AppContext.BaseDirectory;
 static readonly Guid[] ids=Enumerable.Range(0,6).Select(_=>Guid.NewGuid()).ToArray();
 static bool created,finished,passed;static int phase;static double phaseStart;
 static ComponentPlayer player;static SubsystemTerrain terrain;static SubsystemSpawn spawn;
 static ScapiInterestBackend backend;static Position[] centers;static int[] originalNativeKeys;
 static void Record(object o){var s=JsonSerializer.Serialize(o);File.AppendAllText(Path.Combine(root,"interest.jsonl"),s+Environment.NewLine);Console.WriteLine(s);}
 static int Main(string[] args){
  if(args.Length!=1||args[0]!="--new-disposable-world"||!root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar)||Directory.Exists(Path.Combine(root,"doc"))||Directory.Exists(Path.Combine(root,"Mods")))return 2;
  Record(new{kind="start",scripted=true,scope="native interest centers only; one real player; no AI actors"});
  Window.Created+=()=>Window.Frame+=Frame;
  try{Game.Program.EntryPoint();}catch(Exception e){Record(new{kind="fatal",error=e.ToString()});return 1;}
  return passed?0:1;
 }
 static void Frame(){try{
  if(finished)return;
  if(clock.Elapsed.TotalSeconds>240){Finish(false,"timeout");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){
   created=true;SettingsManager.VisibilityRange=32;
   var world=WorldsManager.CreateWorld(new WorldSettings{Name="Westworld cloud interest-only experiment",Seed="interest-cloud-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});
   Record(new{kind="world_created",world.DirectoryName});ScreensManager.SwitchScreen("GameLoading",world,null);
  }
  if(!created||GameManager.Project is null)return;
  var players=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);
  if(players.PlayersData.Count==0){players.AddPlayerData(new PlayerData(GameManager.Project){Name="CloudObserver"});ScreensManager.SwitchScreen("Game");}
  if(player is null){
   if(players.ComponentPlayers.Count!=1||!players.ComponentPlayers[0].PlayerData.IsReadyForPlaying)return;
   player=players.ComponentPlayers[0];terrain=GameManager.Project.FindSubsystem<SubsystemTerrain>(true);spawn=GameManager.Project.FindSubsystem<SubsystemSpawn>(true);
   var p=player.ComponentBody.Position;
   centers=new[]{new Position(p.X,p.Y,p.Z)}.Concat(Enumerable.Range(0,5).Select(i=>new Position(p.X+512+i*96,p.Y,p.Z))).ToArray();
   if(centers.Skip(1).Any(c=>terrain.Terrain.GetChunkAtCell(Terrain.ToCell((float)c.X),Terrain.ToCell((float)c.Z))is not null))throw new InvalidOperationException("Remote centers already allocated: isolation precondition failed");
   originalNativeKeys=terrain.TerrainUpdater.m_updateParameters.Locations.Keys.Where(k=>k>=0).Order().ToArray();
   if(originalNativeKeys.Length!=1)throw new InvalidOperationException("Expected one native viewpoint key");
   backend=new ScapiInterestBackend(terrain,spawn);
   for(int i=0;i<centers.Length;i++)backend.Upsert(ids[i],centers[i],new InterestRadii(16,32));
   backend.Publish();phaseStart=clock.Elapsed.TotalSeconds;
   Record(new{kind="submitted",nativePlayers=players.ComponentPlayers.Count,remoteCenters=5,aiActors=0,centers,negativePending=terrain.TerrainUpdater.m_pendingLocations.Keys.Count(k=>k<0)});
  }
  if(players.ComponentPlayers.Count!=1)throw new InvalidOperationException("Unexpected native player count");
  if(phase==0){
   var states=centers.Skip(1).Select(c=>terrain.Terrain.GetChunkAtCell(Terrain.ToCell((float)c.X),Terrain.ToCell((float)c.Z))?.State.ToString()).ToArray();
   if(states.All(s=>s==nameof(TerrainChunkState.Valid))){
    Record(new{kind="remote_ready",seconds=clock.Elapsed.TotalSeconds-phaseStart,states,negativeActive=terrain.TerrainUpdater.m_updateParameters.Locations.Keys.Count(k=>k<0),nativeKeys=terrain.TerrainUpdater.m_updateParameters.Locations.Keys.Where(k=>k>=0).ToArray(),simulationCenters=spawn.GetSimulationCenters().Length,nativePlayers=players.ComponentPlayers.Count,health=player.ComponentHealth.Health});
    if(spawn.GetSimulationCenters().Length!=6||terrain.TerrainUpdater.m_updateParameters.Locations.Keys.Count(k=>k<0)!=6)throw new InvalidOperationException("Native adapter publication mismatch");
    backend.Dispose();phase=1;phaseStart=clock.Elapsed.TotalSeconds;
   }
  }else if(phase==1&&clock.Elapsed.TotalSeconds-phaseStart>1){
   bool released=!terrain.TerrainUpdater.m_updateParameters.Locations.Keys.Any(k=>k<0)&&!terrain.TerrainUpdater.m_pendingLocations.Keys.Any(k=>k<0)&&spawn.SimulationCentersProvider is null;
   if(released){if(!originalNativeKeys.SequenceEqual(terrain.TerrainUpdater.m_updateParameters.Locations.Keys.Where(k=>k>=0).Order()))throw new InvalidOperationException("Native viewpoint keys changed");Record(new{kind="released",released,nativeKeys=terrain.TerrainUpdater.m_updateParameters.Locations.Keys.ToArray()});GameManager.SaveProject(true,false);Finish(true,"remote chunks valid and owned keys released");}
  }
 }catch(Exception e){Record(new{kind="error",error=e.ToString()});Finish(false,"exception");}}
 static void Finish(bool success,string reason){if(finished)return;finished=true;passed=success;try{backend?.Dispose();Record(new{kind="result",passed,reason,seconds=clock.Elapsed.TotalSeconds,scope="terrain adapter only; no five-actor or reload claim"});}finally{Window.Close();}}
}
