using System.Text.Json;
using Engine;
using Game;
using GameEntitySystem;
namespace RecoveryVision;
// New recovery implementation. Never load alongside prior agent bridges.
public sealed class BridgeMod : ModLoader {
 record Command(long Id,string Session,float Yaw,float Seconds,long IssuedUnixMs);
 readonly string session=Guid.NewGuid().ToString("N");
 string? directory; ComponentPlayer? player; Command? active;
 readonly HashSet<ComponentInput> registered=new();
 readonly System.Diagnostics.Stopwatch clock=new(); double gameTime,until,usedActive,nextObs; int pulses;long lastId;
 string outcome="observing"; bool terminated; Vector3? previousForward; double accumulatedDegrees;
 public override void __ModInitialize(){
  directory=Environment.GetEnvironmentVariable("SC_RECOVERY_VISION_DIR");if(string.IsNullOrWhiteSpace(directory))return;
  directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
  ModsManager.RegisterHook("OnPlayerSpawned",this);ModsManager.RegisterHook("SubsystemUpdate",this);ModsManager.RegisterHook("UpdateInput",this);
 }
 public override bool OnPlayerSpawned(PlayerData.SpawnMode mode,ComponentPlayer p,Vector3 pos){
  if(directory!=null){if(player!=null&&player!=p){terminated=true;outcome="multiple_players_rejected";}else {player=p;if(!clock.IsRunning)clock.Start();}}return false;
 }
 bool Safe(out object flags){
  var p=player;var v=p?.Entity.FindComponent<ComponentVitalStats>();var sl=p?.Entity.FindComponent<ComponentSleep>();
  bool ready=p?.PlayerData.IsReadyForPlaying==true, standing=p?.ComponentBody.StandingOnValue.HasValue==true||p?.ComponentBody.StandingOnBody!=null;
  bool awake=sl!=null&&!sl.IsSleeping, healthy=p!=null&&float.IsFinite(p.ComponentHealth.Health)&&p.ComponentHealth.Health>=.99f;
  bool air=p!=null&&float.IsFinite(p.ComponentHealth.Air)&&p.ComponentHealth.Air>=.99f;
  bool fed=v!=null&&float.IsFinite(v.Food)&&v.Food>=.15f;
  bool still=p!=null&&float.IsFinite(p.ComponentBody.Velocity.LengthSquared())&&p.ComponentBody.Velocity.LengthSquared()<=.0025f;
  var m=p?.Entity.FindComponent<ComponentCreatureModel>();var q=m?.EyeRotation;bool orientation=q.HasValue&&float.IsFinite(q.Value.X)&&float.IsFinite(q.Value.Y)&&float.IsFinite(q.Value.Z)&&float.IsFinite(q.Value.W)&&MathF.Abs(q.Value.X*q.Value.X+q.Value.Y*q.Value.Y+q.Value.Z*q.Value.Z+q.Value.W*q.Value.W-1f)<.0001f;
  bool finitePosition=p!=null&&V(p.ComponentBody.Position).Finite&&m!=null&&V(m.EyePosition).Finite;
  bool main=p?.PlayerData.IsMainPlayer==true;
  flags=new{ready,standing,awake,healthy,air,fed,still,orientation,finitePosition,main};return ready&&standing&&awake&&healthy&&air&&fed&&still&&orientation&&finitePosition&&main;
 }
 public override void SubsystemUpdate(float dt){
  if(directory==null||player==null)return;
  if(!float.IsFinite(dt)||dt<0){terminated=true;outcome="invalid_dt";active=null;return;}
  gameTime+=dt;
  var cm=player.Entity.FindComponent<ComponentCreatureModel>();if(cm==null){terminated=true;active=null;outcome="missing_eye_model";return;}var forward=Matrix.CreateFromQuaternion(cm.EyeRotation).Forward;
  if(!float.IsFinite(forward.LengthSquared())){terminated=true;active=null;outcome="invalid_orientation";}
  else if(previousForward.HasValue){var dot=Vector3.Dot(previousForward.Value,forward);accumulatedDegrees+=Math.Atan2(Vector3.Cross(previousForward.Value,forward).Length(),dot)*180/Math.PI;}
  previousForward=forward;
  if(accumulatedDegrees>=100){terminated=true;active=null;outcome="angle_budget_exhausted";}
  if(clock.Elapsed.TotalSeconds>240||usedActive>=4.5||pulses>128){terminated=true;active=null;outcome="session_budget_exhausted";}
  if(active!=null&&(gameTime>=until||!Safe(out _))){active=null;outcome="stopped";}
  var path=Path.Combine(directory,"command.json");
  if(File.Exists(path))try{
   if(new FileInfo(path).Length>2048)throw new Exception("oversize");
   var c=JsonSerializer.Deserialize<Command>(File.ReadAllText(path));File.Delete(path);
   if(c==null||c.Session!=session||c.Id<=lastId)throw new Exception("session_or_sequence");lastId=c.Id;
   if(!float.IsFinite(c.Yaw)||MathF.Abs(c.Yaw)>.1f||!float.IsFinite(c.Seconds)||c.Seconds<0||c.Seconds>.1f)throw new Exception("invalid_float32_command");
   if(c.Yaw==0){active=null;outcome="stop_ack";}
   else {
    bool safeNow=Safe(out var commandFlags);Audit("command_guard",c.Id,commandFlags);long age=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-c.IssuedUnixMs;
    if(age<0||age>1000||terminated||active!=null||pulses>=128||usedActive+c.Seconds>4.5||!safeNow)throw new Exception("guard_rejected");
    active=c;until=gameTime+c.Seconds;usedActive+=c.Seconds;pulses++;outcome="yaw_accepted";
   }
  }catch(Exception ex){active=null;outcome="rejected:"+ex.Message;}
  if(gameTime>=nextObs){nextObs=gameTime+.2;try{Observe();}catch(Exception ex){active=null;terminated=true;File.WriteAllText(Path.Combine(directory,"fault.txt"),ex.ToString());}}
  if(player.PlayerData.IsReadyForPlaying&&player.PlayerData.GameWidget!=null&&File.Exists(Path.Combine(directory,"hide-views"))){
   player.PlayerData.GameWidget.IsVisible=false;
   player.Project.FindSubsystem<SubsystemTerrain>(true).TerrainUpdater.SetUpdateLocation(player.PlayerData.PlayerIndex,player.ComponentBody.Position.XZ,0f,32f);
  }
 }
 sealed class Runner(BridgeMod owner,ComponentInput input):IUpdateable {public UpdateOrder UpdateOrder=>(UpdateOrder)(-5);public void Update(float dt)=>owner.Apply(input);}
 public override void UpdateInput(ComponentInput input,WidgetInput widget){if(directory!=null&&input.m_componentPlayer==player&&registered.Add(input))input.Project.FindSubsystem<SubsystemUpdate>(true).AddUpdateable(new Runner(this,input));}
 void Apply(ComponentInput input){
  if(input.m_componentPlayer!=player)return;
  input.m_playerInput.Look=Vector2.Zero;input.m_playerInput.Move=Vector3.Zero;input.m_playerInput.SneakMove=Vector3.Zero;input.m_playerInput.Jump=false;
  if(active!=null){bool safeNow=Safe(out var applyFlags);if(terminated||gameTime>=until||!safeNow){Audit("apply_stop",active.Id,applyFlags);active=null;outcome="apply_guard_stop";}else input.m_playerInput.Look=new Vector2(active.Yaw,0);}
 }
 static VisionCore.Vec V(Vector3 v)=>new(v.X,v.Y,v.Z);
 void Audit(string kind,long id,object flags)=>File.AppendAllText(Path.Combine(directory!,"guard-audit.jsonl"),JsonSerializer.Serialize(new{kind,id,tickSeconds=gameTime,wallSeconds=clock.Elapsed.TotalSeconds,flags})+"\n");
 void Observe(){
  var p=player!;var model=p.Entity.FindComponent<ComponentCreatureModel>(true) ?? throw new InvalidOperationException("Missing eye model");var eye=model.EyePosition;var rotation=model.EyeRotation;
  var basis=Matrix.CreateFromQuaternion(rotation);var terrain=p.Project.FindSubsystem<SubsystemTerrain>(true).Terrain;
  bool safe=Safe(out var flags);var v=p.Entity.FindComponent<ComponentVitalStats>();
  var state=new{session,tickSeconds=gameTime,capturedUnixMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),lastId,outcome,active=active!=null,terminated,pulses,usedActive,accumulatedDegrees,safe,flags,
   position=new{x=p.ComponentBody.Position.X,y=p.ComponentBody.Position.Y,z=p.ComponentBody.Position.Z},eye=new{x=eye.X,y=eye.Y,z=eye.Z},forward=new{x=basis.Forward.X,y=basis.Forward.Y,z=basis.Forward.Z},health=p.ComponentHealth.Health,air=p.ComponentHealth.Air,food=v?.Food};
  Atomic("state.json",state);
  var visible=new List<object>();
  bool Loaded(int x,int z){var chunk=terrain.GetChunkAtCell(x,z);return chunk!=null&&chunk.State>=TerrainChunkState.Valid;}
  bool Clear(int x,int y,int z)=>Loaded(x,z)&&terrain.IsCellValid(x,y,z)&&terrain.GetCellContents(x,y,z)==0;
  if(!V(eye).Finite||Math.Abs(eye.X)>1000000||Math.Abs(eye.Z)>1000000){Atomic("vision.json",new{session,valid=false,resources=visible});return;}
  for(int x=(int)MathF.Floor(eye.X)-8;x<=(int)MathF.Floor(eye.X)+8;x++)
  for(int y=(int)MathF.Floor(eye.Y)-8;y<=(int)MathF.Floor(eye.Y)+8;y++)
  for(int z=(int)MathF.Floor(eye.Z)-8;z<=(int)MathF.Floor(eye.Z)+8;z++){
   if(!terrain.IsCellValid(x,y,z)||!Loaded(x,z))continue;
   var point=new VisionCore.Vec(x+.5,y+.5,z+.5);
   if(!VisionCore.ContainsWorld(V(eye),point,V(basis.Right),V(basis.Up),V(basis.Forward)))continue;
   int value=terrain.GetCellValue(x,y,z);int content=Terrain.ExtractContents(value);if(content==0)continue;
   var block=BlocksManager.Blocks[content];if(!block.GetType().Name.Contains("Pumpkin"))continue;
   if(!VoxelVisibility.HasClearLine(V(eye),x,y,z,Clear))continue;
   visible.Add(new{kind=block.GetType().Name,relative=new{x=x+.5-eye.X,y=y+.5-eye.Y,z=z+.5-eye.Z},distance=Math.Sqrt((point-V(eye)).Dot(point-V(eye)))});
  }
  Atomic("vision.json",new{session,tickSeconds=gameTime,capturedUnixMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),radius=8,horizontalFov=90,verticalFov=70,resources=visible});
 }
 void Atomic(string name,object value){var path=Path.Combine(directory!,name);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(value));File.Move(path+".tmp",path,true);}
 public override void ModDispose(){active=null;terminated=true;}
}
