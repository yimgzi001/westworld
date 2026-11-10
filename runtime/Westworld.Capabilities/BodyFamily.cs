using System.Text.Json;
using System.Text.Json.Nodes;
using Westworld.Decisions.Local;
namespace Capability39;
public sealed record OwnSlot(int Slot,int Value,int Count);
public sealed record WornSlot(string Slot,int Layer,int Value);
// All supplied references must originate from this actor's SAME public capture. No world reads.
public sealed record BodyCapture(Guid Session,Guid Actor,Guid Capture,bool? Locomotion,bool? Rider,bool? Clothing,bool? Mounted,
 string[] LiveBlocks,string[] LiveEntities,string[] LiveLocations,string[] RememberedLocations,OwnSlot[]? Inventory,WornSlot[]? Equipment,IReadOnlyDictionary<string,bool?>? SupportByTool=null);
public sealed record BodyCapability(CapabilityDescriptor Descriptor,CapabilityState State,JsonElement Schema,ToolOffer? Offer);
public static class BodyFamily {
 static JsonElement J(object o)=>JsonSerializer.SerializeToElement(o);
 static object Num(double lo,double hi)=>new{type="number",minimum=lo,maximum=hi};
 static object Int(int lo,int hi)=>new{type="integer",minimum=lo,maximum=hi};
 static object EnumStr(IEnumerable<string> s)=>new{type="string",@enum=s.Distinct().ToArray()};
 static object Obj(Dictionary<string,object> p)=>new{type="object",properties=p,required=p.Keys.ToArray(),additionalProperties=false};
 static object Union(IEnumerable<object> branches)=>new{oneOf=branches.ToArray()};

