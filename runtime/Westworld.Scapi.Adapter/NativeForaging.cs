using Engine;
using Game;
using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

// One finite, local, straight-line skill. Not a global navigator or a world scanner.
// Each stage advances from native evidence; no inventory grants or terrain writes.
public sealed partial class NativeActorSession {
 sealed class ForageTicket {
  public required ForageIntent Intent;public required ForageResult Result;public required double Started;
  public double StageSeconds,StillSeconds;public float BestDistance=float.MaxValue;
  public int InitialCount,DropValue;public long BlocksBefore;public Guid? EatId;
 }
 readonly Dictionary<Guid,ForageTicket> forageTickets=new();
 int ReservedForageSlots=>forageTickets.Values.Count(t=>t.EatId is null&&!ForageTerminal(t.Result.Status));
 bool ForageBusy(Guid actor,Guid? except=null)=>forageTickets.Values.Any(t=>t.Intent.ActorId==actor&&t.Intent.ActionId!=except&&!ForageTerminal(t.Result.Status));
 static bool ForageTerminal(string status)=>status is "Succeeded" or "Blocked" or "Canceled" or "Failed" or "Uncertain";
 static bool MaturePumpkin(int value)=>Terrain.ExtractContents(value)==PumpkinBlock.Index&&BasePumpkinBlock.GetSize(Terrain.ExtractData(value))==7&&BlocksManager.Blocks[PumpkinBlock.Index].GetNutritionalValue(value)>0;
 static int InventoryCount(ComponentPlayer p,int value)=>Enumerable.Range(0,p.ComponentMiner.Inventory.SlotsCount).Where(i=>p.ComponentMiner.Inventory.GetSlotValue(i)==value).Sum(i=>p.ComponentMiner.Inventory.GetSlotCount(i));
 public ForageResult ApplyForageDecision(HighLevelDecision d){
  if(d.Goal!="SatisfyHunger"||d.Skill!="ForageOne"||d.Target is null||d.Slot!=-1||d.ExpectedValue!=0)throw new InvalidOperationException("Unsupported forage decision");
  return SubmitForage(new(d.SessionId,d.ActorId,d.DecisionId,d.ObservationId,d.Target));
 }
 public ForageResult SubmitForage(ForageIntent intent){
  Owner();if(intent.SessionId!=SessionId)throw new InvalidOperationException("Stale session");
  if(forageTickets.TryGetValue(intent.ActionId,out var old)){if(old.Intent!=intent)throw new InvalidOperationException("Action ID payload conflict");return old.Result;}
  if(tickets.ContainsKey(intent.ActionId)||foodTickets.ContainsKey(intent.ActionId))throw new InvalidOperationException("Action ID used by other skill");
  var p=Actor(intent.SessionId,intent.ActorId);var target=intent.Target;
  target?.Validate();
  if(intent.ActionId==Guid.Empty||target is null||!MaturePumpkin(target.ExpectedValue))throw new ArgumentException("Only observed mature ordinary pumpkin supported");
  if(!observations.TryGetValue(intent.ObservationId,out var o)||o.Actor!=intent.ActorId||clock.Elapsed.TotalSeconds-o.Time>3||Math.Sqrt(Math.Pow(o.Origin.X-p.ComponentBody.Position.X,2)+Math.Pow(o.Origin.Y-p.ComponentBody.Position.Y,2)+Math.Pow(o.Origin.Z-p.ComponentBody.Position.Z,2))>2||!o.Cells.Any(c=>c.X==target.X&&c.Y==target.Y&&c.Z==target.Z&&c.Visibility=="known"&&c.Value==target.ExpectedValue))throw new InvalidOperationException("Missing current actor target evidence");
  if(!p.PlayerData.IsReadyForPlaying||p.ComponentHealth.Health<=0||p.ComponentSleep.SleepFactor>0)throw new InvalidOperationException("Actor unavailable");
  if(ForageBusy(intent.ActorId)||FoodBusy(intent.ActorId)||tickets.Values.Any(t=>t.Intent.ActorId==intent.ActorId&&!Terminal(t.Result.Status)))throw new InvalidOperationException("Actor already has active skill");
  if(tickets.Count+foodTickets.Count+forageTickets.Count+ReservedForageSlots>=255)throw new InvalidOperationException("Ticket budget cannot reserve child eating ticket");
  int data=Terrain.ExtractData(target.ExpectedValue);int drop=BlocksManager.Blocks[PumpkinBlock.Index].SetDamage(Terrain.MakeBlockValue(PumpkinBlock.Index,0,BasePumpkinBlock.SetSize(BasePumpkinBlock.SetIsDead(0,true),BasePumpkinBlock.GetSize(data))),BlocksManager.Blocks[PumpkinBlock.Index].GetDamage(target.ExpectedValue));
  var pos=PositionOf(p);var result=new ForageResult(SessionId,intent.ActorId,intent.ActionId,"Accepted","Approaching",pos,pos,false,0,p.ComponentVitalStats.Food,p.ComponentVitalStats.Food,"one observed resource; no mutation yet");
  forageTickets.Add(intent.ActionId,new(){Intent=intent,Result=result,Started=clock.Elapsed.TotalSeconds,InitialCount=InventoryCount(p,drop),DropValue=drop,BlocksBefore=p.PlayerStats.BlocksDug});return result;
 }
 public ForageResult ReadForage(Guid session,Guid actor,Guid action){Owner();if(session!=SessionId||!forageTickets.TryGetValue(action,out var t)||t.Intent.ActorId!=actor)throw new InvalidOperationException("Unknown scoped forage ticket");return t.Result;}
 public ForageResult CancelForage(Guid session,Guid actor,Guid action){ReadForage(session,actor,action);var t=forageTickets[action];if(!ForageTerminal(t.Result.Status))EndForage(t,actors[actor],"Canceled","cancel requested; prior effects retained");return t.Result;}
 void EndForage(ForageTicket t,ComponentPlayer p,string status,string why){Stop(p);p.ComponentMiner.DigCellFace=null;if(t.EatId is Guid eat&&foodTickets.TryGetValue(eat,out var child)&&child.Result.Status=="Accepted")child.Result=child.Result with {Status="Canceled",Reason="parent ended before eating"};t.Result=t.Result with {Status=status,End=PositionOf(p),Collected=Math.Max(t.Result.Collected,Math.Max(0,InventoryCount(p,t.DropValue)-t.InitialCount)),FoodAfter=p.ComponentVitalStats.Food,Reason=why};}
 void FailForaging(string reason){foreach(var t in forageTickets.Values.Where(t=>!ForageTerminal(t.Result.Status)))EndForage(t,actors[t.Intent.ActorId],"Failed",reason);}
 void Stage(ForageTicket t,string stage){t.StageSeconds=0;t.StillSeconds=0;t.BestDistance=float.MaxValue;t.Result=t.Result with {Status="Running",Stage=stage,Reason="native stage transition"};}
 bool TargetVisible(ComponentPlayer p,Vector3 target){var eye=p.ComponentCreatureModel.EyePosition;var delta=target-eye;var forward=p.ComponentCreatureModel.EyeRotation.GetForwardVector();return delta.Length()<=5&&Vector3.Dot(Vector3.Normalize(delta),Vector3.Normalize(forward))>=0.5f&&LoadedRectangle(eye,target)&&OpaqueVoxelLineOfSight(eye,target);}
 // The executor may move toward remembered, just-harvested location; it never scans hidden cells.
 bool StepToward(ComponentPlayer p,Vector3 target,ForageTicket t,float dt){
  var pos=p.ComponentBody.Position;var delta=(target-pos).XZ;float distance=delta.Length();
  if(distance<t.BestDistance-0.03f){t.BestDistance=distance;t.StillSeconds=0;}else t.StillSeconds+=dt;
  if(t.StillSeconds>2)return false;
  if(distance<0.25f){Stop(p);return true;}
  var direction=Vector2.Normalize(delta);var probe=pos+new Vector3(direction.X,0,direction.Y)*0.65f;
  if(!LoadedRectangle(pos,probe)){Stop(p);t.StillSeconds=0;return true;}
  int x=Terrain.ToCell(probe.X),z=Terrain.ToCell(probe.Z),y=Terrain.ToCell(pos.Y-0.1f);
  if(y<0||y+2>=TerrainChunk.Height)return false;
  int support=terrain.Terrain.GetCellValue(x,y,z);if(!BlocksManager.Blocks[Terrain.ExtractContents(support)].IsCollidable_(support))return false;
  var world=new Vector3(direction.X,0,direction.Y)*MathF.Min(0.4f,distance*0.5f);var matrix=p.ComponentBody.Matrix;
  p.ComponentInput.SubmitExternalMovement(new Vector3(Vector3.Dot(world,matrix.Right),0,Vector3.Dot(world,matrix.Forward)),Vector2.Zero);return true;
 }
 void UpdateForaging(float dt){foreach(var t in forageTickets.Values.Where(t=>!ForageTerminal(t.Result.Status)).ToArray()){
  var p=actors[t.Intent.ActorId];var target=t.Intent.Target;var center=new Vector3(target.X+0.5f,target.Y+0.5f,target.Z+0.5f);t.StageSeconds+=dt;t.Result=t.Result with {End=PositionOf(p),FoodAfter=p.ComponentVitalStats.Food};
  if(retiredBindings.Contains(t.Intent.ActorId)||!p.Entity.IsAddedToProject||!p.PlayerData.IsReadyForPlaying||p.ComponentHealth.Health<=0||p.ComponentSleep.SleepFactor>0||clock.Elapsed.TotalSeconds-t.Started>30){EndForage(t,p,"Failed","actor unavailable or finite goal deadline");continue;}
  try{
   if(t.Result.Stage is "Approaching" or "Harvesting"){
    if(!TargetVisible(p,center)||terrain.Terrain.GetCellValue(target.X,target.Y,target.Z)!=target.ExpectedValue){EndForage(t,p,"Blocked","target changed or no longer locally visible");continue;}
    if(t.Result.Stage=="Approaching"){
     if(Vector2.Distance(p.ComponentBody.Position.XZ,center.XZ)<=1.8f){Stop(p);Stage(t,"Harvesting");}
     else if(!StepToward(p,center,t,dt))EndForage(t,p,"Blocked","local approach lacks safe support or progress");
    }else{
     Stop(p);var eye=p.ComponentCreatureModel.EyePosition;var hit=p.ComponentMiner.Raycast<TerrainRaycastResult>(new Ray3(eye,Vector3.Normalize(center-eye)),RaycastMode.Digging);
     if(hit is not TerrainRaycastResult ray||ray.CellFace.X!=target.X||ray.CellFace.Y!=target.Y||ray.CellFace.Z!=target.Z||ray.Distance>3){EndForage(t,p,"Blocked","native first hit or reach changed");continue;}
     bool dug=p.ComponentMiner.Dig(ray);
     if(dug){
      bool changed=Terrain.ExtractContents(terrain.Terrain.GetCellValue(target.X,target.Y,target.Z))==0;
      if(!changed||p.PlayerStats.BlocksDug!=t.BlocksBefore+1){EndForage(t,p,"Uncertain","native dig outcome requires reconciliation");continue;}
      t.Result=t.Result with {Harvested=true};Stage(t,"Collecting");
     }else if(t.StageSeconds>8)EndForage(t,p,"Blocked","native harvesting made no completed progress");
    }
   }else if(t.Result.Stage=="Collecting"){
    int gained=InventoryCount(p,t.DropValue)-t.InitialCount;
    if(gained>=1){Stop(p);t.Result=t.Result with {Collected=gained};Stage(t,"Eating");}
    else if(t.StageSeconds>8||!StepToward(p,new Vector3(center.X,p.ComponentBody.Position.Y,center.Z),t,dt))EndForage(t,p,"Blocked","harvested but natural pickup not confirmed");
   }else if(t.Result.Stage=="Eating"){
    Stop(p);
    if(t.EatId is null){var observation=Observe(SessionId,t.Intent.ActorId);var slot=observation.Inventory.FirstOrDefault(s=>s.Value==t.DropValue&&s.Count>0&&s.CanEat);if(slot is null){EndForage(t,p,"Blocked","collected food no longer present");continue;}var id=Guid.NewGuid();SubmitEatCore(new(SessionId,t.Intent.ActorId,id,observation.ObservationId,slot.Slot,slot.Value),t.Intent.ActionId);t.EatId=id;}
    else{var eaten=ReadEat(SessionId,t.Intent.ActorId,t.EatId.Value);if(eaten.Status is not ("Accepted" or "Executing")){EndForage(t,p,eaten.Status=="Succeeded"?"Succeeded":eaten.Status=="Uncertain"?"Uncertain":"Blocked","food outcome: "+eaten.Reason);}}
   }
  }catch(Exception e){EndForage(t,p,"Uncertain","native operation requires reconciliation: "+e.GetType().Name);}
 }}
}
