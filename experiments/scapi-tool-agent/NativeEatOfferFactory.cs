using System.Text.Json;
using Westworld.Decisions.Local;

public sealed record EatMenuCoverage(string Status,int KnownEdibleSlots,int OfferedSlots,int OmittedSlots,int UnknownNutritionSlots,int BranchCount);
public sealed record EatMenuResult(ToolOffer? Offer,EatMenuCoverage Coverage);
/// <summary>Current same-actor inventory only. Correlates legal parameters without selecting food or a goal.</summary>
public static class NativeEatOfferFactory {
 public static EatMenuResult Build(JsonElement inventory,JsonElement parameters,string description,JsonElement? authorityInventory=null){
  if(inventory.ValueKind!=JsonValueKind.Object||!inventory.TryGetProperty("slots",out var rows)||rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()>4096)
   throw new ArgumentException("Bounded current own inventory slots required");
  var props=parameters.GetProperty("properties");var countSchema=props.GetProperty("count");
  int maximum=countSchema.GetProperty("maximum").GetInt32(),minimum=countSchema.GetProperty("minimum").GetInt32();
  if(minimum<1||maximum<minimum)throw new ArgumentException("Invalid native count bounds");
  bool Allowed(JsonElement schema,int value){
   if(schema.TryGetProperty("enum",out var values)&&!values.EnumerateArray().Any(v=>v.TryGetInt32(out int n)&&n==value))return false;
   if(schema.TryGetProperty("const",out var c)&&(!c.TryGetInt32(out int n)||n!=value))return false;
   return (!schema.TryGetProperty("minimum",out var low)||value>=low.GetDouble())&&(!schema.TryGetProperty("maximum",out var high)||value<=high.GetDouble());
  }
  Dictionary<int,(int Value,int Count)>? authoritative=null;
  if(authorityInventory.HasValue){
   authoritative=new();
   foreach(var row in authorityInventory.Value.GetProperty("slots").EnumerateArray()){
    int slot=row.GetProperty("slot").GetInt32(),value=row.GetProperty("value").GetInt32(),count=row.GetProperty("count").GetInt32();
    if(!authoritative.TryAdd(slot,(value,count)))throw new ArgumentException("Duplicate authoritative inventory slot");
   }
  }
  var candidates=new List<(int Slot,int Value,int Count)>();var seen=new HashSet<int>();int known=0,unknown=0;
  foreach(var row in rows.EnumerateArray()){
   if(row.ValueKind!=JsonValueKind.Object||!row.TryGetProperty("slot",out var slotJson)||!slotJson.TryGetInt32(out int slot)||slot<0||slot>4095||!seen.Add(slot)
    ||!row.TryGetProperty("value",out var valueJson)||!valueJson.TryGetInt32(out int value)||value<0
    ||!row.TryGetProperty("count",out var countJson)||!countJson.TryGetInt32(out int count)||count<0||value==0&&count>0)
    throw new ArgumentException("Malformed or duplicated current inventory slot");
   if(authoritative is not null&&(!authoritative.TryGetValue(slot,out var current)||current!=(value,count)))throw new ArgumentException("Visible inventory does not match current native snapshot");
   if(count==0)continue;
   // Read actual metadata only. Never infer nutrition from names, IDs or previous inventory.
   JsonElement nutrition;
   bool has=row.TryGetProperty("nutrition",out nutrition);
   if(!has&&row.TryGetProperty("item",out var item)&&item.ValueKind==JsonValueKind.Object)has=item.TryGetProperty("nutrition",out nutrition);
   if(!has||nutrition.ValueKind!=JsonValueKind.Number||!nutrition.TryGetDouble(out double amount)||!double.IsFinite(amount)){unknown++;continue;}
   if(amount<=0)continue;
   known++;
   int cap=Math.Min(count,maximum);
   if(cap<minimum||!Allowed(props.GetProperty("slot"),slot)||!Allowed(props.GetProperty("value"),value))continue;
   candidates.Add((slot,value,cap));
  }
  var branches=new List<ToolParameterBranch>();int offered=0;
  foreach(var group in candidates.OrderBy(x=>x.Slot).GroupBy(x=>(x.Value,x.Count))){
   foreach(var chunk in group.Chunk(128)){
    if(branches.Count==32)continue;
    var constraints=new Dictionary<string,JsonElement>{
     ["slot"]=JsonSerializer.SerializeToElement(new{@enum=chunk.Select(x=>x.Slot).ToArray()}),
     ["value"]=JsonSerializer.SerializeToElement(new{@const=group.Key.Value}),
     ["count"]=JsonSerializer.SerializeToElement(new{maximum=group.Key.Count})};
    branches.Add(new(new[]{"slot","value","count"},Array.Empty<string>(),constraints));offered+=chunk.Length;
   }
  }
  int omitted=known-offered;
  var coverage=new EatMenuCoverage(omitted>0?"partial":known==0?"no_known_edible_slot":"complete",known,offered,omitted,unknown,branches.Count);
  if(branches.Count==0)return new(null,coverage);
  string text="Consume current owned food; slot/value/count alternatives are paired from this inventory. Initial supplies remain available.";
  if(omitted>0)text+=$" Partial menu: {offered} of {known} known edible slots offered; {omitted} omitted by bounds.";
  return new(new("act_Eat",text,parameters,ParameterBranches:branches),coverage);
 }
}
