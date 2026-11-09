using Engine;
using Game;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using System.Globalization;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;

internal static class ActorGate {
 static readonly Stopwatch clock=Stopwatch.StartNew();static readonly string root=AppContext.BaseDirectory;
 static bool reload,created,finished,passed,human;static Pulse pulse;static SubsystemUpdate updates;
 static void Log(object o){string s=JsonSerializer.Serialize(o);Console.WriteLine(s);File.AppendAllText(Path.Combine(root,"actors.jsonl"),s+Environment.NewLine);}
 static int Main(string[] args){
  if(args.Length<1||args.Length>2||(args.Length==2&&args[1]!="--human")||!new[]{"--new-disposable-world","--reload"}.Contains(args[0])||!root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar))return 2;
  reload=args[0]=="--reload";human=args.Length==2;
  if(!reload&&(Directory.Exists(Path.Combine(root,"doc"))||Directory.Exists(Path.Combine(root,"Mods"))))return 2;
  if(reload&&(!File.Exists(Path.Combine(root,"actor-gate.marker"))||File.ReadAllText(Path.Combine(root,"actor-gate.marker"))!=(human?"westworld-actor-human-gate-v1":"westworld-actor-gate-v1")))return 2;
  if(Directory.Exists(Path.Combine(root,"Mods"))&&Directory.EnumerateFileSystemEntries(Path.Combine(root,"Mods")).Any())return 2;
  File.WriteAllText(Path.Combine(root,"actor-gate.marker"),human?"westworld-actor-human-gate-v1":"westworld-actor-gate-v1");Log(new{kind="start",reload,human,scripted=true,humanControl="program-created native viewport, no human keypresses"});
  Window.Created+=()=>Window.Frame+=Frame;
  try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}
  return passed?0:1;
 }
 static void Frame(){try{
  if(finished)return;if(clock.Elapsed.TotalSeconds>120){Finish(false,"timeout");return;}
  if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){
   created=true;SettingsManager.VisibilityRange=32;
   var w=reload?WorldsManager.GetWorldInfo("app:/doc/Worlds/World"):WorldsManager.CreateWorld(new WorldSettings{Name=(human?"Westworld five actors plus human":"Westworld five external actors"),Seed="actors-cloud-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});
   if(w is null||w.WorldSettings.Name!=(human?"Westworld five actors plus human":"Westworld five external actors"))throw new InvalidOperationException("Wrong isolated world");
   ScreensManager.SwitchScreen("GameLoading",w,null);
  }
  if(GameManager.Project is null)return;
  updates??=GameManager.Project.FindSubsystem<SubsystemUpdate>(true);
  if(updates.UpdateFailureCount!=0)throw new InvalidOperationException(updates.LastUpdateFailure);
  var players=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);
  if(!reload&&players.PlayersData.Count==0){for(int i=0;i<5;i++)players.AddPlayerData(new PlayerData(GameManager.Project){Name="Actor_"+(i+1),IsExternalActor=true});if(human)players.AddPlayerData(new PlayerData(GameManager.Project){Name="HumanObserver"});ScreensManager.SwitchScreen("Game");}
  if(players.PlayersData.Count!=(human?6:5)||players.PlayersData.Count(p=>p.IsExternalActor)!=5)throw new InvalidOperationException("Identity/control mismatch");
  if(pulse is null&&players.ComponentPlayers.Count==(human?6:5)&&players.PlayersData.All(p=>p.IsReadyForPlaying)){
   ScreensManager.SwitchScreen("Game");
   pulse=new Pulse(players.ComponentPlayers.OrderBy(p=>p.PlayerData.PlayerIndex).ToArray(),reload,human,Log);
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
    bool saved=pv.Length==(human?6:5)&&pv.Count(x=>x.Elements("Value").Any(v=>(string)v.Attribute("Name")=="IsExternalActor"&&(string)v.Attribute("Value")=="True"))==5;
    var entityIds=xml.Element("Entities").Elements("Entity").SelectMany(e=>e.Elements("Values").Where(v=>(string)v.Attribute("Name")=="Player")).SelectMany(v=>v.Elements("Value").Where(x=>(string)x.Attribute("Name")=="PlayerIndex")).Select(v=>(string)v.Attribute("Value")).Order().ToArray();
    saved=saved&&pulse.VerifySaved(xml)&&entityIds.SequenceEqual(pv.Select(v=>(string)v.Attribute("Name")).Order());
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
 readonly ComponentPlayer[] p;readonly bool reload,human;readonly Action<object> log;readonly ScapiInterestBackend backend;readonly Guid[] ids=Enumerable.Range(0,6).Select(_=>Guid.NewGuid()).ToArray();
 double elapsed;int previousTarget=-1;int isolationChecks;readonly int[] observedInputs=new int[5];Vector3[] start;float[] food;bool captured;public bool Done{get;private set;}public bool Passed{get;private set;}
 public Pulse(ComponentPlayer[] players,bool reload,bool human,Action<object> log){p=players;this.reload=reload;this.human=human;this.log=log;var project=p[0].Project;backend=new ScapiInterestBackend(project.FindSubsystem<SubsystemTerrain>(true),project.FindSubsystem<SubsystemSpawn>(true));Sync();foreach(var actor in p.Where(x=>x.PlayerData.IsExternalActor))project.FindSubsystem<SubsystemTerrain>(true).TerrainUpdater.RemoveUpdateLocation(actor.PlayerData.PlayerIndex);if(human){bool rejected=false;try{p[5].ComponentInput.SubmitExternalMovement(new Vector3(0,0,0.4f),Vector2.Zero);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new InvalidOperationException("Human accepted AI input");log(new{kind="human_external_input_rejected",rejected});}log(new{kind="bound",actors=5,human,views=project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count,state=State()});}
 void Sync(){for(int i=0;i<p.Length;i++){var v=p[i].ComponentBody.Position;backend.Upsert(ids[i],new Position(v.X,v.Y,v.Z),new InterestRadii(16,32));}backend.Publish();}
 object[] State()=>p.Select(x=>(object)new{index=x.PlayerData.PlayerIndex,name=x.PlayerData.Name,external=x.PlayerData.IsExternalActor,position=new[]{x.ComponentBody.Position.X,x.ComponentBody.Position.Y,x.ComponentBody.Position.Z},health=x.ComponentHealth.Health,food=x.ComponentVitalStats.Food,stamina=x.ComponentVitalStats.Stamina,sleep=x.ComponentVitalStats.Sleep,slots=Enumerable.Range(0,x.ComponentMiner.Inventory.SlotsCount).Select(i=>new{value=x.ComponentMiner.Inventory.GetSlotValue(i),count=x.ComponentMiner.Inventory.GetSlotCount(i)}).ToArray()}).ToArray();
 public string Capture()=>JsonSerializer.Serialize(State());
 public bool VerifySaved(XElement xml){
  foreach(var actor in p){
   var entity=xml.Element("Entities").Elements("Entity").Single(e=>e.Elements("Values").Where(v=>(string)v.Attribute("Name")=="Player").SelectMany(v=>v.Elements("Value")).Any(v=>(string)v.Attribute("Name")=="PlayerIndex"&&(string)v.Attribute("Value")==actor.PlayerData.PlayerIndex.ToString(CultureInfo.InvariantCulture)));
   string Value(string component,string name)=>(string)entity.Elements("Values").Single(v=>(string)v.Attribute("Name")==component).Elements("Value").Single(v=>(string)v.Attribute("Name")==name).Attribute("Value");
   var coords=Value("Body","Position").Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();var pos=actor.ComponentBody.Position;
   if(coords.Length!=3||new[]{pos.X,pos.Y,pos.Z}.Where((v,i)=>!float.IsFinite(coords[i])||MathF.Abs(v-coords[i])>0.0001f).Any())return false;
   foreach(var pair in new[]{("Health","Health",actor.ComponentHealth.Health),("VitalStats","Food",actor.ComponentVitalStats.Food),("VitalStats","Stamina",actor.ComponentVitalStats.Stamina),("VitalStats","Sleep",actor.ComponentVitalStats.Sleep)}){
    float value=float.Parse(Value(pair.Item1,pair.Item2),CultureInfo.InvariantCulture);if(!float.IsFinite(value)||MathF.Abs(value-pair.Item3)>0.00001f)return false;
   }
  }return true;
 }
 public void CheckReload(string saved){using var a=JsonDocument.Parse(saved);using var b=JsonDocument.Parse(Capture());for(int i=0;i<p.Length;i++){var x=a.RootElement[i];var y=b.RootElement[i];if(x.GetProperty("index").GetInt32()!=y.GetProperty("index").GetInt32()||x.GetProperty("slots").GetRawText()!=y.GetProperty("slots").GetRawText()||x.GetProperty("external").GetBoolean()!=y.GetProperty("external").GetBoolean()||new[]{"health","food","stamina","sleep"}.Any(k=>!float.IsFinite(y.GetProperty(k).GetSingle())||Math.Abs(x.GetProperty(k).GetSingle()-y.GetProperty(k).GetSingle())>0.001)||Enumerable.Range(0,3).Any(j=>Math.Abs(x.GetProperty("position")[j].GetSingle()-y.GetProperty("position")[j].GetSingle())>0.15))throw new InvalidOperationException("Reload mismatch");}log(new{kind="reload_match",passed=true});}
 public void Update(float dt){if(Done)return;try{
  if(p[0].Project.FindSubsystem<SubsystemGameWidgets>(true).GameWidgets.Count!=(human?1:0)||p.Any(x=>(x.PlayerData.IsExternalActor&&x.GameWidget is not null)||x.ComponentHealth.Health<=0)||(human&&p[5].GameWidget is null))throw new InvalidOperationException("Unexpected viewport or dead actor");
  var moving=p.Take(5).Select((x,i)=>new{x,i}).Where(a=>a.x.ComponentInput.PlayerInput.Move.LengthSquared()>0).Select(a=>a.i).ToArray();
  if(moving.Length>1||(moving.Length==1&&moving[0]!=previousTarget))throw new InvalidOperationException("AI input routed to wrong actor");
  if(moving.Length==1)observedInputs[moving[0]]++;
  if(human&&p[5].ComponentInput.PlayerInput.Move.LengthSquared()!=0)throw new InvalidOperationException("Unexpected human movement input");
  isolationChecks++;previousTarget=-1;elapsed+=dt;Sync();
  if(!captured&&elapsed>=3){captured=true;elapsed=0;start=p.Select(x=>x.ComponentBody.Position).ToArray();food=p.Select(x=>x.ComponentVitalStats.Food).ToArray();log(new{kind="before",state=State()});}
  if(!captured)return;
  if(!reload){int i=(int)(elapsed/1.5);if(i<5&&elapsed-i*1.5<0.6){p[i].ComponentInput.SubmitExternalMovement(new Vector3(0,0,0.4f),Vector2.Zero);previousTarget=i;}}
  if(elapsed>=10){var distances=p.Select((x,i)=>Vector2.Distance(start[i].XZ,x.ComponentBody.Position.XZ)).ToArray();var foodChanged=p.Select((x,i)=>x.ComponentVitalStats.Food<food[i]).ToArray();var stopSpeeds=p.Select(x=>x.ComponentBody.Velocity.Length()).ToArray();var keys=p[0].Project.FindSubsystem<SubsystemTerrain>(true).TerrainUpdater.m_updateParameters.Locations.Keys.ToArray();Passed=(reload||observedInputs.All(n=>n>0))&&(!human||distances[5]<0.1f)&&foodChanged.All(x=>x)&&stopSpeeds.All(v=>float.IsFinite(v)&&v<0.1f)&&keys.Count(k=>k<0)==p.Length&&keys.Count(k=>k>=0)==(human?1:0)&&(reload?distances.All(d=>d<0.1f):distances.Take(5).All(d=>d>0.01f&&d<5));log(new{kind="after",state=State(),distances,foodChanged,stopSpeeds,keys,isolationChecks,observedInputs,Passed});Done=true;}
 }catch(Exception e){Done=true;Passed=false;log(new{kind="pulse_error",error=e.ToString()});}}
 public void Dispose()=>backend.Dispose();
}
