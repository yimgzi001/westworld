using System.Diagnostics;
using System.Text.Json;
using Engine;
using Game;
using RecoveryVision;
using ScapiDualSmoke;

internal static class DualSmoke {
    static readonly Stopwatch clock=Stopwatch.StartNew();
    static readonly string root=AppContext.BaseDirectory;
    static bool created,attached,finished,passed;
    static Driver driver;
    static void Log(object entry)=>File.AppendAllText(Path.Combine(root,"dual.jsonl"),JsonSerializer.Serialize(entry)+Environment.NewLine);
    [STAThread] static int Main(string[] args){
        if(args.Length!=1||args[0]!="--new-disposable-dual-world"||
           !root.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar)||
           Directory.Exists(Path.Combine(root,"doc"))||Directory.Exists(Path.Combine(root,"Mods")))return 2;
        Log(new{kind="start",root,scripted=true});
        Window.Created+=()=>Window.Frame+=Frame;
        try{Game.Program.EntryPoint();}catch(Exception e){Log(new{kind="fatal",error=e.ToString()});return 1;}
        return finished&&passed?0:1;
    }
    static void Frame(){
        try{
            if(clock.Elapsed.TotalSeconds>180){Finish(false,"wall_timeout");return;}
            if(!created&&ScreensManager.CurrentScreen is MainMenuScreen&&!ScreensManager.IsAnimating){
                created=true;
                var world=WorldsManager.CreateWorld(new WorldSettings{Name="Westworld dual isolated",Seed="westworld-native-20261109",GameMode=GameMode.Survival,OriginalSerializationVersion=VersionsManager.SerializationVersion});
                Log(new{kind="world_created",world.DirectoryName});
                ScreensManager.SwitchScreen("GameLoading",world,null);
            }
            if(created&&!attached&&GameManager.Project is not null){
                var ps=GameManager.Project.FindSubsystem<SubsystemPlayers>(true);
                if(ps.PlayersData.Count==0){
                    ps.AddPlayerData(new PlayerData(GameManager.Project){Name="ProbeA"});
                    ps.AddPlayerData(new PlayerData(GameManager.Project){Name="ProbeB"});
                    ScreensManager.SwitchScreen("Game");
                }
                if(ps.ComponentPlayers.Count==2&&ps.ComponentPlayers.All(p=>p.PlayerData.IsReadyForPlaying)){
                    attached=true;driver=new Driver(ps.ComponentPlayers.ToArray(),clock,Log);
                    GameManager.Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(driver);
                }
            }
            if(driver?.Done==true){
                bool post=driver.AfterPhysics();
                Finish(driver.Passed&&post,"completed");
            }
        }catch(Exception e){Log(new{kind="frame_error",error=e.ToString()});Finish(false,"exception");}
    }
    static void Finish(bool ok,string reason){
        if(finished)return;finished=true;
        try{
            driver?.Stop();
            if(GameManager.Project is not null)GameManager.SaveProject(true,false);
            var xml=System.Xml.Linq.XElement.Load(Path.Combine(root,"doc","Worlds","World","Project.xml"));
            var p=xml.Element("Subsystems")?.Elements("Values").Single(e=>(string)e.Attribute("Name")=="Players").Elements("Values").Single(e=>(string)e.Attribute("Name")=="Players");
            var ids=p?.Elements("Values").Select(e=>(string)e.Attribute("Name")).ToArray();
            passed=ok&&ids?.Length==2&&driver is not null&&ids.OrderBy(x=>x).SequenceEqual(driver.PlayerIds.Select(x=>x.ToString()).OrderBy(x=>x));
            Log(new{kind="result",reason,passed,ids,seconds=clock.Elapsed.TotalSeconds});
        }catch(Exception e){passed=false;Log(new{kind="save_error",error=e.ToString()});}
        finally{Window.Close();}
    }
}
internal sealed class Driver : IUpdateable{
    public float FloatUpdateOrder=>-5;
    readonly ComponentPlayer[] players;
    readonly Stopwatch clock;
    readonly Action<object> log;
    readonly Guid session=Guid.NewGuid();
    readonly ActionQueue queue;
    double settled,stageAt;
    int stage=-1;
    bool stopSubmitted;
    Vector3[] before;
    float[] health;
    string[] inventories;
    int accepted,completed,rejected; bool observedStop,observedExpiry;
    readonly HashSet<string> rejections=new();
    public int[] PlayerIds=>players.Select(p=>p.PlayerData.PlayerIndex).ToArray();
    public bool Done{get;private set;}
    public bool Passed{get;private set;}=true;
    public Driver(ComponentPlayer[] ps,Stopwatch c,Action<object> l){players=ps;clock=c;log=l;queue=new(session,ps.Select(p=>p.PlayerData.PlayerIndex));}
    string Inventory(ComponentPlayer p)=>JsonSerializer.Serialize(Enumerable.Range(0,p.ComponentMiner.Inventory.SlotsCount).Select(i=>new{v=p.ComponentMiner.Inventory.GetSlotValue(i),n=p.ComponentMiner.Inventory.GetSlotCount(i)}));
    public void Stop(){foreach(var p in players)p.ComponentInput.m_playerInput=default;}
    public bool AfterPhysics(){
        bool ok=players.All(p=>p.ComponentInput.m_playerInput.Move.LengthSquared()==0&&float.IsFinite(p.ComponentBody.Velocity.Length())&&p.ComponentBody.Velocity.Length()<0.1f);
        log(new{kind="post_physics",ok,velocities=players.Select(p=>p.ComponentBody.Velocity.Length()).ToArray()});return ok;
    }
    void Submit(ActionRequest r){
        // This worker handles only immutable requests and the queue; no game objects.
        _=Task.Run(()=>queue.Submit(r,clock.Elapsed.TotalSeconds));
    }
    void BeginStage(int value){
        stage=value;stageAt=clock.Elapsed.TotalSeconds;stopSubmitted=false;
        before=players.Select(p=>p.ComponentBody.Position).ToArray();
        health=players.Select(p=>p.ComponentHealth.Health).ToArray();
        inventories=players.Select(Inventory).ToArray();
        int id=players[stage].PlayerData.PlayerIndex;
        long request=stage*10+1;
        var move=new ActionRequest(session,id,request,ActionKind.Move,stageAt,stageAt+2,0.4,new(0,0,0.25f));
        Submit(move);Submit(move); // one valid request and an intentional replay
        Submit(move with{Session=Guid.NewGuid(),RequestId=request+100});
        Submit(move with{PlayerId=99999,RequestId=request+101});
        log(new{kind="stage_start",stage,actor=id,positions=before.Select(v=>new[]{v.X,v.Y,v.Z}).ToArray()});
    }
    public void Update(float dt){
        try{
            if(Done){Stop();return;}
            if(!float.IsFinite(dt)||dt<0)throw new InvalidOperationException("invalid dt");
            if(players.Any(p=>!p.PlayerData.IsReadyForPlaying||p.ComponentHealth.Health<=0||p.ComponentSleep.SleepFactor>0||p.Project!=GameManager.Project)||!Window.IsActive){
                Stop();if(stage>=0)throw new InvalidOperationException("control state changed");return;
            }
            if(stage<0){
                settled+=dt;if(settled<3)return;
                foreach(var p in players){var perception=Perception.Capture(p);log(new{kind="perception",player=p.PlayerData.PlayerIndex,perception});Passed&=perception.Visible.Length>0;}
                BeginStage(0);
            }
            double now=clock.Elapsed.TotalSeconds;
            if(stage==0&&!stopSubmitted&&now-stageAt>=0.2){stopSubmitted=true;Submit(new(session,players[0].PlayerData.PlayerIndex,2,ActionKind.Stop,now,now+1,0,new(0,0,0)));}
            var tick=queue.Tick(now);
            foreach(var e in tick.Events){log(new{kind="queue_event",e});if(e.Status=="accepted")accepted++;if(e.Status=="completed")completed++;if(e.Status=="rejected"){rejected++;rejections.Add(e.Reason);}if(e.Status=="completed"&&e.Reason=="stopped")observedStop=true;if(e.Status=="completed"&&e.Reason=="duration_or_deadline")observedExpiry=true;}
            foreach(var p in players){var intent=tick.Intents[p.PlayerData.PlayerIndex];p.ComponentInput.m_playerInput=new PlayerInput{Move=new Vector3(intent.X,intent.Y,intent.Z)};}
            if(players.Where((p,i)=>p.ComponentHealth.Health<health[i]).Any())throw new InvalidOperationException("health dropped");
            if(now-stageAt>=2.5){
                Stop();
                var horizontal=players.Select((p,i)=>new Vector2(p.ComponentBody.Position.X-before[i].X,p.ComponentBody.Position.Z-before[i].Z).Length()).ToArray();
                bool ok=horizontal[stage]>0.005f&&horizontal[stage]<4&&horizontal[1-stage]<0.1f&&players.Select((p,i)=>MathF.Abs(p.ComponentBody.Position.Y-before[i].Y)<0.5f&&p.ComponentHealth.Health==health[i]&&Inventory(p)==inventories[i]).All(x=>x);
                Passed&=ok;log(new{kind="stage_result",stage,horizontal,ok});
                if(stage==0)BeginStage(1);
                else{Done=true;Passed&=accepted==3&&completed==3&&rejected==6&&observedStop&&observedExpiry&&new[]{"wrong_session","unknown_player","replayed_id"}.All(rejections.Contains);log(new{kind="queue_totals",accepted,completed,rejected,Passed});}
            }
        }catch(Exception e){Stop();Done=true;Passed=false;log(new{kind="driver_error",error=e.ToString()});}
    }
}
internal static class Perception{
    internal record Cell(int X,int Y,int Z,int Contents,int Data,float Distance);
    internal record Observation(float[] Eye,Cell[] Visible,int Rays,int NoHit,int RejectedUnloaded);
public static Observation Capture(ComponentPlayer p){
        var subsystem=p.Project.FindSubsystem<SubsystemTerrain>(true);
        var terrain=subsystem.Terrain;
        var eye=p.ComponentCreatureModel.EyePosition;
        var matrix=Matrix.CreateFromQuaternion(p.ComponentCreatureModel.EyeRotation);
        var visible=new Dictionary<(int,int,int),Cell>();int rays=0,noHit=0,unknown=0;
        bool Loaded(int x,int y,int z)=>terrain.GetChunkAtCell(x,y,z)?.State==TerrainChunkState.Valid;
        for(int h=-4;h<=4;h++)
        for(int v=-3;v<=3;v++){
            var direction=Vector3.Normalize(matrix.Forward+matrix.Right*(h/4f*0.9f)+matrix.Up*(v/3f*(float)VisionCore.VerticalSlope*0.9f));
            rays++;
            var hit=subsystem.Raycast(eye,eye+direction*8,false,true,(_,_)=>true);
            if(hit is null){noHit++;continue;}
            var c=hit.Value.CellFace;
            // Native first-hit collision boxes decide occlusion; verify actual segment bounds below.
            var point=eye+direction*hit.Value.Distance;
            bool loaded=eye.Y>=0&&eye.Y<TerrainChunk.Height&&point.Y>=0&&point.Y<TerrainChunk.Height&&Loaded(c.X,c.Y,c.Z);
            // Every chunk intersecting this XZ rectangle covers the actual segment, including
            // boundary contacts. This may reject extra chunks but cannot skip an unloaded ray chunk.
            for(int a=Terrain.ToCell(MathF.Min(eye.X,point.X))-1;a<=Terrain.ToCell(MathF.Max(eye.X,point.X))+1;a++)
            for(int b=Terrain.ToCell(MathF.Min(eye.Z,point.Z))-1;b<=Terrain.ToCell(MathF.Max(eye.Z,point.Z))+1;b++)
                loaded &= Loaded(a,c.Y,b);
            if(!loaded){unknown++;continue;}
            int value=terrain.GetCellValue(c.X,c.Y,c.Z);
            visible[(c.X,c.Y,c.Z)]=new(c.X,c.Y,c.Z,Terrain.ExtractContents(value),Terrain.ExtractData(value),hit.Value.Distance);
        }
        return new(new[]{eye.X,eye.Y,eye.Z},visible.Values.ToArray(),rays,noHit,unknown);
    }
}
