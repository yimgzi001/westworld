using Game;
using System.Text.Json;

// Artificial owned-item fixture. Drop/PickUp effects are native, with unchanged
// physics, 0.5-game-second automatic collection delay and pickup range.
internal sealed partial class ToolAgentScenario {
 int items41Value=DirtBlock.Index,items41Before;
 double items41DropTime;
 int Items41WorldCount()=>p.Project.FindSubsystem<SubsystemPickables>(true).Pickables.Where(d=>!d.ToRemove&&d.Value==items41Value).Sum(d=>d.Count);
 void Items41Audit(string phase)=>ToolAgentGate.Log(new{kind="items41_state",phase,value=items41Value,inventory=Matrix40Read("read_inventory",new{}),worldCount=Items41WorldCount(),gameTime=p.Project.FindSubsystem<SubsystemGameInfo>(true).TotalElapsedGameTime,worldCountHostAuditOnly=true,modelApi=false});
 void Items41Prepare(){
  Matrix40Check(Items41WorldCount()==0,"items41_no_competing_same_value_world_stack");
  var empty=Matrix40Read("read_inventory",new{}).GetProperty("slots").EnumerateArray().Where(s=>s.GetProperty("count").GetInt32()==0).ToArray();
  if(empty.Length==0)throw new InvalidOperationException("items41_no_empty_fixture_slot");
  int slot=empty.Last().GetProperty("slot").GetInt32();p.ComponentMiner.Inventory.AddSlotItems(slot,items41Value,3);
  Matrix40Check(p.ComponentMiner.Inventory.GetSlotCount(slot)==3&&p.ComponentMiner.Inventory.GetSlotValue(slot)==items41Value,"items41_fixture_exact_three");items41Before=ExactCount(items41Value);
  ToolAgentGate.Log(new{kind="items41_fixture",artificial=true,inventoryInjected=true,worldEdited=false,slot,value=items41Value,count=3,autoPickupModified=false,physicsModified=false,modelApi=false});Items41Audit("before_drop");
 }
 object Items41DropArgs(){
  var slot=Matrix40Read("read_inventory",new{}).GetProperty("slots").EnumerateArray().First(s=>s.GetProperty("value").GetInt32()==items41Value&&s.GetProperty("count").GetInt32()>=2);
  items41DropTime=p.Project.FindSubsystem<SubsystemGameInfo>(true).TotalElapsedGameTime;
  return new{slot=slot.GetProperty("slot").GetInt32(),value=slot.GetProperty("value").GetInt32(),count=2};
 }
 void Items41VerifyDrop(){
  Items41Audit("after_drop");
  Matrix40Check(Matrix40Read("action_status",new{}).GetProperty("affectedCount").GetInt32()==2,"items41_drop_native_affected_two");
  Matrix40Check(ExactCount(items41Value)==items41Before-2&&Items41WorldCount()==2,"items41_drop_inventory_world_conservation");
 }
 object Items41PickupArgs(){
  // Never respawn/teleport or suppress automatic collection to force success.
  // A lost race is a real failed case, not evidence of explicit pickup.
  if(ExactCount(items41Value)!=items41Before-2)throw new InvalidOperationException("items41_auto_pickup_race_before_explicit_action");
  var observation=Snapshot().Observation;
  var drops=observation.GetProperty("pickables").EnumerateArray().Where(d=>d.GetProperty("value").GetInt32()==items41Value&&d.GetProperty("count").GetInt32()==2).ToArray();
  ToolAgentGate.Log(new{kind="items41_pickup_observation",elapsedGameSeconds=p.Project.FindSubsystem<SubsystemGameInfo>(true).TotalElapsedGameTime-items41DropTime,pickables=observation.GetProperty("pickables"),automaticPickupCooldownSeconds=.5});
  if(drops.Length!=1)throw new InvalidOperationException("items41_drop_not_uniquely_publicly_observed");
  return new{target=drops[0].GetProperty("reference").GetString(),count=2};
 }
 void Items41VerifyPickup(){
  Items41Audit("after_explicit_pickup");var receipt=Matrix40Read("action_status",new{});
  Matrix40Check(receipt.GetProperty("action").GetString()=="PickUp"&&receipt.GetProperty("affectedCount").GetInt32()==2,"items41_explicit_native_pickup_affected_two");
  Matrix40Check(ExactCount(items41Value)==items41Before&&Items41WorldCount()==0,"items41_pickup_inventory_world_conservation");
 }
 void Matrix41EnqueueItems(){
  Matrix40Enqueue("act_SetPosture",()=>new{posture="standing"});
  Matrix40Enqueue("act_FaceTowards",()=>new{bearing=0,pitch=-82,timeout=4},Items41Prepare);
  Matrix40Enqueue("read_item",()=>new{value=items41Value},()=>{var entry=Matrix40Read("read_item",new{value=items41Value});Matrix40Check(entry.GetProperty("value").GetInt32()==items41Value&&entry.GetProperty("type").GetString()=="DirtBlock","items41_read_item_matches_owned_value");});
  Matrix40Enqueue("find_items",()=>new{search="DirtBlock",offset=0,limit=4},()=>{var entries=Matrix40Read("find_items",new{search="DirtBlock",offset=0,limit=4});Matrix40Check(entries.EnumerateArray().Any(e=>e.GetProperty("value").GetInt32()==items41Value),"items41_find_items_contains_known_dirt");});
  Matrix40Enqueue("act_Drop",Items41DropArgs,Items41VerifyDrop);
  Matrix40Enqueue("act_PickUp",Items41PickupArgs,Items41VerifyPickup);
 }
}
