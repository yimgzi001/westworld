using Game;
using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

public sealed partial class NativeActorSession {
 sealed class FoodTicket { public required EatIntent Intent;public required EatResult Result;public required double AcceptedAt; }
 readonly Dictionary<Guid,FoodTicket> foodTickets=new();
 bool FoodBusy(Guid actor)=>foodTickets.Values.Any(t=>t.Intent.ActorId==actor&&t.Result.Status is "Accepted" or "Executing");
 // Explicit high-level skill allowlist; no generated code or arbitrary native calls.
 public EatResult? ApplyDecision(HighLevelDecision decision){
  if(decision.Goal=="SatisfyHunger"&&decision.Skill=="EatOne")return SubmitEat(new EatIntent(decision.SessionId,decision.ActorId,decision.DecisionId,decision.ObservationId,decision.Slot,decision.ExpectedValue));
  if(decision.Goal=="Observe"&&decision.Skill=="Wait"&&decision.Slot==-1&&decision.ExpectedValue==0){Actor(decision.SessionId,decision.ActorId);return null;}
  throw new InvalidOperationException("Unsupported high-level decision");
 }
 static bool SupportedFood(int value)=>Terrain.ExtractContents(value)==CookedFishBlock.Index||(Terrain.ExtractContents(value)==PumpkinBlock.Index&&BasePumpkinBlock.GetSize(Terrain.ExtractData(value))==7);
 public EatResult SubmitEat(EatIntent intent)=>SubmitEatCore(intent,null);
 EatResult SubmitEatCore(EatIntent intent,Guid? parent){
  DemandActions();if(intent.SessionId!=SessionId)throw new InvalidOperationException("Stale session");
  if(foodTickets.TryGetValue(intent.ActionId,out var old)){if(old.Intent!=intent)throw new InvalidOperationException("Action ID payload conflict");return old.Result;}
  if(tickets.ContainsKey(intent.ActionId)||forageTickets.ContainsKey(intent.ActionId))throw new InvalidOperationException("Action ID already used for a different skill");
  var p=Actor(intent.SessionId,intent.ActorId);var inventory=p.ComponentMiner.Inventory;
  if(intent.ActionId==Guid.Empty||intent.Slot<0||intent.Slot>=inventory.SlotsCount)throw new ArgumentException("Invalid eat intent");
  if(!p.PlayerData.IsReadyForPlaying||p.ComponentHealth.Health<=0||p.ComponentSleep.SleepFactor>0)throw new InvalidOperationException("Actor unavailable");
  if(!observations.TryGetValue(intent.ObservationId,out var o)||o.Actor!=intent.ActorId||clock.Elapsed.TotalSeconds-o.Time>3||Math.Sqrt(Math.Pow(o.Origin.X-p.ComponentBody.Position.X,2)+Math.Pow(o.Origin.Y-p.ComponentBody.Position.Y,2)+Math.Pow(o.Origin.Z-p.ComponentBody.Position.Z,2))>2)throw new InvalidOperationException("Observation stale or wrong actor");
  var observed=o.Inventory.SingleOrDefault(s=>s.Slot==intent.Slot);
  if(observed is null||!observed.CanEat||observed.Value!=intent.ExpectedValue||observed.Count<1||inventory.GetSlotValue(intent.Slot)!=intent.ExpectedValue||inventory.GetSlotCount(intent.Slot)!=observed.Count||!SupportedFood(intent.ExpectedValue))throw new InvalidOperationException("Observed food slot no longer matches");
  if(tickets.Count+foodTickets.Count+forageTickets.Count+ReservedForageSlots-(parent.HasValue?1:0)>=256)throw new InvalidOperationException("Ticket budget full");
  if(ForageBusy(intent.ActorId,parent)||FoodBusy(intent.ActorId)||tickets.Values.Any(t=>t.Intent.ActorId==intent.ActorId&&!Terminal(t.Result.Status)))throw new InvalidOperationException("Actor already has active skill");
  var result=new EatResult(SessionId,intent.ActorId,intent.ActionId,"Accepted",intent.Slot,observed.Count,observed.Count,p.ComponentVitalStats.Food,p.ComponentVitalStats.Food,"not yet executed");
  foodTickets.Add(intent.ActionId,new FoodTicket{Intent=intent,Result=result,AcceptedAt=clock.Elapsed.TotalSeconds});return result;
 }
 public EatResult ReadEat(Guid session,Guid actor,Guid action){Owner();if(session!=SessionId||!foodTickets.TryGetValue(action,out var t)||t.Intent.ActorId!=actor)throw new InvalidOperationException("Unknown scoped food ticket");return t.Result;}
 public EatResult CancelEat(Guid session,Guid actor,Guid action){ReadEat(session,actor,action);var t=foodTickets[action];if(t.Result.Status=="Accepted")t.Result=t.Result with {Status="Canceled",Reason="canceled before native consumption"};return t.Result;}
 void FailEating(string reason){foreach(var t in foodTickets.Values.Where(t=>t.Result.Status=="Accepted"))t.Result=t.Result with {Status="Failed",Reason=reason};}
 void UpdateEating(){foreach(var t in foodTickets.Values.Where(t=>t.Result.Status=="Accepted").ToArray()){
  var p=actors[t.Intent.ActorId];var inventory=p.ComponentMiner.Inventory;int slot=t.Intent.Slot,value=t.Intent.ExpectedValue;
  if(retiredBindings.Contains(t.Intent.ActorId)||!p.Entity.IsAddedToProject||!p.PlayerData.IsReadyForPlaying||p.ComponentHealth.Health<=0||p.ComponentSleep.SleepFactor>0||clock.Elapsed.TotalSeconds-t.AcceptedAt>4||inventory.GetSlotValue(slot)!=value||inventory.GetSlotCount(slot)!=t.Result.CountBefore){t.Result=t.Result with {Status="Failed",Reason="native precondition changed; no consumption attempted"};continue;}
  t.Result=t.Result with {Status="Executing",FoodBefore=p.ComponentVitalStats.Food,ItemsEatenBefore=p.PlayerStats.FoodItemsEaten,ItemsEatenAfter=p.PlayerStats.FoodItemsEaten,Reason="native inventory processing started"};
  try{
   if(p.ComponentClothing.GetSlotProcessCapacity(0,value)<1){t.Result=t.Result with {Status="Blocked",Reason="native processing unavailable"};continue;}
   int removed=inventory.RemoveSlotItems(slot,1);if(removed!=1)throw new InvalidOperationException("Removal did not consume exactly one item");
   // Same native inventory-processing route used by original drag-to-body eating.
   p.ComponentClothing.ProcessSlotItems(0,value,1,1,out int returnedValue,out int returnedCount);
   if(returnedCount!=0){
    if(returnedCount!=1||returnedValue!=value||inventory.GetSlotCount(slot)>=inventory.GetSlotCapacity(slot,returnedValue)||(inventory.GetSlotCount(slot)>0&&inventory.GetSlotValue(slot)!=returnedValue))throw new InvalidOperationException("Unexpected native processing remainder");
    inventory.AddSlotItems(slot,returnedValue,returnedCount);
   }
   int after=inventory.GetSlotCount(slot);float food=p.ComponentVitalStats.Food;
   string status=after==t.Result.CountBefore-1&&float.IsFinite(food)&&food>t.Result.FoodBefore&&p.PlayerStats.FoodItemsEaten==t.Result.ItemsEatenBefore+1?"Succeeded":after==t.Result.CountBefore&&food==t.Result.FoodBefore&&p.PlayerStats.FoodItemsEaten==t.Result.ItemsEatenBefore?"Blocked":"Uncertain";
   t.Result=t.Result with {Status=status,CountAfter=after,FoodAfter=food,ItemsEatenAfter=p.PlayerStats.FoodItemsEaten,Reason="native food/inventory postconditions"};
  }catch(Exception e){
   // Do not manufacture a rollback: food/consumption may already have happened.
   t.Result=t.Result with {Status="Uncertain",CountAfter=inventory.GetSlotCount(slot),FoodAfter=p.ComponentVitalStats.Food,ItemsEatenAfter=p.PlayerStats.FoodItemsEaten,Reason="partial native operation; reconcile, never retry: "+e.GetType().Name};
  }
 }}
}
