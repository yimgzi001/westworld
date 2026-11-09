using Engine;
using Game;
using Game.Westworld;
using System.Diagnostics;
using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;
internal static class CookingGate {
 static readonly string Root=AppContext.BaseDirectory;static readonly Stopwatch clock=Stopwatch.StartNew();
 static bool created,finished,passed;static ProductionScenario scenario;static CookingScenario cooking;static SubsystemUpdate updates;
 internal static void Log(object value){string json=JsonSerializer.Serialize(value);Console.WriteLine(json);File.AppendAllText(Path.Combine(Root,"cooking.jsonl"),json+"\n");}
 static int Main(string[] args){
  if(args.Length!=1||args[0]!="fixture"||!Root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar)
    ||Directory.Exists(Path.Combine(Root,"doc"))||Directory.Exists(Path.Combine(Root,"Mods")))return 2;
  Log(new{kind="start",artificialFixture=true,inventoryInjection=false,models=0,scope="native workbench production -> fuel and stone collection -> furnace -> artificial raw-meat pickup -> cook -> eat"});
  Window.Created+=()=>Window.Frame+=Frame;
  try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}return passed?0:1;
 }
 static void Frame(){try{
  if(finished)return;if(clock.Elapsed.TotalSeconds>450){Finish(false,"wall timeout");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){created=true;SettingsManager.VisibilityRange=32;
   var w=WorldsManager.CreateWorld(new WorldSettings{Name="Production artificial resource gate",Seed="production-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});ScreensManager.SwitchScreen("GameLoading",w,null);}
  if(GameManager.Project is null)return;updates??=GameManager.Project.FindSubsystem<SubsystemUpdate>(true);
  if(updates.UpdateFailureCount!=0)throw new InvalidOperationException(updates.LastUpdateFailure);
  var players=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);
  if(players.PlayersData.Count==0){players.AddPlayerData(new PlayerData(GameManager.Project){Name="ProductionActor",IsExternalActor=true});ScreensManager.SwitchScreen("Game");}
  if(scenario is null&&players.ComponentPlayers.Count==1&&players.PlayersData[0].IsReadyForPlaying){scenario=new(players.ComponentPlayers[0]);updates.AddUpdateable(scenario);}
  if(scenario?.Done==true&&cooking is null){var p=players.ComponentPlayers[0];var table=scenario.EarnedWorkbench;scenario.Dispose();cooking=new CookingScenario(p,table,scenario.SharedActions,scenario.SharedTools);updates.AddUpdateable(cooking);}
  if(cooking?.Done==true)Finish(true,"native furnace cooking and eating verified");
 }catch(Exception e){Log(new{kind="failure",error=e.ToString()});Finish(false,"exception");}}
 static void Finish(bool ok,string reason){if(finished)return;finished=true;passed=ok;Log(new{kind="result",passed,reason,seconds=clock.Elapsed.TotalSeconds,updateFailures=updates?.UpdateFailureCount});try{if(cooking is not null)cooking.Dispose();else scenario?.Dispose();}finally{Window.Close();}}
}
