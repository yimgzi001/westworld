using System.Text.Json;
using Engine;
using Game;
namespace SurvivalcraftAgentLab;
// Experimental loader; single-player loop verified on earlier input hook; latest multi-client scheduling still under test.
// Opt in only for a private disposable world: SC_AGENT_DIR=/absolute/private/path.
public sealed class BridgeMod : ModLoader
{
    sealed class Session(ComponentPlayer p) { public ComponentPlayer Player=p; public AgentBridge Bridge=new(p); public double Until; public Command? Active; public long LastId=-1; }
    public sealed record Command(long Id,string Action,float X=0,float Y=0,float Z=0,bool Jump=false,float Seconds=.2f,int Slot=-1);
    readonly Dictionary<string,Session> sessions=new();
    string? directory; double time; double nextObservation;
    public override void __ModInitialize() {
        directory=Environment.GetEnvironmentVariable("SC_AGENT_DIR");
        if(string.IsNullOrWhiteSpace(directory))return;
        directory=Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        ModsManager.RegisterHook("OnPlayerSpawned",this);
        ModsManager.RegisterHook("SubsystemUpdate",this);
        ModsManager.RegisterHook("UpdateInput",this);
    }
    public override bool OnPlayerSpawned(PlayerData.SpawnMode mode,ComponentPlayer p,Vector3 pos) {
        if(directory!=null)sessions[p.PlayerGuid.ToString()]=new(p);
        return false;
    }
    public override void SubsystemUpdate(float dt) {
        if(directory==null)return;
        time+=dt;
        var anchor=sessions.Values.FirstOrDefault();
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
                    if(c!=null && !float.IsFinite(c.Seconds))throw new InvalidDataException("Non-finite duration");
                    if(c!=null && c.Id>s.LastId) { s.LastId=c.Id;s.Active=c;s.Until=time+Math.Clamp(c.Seconds,0,.5f); }
                }
                if(s.Player.PlayerData.IsMainPlayer && s.Active!=null && time>=s.Until) {s.Bridge.Stop();s.Active=null;}
                if(time>=nextObservation) {
                    string state=Path.Combine(directory,id+".state.json");
                    File.WriteAllText(state+".tmp",JsonSerializer.Serialize(new {tickSeconds=time,lastCommand=s.LastId,state=s.Bridge.Observe()}));
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
    public override void ModDispose() {foreach(var s in sessions.Values)s.Bridge.Stop();sessions.Clear();}
}
