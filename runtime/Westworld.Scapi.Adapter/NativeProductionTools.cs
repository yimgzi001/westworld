using Game.Westworld;
using System.Text.Json;
namespace Westworld.Scapi.Adapter;

public sealed record NativeProductionSnapshot(ActorToolResult Inventory,ActorToolResult OwnWorkspace,
    ActorToolResult? VisibleWorkspace,ActorToolResult? Recipes,ActorToolResult ActionStatus,ActorToolResult TaskStatus);

/// <summary>Capability profile only: no session, request ledger, action registration,
/// policy loop or world writes. The shared host gateway owns actor/thread binding,
/// freshness, replay protection and tool invocation. All schemas remain native.</summary>
public static class NativeProductionTools {
 static readonly HashSet<string> Names=new(StringComparer.Ordinal){
  "observe","read_self","read_inventory","read_storage","read_item","find_items","find_recipes",
  "plan_craft","act_Craft","act_TransferItems","act_Place","act_SelectItem","act_Wait","act_Eat",
  "action_status","task_status","cancel_action","cancel_task"
 };
 public static IReadOnlyList<ActorToolDefinition> Select(IReadOnlyList<ActorToolDefinition> nativeTools){
  ArgumentNullException.ThrowIfNull(nativeTools);
  return Array.AsReadOnly(nativeTools.Where(d=>Names.Contains(d.Name)).Select(d=>d with{Parameters=d.Parameters.Clone()}).ToArray());
 }
 public static bool Contains(string name)=>name is not null&&Names.Contains(name);
 /// <summary>Extract ONLY issued references from the caller's existing observation.
 /// Does not capture a fresh observation (which could invalidate a running plan's refs).
 /// The native read_storage call still rechecks liveness, access and visibility.</summary>
 public static (string? Own,string? Visible) WorkspaceReferences(JsonElement observation){
  if(observation.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Expected native observe data object.");
  string? Ref(JsonElement obj,string key)=>obj.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
  var own=Ref(observation,"craftingReference");string? visible=null;
  if(observation.TryGetProperty("focus",out var focus)&&focus.ValueKind==JsonValueKind.Object)visible=Ref(focus,"storageReference");
  return(own,visible);
 }
 /// <summary>Optional game-thread query bundle over the same actor-private ActorTools.
 /// The shared gateway may instead dispatch each named query separately. Recipes
 /// only use the provided known item filter; native knowledge controls eligibility.
 /// Every native failure is retained, never converted into an empty successful view.</summary>
 public static NativeProductionSnapshot Read(ActorTools tools,JsonElement existingObservation,int? knownResultValue=null){
  ArgumentNullException.ThrowIfNull(tools);var refs=WorkspaceReferences(existingObservation);
  ActorToolResult Call(string name,object args)=>tools.Invoke(name,JsonSerializer.SerializeToElement(args));
  var inventory=Call("read_inventory",new{});
  var own=refs.Own is null?new ActorToolResult(false,JsonSerializer.SerializeToElement<object?>(null),"missing_own_workspace_reference")
      :Call("read_storage",new{storage=refs.Own});
  ActorToolResult? visible=refs.Visible is null||refs.Visible==refs.Own?null:Call("read_storage",new{storage=refs.Visible});
  ActorToolResult? recipes=knownResultValue.HasValue?Call("find_recipes",new{result_value=knownResultValue.Value,offset=0,limit=32}):null;
  return new(inventory,own,visible,recipes,Call("action_status",new{}),Call("task_status",new{}));
 }
}
