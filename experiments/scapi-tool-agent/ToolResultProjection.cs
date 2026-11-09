using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>Model-only view. Caller retains raw receipts for audit and reference revalidation.
/// Omissions are not native failures or proof that more native pages exist.</summary>
public static class ToolResultProjection {
 public static JsonElement Project(string tool,JsonElement raw,int maxChars=3000,int maxUtf8Bytes=6000){
  Bounds(maxChars,maxUtf8Bytes);
  var data=raw;bool? hasMore=null;
  if(raw.ValueKind==JsonValueKind.Object&&raw.TryGetProperty("data",out var wrapped)){
   if(raw.TryGetProperty("ok",out var ok)&&ok.ValueKind!=JsonValueKind.True)return Result("native_not_success",null,0,null);
   data=wrapped;
   if(raw.TryGetProperty("hasMore",out var hm)&&hm.ValueKind is JsonValueKind.True or JsonValueKind.False)hasMore=hm.GetBoolean();
  }
  try{
   if(tool is "host_navigate_local" or "host_navigation_status" or "host_cancel_navigation"){
    // Explicit rejection is retained as failure evidence, never rewritten to success.
    bool rejected=data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("status",out var status)&&status.ValueKind==JsonValueKind.String&&status.GetString()=="rejected";
    if(!rejected&&!ValidNavigationStatus(data))return Result("unsupported_shape",null,0,null);
    var nav=Result("projected",JsonNode.Parse(data.GetRawText()),0,null);
    return Fits(nav,maxChars,maxUtf8Bytes)?nav:Result("budget_exceeded",null,1,null);
   }
   if(tool=="read_map"){
    if(!ValidMapPage(data))return Result("unsupported_shape",null,0,null);
    // Complete actual page; never invent a continuation cursor from a truncated list.
    var page=Result("projected",JsonNode.Parse(data.GetRawText()),0,data.GetProperty("hasMore").GetBoolean());
    return Fits(page,maxChars,maxUtf8Bytes)?page:Result("budget_exceeded",null,data.GetProperty("entries").GetArrayLength(),data.GetProperty("hasMore").GetBoolean());
   }

   if(tool is "find_items" or "find_creatures" or "find_recipes"){
    if(data.ValueKind!=JsonValueKind.Array)return Result("unsupported_shape",null,0,hasMore);
    var output=new JsonArray();int total=data.GetArrayLength(),kept=0;
    foreach(var entry in data.EnumerateArray()){
     if(kept>=4)break;
     JsonNode item=tool switch{"find_items"=>Item(entry),"find_creatures"=>Select(entry,["type","name","walkSpeed","swimSpeed","flySpeed","canBeRidden"],["type","name"]),_=>RecipeReference(entry)};
     output.Add(item);
     if(!Fits(Result("projected",output,total-kept-1,hasMore),maxChars,maxUtf8Bytes)){output.RemoveAt(output.Count-1);break;}kept++;
    }
    return Result("projected",output,total-kept,hasMore);
   }
   JsonNode? view=tool switch{
    "read_item"=>data.ValueKind==JsonValueKind.Null?null:Item(data),
    "read_storage"=>Storage(data),
    _=>JsonNode.Parse(data.GetRawText())};
   var result=Result("projected",view,0,hasMore);
   return Fits(result,maxChars,maxUtf8Bytes)?result:Result("budget_exceeded",null,1,hasMore);
  }catch(ArgumentException){return Result("unsupported_shape",null,0,hasMore);}
 }
 // Facts are atomic: never silently remove identity/health/inventory or truncate references.
 // Caller must not request a model decision when status is facts_too_large.
 public static JsonElement BuildContext(JsonElement facts,IReadOnlyList<JsonElement> projectedRecent,int maxChars=14000,int maxUtf8Bytes=16000){
  Bounds(maxChars,maxUtf8Bytes);ArgumentNullException.ThrowIfNull(projectedRecent);
  if(facts.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Facts must be an object");
  var list=new JsonArray();int total=projectedRecent.Count,kept=0;
  JsonElement Wrap(string status)=>JsonSerializer.SerializeToElement(new{projectionStatus=status,facts, recentResults=list,omittedRecent=total-kept});
  if(!Fits(Wrap("projected"),maxChars,maxUtf8Bytes))return JsonSerializer.SerializeToElement(new{projectionStatus="facts_too_large",facts=(object?)null,recentResults=Array.Empty<object>(),omittedRecent=total});
  // Most recent complete records first; no partial ingredient lists or partial slot records.
  foreach(var r in projectedRecent.Reverse()){
   list.Insert(0,JsonNode.Parse(r.GetRawText()));kept++;
   if(!Fits(Wrap("projected"),maxChars,maxUtf8Bytes)){list.RemoveAt(0);kept--;break;}
  }
  return Wrap("projected");
 }
 internal static bool ValidNavigationStatus(JsonElement e){
  if(e.ValueKind!=JsonValueKind.Object||!e.TryGetProperty("busy",out var busy)||busy.ValueKind is not(JsonValueKind.True or JsonValueKind.False)
   ||!e.TryGetProperty("request_id",out var id)||id.ValueKind!=JsonValueKind.String||!id.TryGetGuid(out var request)
   ||!e.TryGetProperty("status",out var status)||status.ValueKind!=JsonValueKind.String)return false;
  string s=status.GetString()!;
  if(s is not("idle" or "running" or "stopping" or "arrived" or "failed" or "cancelled"))return false;
  return busy.GetBoolean()?(request!=Guid.Empty&&s is "running" or "stopping"):s is "idle" or "arrived" or "failed" or "cancelled";
 }
 internal static bool ValidMapPage(JsonElement e){
  if(e.ValueKind!=JsonValueKind.Object||!e.TryGetProperty("entries",out var entries)||entries.ValueKind!=JsonValueKind.Array||entries.GetArrayLength()>4
   ||!Int(e,"offset",out int offset)||!Int(e,"total",out int total)||offset<0||total<0||total>128||offset>total
   ||!e.TryGetProperty("snapshotGameTime",out var time)||time.ValueKind!=JsonValueKind.Number||!time.TryGetDouble(out double now)||!double.IsFinite(now)||now<0
   ||!e.TryGetProperty("hasMore",out var more)||more.ValueKind is not(JsonValueKind.True or JsonValueKind.False))return false;
  bool hasNext=e.TryGetProperty("nextOffset",out var next);
  int end=offset+entries.GetArrayLength();if(end>total||more.GetBoolean()!=(end<total)||(end<total?(!hasNext||next.ValueKind!=JsonValueKind.Number||!next.TryGetInt32(out int n)||n!=end):(hasNext&&next.ValueKind!=JsonValueKind.Null)))return false;
  if(end<total&&entries.GetArrayLength()==0)return false;
  var ids=new HashSet<string>();
  foreach(var p in entries.EnumerateArray()){
   if(p.ValueKind!=JsonValueKind.Object||!p.TryGetProperty("reference",out var r)||r.ValueKind!=JsonValueKind.String||r.GetString() is not {Length:>0 and <=160} id||!ids.Add(id)
    ||!p.TryGetProperty("lastSeenGameTime",out var last)||last.ValueKind!=JsonValueKind.Number||!last.TryGetDouble(out double seen)||!double.IsFinite(seen)||seen<0||seen>now
    ||!p.TryGetProperty("position",out var pos)||pos.ValueKind!=JsonValueKind.Object)return false;
   foreach(string axis in new[]{"x","y","z"})if(!pos.TryGetProperty(axis,out var v)||v.ValueKind!=JsonValueKind.Number||!v.TryGetDouble(out double number)||!double.IsFinite(number)||Math.Abs(number)>10000000)return false;
  }
  return true;
 }
 static bool Int(JsonElement e,string key,out int value){value=0;return e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out value);}
 static JsonNode Item(JsonElement e)=>Select(e,["name","value","type","craftingId","nutrition","fuelHeat","fuelDuration","maxStack","durability","canWear","canAim"],["name","value","craftingId","nutrition","fuelHeat","fuelDuration"]);
 static JsonNode RecipeReference(JsonElement e){
  var node=Select(e,["reference"],["reference"]);
  if(!e.TryGetProperty("recipe",out var r))throw new ArgumentException();
  node["recipe"]=Recipe(r);return node;
 }
 static JsonNode Recipe(JsonElement e){
  var node=Select(e,["resultValue","resultCount","remainsValue","remainsCount","station","requiredHeat","requiredLevel","ingredients"],["resultValue","resultCount","station","requiredHeat","ingredients"]);
  if(e.GetProperty("ingredients").ValueKind!=JsonValueKind.Array)throw new ArgumentException();
  foreach(var ingredient in e.GetProperty("ingredients").EnumerateArray())
   if(ingredient.ValueKind!=JsonValueKind.Object||!ingredient.TryGetProperty("craftingId",out _)||!ingredient.TryGetProperty("column",out _)||!ingredient.TryGetProperty("row",out _))throw new ArgumentException();
  return node;
 }
 static JsonNode Storage(JsonElement e){
  if(e.ValueKind!=JsonValueKind.Object||!e.TryGetProperty("inventory",out var inv)||inv.ValueKind!=JsonValueKind.Object||!inv.TryGetProperty("slots",out var slots)||slots.ValueKind!=JsonValueKind.Array)throw new ArgumentException();
  foreach(var slot in slots.EnumerateArray())if(slot.ValueKind!=JsonValueKind.Object||!slot.TryGetProperty("slot",out _)||!slot.TryGetProperty("value",out _)||!slot.TryGetProperty("count",out _))throw new ArgumentException();
  // Keep complete native inventory and furnace state, including empty slots and slot roles.
  var node=Select(e,["inventory","furnace"],["inventory"]);
  if(e.TryGetProperty("crafting",out var crafting)){
   if(crafting.ValueKind==JsonValueKind.Null)node["crafting"]=null;
   else {var c=Select(crafting,["materialBatches","message","resultSlot","remainsSlot"],[]);
    if(crafting.TryGetProperty("recipe",out var recipe))c["recipe"]=recipe.ValueKind==JsonValueKind.Null?null:Recipe(recipe);
    node["crafting"]=c;}
  }
  return node;
 }
 static JsonObject Select(JsonElement e,string[] keys,string[] required){
  if(e.ValueKind!=JsonValueKind.Object||required.Any(k=>!e.TryGetProperty(k,out _)))throw new ArgumentException();
  var result=new JsonObject();foreach(var k in keys)if(e.TryGetProperty(k,out var value))result[k]=JsonNode.Parse(value.GetRawText());return result;
 }
 static JsonElement Result(string status,JsonNode? data,int omitted,bool? hasMore)=>JsonSerializer.SerializeToElement(new{projectionStatus=status,data,truncated=omitted>0,omitted,hasMore});
 static bool Fits(JsonElement value,int chars,int bytes){string text=value.GetRawText();return text.Length<=chars&&Encoding.UTF8.GetByteCount(text)<=bytes;}
 static void Bounds(int chars,int bytes){if(chars<256||chars>16000||bytes<256||bytes>16000)throw new ArgumentOutOfRangeException("Projection budgets must be 256..16000");}
}
