using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>Lossless operational-fact deduplication of an already bounded model context.
/// Does not change raw receipts, infer possessions, choose recipes, or merge snapshots.</summary>
public static class ToolContextMemory {
 public static JsonElement CompactBounded(JsonElement context,int maxChars=14000,int maxUtf8Bytes=16000){
  if(maxChars is <256 or >16000)throw new ArgumentOutOfRangeException(nameof(maxChars),"Budget must be 256..16000");
  if(maxUtf8Bytes is <256 or >16000)throw new ArgumentOutOfRangeException(nameof(maxUtf8Bytes),"Budget must be 256..16000");
  var compact=Compact(context);string text=compact.GetRawText();int bytes=Encoding.UTF8.GetByteCount(text);
  if(text.Length>maxChars||bytes>maxUtf8Bytes)throw new InvalidOperationException(
   $"Compacted tool context exceeds budget: chars={text.Length}/{maxChars}, utf8Bytes={bytes}/{maxUtf8Bytes}. No partial context returned.");
  return compact;
 }
 public static JsonElement Compact(JsonElement context){
  if(context.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Context object required");
  var root=JsonNode.Parse(context.GetRawText())!.AsObject();
  if(root["facts"] is not JsonObject facts||root["recentResults"] is not JsonArray recent)return context.Clone();
  var canonical=new Dictionary<string,JsonObject>(StringComparer.Ordinal);
  var conflicted=new HashSet<string>(StringComparer.Ordinal);
  int deduped=0,emptyCount=0;
  JsonObject? Normalize(JsonNode? node){
   if(node is not JsonObject entry||entry["reference"] is not JsonValue reference||!reference.TryGetValue<string>(out var id)||string.IsNullOrEmpty(id)
    ||entry["recipe"] is not JsonObject recipe||recipe["ingredients"] is not JsonArray)return null;
   var clone=(JsonObject)entry.DeepClone();clone["recipe"]!.AsObject().Remove("description");return clone;
  }
  void Register(JsonObject item){
   var id=item["reference"]!.GetValue<string>();
   if(canonical.TryGetValue(id,out var previous)){if(!JsonNode.DeepEquals(previous,item))conflicted.Add(id);}
   else canonical.Add(id,item);
  }
  var learned=facts["learnedRecipes"] as JsonArray;
  if(learned is not null)foreach(var node in learned){var item=Normalize(node);if(item is not null)Register(item);}
  // First collect all conflicts, so earlier events are not reduced before a later conflict appears.
  foreach(var node in recent)if(IsRecipeSuccess(node,out var data))foreach(var entry in data!){var item=Normalize(entry);if(item is not null)Register(item);}
  var newLearned=new JsonArray();
  if(learned is not null)foreach(var node in learned){var item=Normalize(node);if(item is null||conflicted.Contains(item["reference"]!.GetValue<string>()))newLearned.Add(node?.DeepClone());}
  foreach(var pair in canonical)if(!conflicted.Contains(pair.Key))newLearned.Add(pair.Value.DeepClone());
  if(learned is not null||canonical.Count>0)facts["learnedRecipes"]=newLearned;
  foreach(var node in recent){
   if(node is not JsonObject receipt)continue;
   if(IsRecipeSuccess(receipt,out var entries)){
    var refs=new JsonArray();bool safe=true;
    foreach(var entry in entries!){var item=Normalize(entry);if(item is null||conflicted.Contains(item["reference"]!.GetValue<string>())){safe=false;break;}refs.Add(item["reference"]!.GetValue<string>());}
    if(safe){var recipeProjection=receipt["data"]!.AsObject();deduped+=entries!.Count;recipeProjection.Remove("data");recipeProjection["recipeRefs"]=refs;recipeProjection["factsLocation"]="facts.learnedRecipes";}
    else receipt["memoryNote"]="Recipe shape or same-reference conflict; complete event retained, do not assume canonical identity.";
   }
   if(Text(receipt["tool"])=="read_storage"&&receipt["ok"] is JsonValue ok&&ok.TryGetValue<bool>(out var success)&&success
    &&receipt["data"] is JsonObject projection&&Text(projection["projectionStatus"])=="projected"
    &&projection["data"] is JsonObject storage&&storage["inventory"] is JsonObject inv&&inv["slots"] is JsonArray slots
    &&!inv.ContainsKey("emptySlotIds")){
    var occupied=new JsonArray();var empty=new JsonArray();var ids=new HashSet<int>();bool valid=true;
    foreach(var entry in slots){
     if(entry is not JsonObject s||s["slot"] is not JsonValue sv||!sv.TryGetValue<int>(out var slot)||slot<0||!ids.Add(slot)){valid=false;break;}
     if(s.Count==3&&s["value"] is JsonValue v&&v.TryGetValue<int>(out var value)&&value==0&&s["count"] is JsonValue c&&c.TryGetValue<int>(out var count)&&count==0)empty.Add(slot);
     else occupied.Add(s.DeepClone());
    }
    if(valid&&empty.Count>0){emptyCount+=empty.Count;inv["slots"]=occupied;inv["emptySlotIds"]=empty;inv["emptySlotEncoding"]="Listed ids encode exactly {slot:id,value:0,count:0}; unlisted slots are not inferred.";}
   }
  }
  root["memoryCompaction"]=JsonSerializer.SerializeToNode(new{recipeEntriesReplacedByReferences=deduped,emptySlotsEncoded=emptyCount,conflictingRecipeRefs=conflicted.Order().ToArray(),storageSnapshotsMerged=false});
  return JsonSerializer.SerializeToElement(root);
 }
 static string? Text(JsonNode? n)=>n is JsonValue v&&v.TryGetValue<string>(out var s)?s:null;
 static bool IsRecipeSuccess(JsonNode? node,out JsonArray? entries){
  entries=null;
  if(node is not JsonObject r||r["tool"] is not JsonValue t||!t.TryGetValue<string>(out var tool)||tool!="find_recipes"
   ||r["ok"] is not JsonValue ok||!ok.TryGetValue<bool>(out var success)||!success
   ||r["data"] is not JsonObject p||p["projectionStatus"] is not JsonValue status||!status.TryGetValue<string>(out var s)||s!="projected")return false;
  entries=p["data"] as JsonArray;return entries is not null;
 }
}
