using Game;
using System.Text.Json;

// Disposable, explicitly artificial equipment fixtures. All effects under test
// use current public inventory/equipment facts and the matrix's menu validator.
internal sealed partial class ToolAgentScenario {
 readonly HashSet<string> equipment41Slots=[];
 int equipment41Value,equipment41CountBefore,equipment41LayerCount;
 string equipment41Slot,equipment41Baseline;
 bool equipment41Prepared;
 string Equipment41Canonical(JsonElement equipment)=>JsonSerializer.Serialize(equipment.EnumerateArray().Select(e=>new{slot=e.GetProperty("slot").GetString(),layer=e.GetProperty("layer").GetInt32(),value=e.GetProperty("value").GetInt32()}).OrderBy(e=>e.slot).ThenBy(e=>e.layer));
 void Equipment41Audit(string phase)=>ToolAgentGate.Log(new{kind="equipment41_state",phase,slot=equipment41Slot,value=equipment41Value,inventory=Matrix40Read("read_inventory",new{}),equipment=Matrix40Read("read_equipment",new{}),route="adapter_validated",modelApi=false});
 void Equipment41Prepare(){
  equipment41Prepared=false;
  var clothing=p.ComponentClothing;
  var block=BlocksManager.Blocks[ClothingBlock.Index];
  var candidates=block.GetCreativeValues().Select(v=>new{value=v,data=block.GetClothingData(v)})
   .Where(c=>c.data!=null&&!equipment41Slots.Contains(c.data.Slot.Name)&&c.data.PlayerLevelRequired<=p.PlayerData.Level&&clothing.CanWearClothing(c.value)).ToArray();
  if(candidates.Length==0)throw new InvalidOperationException("equipment41_no_second_distinct_wearable_slot_fixture");
  var candidate=candidates[0];
  var inventory=Matrix40Read("read_inventory",new{});
  var empty=inventory.GetProperty("slots").EnumerateArray().Where(s=>s.GetProperty("count").GetInt32()==0).ToArray();
  if(empty.Length==0)throw new InvalidOperationException("equipment41_no_empty_fixture_slot");
  equipment41Value=candidate.value;equipment41Slot=candidate.data.Slot.Name;
  var baseline=Matrix40Read("read_equipment",new{});equipment41Baseline=Equipment41Canonical(baseline);
  equipment41LayerCount=baseline.EnumerateArray().Count(e=>e.GetProperty("slot").GetString()==equipment41Slot);
  int slot=empty.Last().GetProperty("slot").GetInt32();
  p.ComponentMiner.Inventory.AddSlotItems(slot,equipment41Value,1);
  equipment41CountBefore=ExactCount(equipment41Value);
  Matrix40Check(p.ComponentMiner.Inventory.GetSlotValue(slot)==equipment41Value&&p.ComponentMiner.Inventory.GetSlotCount(slot)==1,"equipment41_fixture_injected_exact_one");
  equipment41Slots.Add(equipment41Slot);equipment41Prepared=true;
  ToolAgentGate.Log(new{kind="equipment41_fixture",artificial=true,inventoryInjected=true,worldEdited=false,equipmentEdited=false,slot,value=equipment41Value,count=1,equipmentSlot=equipment41Slot,modelApi=false});
  Equipment41Audit("before_equip");
 }
 object Equipment41EquipArgs(){
  if(!equipment41Prepared)throw new InvalidOperationException("equipment41_fixture_dependency_failed");
  var slot=Matrix40Read("read_inventory",new{}).GetProperty("slots").EnumerateArray().First(s=>s.GetProperty("value").GetInt32()==equipment41Value&&s.GetProperty("count").GetInt32()>0);
  return new{slot=slot.GetProperty("slot").GetInt32(),value=slot.GetProperty("value").GetInt32()};
 }
 void Equipment41VerifyEquipped(){
  Equipment41Audit("after_equip");
  var layers=Matrix40Read("read_equipment",new{}).EnumerateArray().Where(e=>e.GetProperty("slot").GetString()==equipment41Slot).OrderBy(e=>e.GetProperty("layer").GetInt32()).ToArray();
  Matrix40Check(layers.Length==equipment41LayerCount+1&&layers.Last().GetProperty("value").GetInt32()==equipment41Value,"equipment41_native_top_layer_added");
  Matrix40Check(ExactCount(equipment41Value)==equipment41CountBefore-1,"equipment41_inventory_consumed_exact_one");
 }
 object Equipment41UnequipArgs(){
  if(!equipment41Prepared)throw new InvalidOperationException("equipment41_fixture_dependency_failed");
  var layers=Matrix40Read("read_equipment",new{}).EnumerateArray().Where(e=>e.GetProperty("slot").GetString()==equipment41Slot).OrderBy(e=>e.GetProperty("layer").GetInt32()).ToArray();
  if(layers.Length!=equipment41LayerCount+1||layers.Last().GetProperty("value").GetInt32()!=equipment41Value)throw new InvalidOperationException("equipment41_equip_dependency_failed_no_original_clothing_removed");
  return new{equipment_slot=layers.Last().GetProperty("slot").GetString(),value=layers.Last().GetProperty("value").GetInt32()};
 }
 void Equipment41VerifyRemoved(){
  Equipment41Audit("after_unequip");
  Matrix40Check(Equipment41Canonical(Matrix40Read("read_equipment",new{}))==equipment41Baseline,"equipment41_original_equipment_layers_restored");
  Matrix40Check(ExactCount(equipment41Value)==equipment41CountBefore,"equipment41_native_return_exact_inventory_conservation");
  equipment41Prepared=false;
 }
 // Parent hooks once into Matrix40Prepare; no separate update loop or API.
 void Matrix41EnqueueEquipment(){
  Matrix40Enqueue("act_SetPosture",()=>new{posture="standing"});
  for(int i=0;i<2;i++){
   Matrix40Enqueue("act_Wait",()=>new{duration=.1},Equipment41Prepare);
   Matrix40Enqueue("read_equipment",()=>new{});
   Matrix40Enqueue("act_Equip",Equipment41EquipArgs,Equipment41VerifyEquipped);
   Matrix40Enqueue("act_Unequip",Equipment41UnequipArgs,Equipment41VerifyRemoved);
  }
 }
}
