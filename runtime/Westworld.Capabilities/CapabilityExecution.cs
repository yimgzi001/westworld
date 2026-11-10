namespace Capability39;
// Native task submissions share the existing task executor; they are not queries
// and must be drained before the next direct action. This does not enable prefetch.
public static class CapabilityExecution {
 public static IReadOnlyList<Westworld.Decisions.Local.ToolOffer> MergeActions(IEnumerable<Westworld.Decisions.Local.ToolOffer> legacy,IEnumerable<KeyValuePair<string,Westworld.Decisions.Local.ToolOffer?>> overrides){
  var all=legacy.ToDictionary(x=>x.Name,x=>(Westworld.Decisions.Local.ToolOffer?)x,StringComparer.Ordinal);
  foreach(var item in overrides)all[item.Key]=item.Value; // null is an explicit tombstone, never fall back to loose legacy schema
  return all.Where(x=>x.Key.StartsWith("act_",StringComparison.Ordinal)&&x.Value is not null).Select(x=>x.Value!).ToArray();
 }
 public static bool IsNativeTask(string tool)=>tool is "plan_craft" or "submit_plan";
 public static bool TracksOutcome(string tool)=>tool.StartsWith("act_",StringComparison.Ordinal)||tool=="host_navigate_local"||IsNativeTask(tool);
 public static string? StatusTool(string tool)=>tool is "host_navigate_local" or "host_cancel_navigation"?"host_navigation_status":IsNativeTask(tool)?"task_status":tool.StartsWith("act_",StringComparison.Ordinal)?"action_status":null;
}
public static class CapabilityDispatchPolicy {
 public static bool KnownIdle(System.Text.Json.JsonElement s,bool direct=false)=>direct&&s.ValueKind==System.Text.Json.JsonValueKind.Null||s.ValueKind==System.Text.Json.JsonValueKind.Object&&s.TryGetProperty("status",out var status)&&(direct?status.GetString() is "succeeded" or "failed" or "cancelled":status.GetString() is "idle" or "succeeded" or "failed" or "cancelled" or "skipped" or "arrived");
 public static bool IsBusy(System.Text.Json.JsonElement s)=>s.ValueKind==System.Text.Json.JsonValueKind.Object&&s.TryGetProperty("status",out var v)&&v.GetString() is "running" or "waiting" or "blocked" or "stopping";
 public static bool MayOffer(string tool,bool actionBusy,bool taskBusy,bool navBusy,bool afterPrecedingSuccess=false)=>!CapabilityExecution.TracksOutcome(tool)||afterPrecedingSuccess||!(actionBusy||taskBusy||navBusy);
 public static bool SameTask(System.Text.Json.JsonElement original,System.Text.Json.JsonElement current)=>original.ValueKind==System.Text.Json.JsonValueKind.Object&&current.ValueKind==System.Text.Json.JsonValueKind.Object&&original.TryGetProperty("taskId",out var a)&&current.TryGetProperty("taskId",out var b)&&a.TryGetGuid(out var x)&&x!=Guid.Empty&&b.TryGetGuid(out var y)&&x==y;
 public static string ReadKey(string tool,System.Text.Json.JsonElement args)=>tool+":"+Canonical(args);
 static string Canonical(System.Text.Json.JsonElement x)=>x.ValueKind==System.Text.Json.JsonValueKind.Object?"{"+string.Join(",",x.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>System.Text.Json.JsonSerializer.Serialize(p.Name)+":"+Canonical(p.Value)))+"}":x.ValueKind==System.Text.Json.JsonValueKind.Array?"["+string.Join(",",x.EnumerateArray().Select(Canonical))+"]":x.GetRawText();
}
