using Engine;using Game;using HarmonyLib;using Westworld.Scapi.Adapter;using Westworld.Runtime.Core;using System.Diagnostics;using System.Text.Json;
static class FaunaGate {
 static readonly string Root=AppContext.BaseDirectory;static readonly Stopwatch Wall=Stopwatch.StartNew();static bool created,done,passed;static NativeActorSession session;static ScapiInterestBackend backend;static InterestCoordinator interest;static Harmony harmony;static int active,periodicCalls,candidateTrials,spawnCalls;static double boundTime;static bool bound;static bool empty;static Func<IEnumerable<Vector3>> ownedProvider;
 static void Log(object x){string line=JsonSerializer.Serialize(x);Console.WriteLine(line);File.AppendAllText(Path.Combine(Root,"fauna.jsonl"),line+"\n");}
 static int Main(string[] args){if(args.Length!=1||args[0] is not("centers" or "empty")||!Root.Contains("/work/")||Directory.Exists(Path.Combine(Root,"doc")))return 2;empty=args[0]=="empty";
  harmony=new("westworld.fauna.readonly-counters");
  harmony.Patch(AccessTools.Method(typeof(SubsystemCreatureSpawn),nameof(SubsystemCreatureSpawn.SpawnRandomCreature)),prefix:new HarmonyMethod(typeof(FaunaGate),nameof(BeforeRandom)),finalizer:new HarmonyMethod(typeof(FaunaGate),nameof(AfterRandom)));
  harmony.Patch(AccessTools.Method(typeof(SubsystemCreatureSpawn),nameof(SubsystemCreatureSpawn.ProcessSpawnPoint)),prefix:new HarmonyMethod(typeof(FaunaGate),nameof(BeforePoint)));
  harmony.Patch(AccessTools.Method(typeof(SubsystemCreatureSpawn),nameof(SubsystemCreatureSpawn.SpawnCreatures)),prefix:new HarmonyMethod(typeof(FaunaGate),nameof(BeforeSpawn)));
  Log(new{kind="start",mode=args[0],models=0,diagnosticOnly=true,changesSpawnOutcome=false});Window.Created+=()=>Window.Frame+=Frame;
  try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}finally{harmony.UnpatchSelf();}return passed?0:1;
 }
 static void BeforeRandom(SubsystemCreatureSpawn __instance){active++;if(!bound)return;periodicCalls++;Log(new{kind="native_periodic_entry",gameTime=__instance.m_subsystemTime.GameTime,centers=__instance.m_subsystemSpawn.GetSimulationCenters().Length,views=__instance.m_subsystemViews.GameWidgets.Count,season=__instance.m_subsystemSeasons.Season.ToString(),totalNonconstant=__instance.CountCreatures(false)});}
 static void AfterRandom(){active--;}
 static void BeforePoint(){if(bound&&active>0)candidateTrials++;}
 static void BeforeSpawn(){if(bound&&active>0)spawnCalls++;}
 static void Frame(){if(done)return;try{
  if(Wall.Elapsed.TotalSeconds>240){Finish(false,"wall_timeout");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){created=true;SettingsManager.VisibilityRange=32;var w=WorldsManager.CreateWorld(new WorldSettings{Name="Fauna periodic audit",Seed="actors-cloud-20261109",GameMode=GameMode.Survival,EnvironmentBehaviorMode=EnvironmentBehaviorMode.Living,OriginalSerializationVersion=VersionsManager.SerializationVersion});ScreensManager.SwitchScreen("GameLoading",w,null);}
  if(GameManager.Project is null)return;var project=GameManager.Project;var updates=project.FindSubsystem<SubsystemUpdate>(true);if(updates.UpdateFailureCount!=0)throw new Exception(updates.LastUpdateFailure);var players=project.FindSubsystem<SubsystemPlayers>(true);
  if(players.PlayersData.Count==0){players.AddPlayerData(new PlayerData(project){Name="FaunaActor",IsExternalActor=true});ScreensManager.SwitchScreen("Game");}
  if(!bound&&players.ComponentPlayers.Count==1&&players.PlayersData[0].IsReadyForPlaying){var p=players.ComponentPlayers[0];session=new(project,Guid.NewGuid(),false);session.Bind(Guid.NewGuid(),p);updates.AddUpdateable(session);var terrain=project.FindSubsystem<SubsystemTerrain>(true);var spawn=project.FindSubsystem<SubsystemSpawn>(true);backend=new(terrain,spawn);interest=new(session.Registry,backend,new(16,32));interest.Sync();terrain.TerrainUpdater.RemoveUpdateLocation(p.PlayerData.PlayerIndex);ownedProvider=spawn.SimulationCentersProvider;if(empty)spawn.SimulationCentersProvider=()=>Array.Empty<Vector3>();boundTime=project.FindSubsystem<SubsystemTime>(true).GameTime;bound=true;Log(new{kind="bound",views=project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count,gameTime=boundTime,fullPlayer=p.ComponentVitalStats!=null});}
  if(!bound)return;if(!empty)interest.Sync();var player=players.ComponentPlayers.Single();if(player.GameWidget!=null||player.ComponentHealth.Health<=0)throw new Exception("actor not living viewless player");
  double time=project.FindSubsystem<SubsystemTime>(true).GameTime;var fauna=project.FindSubsystem<SubsystemCreatureSpawn>(true);double duration=fauna.m_subsystemSeasons.Season==Season.Winter?123:63;
  if(time-boundTime>=duration)Finish(periodicCalls>0&&(empty?candidateTrials==0:candidateTrials>0),empty?"empty centers periodic branch verified":"periodic candidate attempts checked");
 }catch(Exception e){Log(new{kind="error",error=e.ToString()});Finish(false,"exception");}}
 static void Finish(bool ok,string reason){if(done)return;done=true;passed=ok;
  try{if(session!=null){if(empty)GameManager.Project.FindSubsystem<SubsystemSpawn>(true).SimulationCentersProvider=ownedProvider;GameManager.Project.FindSubsystem<SubsystemUpdate>(true).RemoveUpdateable(session);session.Dispose();interest.Release();backend.Dispose();}}
  catch(Exception e){passed=false;reason="cleanup_failed";Log(new{kind="cleanup_error",error=e.ToString()});}
  finally{Log(new{kind="result",passed,reason,periodicCalls,candidateTrials,spawnCalls,seconds=Wall.Elapsed.TotalSeconds,meaning="candidate attempts are not animal generation or actor sightings"});Window.Close();}
 }
}
