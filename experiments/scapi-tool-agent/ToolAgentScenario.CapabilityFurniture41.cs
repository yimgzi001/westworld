using Engine;
using Game;
using System.Text.Json;

// Isolated artificial fixture, not survival progress. No model calls and no
// hidden-world information supplied to the planner. Actual actions use Matrix40
// menu validation; direct terrain reads below are test-oracle assertions only.
internal sealed partial class ToolAgentScenario {
 Point3 matrix41FurnitureCell;
 int matrix41HammerSlot=-1,matrix41HammerValue,matrix41FurnitureBefore;
 int matrix41CircuitSlot=-1,matrix41CircuitValue;
 float matrix41FurnitureLevelBefore; bool matrix41FurnitureRestoreLevel;
 JsonElement Matrix41OwnSlot(int slot)=>Matrix40Read("read_inventory",new{}).GetProperty("slots").EnumerateArray().Single(s=>s.GetProperty("slot").GetInt32()==slot);
 int Matrix41EmptySlot(bool selectable=false){
  int limit=selectable?Math.Min(p.ComponentMiner.Inventory.VisibleSlotsCount,p.ComponentMiner.Inventory.SlotsCount):p.ComponentMiner.Inventory.SlotsCount;
  var candidates=Matrix40Read("read_inventory",new{}).GetProperty("slots").EnumerateArray().Where(s=>s.GetProperty("count").GetInt32()==0&&s.GetProperty("slot").GetInt32()<limit).Select(s=>s.GetProperty("slot").GetInt32()).ToArray();
  if(candidates.Length==0)throw new InvalidOperationException(selectable?"furniture41_no_empty_selectable_slot":"furniture41_no_empty_owned_slot");
  return candidates.Last();
 }
 int Matrix41FurniturePublicCount(){
  int total=Matrix40Read("read_inventory",new{}).GetProperty("slots").EnumerateArray().Where(s=>Terrain.ExtractContents(s.GetProperty("value").GetInt32())==FurnitureBlock.Index).Sum(s=>s.GetProperty("count").GetInt32());
  var observation=Matrix40Read("observe",new{});
  if(observation.TryGetProperty("pickables",out var pickables))total+=pickables.EnumerateArray().Where(s=>Terrain.ExtractContents(s.GetProperty("value").GetInt32())==FurnitureBlock.Index).Sum(s=>s.GetProperty("count").GetInt32());
  return total;
 }
 void Matrix41PrepareFurniture(){
  var forward=p.ComponentBody.Matrix.Forward;var front=p.ComponentBody.Position+2*new Vector3(forward.X,0,forward.Z);
  matrix41FurnitureCell=new(Terrain.ToCell(front.X),Terrain.ToCell(p.ComponentBody.Position.Y)+1,Terrain.ToCell(front.Z));
  // Isolate a single admitted material: native flood-fill must not absorb a
  // neighboring fixture. Matrix40Set registers every changed cell for cleanup.
  for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)Matrix40Set(matrix41FurnitureCell.X+x,matrix41FurnitureCell.Y+y,matrix41FurnitureCell.Z+z,0);
  Matrix40Set(matrix41FurnitureCell.X,matrix41FurnitureCell.Y,matrix41FurnitureCell.Z,PlanksBlock.Index);
  matrix41HammerSlot=Matrix41EmptySlot(selectable:true);matrix41HammerValue=Terrain.MakeBlockValue(IronHammerBlock.Index);p.ComponentMiner.Inventory.AddSlotItems(matrix41HammerSlot,matrix41HammerValue,1);
  matrix41FurnitureLevelBefore=p.PlayerData.Level;matrix41FurnitureRestoreLevel=true;p.PlayerData.Level=Math.Max(p.PlayerData.Level,BlocksManager.Blocks[IronHammerBlock.Index].GetPlayerLevelRequired(matrix41HammerValue));
  matrix41FurnitureBefore=Matrix41FurniturePublicCount();
  ToolAgentGate.Log(new{kind="matrix41_furniture_fixture",artificial=true,worldEdited=true,inventoryInjected=true,levelEdited=p.PlayerData.Level!=matrix41FurnitureLevelBefore,levelBefore=matrix41FurnitureLevelBefore,levelAfter=p.PlayerData.Level,cell=new[]{matrix41FurnitureCell.X,matrix41FurnitureCell.Y,matrix41FurnitureCell.Z},material=PlanksBlock.Index,hammer=new{slot=matrix41HammerSlot,value=matrix41HammerValue,count=1},modelApi=false,worldMustBeDisposed=true});
 }
 void Matrix41EnqueueFurniture(){
  Matrix40Enqueue("act_SetPosture",()=>new{posture="standing"});
  Matrix40Enqueue("act_FaceTowards",()=>new{bearing=0,pitch=0,timeout=4},Matrix41PrepareFurniture);
  Matrix40Enqueue("act_SelectItem",()=>new{slot=matrix41HammerSlot,value=matrix41HammerValue});
  Matrix40Enqueue("act_BuildFurniture",()=>new{target=Matrix40Block(matrix41FurnitureCell.X,matrix41FurnitureCell.Y,matrix41FurnitureCell.Z),slot=matrix41HammerSlot,value=matrix41HammerValue,options=new{name="matrix41",resolution=2,rotation_axis=1,quarter_turns=1}},()=>{
   Matrix40Check(p.Project.FindSubsystem<SubsystemTerrain>(true).Terrain.GetCellContents(matrix41FurnitureCell.X,matrix41FurnitureCell.Y,matrix41FurnitureCell.Z)==0,"furniture41_native_consumed_source_plank");
   var hammer=Matrix41OwnSlot(matrix41HammerSlot);int after=hammer.GetProperty("value").GetInt32();
   Matrix40Check(hammer.GetProperty("count").GetInt32()==1&&Terrain.ExtractContents(after)==IronHammerBlock.Index&&BlocksManager.Blocks[IronHammerBlock.Index].GetDamage(after)>=BlocksManager.Blocks[IronHammerBlock.Index].GetDamage(matrix41HammerValue),"furniture41_owned_hammer_retained_damage_not_reversed");
   ToolAgentGate.Log(new{kind="matrix41_hammer_damage",before=BlocksManager.Blocks[IronHammerBlock.Index].GetDamage(matrix41HammerValue),after=BlocksManager.Blocks[IronHammerBlock.Index].GetDamage(after),note="Vanilla adds one damage; hooks may alter durability behavior."});
   Matrix40Check(Matrix41FurniturePublicCount()==matrix41FurnitureBefore+4,"furniture41_native_output_four_visible_or_owned");
   DisposeCapabilityFurniture41();
  });
  Matrix40Enqueue("act_Wait",()=>new{duration=.1},()=>{
   matrix41CircuitSlot=Matrix41EmptySlot();matrix41CircuitValue=Terrain.MakeBlockValue(TruthTableCircuitBlock.Index);p.ComponentMiner.Inventory.AddSlotItems(matrix41CircuitSlot,matrix41CircuitValue,1);
   ToolAgentGate.Log(new{kind="matrix41_circuit_fixture",artificial=true,inventoryInjected=true,worldEdited=false,slot=matrix41CircuitSlot,value=matrix41CircuitValue,count=1,modelApi=false});
  });
  Matrix40Enqueue("read_item_settings",()=>new{slot=matrix41CircuitSlot,value=matrix41CircuitValue},()=>Matrix40Check(Matrix40Read("read_item_settings",new{slot=matrix41CircuitSlot,value=matrix41CircuitValue}).GetProperty("settings").GetProperty("outputs").GetString()=="","truth_table41_initial_empty"));
  Matrix40Enqueue("act_ConfigureItem",()=>new{slot=matrix41CircuitSlot,value=matrix41CircuitValue,settings=new{outputs="10101"}},()=>{
   var slot=Matrix41OwnSlot(matrix41CircuitSlot);Matrix40Check(slot.GetProperty("count").GetInt32()==1&&slot.GetProperty("value").GetInt32()!=matrix41CircuitValue,"truth_table41_changed_value_preserved_count");matrix41CircuitValue=slot.GetProperty("value").GetInt32();
  });
  Matrix40Enqueue("read_item_settings",()=>new{slot=matrix41CircuitSlot,value=matrix41CircuitValue},()=>Matrix40Check(Matrix40Read("read_item_settings",new{slot=matrix41CircuitSlot,value=matrix41CircuitValue}).GetProperty("settings").GetProperty("outputs").GetString()=="10101","truth_table41_native_configuration_persisted"));
 }
 void DisposeCapabilityFurniture41(){
  if(!matrix41FurnitureRestoreLevel)return;
  p.PlayerData.Level=matrix41FurnitureLevelBefore;matrix41FurnitureRestoreLevel=false;
  ToolAgentGate.Log(new{kind="matrix41_furniture_level_restored",level=matrix41FurnitureLevelBefore});
 }

}
