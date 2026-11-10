using Game;
using System.Text.Json;

// Reuses the isolated world's declared two-log fixture. No materials/results
// injected here: a public native transfer fills the personal grid, then act_Craft
// collects its actual preview. This is not plan_craft or natural resource gathering.
internal sealed partial class ToolAgentScenario {
 int queries43WoodBefore,queries43PlanksBefore,queries43Output;
 string Queries43Grid()=>Matrix40Read("observe",new{look_distance=12,object_distance=8,limit=4,block_limit=32}).GetProperty("craftingReference").GetString();
 JsonElement Queries43Storage()=>Matrix40Read("read_storage",new{storage=Queries43Grid()});
 void Queries43Audit(string phase)=>ToolAgentGate.Log(new{kind="queries43_state",phase,inventory=Matrix40Read("read_inventory",new{}),storage=Queries43Storage(),modelApi=false});
 object Queries43TransferArgs(){
  var observation=Matrix40Read("observe",new{look_distance=12,object_distance=8,limit=4,block_limit=32});var storage=Queries43Storage();
  var slot0=storage.GetProperty("inventory").GetProperty("slots").EnumerateArray().Single(s=>s.GetProperty("slot").GetInt32()==0);
  if(slot0.GetProperty("count").GetInt32()!=0)throw new InvalidOperationException("queries43_personal_grid_not_empty");
  var source=Matrix40Read("read_inventory",new{}).GetProperty("slots").EnumerateArray().First(s=>s.GetProperty("value").GetInt32()==OakWoodBlock.Index&&s.GetProperty("count").GetInt32()>0);
  queries43WoodBefore=ExactCount(OakWoodBlock.Index);queries43PlanksBefore=ExactCount(PlanksBlock.Index);
  Queries43Audit("before_material_transfer");
  return new{source=observation.GetProperty("inventoryReference").GetString(),source_slot=source.GetProperty("slot").GetInt32(),source_value=OakWoodBlock.Index,destination=observation.GetProperty("craftingReference").GetString(),destination_slot=0,destination_value=0,count=1};
 }
 void Queries43Preview(){
  var storage=Queries43Storage();var grid=storage.GetProperty("inventory").GetProperty("slots").EnumerateArray().Single(s=>s.GetProperty("slot").GetInt32()==0);
  Matrix40Check(grid.GetProperty("value").GetInt32()==OakWoodBlock.Index&&grid.GetProperty("count").GetInt32()==1&&ExactCount(OakWoodBlock.Index)==queries43WoodBefore-1,"queries43_native_transfer_exact_material_conservation");
  var crafting=storage.GetProperty("crafting");var recipe=crafting.GetProperty("recipe");queries43Output=recipe.GetProperty("resultCount").GetInt32();
  Matrix40Check(recipe.GetProperty("resultValue").GetInt32()==PlanksBlock.Index&&queries43Output>0&&crafting.GetProperty("materialBatches").GetInt32()==1,"queries43_public_preview_one_real_material_batch");
  Queries43Audit("prepared_grid_preview");
 }
 object Queries43CraftArgs(){
  var storage=Queries43Storage();var recipe=storage.GetProperty("crafting").GetProperty("recipe");
  return new{workspace=Queries43Grid(),result_value=recipe.GetProperty("resultValue").GetInt32(),count=recipe.GetProperty("resultCount").GetInt32()};
 }
 void Queries43VerifyCraft(){
  Queries43Audit("after_direct_native_craft");var receipt=Matrix40Read("action_status",new{});
  Matrix40Check(receipt.GetProperty("action").GetString()=="Craft"&&receipt.GetProperty("affectedCount").GetInt32()==queries43Output,"queries43_direct_craft_native_output_receipt");
  Matrix40Check(ExactCount(PlanksBlock.Index)==queries43PlanksBefore+queries43Output&&ExactCount(OakWoodBlock.Index)==queries43WoodBefore-1,"queries43_actual_material_and_product_inventory_delta");
  var storage=Queries43Storage();var grid=storage.GetProperty("inventory").GetProperty("slots").EnumerateArray().Single(s=>s.GetProperty("slot").GetInt32()==0);
  Matrix40Check(grid.GetProperty("count").GetInt32()==0,"queries43_recipe_consumed_real_grid_material");
 }
 void Matrix43EnqueueQueries(){
  Matrix40Enqueue("observe",()=>new{look_distance=12,object_distance=8,limit=4,block_limit=32},()=>{var o=matrix40Receipt.Data;Matrix40Check(o.GetProperty("self").ValueKind==JsonValueKind.Object&&o.GetProperty("visibleBlocks").ValueKind==JsonValueKind.Array&&!string.IsNullOrEmpty(o.GetProperty("inventoryReference").GetString())&&!string.IsNullOrEmpty(o.GetProperty("craftingReference").GetString()),"queries43_public_observation_and_owned_refs_present");});
  Matrix40Enqueue("find_creatures",()=>new{search="Cow",offset=0,limit=4},()=>{var entries=Matrix40Read("find_creatures",new{search="Cow",offset=0,limit=4});Matrix40Check(entries.GetArrayLength()>0&&entries.GetArrayLength()<=4&&entries.EnumerateArray().All(e=>e.GetProperty("type").GetString().Contains("Cow",StringComparison.OrdinalIgnoreCase)),"queries43_creature_guide_bounded_cow_results_not_live_entities");});
  Matrix40Enqueue("read_storage",()=>new{storage=Queries43Grid()},()=>{var s=Queries43Storage();Matrix40Check(s.GetProperty("inventory").GetProperty("slots").GetArrayLength()>0&&s.GetProperty("crafting").ValueKind==JsonValueKind.Object,"queries43_personal_storage_read_actual_grid");});
  Matrix40Enqueue("act_TransferItems",Queries43TransferArgs,Queries43Preview);
  Matrix40Enqueue("read_storage",()=>new{storage=Queries43Grid()},Queries43Preview);
  Matrix40Enqueue("act_Craft",Queries43CraftArgs,Queries43VerifyCraft);
 }
}
