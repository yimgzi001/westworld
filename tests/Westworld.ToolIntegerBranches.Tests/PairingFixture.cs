using System.Text.Json;
using Westworld.Decisions.Local;
// Test-only fixture, not production factory. Only actor-owned CURRENT slots. Caller must bind this inventory to current observation/actor.
public sealed record EatPairingResult(ToolOffer? Offer,int Omitted,string Status);
public static class EatPairing {
 public static EatPairingResult Build(JsonElement slots,JsonElement nativeParameters){
  if(slots.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Actual slots required");
  var branches=new List<ToolParameterBranch>();var seen=new HashSet<int>();int omitted=0;var edible=new List<(int Slot,int Value,int Count)>();
  var countSchema=nativeParameters.GetProperty("properties").GetProperty("count");
  int countMax=countSchema.GetProperty("maximum").GetInt32();if(countMax<1)throw new ArgumentException("Invalid native count limit");
  foreach(var row in slots.EnumerateArray()){
   if(!row.TryGetProperty("slot",out var sr)||!sr.TryGetInt32(out int slot)||slot<0||!seen.Add(slot))throw new ArgumentException("Invalid or duplicate slot");
   if(!row.TryGetProperty("value",out var vr)||!vr.TryGetInt32(out int value)||value<0||!row.TryGetProperty("count",out var cr)||!cr.TryGetInt32(out int count)||count<0)throw new ArgumentException("Invalid slot contents");
   if(value==0||count==0)continue;
   if(!row.TryGetProperty("nutrition",out var nr)||!nr.TryGetDouble(out double nutrition)||!double.IsFinite(nutrition)||nutrition<=0)continue;
   edible.Add((slot,value,Math.Min(count,countMax)));
  }
  foreach(var group in edible.OrderBy(x=>x.Slot).GroupBy(x=>(x.Value,x.Count))){
   if(branches.Count==32){omitted+=group.Count();continue;}
   var constraints=new Dictionary<string,JsonElement>{["slot"]=JsonSerializer.SerializeToElement(new{@enum=group.Select(x=>x.Slot).ToArray()}),["value"]=JsonSerializer.SerializeToElement(new{@const=group.Key.Value}),["count"]=JsonSerializer.SerializeToElement(new{maximum=group.Key.Count})};
   branches.Add(new(new[]{"slot","value","count"},Array.Empty<string>(),constraints));
  }
  return new(branches.Count==0?null:new ToolOffer("act_Eat","Consume existing owned food; each slot/value/count alternative comes from current observed inventory.",nativeParameters,ParameterBranches:branches),omitted,omitted>0?"partial":branches.Count==0?"no_known_edible_slot":"complete");
 }
}
