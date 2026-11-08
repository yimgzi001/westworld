using System.Text.Json;
using Engine;
using Game;
using Game.NetWork;
using GameEntitySystem;
namespace SurvivalcraftAgentLab;
// Experimental loader; single-player loop verified on earlier input hook; latest multi-client scheduling still under test.
// Opt in only for a private disposable world: SC_AGENT_DIR=/absolute/private/path.
public sealed class BridgeMod : ModLoader
{
    sealed class Session(ComponentPlayer p) { public ComponentPlayer Player=p; public AgentBridge Bridge=new(p); public double Until; public Command? Active; public long LastId=-1; }
    public sealed record Command(long Id,string Action,float X=0,float Y=0,float Z=0,bool Jump=false,float Seconds=.2f,int Slot=-1);
    readonly Dictionary<string,Session> sessions=new();
    readonly HashSet<ComponentInput> retiredInputs=new(); double nextRoster; string? directory; double time; double nextObservation;
    public override void __ModInitialize() {
        directory=Environment.GetEnvironmentVariable("SC_AGENT_DIR");
        if(string.IsNullOrWhiteSpace(directory))return;
        directory=Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        GameManager.ProjectLoaded += RestoreBots;
        ModsManager.RegisterHook("OnCreatureInjure",this);
        ModsManager.RegisterHook("OnPlayerSpawned",this);
        ModsManager.RegisterHook("SubsystemUpdate",this);
        ModsManager.RegisterHook("UpdateInput",this);
    }
    int injuryEvents;
    public override void OnCreatureInjure(ComponentHealth h,float amount,ComponentCreature attacker,bool ignoreInvulnerability,string cause,out bool Skip) {
        Skip=false; // Observation only: never suppress or alter game damage.
        if(directory==null || injuryEvents>=1000)return;
        var p=h.Entity.FindComponent<ComponentPlayer>();if(p==null)return;
        injuryEvents++;
        try {File.AppendAllText(Path.Combine(directory,"injury-attempts.jsonl"),JsonSerializer.Serialize(new {tickSeconds=time,alias=p.PlayerData.Name,amount,healthBefore=h.Health,air=h.Air,ignoreInvulnerability,cause,hasAttacker=attacker!=null})+"\n");}catch { }
    }
    void RestoreBots(Project project) {
        if(directory==null || CommonLib.WorkType!=WorkType.Local)return;
        int restored=0;
        foreach(var pd in project.FindSubsystem<SubsystemPlayers>(true).PlayersData)if(pd.Name.StartsWith("LabBot")){pd.SetMain();restored++;}
        File.WriteAllText(Path.Combine(directory,"restored-count.txt"),restored.ToString());
    }
    public override bool OnPlayerSpawned(PlayerData.SpawnMode mode,ComponentPlayer p,Vector3 pos) {
        if(directory!=null)sessions[p.PlayerGuid.ToString()]=new(p);
        return false;
    }
    public override void SubsystemUpdate(float dt) {
        if(directory==null)return;
        time+=dt;
        var anchor=sessions.Values.FirstOrDefault();
        if(anchor!=null && anchor.Player.PlayerData.IsReadyForPlaying) {
            var sp=anchor.Player.Project.FindSubsystem<SubsystemPlayers>(true);
            if(time>=nextRoster) {
                nextRoster=time+1;
                try {
                    int target=1; var targetPath=Path.Combine(directory,"target-count");
                    if(File.Exists(targetPath))int.TryParse(File.ReadAllText(targetPath),out target);
                    if(!new[]{1,2,4,8,16,32,50}.Contains(target))throw new InvalidDataException("Only validated staircase target values allowed");
                    var bots=sp.PlayersData.Where(pd=>pd.Name.StartsWith("LabBot")).ToList();
                    foreach(var bot in bots)bot.SetMain();
                    while(bots.Count>target-1) {
                        var bot=bots[^1];bots.RemoveAt(bots.Count-1);
                        if(bot.ComponentPlayer!=null) {
                            var input=bot.ComponentPlayer.Entity.FindComponent<ComponentInput>(true);if(input!=null)retiredInputs.Add(input);
                            sessions.Remove(bot.ComponentPlayer.PlayerGuid.ToString());
                        }
                        sp.RemovePlayerData(bot);
                    }
                    while(bots.Count<target-1) {
                        int ix=Enumerable.Range(1,49).First(i=>!bots.Any(b=>b.Name=="LabBot"+i));
                        var bot=new PlayerData(anchor.Player.Project) {PlayerGUID=Guid.NewGuid(),Name="LabBot"+ix,InputDevice=WidgetInputDevice.None,SpawnPosition=anchor.Player.ComponentBody.Position+new Vector3((ix%7-3)*1.5f,0,-(ix/7+2)*1.5f)};
                        bot.SetMain();sp.AddPlayerData(bot);bots.Add(bot);
                    }
                    File.WriteAllText(Path.Combine(directory,"roster.json"),JsonSerializer.Serialize(new {target,processId=Environment.ProcessId,players=sp.PlayersData.Select(pd=>new {alias=pd.Name,main=pd.IsMainPlayer,ready=pd.IsReadyForPlaying,health=pd.ComponentPlayer?.ComponentHealth.Health})}));
                }catch(Exception e){File.WriteAllText(Path.Combine(directory,"roster-error.txt"),e.ToString());}
            }
            foreach(var pd in sp.PlayersData)if(pd.GameWidget!=null && pd.IsReadyForPlaying)pd.GameWidget.IsVisible=!File.Exists(Path.Combine(directory,"hide-views"));
        }
        if(anchor!=null)foreach(var p in anchor.Player.Project.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers) {
            string key=p.PlayerGuid.ToString();
            if(!sessions.TryGetValue(key,out var old) || old.Player!=p)sessions[key]=new Session(p);
        }
        foreach(var (id,s) in sessions) {
            string command=Path.Combine(directory,id+".command.json");
            try {
                if(s.Player.PlayerData.IsMainPlayer && File.Exists(command)) {
                    // Controller must atomically rename a completed <= 4 KiB file into place.
                    if(new FileInfo(command).Length>4096)throw new InvalidDataException("Command too large");
                    var c=JsonSerializer.Deserialize<Command>(File.ReadAllText(command));
                    File.Delete(command);
                    if(c!=null && !CommandPolicy.Valid(c.Action,c.X,c.Y,c.Z,c.Seconds,c.Slot,s.Player.ComponentMiner.Inventory.SlotsCount))throw new InvalidDataException("Invalid command fields");
                    if(c!=null && c.Id>s.LastId) { s.LastId=c.Id;s.Active=c;s.Until=time+Math.Clamp(c.Seconds,0,.5f); }
                }
                if(s.Player.PlayerData.IsMainPlayer && s.Active!=null && time>=s.Until) {s.Bridge.Stop();s.Active=null;}
                if(time>=nextObservation) {
                    string state=Path.Combine(directory,id+".state.json");
                    File.WriteAllText(state+".tmp",JsonSerializer.Serialize(new {tickSeconds=time,lastCommand=s.LastId,playerAlias=s.Player.PlayerData.Name,processId=Environment.ProcessId,state=s.Bridge.Observe()}));
                    File.Move(state+".tmp",state,true);
                }
            } catch(Exception e) { s.Bridge.Stop(); s.Active=null; File.WriteAllText(Path.Combine(directory,id+".error.txt"),e.ToString()); }
        }
        if(time>=nextObservation)nextObservation=time+.2;
    }
    sealed class InputRunner(BridgeMod owner,ComponentInput input) : IUpdateable {
        public UpdateOrder UpdateOrder => (UpdateOrder)(-5);
        public void Update(float dt) => owner.ApplyInput(input);
    }
    readonly HashSet<ComponentInput> registeredInputs=new();
    public override void UpdateInput(ComponentInput input,WidgetInput widget) {
        if(!input.m_componentPlayer.PlayerData.IsMainPlayer)return;
        if(registeredInputs.Add(input))input.Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(new InputRunner(this,input));
    }
    void ApplyInput(ComponentInput input) {
        if(retiredInputs.Contains(input))return;
        var p=input.m_componentPlayer;
        if(!sessions.TryGetValue(p.PlayerGuid.ToString(),out var s)) {s=new Session(p);sessions[p.PlayerGuid.ToString()]=s;}
        var a=s.Active; if(a==null || time>=s.Until || !p.PlayerData.IsReadyForPlaying || p.ComponentHealth.Health<=0)return;
        if(a.Action=="move") {input.m_playerInput.Move=new Vector3(Math.Clamp(a.X,-1,1),0,Math.Clamp(a.Z,-1,1));input.m_playerInput.SneakMove=input.m_playerInput.Move;input.m_playerInput.Jump=a.Jump;}
        else if(a.Action=="select") {input.m_playerInput.SelectInventorySlot=a.Slot;s.Active=null;}
        else if(a.Action=="dig" || a.Action=="place") {
            var d=new Vector3(a.X,a.Y,a.Z);
            if(!float.IsFinite(d.LengthSquared()) || d.LengthSquared()<.0001f)return;
            var model=p.Entity.FindComponent<ComponentCreatureModel>(true);
            var ray=new Ray3(model!.EyePosition,Vector3.Normalize(d));
            if(a.Action=="dig")input.m_playerInput.Dig=ray;
            else {input.m_playerInput.Interact=ray;s.Active=null;}
        }
    }
    public override void ModDispose() {GameManager.ProjectLoaded -= RestoreBots;foreach(var s in sessions.Values)s.Bridge.Stop();sessions.Clear();}
}
