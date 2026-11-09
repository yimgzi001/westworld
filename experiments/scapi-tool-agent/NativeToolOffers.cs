using System.Text.Json;
using System.Text.Json.Nodes;
using Game.Westworld;
using Westworld.Decisions.Local;

// Fact/capability filter, not a survival policy. Inputs must be same-actor native results;
// the caller owns freshness and must revalidate each dispatched action.
public static class NativeToolOffers {
 static readonly HashSet<string> Profile=new(StringComparer.Ordinal){
  "observe","read_self","read_environment","read_inventory","read_storage","read_item",
  "find_items","find_creatures","find_recipes","action_status","cancel_action","task_status","cancel_task","plan_craft",
  "act_MoveTo","act_FaceTowards","act_Wait","act_Eat","act_PickUp","act_SelectItem","act_TransferItems",
  "act_Dig","act_Place","act_MeleeAttack","act_Craft","act_Jump","act_Sleep","act_WakeUp"
 };
 public static IReadOnlyList<ToolOffer> Build(IReadOnlyList<ActorToolDefinition> native,JsonElement observation,JsonElement inventory,IReadOnlyList<JsonElement> recentResults){
  ArgumentNullException.ThrowIfNull(native);ArgumentNullException.ThrowIfNull(recentResults);
  if(native.Count>128||recentResults.Count>32)throw new ArgumentException("Bounded native context required");
  observation=Data(observation);inventory=Data(inventory);
  var positions=new HashSet<string>();var blocks=new HashSet<string>();var entities=new HashSet<string>();var drops=new HashSet<string>();
  var storage=new HashSet<string>();var workspaces=new HashSet<string>();var recipes=new HashSet<string>();
  var slots=new HashSet<int>();var values=new HashSet<int>();var allSlots=new HashSet<int>();var allValues=new HashSet<int>();var foodValues=new HashSet<int>();
  void Ref(JsonElement o,string field,params HashSet<string>[] sets){if(o.ValueKind==JsonValueKind.Object&&o.TryGetProperty(field,out var v)&&v.ValueKind==JsonValueKind.String&&v.GetString() is {Length:>0 and <=160} text)foreach(var set in sets)set.Add(text);}
  Ref(observation,"inventoryReference",storage);Ref(observation,"craftingReference",storage,workspaces);
  if(observation.ValueKind==JsonValueKind.Object&&observation.TryGetProperty("focus",out var focus)&&focus.ValueKind==JsonValueKind.Object){
   Ref(focus,"reference",positions);Ref(focus,"storageReference",storage);
   string kind=focus.TryGetProperty("kind",out var k)?k.ToString():"";
   if(kind is "block" or "movingBlocks" or "Block" or "MovingBlocks" or "0" or "1")Ref(focus,"reference",blocks);
   // A visible storage is not assumed to be a crafting table. Personal grid is
   // always available; external workspaces are added only after read_storage preview.
  }
  foreach(var item in Array(observation,"entities"))Ref(item,"reference",entities,positions);
  foreach(var item in Array(observation,"pickables")){
   Ref(item,"reference",drops,positions);
   // target-based pickup accepts non-food too. The food_value shortcut requires an
   // actual host/native nutritional fact, never an item-ID guess or mere visibility.
   if(item.ValueKind==JsonValueKind.Object&&item.TryGetProperty("value",out var v)&&v.TryGetInt32(out int n)&&n>=0
    &&item.TryGetProperty("nutrition",out var nutrition)&&nutrition.ValueKind==JsonValueKind.Number
    &&nutrition.TryGetDouble(out double amount)&&double.IsFinite(amount)&&amount>0)foodValues.Add(n);
  }
  void ReadSlots(JsonElement view,bool own){foreach(var item in Array(view,"slots")){
   if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("slot",out var s)||!s.TryGetInt32(out int slot)||slot<0||slot>4095
    ||!item.TryGetProperty("value",out var v)||!v.TryGetInt32(out int value)||value<0)continue;
   allSlots.Add(slot);allValues.Add(value);if(own){slots.Add(slot);values.Add(value);}
  }}
  ReadSlots(inventory,true);
  foreach(var wrapped in recentResults){
   if(wrapped.ValueKind!=JsonValueKind.Object&&wrapped.ValueKind!=JsonValueKind.Array)continue;
   if(wrapped.ValueKind==JsonValueKind.Object&&wrapped.TryGetProperty("ok",out var ok)&&ok.ValueKind!=JsonValueKind.True)continue;
   var data=Data(wrapped);
   // Native find_recipes yields an array of {reference,recipe:{...}}. Never mine
   // arbitrary strings/text or a future-result placeholder for references.
   if(data.ValueKind==JsonValueKind.Array)foreach(var entry in data.EnumerateArray()){
    if(entry.ValueKind==JsonValueKind.Object&&entry.TryGetProperty("recipe",out var recipe)&&recipe.ValueKind==JsonValueKind.Object)Ref(entry,"reference",recipes);
   }
   if(data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("inventory",out var inv)){
    ReadSlots(inv,false);
    // Association must be explicitly returned by host wrapper, not guessed from slot content.
    if(data.TryGetProperty("crafting",out var preview)&&preview.ValueKind==JsonValueKind.Object&&wrapped.TryGetProperty("storage",out var st)&&st.ValueKind==JsonValueKind.String&&storage.Contains(st.GetString()!))workspaces.Add(st.GetString()!);
   }
  }
  var result=new List<ToolOffer>();
  foreach(var tool in native.Where(t=>Profile.Contains(t.Name))){
   var schema=JsonNode.Parse(tool.Parameters.GetRawText())!.AsObject();StripDescriptions(schema);
   if(schema["properties"] is not JsonObject props||schema["required"] is not JsonArray required)continue;
   bool skip=false;var bindings=new Dictionary<string,IReadOnlyList<string>>();
   foreach(var key in props.Select(p=>p.Key).ToArray()){
    if(props[key] is not JsonObject field){skip=true;break;}
    bool needed=required.Any(n=>n?.GetValue<string>()==key);string type=field["type"]?.GetValue<string>()??"";
    HashSet<string>? refs=key switch{
     "target"=>tool.Name=="act_MeleeAttack"?entities:tool.Name=="act_PickUp"?drops:blocks,
     "destination"=>tool.Name=="act_TransferItems"?storage:positions,
     "source" or "storage"=>storage,"workspace"=>workspaces,"recipe"=>recipes,_=>null};
    if(refs is not null){
     if(refs.Count==0){if(needed){skip=true;break;}props.Remove(key);continue;}
     var choices=refs.Order(StringComparer.Ordinal).Take(128).ToArray();field["enum"]=JsonSerializer.SerializeToNode(choices);bindings.Add(key,choices);continue;
    }
    if(type=="integer"){
     if(key=="limit"&&!needed)required.Add(key);
     HashSet<int>? ints=key=="slot"?slots:key=="value"&&tool.Name.StartsWith("act_",StringComparison.Ordinal)?values:
      key is "source_slot" or "destination_slot"?allSlots:key is "source_value" or "destination_value"?allValues:key=="food_value"?foodValues:null;
     if(ints is not null){if(ints.Count==0){if(needed){skip=true;break;}props.Remove(key);continue;}field["enum"]=JsonSerializer.SerializeToNode(ints.Order().Take(128));}
     else {field["minimum"]=key is "count" or "limit"?1:0;field["maximum"]=key=="count"?64:key=="limit"?4:key=="offset"?4096:int.MaxValue;}
    }else if(type=="number"){
     (double low,double high)=key switch{"bearing"=>(-180,180),"pitch"=>(-82,82),"distance"=>(1,4),"range"=>(.1,1),"duration"=>(.1,tool.Name=="act_Wait"?10:4),"timeout"=>(.1,tool.Name=="act_Dig"?30:4),"look_distance" or "object_distance"=>(1,16),_=>(0,4)};
     field["minimum"]=low;field["maximum"]=high;
     // Native omission defaults can be longer; require explicit finite action deadline.
     if(key=="timeout"&&!needed)required.Add(key);
    }else if(type=="string"){if(field["enum"] is null)field["maxLength"]=96;}
    else if(type!="boolean"){skip=true;break;}
   }
   if(skip)continue;
   // No string positions or coordinate escape hatch; optional references may be absent.
   if(tool.Name=="act_PickUp"&&!props.ContainsKey("target")&&!props.ContainsKey("food_value"))continue;
   IReadOnlyList<ToolParameterBranch>? branches=tool.Name switch{
    "act_MoveTo"=>new[]{new ToolParameterBranch(new[]{"destination"},new[]{"bearing","distance"}),new ToolParameterBranch(new[]{"bearing","distance"},new[]{"destination"})},
    "act_FaceTowards"=>new[]{new ToolParameterBranch(new[]{"destination"},new[]{"bearing","pitch"}),new ToolParameterBranch(new[]{"bearing"},new[]{"destination"})},
    "act_PickUp"=>new[]{new ToolParameterBranch(new[]{"target"},new[]{"food_value"}),new ToolParameterBranch(new[]{"food_value"},new[]{"target"})},_=>null};
   if(branches is not null){branches=branches.Where(b=>b.Required.All(props.ContainsKey)).ToArray();if(branches.Count==0)continue;}
   result.Add(new(tool.Name,Describe(tool),JsonSerializer.SerializeToElement(schema),bindings,branches));
  }
  return result.AsReadOnly();
 }
 // Preserve operational prerequisites lost when verbose parameter descriptions are removed.
 // These explain tool semantics, never prescribe a resource or survival sequence.
 static string Describe(ActorToolDefinition tool)=>tool.Name switch{
  "act_Craft"=>"Collect result from an already correctly arranged crafting grid. Does not fill ingredients. Grid crafting cannot heat/cook food.",
  "plan_craft"=>"Arrange existing carried/grid materials and craft a known recipe at a suitable grid. count is output items, not batches; must be a multiple of recipe.resultCount. No gathering; no heated/furnace recipes.",
  "act_TransferItems"=>"Move existing items. source_value is source slot CURRENT value; destination_value is destination slot CURRENT value (0 if empty), not the transferred item.",
  "read_storage"=>"Read actual slots and crafting preview or furnace heat/fuel/progress. Furnaces process matching ingredients with sufficient native heat/fuel over time, not act_Craft.",
  "find_recipes"=>"Read recipe knowledge: materials, grid/station and heat requirements. Recipe existence does not mean supplies or a usable station exist.",
  _=>tool.Description.Length<=100?tool.Description:tool.Description[..100]
 };
 static JsonElement Data(JsonElement e)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty("data",out var d)?d:e;
 static IEnumerable<JsonElement> Array(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var a)&&a.ValueKind==JsonValueKind.Array?a.EnumerateArray():Enumerable.Empty<JsonElement>();
 static void StripDescriptions(JsonNode node){if(node is JsonObject obj){obj.Remove("description");foreach(var pair in obj.ToArray())if(pair.Value is not null)StripDescriptions(pair.Value);}else if(node is JsonArray array)foreach(var item in array)if(item is not null)StripDescriptions(item);}
}
