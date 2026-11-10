using System.Text;
using System.Text.Json;

public sealed record ToolOutcomeSnapshot(Guid SessionId,Guid ActorId,Guid ObservationId,JsonElement Inventory);
public sealed record ToolOutcomeTerminal(string Tool,long Sequence,string Status);
public sealed record ToolUnexecutedStep(string Tool,string Reason);

/// <summary>Public before/after inventory facts, not attribution, advice, or a new action plan.</summary>
public static class ToolOutcomeDelta {
 public static JsonElement Build(ToolOutcomeSnapshot before,ToolOutcomeSnapshot after,string originalGoal,
  ToolOutcomeTerminal preceding,IReadOnlyList<ToolUnexecutedStep> unexecuted,
  int maxChanges=8,int maxChars=3000,int maxUtf8Bytes=6000){
  ArgumentNullException.ThrowIfNull(before);ArgumentNullException.ThrowIfNull(after);ArgumentNullException.ThrowIfNull(preceding);ArgumentNullException.ThrowIfNull(unexecuted);
  if(before.SessionId==Guid.Empty||before.ActorId==Guid.Empty||before.ObservationId==Guid.Empty||after.ObservationId==Guid.Empty
   ||before.SessionId!=after.SessionId||before.ActorId!=after.ActorId||before.ObservationId==after.ObservationId)throw new ArgumentException("Distinct before/after observations of the same session and actor required");
  Text(originalGoal,1024,"goal");ToolName(preceding.Tool);
  if(preceding.Sequence<=0||preceding.Status is not("succeeded" or "failed" or "cancelled" or "arrived" or "rejected" or "skipped"))throw new ArgumentException("Actual preceding terminal required");
  if(unexecuted.Count>4||maxChanges is <0 or >128||maxChars is <256 or >16000||maxUtf8Bytes is <256 or >16000)throw new ArgumentOutOfRangeException("feedback budget");
  var pending=unexecuted.Select(s=>{if(s is null)throw new ArgumentException("Missing unexecuted step");ToolName(s.Tool);Text(s.Reason,240,"reason");return new{tool=s.Tool,reason=s.Reason,executed=false};}).ToArray();
  var old=Read(before.Inventory,out var oldSlots);var current=Read(after.Inventory,out var currentSlots);
  if(!oldSlots.SetEquals(currentSlots))throw new ArgumentException("Full slot coverage must match; missing slots are not zero inventory");
  var changes=old.Keys.Union(current.Keys).Order().Select(value=>new{value,before=old.GetValueOrDefault(value),after=current.GetValueOrDefault(value),net=current.GetValueOrDefault(value)-old.GetValueOrDefault(value)}).Where(v=>v.net!=0).ToArray();
  int keep=Math.Min(maxChanges,changes.Length);
  JsonElement Encode()=>JsonSerializer.SerializeToElement(new{
   kind="tool_outcome_delta",sessionId=before.SessionId,actorId=before.ActorId,beforeObservationId=before.ObservationId,afterObservationId=after.ObservationId,
   originalGoal,preceding=new{tool=preceding.Tool,sequence=preceding.Sequence,status=preceding.Status},unexecuted=pending,
   inventoryDelta=changes.Take(keep).ToArray(),totalChangedValues=changes.Length,omittedChangedValues=changes.Length-keep,
   note="Historical action-result snapshot delta, not live inventory. Current inventory and goal progress may include subsequent automatic collection; an empty delta does not negate current possession. Source/cause not inferred. Unexecuted steps did not run. Omitted changes unknown. No next action prescribed."});
  while(true){var output=Encode();var json=output.GetRawText();if(json.Length<=maxChars&&Encoding.UTF8.GetByteCount(json)<=maxUtf8Bytes)return output;if(keep==0)throw new InvalidOperationException("Outcome identity/goal/terminal/unexecuted facts exceed budget; no partial feedback emitted");keep--;}
 }
 static Dictionary<int,long> Read(JsonElement inventory,out HashSet<int> ids){
  if(inventory.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Full native inventory object required");Unique(inventory);
  if(!inventory.TryGetProperty("slots",out var slots)||slots.ValueKind!=JsonValueKind.Array||slots.GetArrayLength()>4096||inventory.TryGetProperty("emptySlotIds",out _))throw new ArgumentException("Full slots required; compact omitted slots are not inferred");
  ids=new HashSet<int>();var counts=new Dictionary<int,long>();
  foreach(var item in slots.EnumerateArray()){
   if(item.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Slot object required");Unique(item);
   int slot=Number(item,"slot"),value=Number(item,"value"),count=Number(item,"count");
   if(slot>4095||!ids.Add(slot)||count>0&&value==0)throw new ArgumentException("Duplicate or invalid slot");
   if(count>0)counts[value]=checked(counts.GetValueOrDefault(value)+(long)count);
  }
  return counts;
 }
 static int Number(JsonElement value,string key){if(!value.TryGetProperty(key,out var n)||n.ValueKind!=JsonValueKind.Number||!n.TryGetInt32(out int result)||result<0)throw new ArgumentException("Nonnegative integer "+key+" required");return result;}
 static void Unique(JsonElement value){var keys=new HashSet<string>(StringComparer.Ordinal);foreach(var p in value.EnumerateObject())if(!keys.Add(p.Name))throw new ArgumentException("Duplicate JSON property");}
 static void Text(string text,int maximum,string field){if(string.IsNullOrWhiteSpace(text)||text.Length>maximum||text.Any(char.IsControl))throw new ArgumentException("Invalid "+field);}
 static void ToolName(string tool){Text(tool,64,"tool");if(!char.IsAsciiLetter(tool[0])||tool.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))throw new ArgumentException("Invalid tool name");}
}
