using System.Diagnostics;
using System.Text.Json;
using Engine;
using Game;

// A scripted native smoke test, not an AI controller. No external input, models or terrain writes.
internal static class NativeSmoke {
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static string root = AppContext.BaseDirectory;
    static bool created, bound, finished, reload, saveVerified;
    static Pulse pulse;
    static string lastScreen;
    static void Record(object value) => File.AppendAllText(Path.Combine(root,"smoke.jsonl"),JsonSerializer.Serialize(value)+Environment.NewLine);
    [STAThread]
    static int Main(string[] args) {
        if(args.Length!=1 || (args[0]!="--new-disposable-world" && args[0]!="--reload-observe")) { Console.Error.WriteLine("Explicit --new-disposable-world required.");return 2; }
        reload=args[0]=="--reload-observe";
        // Never load arbitrary worlds or mods.
        if(!reload && (Directory.Exists(Path.Combine(root,"doc")) || Directory.Exists(Path.Combine(root,"Mods")))) {
            Console.Error.WriteLine("Fresh isolated output directory required; preserve old evidence.");return 2;
        }
        if(!root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar)) { Console.Error.WriteLine("Run only from repository ignored work directory.");return 2; }
        if(reload && ((!File.Exists(Path.Combine(root,"isolated.marker")) || File.ReadAllText(Path.Combine(root,"isolated.marker"))!="westworld-native-smoke-v1") || (Directory.Exists(Path.Combine(root,"Mods")) && Directory.EnumerateFileSystemEntries(Path.Combine(root,"Mods")).Any())))return 2;
        File.WriteAllText(Path.Combine(root,"isolated.marker"),"westworld-native-smoke-v1");
        Record(new{kind="start",root,reload,game=typeof(Game.Program).Assembly.GetName().Version,scripted=true});
        Window.Created += () => Window.Frame += Frame;
        try { Game.Program.EntryPoint(); }
        catch(Exception e){Record(new{kind="fatal",error=e.ToString()});return 1;}
        return finished && saveVerified && pulse?.Passed==true ? 0 : 1;
    }
    static void Frame() {
        try {
            string screen=ScreensManager.CurrentScreen?.GetType().Name;
            if(screen!=lastScreen){lastScreen=screen;Record(new{kind="screen",screen,seconds=Clock.Elapsed.TotalSeconds});}
            if(Clock.Elapsed.TotalSeconds>180){Finish("wall_timeout");return;}
            if(!created && ScreensManager.CurrentScreen is MainMenuScreen && !ScreensManager.IsAnimating) {
                created=true;
                var world=reload ? WorldsManager.GetWorldInfo("app:/doc/Worlds/World") : WorldsManager.CreateWorld(new WorldSettings { Name="Westworld isolated native smoke", Seed="westworld-native-20261109", GameMode=GameMode.Survival, OriginalSerializationVersion=VersionsManager.SerializationVersion });
                if(world is null || world.WorldSettings.Name!="Westworld isolated native smoke")throw new InvalidOperationException("Not the isolated smoke world");
                Record(new{kind=reload?"world_reloaded":"world_created",world.DirectoryName});
                ScreensManager.SwitchScreen("GameLoading",world,null);
            }
            if(created && !bound && GameManager.Project is not null) {
                var players=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);
                if(reload && players.PlayersData.Count!=1)throw new InvalidOperationException("Reload must retain exactly one player");
                if(players.PlayersData.Count==0) {
                    // The same standard initial-player constructor as PlayerScreen, only in this fresh world.
                    players.AddPlayerData(new PlayerData(GameManager.Project){Name="NativeSmoke"});
                    ScreensManager.SwitchScreen("Game");
                }
                if(players.ComponentPlayers.Count==1 && players.ComponentPlayers[0].PlayerData.IsReadyForPlaying) {
                    bound=true;
                    if(reload && players.ComponentPlayers[0].PlayerData.PlayerIndex!=int.Parse(File.ReadAllText(Path.Combine(root,"bound-player.txt"))))throw new InvalidOperationException("Player binding changed");
                    File.WriteAllText(Path.Combine(root,"bound-player.txt"),players.ComponentPlayers[0].PlayerData.PlayerIndex.ToString());
                    pulse=new Pulse(players.ComponentPlayers[0],Record,reload);
                    if(reload){bool match=pulse.MatchesSaved(File.ReadAllText(Path.Combine(root,"saved-state.json")));Record(new{kind="reload_match",match});if(!match)throw new InvalidOperationException("Reload state mismatch");}
                    GameManager.Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(pulse);
                }
            }
            if(pulse?.Done==true){pulse.ValidateAfterPhysics();Finish(pulse.Passed?"completed":"native_check_failed");}
        } catch(Exception e){Record(new{kind="frame_error",error=e.ToString()});Finish("exception");}
    }
    static void Finish(string reason){
        if(finished)return;finished=true;
        try {
        if(pulse is not null)pulse.Stop();
        Record(new{kind="finish",reason,seconds=Clock.Elapsed.TotalSeconds,passed=pulse?.Passed??false});
        if(GameManager.Project is not null && pulse is not null) {
            GameManager.SaveProject(true,false);
            var xml=System.Xml.Linq.XElement.Load(Path.Combine(root,"doc","Worlds","World","Project.xml"));
            var playerValues=xml.Element("Subsystems")?.Elements("Values").SingleOrDefault(e=>(string)e.Attribute("Name")=="Players");
            var savedPlayers=playerValues?.Elements("Values").SingleOrDefault(e=>(string)e.Attribute("Name")=="Players");
            var ids=savedPlayers?.Elements("Values").Select(e=>(string)e.Attribute("Name")).ToArray();
            saveVerified=ids?.Length==1 && ids[0]==File.ReadAllText(Path.Combine(root,"bound-player.txt"));
            Record(new{kind="save_verified",saveVerified,ids});
            if(saveVerified && !reload)File.WriteAllText(Path.Combine(root,"saved-state.json"),pulse.Capture());
        }
        Record(new{kind="result",passed=saveVerified && pulse?.Passed==true});
        } catch(Exception e){saveVerified=false;Record(new{kind="save_error",error=e.ToString()});}
        finally {Window.Close();}
    }
}
internal sealed class Pulse : IUpdateable {
    public float FloatUpdateOrder => -5;
    readonly ComponentPlayer player;
    readonly Action<object> log;
    double elapsed;
    Vector3 initial,stopPosition;
    bool started,stopped;
    readonly Stopwatch wall = new();
    readonly bool observeOnly; float initialHealth; string inventoryBefore;
    string Inventory()=>JsonSerializer.Serialize(Enumerable.Range(0,player.ComponentMiner.Inventory.SlotsCount).Select(i=>new{value=player.ComponentMiner.Inventory.GetSlotValue(i),count=player.ComponentMiner.Inventory.GetSlotCount(i)}));
    public bool Done {get;private set;}
    public bool Passed {get;private set;}
    public Pulse(ComponentPlayer p,Action<object> record,bool observe){player=p;log=record;observeOnly=observe;}
        public string Capture()=>JsonSerializer.Serialize(State("saved_state"));
    public bool MatchesSaved(string saved) {
        using var expected=JsonDocument.Parse(saved);
        using var actual=JsonDocument.Parse(Capture());
        var a=actual.RootElement;var e=expected.RootElement;
        return a.GetProperty("player").GetInt32()==e.GetProperty("player").GetInt32()
            && a.GetProperty("health").GetSingle()==e.GetProperty("health").GetSingle()
            && a.GetProperty("slots").GetRawText()==e.GetProperty("slots").GetRawText()
            && Enumerable.Range(0,3).All(i=>Math.Abs(a.GetProperty("position")[i].GetDouble()-e.GetProperty("position")[i].GetDouble())<0.1);
    }
    public void ValidateAfterPhysics(){var v=player.ComponentBody.Velocity; var input=player.ComponentInput.m_playerInput; Passed=Passed && float.IsFinite(v.Length()) && v.Length()<0.1f && input.Move.LengthSquared()==0 && input.Look.LengthSquared()==0;log(State("after_physics"));}
    public void Stop(){player.ComponentInput.m_playerInput=default;}
    object State(string kind){
        var inventory=player.Entity.FindComponent<ComponentMiner>(true).Inventory;
        var slots=Enumerable.Range(0,inventory.SlotsCount).Select(i=>new{slot=i,value=inventory.GetSlotValue(i),count=inventory.GetSlotCount(i)}).ToArray();
        var pos=player.ComponentBody.Position;
        return new{kind,player=player.PlayerData.PlayerIndex,position=new[]{pos.X,pos.Y,pos.Z},health=player.ComponentHealth.Health,velocity=new[]{player.ComponentBody.Velocity.X,player.ComponentBody.Velocity.Y,player.ComponentBody.Velocity.Z},slots,elapsed,active=Window.IsActive};
    }
    public void Update(float dt){
        try {
            if(Done){Stop();return;}
            if(!float.IsFinite(dt)||dt<0)throw new InvalidOperationException("invalid dt");
            // Do not override a dead, sleeping or unready player's control.
            if(!Window.IsActive || !player.PlayerData.IsReadyForPlaying || player.ComponentHealth.Health<=0 || player.ComponentSleep.SleepFactor>0){Stop();return;}
            if(started && (player.ComponentHealth.Health<initialHealth || player.Project!=GameManager.Project))throw new InvalidOperationException("Health or project changed during pulse");
            elapsed+=dt;
            if(!started){
                if(elapsed<3)return;
                started=true;wall.Start();elapsed=0;initial=player.ComponentBody.Position;initialHealth=player.ComponentHealth.Health;inventoryBefore=Inventory();log(State("before"));
            }
            if(!observeOnly && elapsed<0.4 && wall.Elapsed.TotalSeconds<1) {
                var input=new PlayerInput();
                input.Move=new Vector3(0,0,0.25f);
                player.ComponentInput.m_playerInput=input;
            } else {
                Stop();
                if(!stopped){stopped=true;stopPosition=player.ComponentBody.Position;log(State("stop_requested"));}
                if(elapsed>=2.4){
                    var pos=player.ComponentBody.Position;
                    float distance=Vector3.Distance(initial,pos);
                    float horizontal=new Vector2(pos.X-initial.X,pos.Z-initial.Z).Length();
                    float drift=Vector3.Distance(stopPosition,pos);
                    // Report physics drift rather than claim instantaneous physical stop.
                    Passed=float.IsFinite(distance)&&(observeOnly?distance<0.1f:horizontal>0.01f&&distance<4)&&MathF.Abs(pos.Y-initial.Y)<0.5f&&float.IsFinite(drift)&&drift<1 && player.ComponentHealth.Health==initialHealth && Inventory()==inventoryBefore && player.ComponentBody.Velocity.Length()<0.1f;
                    log(State("after"));
                    log(new{kind="assertions",distance,horizontal,drift,Passed});
                    Done=true;
                }
            }
        }catch(Exception e){Stop();Done=true;Passed=false;log(new{kind="update_error",error=e.ToString()});}
    }
}
