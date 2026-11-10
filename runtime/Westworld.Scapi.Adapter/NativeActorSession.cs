using System.Diagnostics;
using Engine;
using Game;
using GameEntitySystem;
using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

// Privileged in-process host adapter. Never hand this object/native entities to a model.
// A network gateway must authenticate an actor scope; this class is not that gateway.
public sealed partial class NativeActorSession:IUpdateable,IDisposable {
 sealed class Ticket {
  public required MoveIntent Intent;public required SkillResult Result;public required double AcceptedAt;
  public required Vector2 Direction;public double Elapsed,Settling;public bool CancelRequested;
 }
 readonly int observationAbove;readonly Project project;readonly SubsystemTerrain terrain;readonly Stopwatch clock=Stopwatch.StartNew();
 readonly Dictionary<Guid,ComponentPlayer> actors=new();readonly Dictionary<Guid,Ticket> tickets=new();
 readonly Dictionary<Guid,(Guid Actor,Position Origin,double Time,SlotObservation[] Inventory,CellObservation[] Cells)> observations=new();
 readonly HashSet<Guid> retiredBindings=new();
 readonly int owner=Environment.CurrentManagedThreadId;bool disposed;
 public bool ActionsEnabled {get;}
 void DemandActions(){Owner();if(!ActionsEnabled)throw new InvalidOperationException("Observation-only session: actions owned by external task executor");}
 public ActorRegistry Registry {get;}
 public Guid SessionId=>Registry.SessionId;
 public float FloatUpdateOrder=>-15;
 public NativeActorSession(Project project,Guid worldId,bool actionsEnabled=true,int observationAbove=1){if(observationAbove<1||observationAbove>3)throw new ArgumentOutOfRangeException(nameof(observationAbove));this.observationAbove=observationAbove;ActionsEnabled=actionsEnabled;this.project=project??throw new ArgumentNullException(nameof(project));terrain=project.FindSubsystem<SubsystemTerrain>(true);Registry=new ActorRegistry(worldId);}
 void Owner(){if(disposed)throw new ObjectDisposedException(nameof(NativeActorSession));if(Environment.CurrentManagedThreadId!=owner)throw new InvalidOperationException("Game thread required");}
 ComponentPlayer Actor(Guid session,Guid id){Owner();if(session!=SessionId)throw new InvalidOperationException("Stale session");if(retiredBindings.Contains(id)||!actors.TryGetValue(id,out var p)||p.Project!=project||!p.Entity.IsAddedToProject)throw new InvalidOperationException("Actor not bound to live native entity");return p;}
 static Position PositionOf(ComponentPlayer p){var v=p.ComponentBody.Position;return new(v.X,v.Y,v.Z);}
 static double Distance(Position a,Position b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
 public void Bind(Guid id,ComponentPlayer player){Owner();if(!player.PlayerData.IsExternalActor||player.Project!=project||!player.Entity.IsAddedToProject||actors.Values.Contains(player))throw new InvalidOperationException("Invalid native binding");Registry.Add(id,"player:"+player.PlayerData.PlayerIndex,PositionOf(player));actors.Add(id,player);}
 public ActorObservation Observe(Guid session,Guid actorId,bool allDirections=false){
  var p=Actor(session,actorId);var origin=PositionOf(p);var eye=p.ComponentCreatureModel.EyePosition;var forward=Vector3.Normalize(p.ComponentCreatureModel.EyeRotation.GetForwardVector());
  var scan=Game.Westworld.SharedVoxelScanner.Scan(terrain,p.ComponentBody.Position,eye,forward,observationAbove,allDirections:allDirections);
  var cells=scan.Cells.Select(c=>new CellObservation(c.X,c.Y,c.Z,c.Value.HasValue?"known":"unknown",c.Value)).ToList();
  // Old observations may be retired; action tickets are never evicted or replayed.
  foreach(var id in observations.Where(kv=>clock.Elapsed.TotalSeconds-kv.Value.Time>3).Select(kv=>kv.Key).ToArray())observations.Remove(id);
  if(observations.Count>=128)throw new InvalidOperationException("Observation budget full");
  var inventory=p.ComponentMiner.Inventory;
  var slots=Enumerable.Range(0,inventory.SlotsCount).Select(i=>{
   int value=inventory.GetSlotValue(i),count=inventory.GetSlotCount(i);float nutrition=BlocksManager.Blocks[Terrain.ExtractContents(value)].GetNutritionalValue(value);
   return new SlotObservation(i,value,count,nutrition,count>0&&SupportedFood(value)&&nutrition>0);
  }).ToArray();
  var oid=Guid.NewGuid();double now=clock.Elapsed.TotalSeconds;observations.Add(oid,(actorId,origin,now,slots.ToArray(),cells.ToArray()));
  return new(SessionId,actorId,oid,now,origin,p.ComponentHealth.Health,p.ComponentVitalStats.Food,slots,cells.ToArray());
 }
 // Conservative visual policy: full opaque voxel occlusion, not collision-box LOS.
 // Transparent blocks do not occlude; sub-voxel holes in opaque blocks are conservatively hidden.
 bool OpaqueVoxelLineOfSight(Vector3 start,Vector3 end)=>VoxelSurfaceVisibility.LineOfSight(
  new(start.X,start.Y,start.Z),new(end.X,end.Y,end.Z),(x,y,z)=>{
   if(y<0||y>=TerrainChunk.Height||terrain.Terrain.GetChunkAtCell(x,z)?.State!=TerrainChunkState.Valid)return null;
   int value=terrain.Terrain.GetCellValue(x,y,z);
   return !BlocksManager.Blocks[Terrain.ExtractContents(value)].IsTransparent_(value);
  });

 bool LoadedRectangle(Vector3 a,Vector3 b){
  int minX=Terrain.ToCell(MathF.Min(a.X,b.X))>>4,maxX=Terrain.ToCell(MathF.Max(a.X,b.X))>>4,minZ=Terrain.ToCell(MathF.Min(a.Z,b.Z))>>4,maxZ=Terrain.ToCell(MathF.Max(a.Z,b.Z))>>4;
  for(int x=minX;x<=maxX;x++)for(int z=minZ;z<=maxZ;z++)if(terrain.Terrain.GetChunkAtCell(x*16,z*16)?.State!=TerrainChunkState.Valid)return false;
  return true;
 }
 public SkillResult Submit(MoveIntent intent){
  DemandActions();if(intent.SessionId!=SessionId)throw new InvalidOperationException("Stale session");
  if(tickets.TryGetValue(intent.ActionId,out var previous)){if(previous.Intent!=intent)throw new InvalidOperationException("Action ID payload conflict");return previous.Result;}
  if(foodTickets.ContainsKey(intent.ActionId)||forageTickets.ContainsKey(intent.ActionId))throw new InvalidOperationException("Action ID already used for a different skill");
  var p=Actor(intent.SessionId,intent.ActorId);
  if(intent.ActionId==Guid.Empty||!float.IsFinite(intent.Right)||!float.IsFinite(intent.Forward)||!float.IsFinite(intent.Seconds)||intent.Right*intent.Right+intent.Forward*intent.Forward>1||intent.Right*intent.Right+intent.Forward*intent.Forward<0.0001f||intent.Seconds<0.05f||intent.Seconds>1)throw new ArgumentException("Invalid bounded move intent");
  if(!p.PlayerData.IsExternalActor||!p.PlayerData.IsReadyForPlaying||p.ComponentHealth.Health<=0||p.ComponentSleep.SleepFactor>0)throw new InvalidOperationException("Actor unavailable");
  if(!observations.TryGetValue(intent.ObservationId,out var o)||o.Actor!=intent.ActorId||clock.Elapsed.TotalSeconds-o.Time>3||Math.Sqrt(Math.Pow(o.Origin.X-p.ComponentBody.Position.X,2)+Math.Pow(o.Origin.Y-p.ComponentBody.Position.Y,2)+Math.Pow(o.Origin.Z-p.ComponentBody.Position.Z,2))>2)throw new InvalidOperationException("Observation missing, stale or belongs to another actor");
  if(tickets.Count+foodTickets.Count+forageTickets.Count+ReservedForageSlots>=256)throw new InvalidOperationException("Ticket budget full; start a reconciled new session");
  if(ForageBusy(intent.ActorId)||FoodBusy(intent.ActorId)||tickets.Values.Any(t=>t.Intent.ActorId==intent.ActorId&&!Terminal(t.Result.Status)))throw new InvalidOperationException("Actor already has an active skill");
  var pos=PositionOf(p);var result=new SkillResult(SessionId,intent.ActorId,intent.ActionId,"Accepted",pos,pos,0,0,"not yet executed");
  tickets.Add(intent.ActionId,new Ticket{Intent=intent,Result=result,AcceptedAt=clock.Elapsed.TotalSeconds,Direction=Vector2.Normalize((p.ComponentBody.Matrix.Right*intent.Right+p.ComponentBody.Matrix.Forward*intent.Forward).XZ)});return result;
 }
 public SkillResult Read(Guid session,Guid actor,Guid action){Owner();if(session!=SessionId||!tickets.TryGetValue(action,out var t)||t.Intent.ActorId!=actor)throw new InvalidOperationException("Unknown scoped ticket");return t.Result;}
 public SkillResult Cancel(Guid session,Guid actor,Guid action){var result=Read(session,actor,action);var p=actors[actor];var t=tickets[action];if(!Terminal(result.Status)){t.CancelRequested=true;Stop(p);t.Result=result with {Status="Stopping",Reason="cancel requested"};}return t.Result;}
 static bool Terminal(string status)=>status is "Succeeded" or "Blocked" or "Canceled" or "Failed";
 public void Update(float dt){Owner();
  if(!float.IsFinite(dt)||dt<0||dt>0.25f){FailAll("invalid game timestep");return;}
  foreach(var (id,p) in actors){if(retiredBindings.Contains(id))continue;if(!p.Entity.IsAddedToProject){if(ActionsEnabled)Stop(p);Registry.Remove(id);retiredBindings.Add(id);foodInventoryVersions.Remove(id);continue;}var a=Registry.Read(id);Registry.Update(SessionId,id,a.Revision,PositionOf(p),p.ComponentHealth.Health>0&&p.PlayerData.IsReadyForPlaying);}
  foreach(var t in tickets.Values.Where(t=>!Terminal(t.Result.Status)).ToArray()){
   var p=actors[t.Intent.ActorId];var now=PositionOf(p);double previousElapsed=t.Elapsed;t.Elapsed+=dt;
   if(retiredBindings.Contains(t.Intent.ActorId)||!p.Entity.IsAddedToProject||!p.PlayerData.IsReadyForPlaying||p.ComponentHealth.Health<=0||p.ComponentSleep.SleepFactor>0||clock.Elapsed.TotalSeconds-t.AcceptedAt>4){Stop(p);t.Result=t.Result with {Status="Failed",End=now,HorizontalDistance=Distance(t.Result.Start,now),ForwardProgress=Vector2.Dot(new Vector2((float)(now.X-t.Result.Start.X),(float)(now.Z-t.Result.Start.Z)),t.Direction),Reason="native availability/deadline"};continue;}
   if(!t.CancelRequested&&previousElapsed<t.Intent.Seconds){p.ComponentInput.SubmitExternalMovement(new Vector3(t.Intent.Right,0,t.Intent.Forward),Vector2.Zero);t.Result=t.Result with {Status="Running",End=now,Reason="native movement pulse"};continue;}
   Stop(p);t.Settling+=dt;t.Result=t.Result with {Status="Stopping",End=now,HorizontalDistance=Distance(t.Result.Start,now),ForwardProgress=Vector2.Dot(new Vector2((float)(now.X-t.Result.Start.X),(float)(now.Z-t.Result.Start.Z)),t.Direction),Reason="waiting for physical settle"};
   float speed=p.ComponentBody.Velocity.XZ.Length();
   if(t.Settling>=0.1&&float.IsFinite(speed)&&speed<0.1f)t.Result=t.Result with {Status=t.CancelRequested?"Canceled":t.Result.ForwardProgress>0.01?"Succeeded":"Blocked",Reason="native horizontal settle and direction evidence"};
   else if(t.Settling>2)t.Result=t.Result with {Status="Failed",Reason="physical settle timeout"};
  }
  UpdateForaging(dt);
  UpdateEating();
 }
 static void Stop(ComponentPlayer p){if(p.PlayerData.IsExternalActor)p.ComponentInput.ClearExternalMovement();}
 void FailAll(string reason){FailForaging(reason);FailEating(reason);foreach(var t in tickets.Values.Where(t=>!Terminal(t.Result.Status))){Stop(actors[t.Intent.ActorId]);t.Result=t.Result with {Status="Failed",Reason=reason};}}
 public void Dispose(){if(disposed)return;Owner();FailAll("session disposed");foodInventoryVersions.Clear();disposed=true;}
}