 static ToolOffer ExecutionOffer(string name,string reason,JsonElement schema){
  var variants=schema.TryGetProperty("oneOf",out var alternatives)?alternatives.EnumerateArray().ToArray():new[]{schema};
  if(variants.Length is <1 or >32)throw new NotSupportedException("schema_branch_budget_requires_paging");
  var props=new JsonObject();
  // Effective template unions only actually supplied field alternatives. Closed variants
  // retain every cross-field association; the union template is never executable alone.
  foreach(var group in variants.SelectMany(v=>v.GetProperty("properties").EnumerateObject()).GroupBy(p=>p.Name)){
   var fields=group.Select(p=>p.Value).ToArray();
   if(fields.All(f=>JsonElement.DeepEquals(f,fields[0]))){props[group.Key]=JsonNode.Parse(fields[0].GetRawText());continue;}
   var values=new List<JsonElement>();string type=fields[0].GetProperty("type").GetString()!;
   foreach(var f in fields){
    if(f.GetProperty("type").GetString()!=type)throw new NotSupportedException("incompatible_field_types");
    if(f.TryGetProperty("const",out var c))values.Add(c);
    else if(f.TryGetProperty("enum",out var e))values.AddRange(e.EnumerateArray());
    else throw new NotSupportedException("explicit_effective_native_template_required");
   }
   var unique=values.GroupBy(v=>v.GetRawText()).Select(g=>g.First()).ToArray();
   if(unique.Length>128)throw new NotSupportedException("enum_budget_requires_paging");
   props[group.Key]=JsonSerializer.SerializeToNode(new{type,@enum=unique});
  }
  var references=new Dictionary<string,IReadOnlyList<string>>();
  foreach(var p in props)if(p.Key is "target" or "destination")references[p.Key]=p.Value!["enum"]!.AsArray().Select(x=>x!.GetValue<string>()).ToArray();
  var obj=new JsonObject{["type"]="object",["properties"]=props,["required"]=new JsonArray(),["additionalProperties"]=false};
  var parameters=JsonSerializer.SerializeToElement(obj);
  _=TypedAssociationSchema.Build(parameters,variants);
  var offer=new ToolOffer(name,reason,parameters,references,AssociatedParameterSchemas:variants);
  if(offer.Parameters.GetRawText().Length>8192)throw new NotSupportedException("schema_budget_requires_paging");
  _=new ToolDecisionPrompt(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),J(new{}),new[]{offer});return offer;
 }
 public static IReadOnlyList<BodyCapability> Project(BodyCapture f){
  if(f.Session==Guid.Empty||f.Actor==Guid.Empty||f.Capture==Guid.Empty)throw new ArgumentException("Real scope required");
  foreach(var refs in new[]{f.LiveBlocks,f.LiveEntities,f.LiveLocations,f.RememberedLocations})if(refs is null||refs.Length>128||refs.Any(s=>string.IsNullOrWhiteSpace(s)||s.Length>160)||refs.Distinct().Count()!=refs.Length)throw new ArgumentException("Bounded distinct capture refs required");
  if(f.Inventory is not null&&(f.Inventory.Length>128||f.Inventory.Any(s=>s is null||s.Slot<0||s.Slot>4095||s.Value<0||s.Count<0||s.Count>0&&s.Value==0)||f.Inventory.Select(s=>s.Slot).Distinct().Count()!=f.Inventory.Length))throw new ArgumentException("Invalid inventory");
  if(f.Equipment is not null&&(f.Equipment.Length>128||f.Equipment.Any(s=>s is null||string.IsNullOrWhiteSpace(s.Slot)||s.Slot.Length>96||s.Layer<0||s.Value<=0)||f.Equipment.Select(s=>(s.Slot,s.Layer)).Distinct().Count()!=f.Equipment.Length))throw new ArgumentException("Invalid equipment");
  var result=new List<BodyCapability>();
  void Add(string name,bool? supported,FactState facts,string reason,object schema,params ParameterKind[] kinds){
   if(f.SupportByTool is not null)supported=f.SupportByTool.GetValueOrDefault(name);
   var state=supported is null?FactState.Unknown:supported==false?FactState.Unavailable:facts;
   var discovery=J(schema);ToolOffer? offer=null;
   if(state==FactState.Available){try{offer=ExecutionOffer(name,reason,discovery);}catch(NotSupportedException e){state=FactState.Unknown;reason=e.Message;}}
   result.Add(new(new(name,"body_transport_equipment",kinds,true),new(name,supported,state,supported is null?"component_support_unknown":supported==false?"component_not_supported":reason,state==FactState.Available),discovery,offer));
  }
  FactState Refs(string[] r)=>r.Length>0?FactState.Available:FactState.Unavailable;
  var destinations=f.LiveLocations.Concat(f.RememberedLocations).Distinct().ToArray();
  var move=new List<object>{Obj(new(){["bearing"]=Num(-180,180),["distance"]=Num(1,4),["range"]=Num(.1,1),["timeout"]=Num(.1,15)})};
  if(destinations.Length>0)move.Add(Obj(new(){["destination"]=EnumStr(destinations),["range"]=Num(.1,1),["timeout"]=Num(.1,15)}));
  Add("act_MoveTo",f.Locomotion,FactState.Available,"native_checks_body_and_path; no_dry_ground_filter; timeout15/range_profile",Union(move),ParameterKind.NavigationLocation,ParameterKind.Scalar);
  var face=new List<object>{Obj(new(){["bearing"]=Num(-180,180),["timeout"]=Num(.1,5)}),Obj(new(){["bearing"]=Num(-180,180),["pitch"]=Num(-82,82),["timeout"]=Num(.1,5)})};
  if(f.LiveLocations.Length>0)face.Add(Obj(new(){["destination"]=EnumStr(f.LiveLocations),["timeout"]=Num(.1,5)}));
  Add("act_FaceTowards",f.Locomotion,FactState.Available,"current_target_only_or_relative_turn",Union(face),ParameterKind.LiveTarget,ParameterKind.Scalar);
  Add("act_SetPosture",f.Locomotion,FactState.Available,"native_posture_preconditions",Obj(new(){["posture"]=EnumStr(new[]{"standing","crouching"})}),ParameterKind.Scalar);
  Add("act_Jump",f.Locomotion,FactState.Available,"input_pulse_not_landing",Obj(new()),ParameterKind.Scalar);
  Add("act_ClimbTo",f.Locomotion,Refs(f.LiveBlocks),"live_block_not_proof_of_ladder; native_validates",Obj(new(){["target"]=EnumStr(f.LiveBlocks),["height"]=Num(0,255),["timeout"]=Num(.1,15)}),ParameterKind.LiveTarget,ParameterKind.Scalar);
  Add("act_Mount",f.Rider,Refs(f.LiveEntities),"live_entity_not_proof_of_mount; native_validates",Obj(new(){["target"]=EnumStr(f.LiveEntities)}),ParameterKind.LiveTarget);
  var riding=f.Mounted is null?FactState.Unknown:f.Mounted==true?FactState.Available:FactState.Unavailable;
  Add("act_Dismount",f.Rider,riding,"actual_mounted_state",Obj(new()),ParameterKind.Scalar);
  Add("act_Steer",f.Rider,riding,"native_mount_support; duration10_profile",Obj(new(){["turn"]=Num(-1,1),["throttle"]=Num(-1,1),["duration"]=Num(.1,10)}),ParameterKind.Scalar);
  Add("read_equipment",f.Clothing,FactState.Available,"own_equipment_query",Obj(new()),ParameterKind.Scalar);
  var owned=f.Inventory?.Where(s=>s.Count>0).Select(s=>Obj(new(){["slot"]=new{type="integer",@const=s.Slot},["value"]=new{type="integer",@const=s.Value}})).ToArray();
  Add("act_Equip",f.Clothing,owned is null?FactState.Unknown:owned.Length==0?FactState.Unavailable:FactState.Available,"owned_pair_not_proof_wearable; native_validates",Union(owned??[]),ParameterKind.OwnedItem);
  // Native Unequip removes the actual top layer; do not offer inaccessible inner layers.
  var worn=f.Equipment?.GroupBy(s=>s.Slot).Select(g=>g.OrderByDescending(s=>s.Layer).First()).Select(s=>Obj(new(){["equipment_slot"]=EnumStr(new[]{s.Slot}),["value"]=new{type="integer",@const=s.Value}})).ToArray();
  Add("act_Unequip",f.Clothing,worn is null?FactState.Unknown:worn.Length==0?FactState.Unavailable:FactState.Available,"current_top_layer_pair; native_validates_capacity",Union(worn??[]),ParameterKind.OwnedItem);
  return result.AsReadOnly();
 }
}
