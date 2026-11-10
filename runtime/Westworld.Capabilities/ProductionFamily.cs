using System.Text.Json;
using System.Text.Json.Nodes;
using Westworld.Decisions.Local;
namespace Capability39;
public enum ProductionTargetKind { Block,MovingBlock,Entity }
public sealed record ProductionScope(Guid Session,Guid Actor,Guid Capture);
public sealed record ProductionTarget(string Reference,ProductionTargetKind Kind);
public sealed record ProductionSlot(int Slot,int Value,int Count);
// These are successful, same-capture public native read results, not guessed item properties.
public sealed record ProductionSettings(string? Target,int? Slot,int? Value,string Type);
public sealed record ProductionCraftPreview(string Workspace,int ResultValue,int AvailableCount,int MinimumCount=1,int CountMultiple=1);
public sealed record ProductionFacts(ProductionScope Scope,IReadOnlyList<ProductionTarget> Targets,IReadOnlyList<ProductionSlot> Slots,IReadOnlyList<ProductionSettings> Settings,IReadOnlyList<ProductionCraftPreview> Crafts);
public sealed record ProductionOffer(CapabilityDescriptor Descriptor,CapabilityState State,ToolOffer? Offer,int OfferedAlternatives,int OmittedAlternatives,string Description);
/// <summary>Pure public-fact adapter. Never executes, chooses a goal, or reads hidden game state.</summary>
public static class ProductionFamily {
 public static readonly string[] Names=["act_Drop","act_UseItem","act_UseAimedItem","act_Interact","act_ConfigureBlock","act_ConfigureItem","act_BuildFurniture","act_Craft"];
 public static ProductionOffer Build(string name,JsonElement nativeSchema,bool actorSupported,ProductionScope expected,ProductionFacts facts){
  if(!Names.Contains(name))throw new ArgumentException("Unknown production tool");
  Validate(expected,facts);var node=JsonNode.Parse(nativeSchema.GetRawText())!.AsObject();var props=node["properties"]!.AsObject();
  var d=new CapabilityDescriptor(name,"production_interaction",Kinds(name),true);
  string description=Description(name);
  ProductionOffer No(FactState state,string reason)=>new(d,new(name,actorSupported,state,reason,false),null,0,0,description);
  if(!actorSupported)return No(FactState.Unavailable,"native_actor_unsupported");
  if(name is "act_Craft" or "act_ConfigureBlock" or "act_ConfigureItem")return Associated(name,node,expected,facts,d,description);
  var refs=new Dictionary<string,IReadOnlyList<string>>();var branches=new List<ToolParameterBranch>();
  var slots=facts.Slots.Where(s=>s.Count>0).ToArray();int omitted=0;
  bool owns=name is "act_Drop" or "act_UseItem" or "act_UseAimedItem" or "act_ConfigureItem" or "act_BuildFurniture";
  string[] targets=facts.Targets.Where(t=>name switch {"act_Interact"=>t.Kind is ProductionTargetKind.Block or ProductionTargetKind.MovingBlock,"act_ConfigureBlock" or "act_BuildFurniture"=>t.Kind==ProductionTargetKind.Block,_=>true}).Select(t=>t.Reference).ToArray();
  if(props.ContainsKey("target")){
   targets=targets.Where(t=>StringAllowed(props["target"]!.AsObject(),t)).ToArray();
   if(targets.Length==0){if(name=="act_UseItem")props.Remove("target");else return No(FactState.Unavailable,"no_current_typed_target");}
   else {refs["target"]=targets.Take(128).ToArray();omitted+=Math.Max(0,targets.Length-128);}
  }
  if(owns){
   slots=slots.Where(s=>IntAllowed(props["slot"]!.AsObject(),s.Slot)&&IntAllowed(props["value"]!.AsObject(),s.Value)).ToArray();
   if(slots.Length==0)return No(FactState.Unavailable,"no_owned_item_in_native_bounds");
   NarrowInteger(props,"slot",slots.Select(s=>s.Slot));NarrowInteger(props,"value",slots.Select(s=>s.Value));
   bool count=props.ContainsKey("count");if(count)Bound(props,"count",1,64);
   foreach(var group in slots.Where(s=>!count||s.Count>=props["count"]!["minimum"]!.GetValue<int>()).OrderBy(s=>s.Slot).GroupBy(s=>(s.Value,Cap:count?Math.Min(s.Count,props["count"]!["maximum"]!.GetValue<int>()):1))){
    foreach(var chunk in group.Chunk(128)){
     if(branches.Count>=32){omitted+=chunk.Length;continue;}
     var c=new Dictionary<string,JsonElement>{["slot"]=JsonSerializer.SerializeToElement(new{@enum=chunk.Select(s=>s.Slot)}),["value"]=JsonSerializer.SerializeToElement(new{@const=group.Key.Value})};
     if(count)c["count"]=JsonSerializer.SerializeToElement(new{maximum=group.Key.Cap});
     branches.Add(new(count?["slot","value","count"]:["slot","value"],[],c));
    }
   }
  }
  if(owns&&branches.Count==0)return No(FactState.Unavailable,"insufficient_owned_count");
  if(name=="act_UseAimedItem")Bound(props,"duration",.05,4);
  if(name=="act_BuildFurniture"){
   // Optional options are omitted until full native semantic constraints are represented. Default build remains usable.
   props.Remove("options");description+=" Uses native default options; custom furniture options not yet adapted.";
  }
  if(omitted>0)description+=$" Partial alternatives; {omitted} omitted by explicit bounds.";
  var schema=JsonSerializer.SerializeToElement(node);var offer=new ToolOffer(name,description,schema,refs.Count==0?null:refs,branches.Count==0?null:branches);
  // Run the same production protocol validator before claiming executable. Never suppress validation failure.
  _=new ToolDecisionPrompt(expected.Session,expected.Actor,expected.Capture,JsonSerializer.SerializeToElement(new{}),[offer]);
  return new(d,new(name,true,FactState.Available,omitted>0?"partial_current_facts":"current_facts_native_recheck_required",true),offer,branches.Count>0?branches.Count:1,omitted,description);
 }
 static ProductionOffer Associated(string name,JsonObject node,ProductionScope scope,ProductionFacts facts,CapabilityDescriptor descriptor,string description){
  var p=node["properties"]!.AsObject();var variants=new List<JsonElement>();var references=new List<string>();int omitted=0;
  ProductionOffer No(string reason)=>new(descriptor,new(name,true,FactState.Unknown,reason,false),null,0,0,description);
  void Add(JsonObject variant,string? reference){if(variants.Count==32){omitted++;return;}variants.Add(JsonSerializer.SerializeToElement(variant));if(reference!=null)references.Add(reference);}
  static JsonObject Constant(string type,object value)=>new(){["type"]=type,["const"]=JsonSerializer.SerializeToNode(value)};
  if(name=="act_Craft"){
   var previews=facts.Crafts.Where(c=>c.AvailableCount>0&&StringAllowed(p["workspace"]!.AsObject(),c.Workspace)&&IntAllowed(p["result_value"]!.AsObject(),c.ResultValue)).ToArray();
   if(previews.Length==0)return No("current_crafting_preview_required");
   if(previews.Select(c=>c.Workspace).Distinct().Count()!=previews.Length)throw new ArgumentException("Conflicting workspace previews");
   Bound(p,"count",1,64);NarrowInteger(p,"result_value",previews.Select(c=>c.ResultValue));
   foreach(var c in previews){
    // Native count means output items; publish only complete recipe batches, not
    // unusable sub-batch counts. Enum stays within the existing 64-item profile.
    var counts=Enumerable.Range(1,64).Where(n=>n>=c.MinimumCount&&n<=c.AvailableCount&&n%c.CountMultiple==0&&IntAllowed(p["count"]!.AsObject(),n)).ToArray();
    if(counts.Length==0){omitted++;continue;}
    var v=node.DeepClone().AsObject();var vp=v["properties"]!.AsObject();vp["workspace"]=Constant("string",c.Workspace);vp["result_value"]=Constant("integer",c.ResultValue);NarrowInteger(vp,"count",counts);Add(v,c.Workspace);
   }
  }else{
   bool block=name=="act_ConfigureBlock";
   var reads=facts.Settings.Where(s=>block ? s.Target!=null&&facts.Targets.Any(t=>t.Reference==s.Target&&t.Kind==ProductionTargetKind.Block)&&StringAllowed(p["target"]!.AsObject(),s.Target):s.Slot.HasValue&&s.Value.HasValue&&facts.Slots.Any(i=>i.Slot==s.Slot&&i.Value==s.Value&&i.Count>0)&&IntAllowed(p["slot"]!.AsObject(),s.Slot.Value)&&IntAllowed(p["value"]!.AsObject(),s.Value.Value)).ToArray();
   if(reads.Length==0)return No("current_typed_settings_read_required");
   if(reads.Select(r=>block?r.Target:$"{r.Slot}/{r.Value}").Distinct().Count()!=reads.Length)throw new ArgumentException("Duplicate/conflicting settings read");
   var known=reads.Where(r=>SettingsSchema(r.Type)!=null).ToArray();omitted+=reads.Length-known.Length;
   if(known.Length==0)return No("settings_type_not_yet_adapted");
   var templates=known.Select(r=>SettingsSchema(r.Type)!).GroupBy(x=>x.ToJsonString()).Select(g=>g.First()).ToArray();
   p["settings"]=templates.Length==1?templates[0]:new JsonObject{["oneOf"]=new JsonArray(templates.Select(t=>(JsonNode)t).ToArray())};
   if(!block){NarrowInteger(p,"slot",known.Select(r=>r.Slot!.Value));NarrowInteger(p,"value",known.Select(r=>r.Value!.Value));}
   foreach(var r in known){var v=node.DeepClone().AsObject();var vp=v["properties"]!.AsObject();vp["settings"]=SettingsSchema(r.Type);
    if(block)vp["target"]=Constant("string",r.Target!);else {vp["slot"]=Constant("integer",r.Slot!.Value);vp["value"]=Constant("integer",r.Value!.Value);}
    Add(v,block?r.Target:null);
   }
  }
  if(variants.Count==0)return No("no_current_alternative_within_bounds");
  if(omitted>0)description+=$" Partial alternatives; {omitted} omitted by bounds or unsupported typed settings.";
  var refs=new Dictionary<string,IReadOnlyList<string>>();if(references.Count>0)refs[name=="act_Craft"?"workspace":"target"]=references.Distinct().ToArray();
  var offer=new ToolOffer(name,description,JsonSerializer.SerializeToElement(node),refs.Count==0?null:refs,AssociatedParameterSchemas:variants);
  _=new ToolDecisionPrompt(scope.Session,scope.Actor,scope.Capture,JsonSerializer.SerializeToElement(new{}),[offer]);
  return new(descriptor,new(name,true,FactState.Available,omitted>0?"partial_current_facts":"current_facts_native_recheck_required",true),offer,variants.Count,omitted,description);
 }
 static void Validate(ProductionScope expected,ProductionFacts f){
  if(expected.Session==Guid.Empty||expected.Actor==Guid.Empty||expected.Capture==Guid.Empty||f.Scope!=expected)throw new ArgumentException("Wrong/stale capture");
  if(f.Targets.Count>512||f.Slots.Count>4096||f.Settings.Count>512||f.Crafts.Count>512)throw new ArgumentException("Unbounded facts");
  if(f.Targets.Any(t=>!Enum.IsDefined(t.Kind)||string.IsNullOrWhiteSpace(t.Reference)||t.Reference.Length>160)||f.Targets.Select(t=>t.Reference).Distinct().Count()!=f.Targets.Count)throw new ArgumentException("Invalid target facts");
  if(f.Slots.Any(s=>s.Slot<0||s.Slot>4095||s.Value<0||s.Count<0||s.Count>0&&s.Value==0)||f.Slots.Select(s=>s.Slot).Distinct().Count()!=f.Slots.Count)throw new ArgumentException("Invalid own inventory");
  if(f.Crafts.Any(c=>string.IsNullOrWhiteSpace(c.Workspace)||c.Workspace.Length>160||c.ResultValue<=0||c.AvailableCount<0||c.MinimumCount<1||c.CountMultiple<1))throw new ArgumentException("Invalid preview");
 }
 static ParameterKind[] Kinds(string name)=>name switch {"act_Interact"=>[ParameterKind.LiveTarget],"act_ConfigureBlock"=>[ParameterKind.LiveTarget,ParameterKind.TypedSettings],"act_ConfigureItem"=>[ParameterKind.OwnedItem,ParameterKind.TypedSettings],"act_Craft"=>[ParameterKind.StorageSlot],"act_Drop"=>[ParameterKind.OwnedItem],_=>[ParameterKind.OwnedItem,ParameterKind.LiveTarget]};
 static bool StringAllowed(JsonObject p,string s)=>(p["enum"] is not JsonArray a||a.Any(x=>x!.GetValue<string>()==s))&&(p["const"]==null||p["const"]!.GetValue<string>()==s);
 static bool IntAllowed(JsonObject p,int n)=>(p["minimum"]==null||n>=p["minimum"]!.Deserialize<double>())&&(p["maximum"]==null||n<=p["maximum"]!.Deserialize<double>())&&(p["enum"] is not JsonArray a||a.Any(x=>x!.GetValue<int>()==n))&&(p["const"]==null||p["const"]!.GetValue<int>()==n);
 static void NarrowInteger(JsonObject props,string key,IEnumerable<int> values){
  var p=props[key]!.AsObject();var allowed=values.Distinct().Where(n=>(p["minimum"]==null||n>=p["minimum"]!.Deserialize<double>())&&(p["maximum"]==null||n<=p["maximum"]!.Deserialize<double>())&&(p["enum"] is not JsonArray a||a.Any(x=>x!.GetValue<int>()==n))&&(p["const"]==null||p["const"]!.GetValue<int>()==n)).ToArray();
  if(allowed.Length==0)throw new ArgumentException("No integer candidates within native schema");
  props[key]=new JsonObject{["type"]="integer",["enum"]=JsonSerializer.SerializeToNode(allowed)};
 }
 static void Bound(JsonObject p,string key,double low,double high){var f=p[key]!.AsObject();if(f["minimum"]!=null)low=Math.Max(low,f["minimum"]!.Deserialize<double>());if(f["maximum"]!=null)high=Math.Min(high,f["maximum"]!.Deserialize<double>());if(low>high)throw new ArgumentException("Empty native bounds");bool integer=f["type"]!.GetValue<string>()=="integer";f["minimum"]=integer?JsonValue.Create((int)low):JsonValue.Create(low);f["maximum"]=integer?JsonValue.Create((int)high):JsonValue.Create(high);}
 static JsonObject? SettingsSchema(string type){
  string? key=type switch {"BatteryBlock" or "ButtonBlock" or "SwitchBlock"=>"voltage","AdjustableDelayGateBlock"=>"delaySteps",_=>null};
  if(key==null)return null;
  return new JsonObject{["type"]="object",["properties"]=new JsonObject{[key]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=key=="voltage"?15:255}},["required"]=new JsonArray(key),["additionalProperties"]=false};
 }
 public static string Description(string n)=>n switch {
  "act_Drop"=>"Drop from the exact current owned slot/value up to the offered count; dropped items leave inventory and are not a delivery receipt.",
  "act_UseItem"=>"Use a currently owned item toward an observed world target, or in the current facing direction when target is omitted. Native item behavior decides effect; acceptance is not success.",
  "act_UseAimedItem"=>"Aim and release a current item at an observed world target for bounded duration. Native ammunition, reach and visibility are rechecked; a shot is not a confirmed kill.",
  "act_Interact"=>"Interact with a currently observed block or moving-block target. Native behavior decides the effect; this does not read hidden container contents.",
  "act_ConfigureBlock"=>"Apply only typed settings obtained from a successful current settings read to its matching observed block. No arbitrary JSON or guessed settings.",
  "act_ConfigureItem"=>"Apply typed settings to the exact current owned item; native mutation may change its full value. Read current inventory again afterwards.",
  "act_BuildFurniture"=>"Use an owned tool on the observed connected materials through native furniture construction. Tool suitability, material connectivity and consumption remain native checks.",
  "act_Craft"=>"Collect the currently previewed native crafting result from the associated workspace; this does not create missing ingredients or autonomously fill a recipe.",_=>throw new ArgumentException()};
}
