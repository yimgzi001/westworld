using Engine;
using Game;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

internal static class ActorGate {
 static readonly Stopwatch clock=Stopwatch.StartNew();static readonly string root=AppContext.BaseDirectory;
 static bool reload,created,finished,passed;static Pulse pulse;static SubsystemUpdate updates;
 static void Log(object o){string s=JsonSerializer.Serialize(o);Console.WriteLine(s);File.AppendAllText(Path.Combine(root,"actors.jsonl"),s+Environment.NewLine);}
 static int Main(string[] args){
  if(args.Length!=1||!new[]{"--new-disposable-world","--reload"}.Contains(args[0])||!root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar))return 2;
  reload=args[0]=="--reload";
  if(!reload&&(Directory.Exists(Path.Combine(root,"doc"))||Directory.Exists(Path.Combine(root,"Mods"))))return 2;
  if(reload&&(!File.Exists(Path.Combine(root,"actor-gate.marker"))||File.ReadAllText(Path.Combine(root,"actor-gate.marker"))!="westworld-actor-gate-v1"))return 2;
  if(Directory.Exists(Path.Combine(root,"Mods"))&&Directory.EnumerateFileSystemEntries(Path.Combine(root,"Mods")).Any())return 2;
  File.WriteAllText(Path.Combine(root,"actor-gate.marker"),"westworld-actor-gate-v1");Log(new{kind="start",reload,scripted=true});
  Window.Created+=()=>Window.Frame+=Frame;
  try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}
  return passed?0:1;
 }
 static void Frame(){try{
  if(finished)return;if(clock.Elapsed.TotalSeconds>120){Finish(false,"timeout");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){
   created=true;SettingsManager.VisibilityRange=32;
   var w=reload?WorldsManager.GetWorldInfo("app:/doc/Worlds/World"):WorldsManager.CreateWorld(new WorldSettings{Name="Westworld five external actors",Seed="actors-cloud-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});
   if(w is null||w.WorldSettings.Name!="Westworld five external actors")throw new InvalidOperationException("Wrong isolated world");
   ScreensManager.SwitchScreen("GameLoading",w,null);
  }
  if(GameManager.Project is null)return;
  updates??=GameManager.Project.FindSubsystem<SubsystemUpdate>(true);
  if(updates.UpdateFailureCount!=0)throw new InvalidOperationException(updates.LastUpdateFailure);
  var players=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);
  if(!reload&&players.PlayersData.Count==0){for(int i=0;i<5;i++)players.AddPlayerData(new PlayerData(GameManager.Project){Name="Actor_"+(i+1),IsExternalActor=true});ScreensManager.SwitchScreen("Game");}
  if(players.PlayersData.Count!=5||players.PlayersData.Any(p=>!p.IsExternalActor))throw new InvalidOperationException("Identity/control mismatch");
  if(pulse is null&&players.ComponentPlayers.Count==5&&players.PlayersData.All(p=>p.IsReadyForPlaying)){
   ScreensManager.SwitchScreen("Game");
   pulse=new Pulse(players.ComponentPlayers.OrderBy(p=>p.PlayerData.PlayerIndex).ToArray(),reload,Log);
   if(reload)pulse.CheckReload(File.ReadAllText(Path.Combine(root,"saved.json")));
   updates.AddUpdateable(pulse);
  }
  if(pulse?.Done==true)Finish(pulse.Passed,"pulse_complete");
 }catch(Exception e){Log(new{kind="failure",error=e.ToString()});Finish(false,"exception");}}
 static void Finish(bool success,string reason){if(finished)return;finished=true;passed=success;
  try{
   if(success){
    GameManager.SaveProject(true,false);
    var xml=XElement.Load(Path.Combine(root,"doc","Worlds","World","Project.xml"));
    var pv=xml.Element("Subsystems").Elements("Values").Single(x=>(string)x.Attribute("Name")=="Players").Elements("Values").Single(x=>(string)x.Attribute("Name")=="Players").Elements("Values").ToArray();
    bool saved=pv.Length==5&&pv.All(x=>x.Elements("Value").Any(v=>(string)v.Attribute("Name")=="IsExternalActor"&&(string)v.Attribute("Value")=="True"));
    var entityIds=xml.Element("Entities").Elements("Entity").SelectMany(e=>e.Elements("Values").Where(v=>(string)v.Attribute("Name")=="Player")).SelectMany(v=>v.Elements("Value").Where(x=>(string)x.Attribute("Name")=="PlayerIndex")).Select(v=>(string)v.Attribute("Value")).Order().ToArray();
    saved=saved&&entityIds.SequenceEqual(pv.Select(v=>(string)v.Attribute("Name")).Order());
    if(!saved)throw new InvalidOperationException("Five persisted actor controls missing");
    if(!reload)File.WriteAllText(Path.Combine(root,"saved.json"),pulse.Capture());
    Log(new{kind="save_verified",saved,ids=pv.Select(v=>(string)v.Attribute("Name")).ToArray()});
   }
  }catch(Exception e){passed=false;Log(new{kind="save_failure",error=e.ToString()});}
  finally{Log(new{kind="result",passed,reload,reason,seconds=clock.Elapsed.TotalSeconds,updateFailures=updates?.UpdateFailureCount});pulse?.Dispose();Window.Close();}
 }
}
internal sealed class Pulse:IUpdateable,IDisposable {
 public float FloatUpdateOrder=>-15;
 readonly ComponentPlayer[] p;readonly bool reload;readonly Action<object> log;readonly ScapiInterestBackend backend;readonly Guid[] ids=Enumerable.Range(0,5).Select(_=>Guid.NewGuid()).ToArray();
 double elapsed;Vector3[] start;float[] food;bool captured;public bool Done{get;private set;}public bool Passed{get;private set;}
 public Pulse(ComponentPlayer[] players,bool reload,Action<object> log){p=players;this.reload=reload;this.log=log;var project=p[0].Project;backend=new ScapiInterestBackend(project.FindSubsystem<SubsystemTerrain>(true),project.FindSubsystem<SubsystemSpawn>(true));Sync();foreach(var actor in p)project.FindSubsystem<SubsystemTerrain>(true).TerrainUpdater.RemoveUpdateLocation(actor.PlayerData.PlayerIndex);log(new{kind="bound",actors=5,views=project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count,state=State()});}
 void Sync(){for(int i=0;i<5;i++){var v=p[i].ComponentBody.Position;backend.Upsert(ids[i],new Position(v.X,v.Y,v.Z),new InterestRadii(16,32));}backend.Publish();}
 object[] State()=>p.Select(x=>(object)new{index=x.PlayerData.PlayerIndex,name=x.PlayerData.Name,external=x.PlayerData.IsExternalActor,position=new[]{x.ComponentBody.Position.X,x.ComponentBody.Position.Y,x.ComponentBody.Position.Z},health=x.ComponentHealth.Health,food=x.ComponentVitalStats.Food,stamina=x.ComponentVitalStats.Stamina,sleep=x.ComponentVitalStats.Sleep,slots=Enumerable.Range(0,x.ComponentMiner.Inventory.SlotsCount).Select(i=>new{value=x.ComponentMiner.Inventory.GetSlotValue(i),count=x.ComponentMiner.Inventory.GetSlotCount(i)}).ToArray()}).ToArray();
 public string Capture()=>JsonSerializer.Serialize(State());
 public void CheckReload(string saved){using var a=JsonDocument.Parse(saved);using var b=JsonDocument.Parse(Capture());for(int i=0;i<5;i++){var x=a.RootElement[i];var y=b.RootElement[i];if(x.GetProperty("index").GetInt32()!=y.GetProperty("index").GetInt32()||x.GetProperty("slots").GetRawText()!=y.GetProperty("slots").GetRawText()||!y.GetProperty("external").GetBoolean()||new[]{"health","food","stamina","sleep"}.Any(k=>!float.IsFinite(y.GetProperty(k).GetSingle())||Math.Abs(x.GetProperty(k).GetSingle()-y.GetProperty(k).GetSingle())>0.001)||Enumerable.Range(0,3).Any(j=>Math.Abs(x.GetProperty("position")[j].GetSingle()-y.GetProperty("position")[j].GetSingle())>0.15))throw new InvalidOperationException("Reload mismatch");}log(new{kind="reload_match",passed=true});}
 public void Update(float dt){if(Done)return;try{
  if(p[0].Project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count!=0||p.Any(x=>x.GameWidget is not null||x.ComponentHealth.Health<=0))throw new InvalidOperationException("Unexpected viewport or dead actor");
  elapsed+=dt;Sync();
  if(!captured&&elapsed>=3){captured=true;elapsed=0;start=p.Select(x=>x.ComponentBody.Position).ToArray();food=p.Select(x=>x.ComponentVitalStats.Food).ToArray();log(new{kind="before",state=State()});}
  if(!captured)return;
  if(!reload){int i=(int)(elapsed/1.5);if(i<5&&elapsed-i*1.5<0.6)p[i].ComponentInput.SubmitExternalMovement(new Vector3(0,0,0.4f),Vector2.Zero);}
  if(elapsed>=10){var distances=p.Select((x,i)=>Vector2.Distance(start[i].XZ,x.ComponentBody.Position.XZ)).ToArray();var foodChanged=p.Select((x,i)=>x.ComponentVitalStats.Food<food[i]).ToArray();var stopSpeeds=p.Select(x=>x.ComponentBody.Velocity.Length()).ToArray();var keys=p[0].Project.FindSubsystem<SubsystemTerrain>(true).TerrainUpdater.m_updateParameters.Locations.Keys.ToArray();Passed=foodChanged.All(x=>x)&&stopSpeeds.All(v=>float.IsFinite(v)&&v<0.1f)&&keys.Length==5&&keys.All(k=>k<0)&&(reload?distances.All(d=>d<0.1f):distances.All(d=>d>0.01f&&d<5));log(new{kind="after",state=State(),distances,foodChanged,stopSpeeds,keys,Passed});Done=true;}
 }catch(Exception e){Done=true;Passed=false;log(new{kind="pulse_error",error=e.ToString()});}}
 public void Dispose()=>backend.Dispose();
}
