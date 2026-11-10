using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Westworld.Decisions.Local;

namespace Capability39.SocialTasks;

public readonly record struct SocialCaptureId(Guid World,Guid Session,Guid Actor,Guid Capture) {
 public void Validate(){if(World==Guid.Empty||Session==Guid.Empty||Actor==Guid.Empty||Capture==Guid.Empty)throw new ArgumentException("Complete actor/world/capture binding required");}
}
public sealed record VisibleSocialEntity(SocialCaptureId Scope,string Reference,bool IsSelf);
public sealed record OwnSocialItem(SocialCaptureId Scope,int Slot,int Value,int Count);
// ReceivedAt is native event game time, not file receive time. Reference came only from
// this actor's read_social. Source is informational and NEVER itself grants a target ref.
public sealed record ReceivedSocialOffer(SocialCaptureId Scope,Guid RecipientActor,string Reference,double ReceivedAt,bool KnownRetired=false,string? SourceReference=null);
public sealed record CurrentActionSchema(SocialCaptureId Scope,ToolOffer Offer);
public sealed record FamilyCapability(CapabilityDescriptor Descriptor,CapabilityState State,JsonElement? Schema,ToolOffer? Offer);
public sealed record FamilyCoverage(int VisibleTargets,int OwnedStacks,int OfferedStacks,int OmittedStacks,int ReceivedOffers,int PlanCandidates,int PlanIncluded,int PlanOmitted);
public sealed record SocialTaskFamilyResult(IReadOnlyList<FamilyCapability> Entries,FamilyCoverage Coverage) { public int PlanPage {get;init;} public int PlanPages {get;init;} public IReadOnlyList<string> PlanRejected {get;init;}=Array.Empty<string>(); }

/// <summary>One immutable trusted public capture. Null-known flags mean unknown, not empty.</summary>
public sealed class SocialTaskFacts {
 public SocialCaptureId Scope{get;} public double GameTime{get;} public bool? OwnerIdle{get;}
 public bool? VisibleKnown{get;} public bool? InventoryKnown{get;} public bool? InboxKnown{get;}
 public IReadOnlyList<VisibleSocialEntity> Visible{get;} public IReadOnlyList<OwnSocialItem> Inventory{get;}
 public IReadOnlyList<ReceivedSocialOffer> Inbox{get;} public IReadOnlyList<CurrentActionSchema> Actions{get;}
 public IReadOnlyDictionary<string,bool?> ActorSupport{get;}
 public SocialTaskFacts(SocialCaptureId scope,double gameTime,bool? ownerIdle,bool? visibleKnown,bool? inventoryKnown,bool? inboxKnown,
  IEnumerable<VisibleSocialEntity> visible,IEnumerable<OwnSocialItem> inventory,IEnumerable<ReceivedSocialOffer> inbox,
  IEnumerable<CurrentActionSchema> actions,IReadOnlyDictionary<string,bool?> actorSupport){
  scope.Validate();if(!double.IsFinite(gameTime)||gameTime<0)throw new ArgumentException("Finite native game time required");
  Scope=scope;GameTime=gameTime;OwnerIdle=ownerIdle;VisibleKnown=visibleKnown;InventoryKnown=inventoryKnown;InboxKnown=inboxKnown;
  var v=visible.ToArray();var i=inventory.ToArray();var m=inbox.ToArray();var a=actions.ToArray();
  if(v.Length>128||i.Length>4096||m.Length>64||a.Length>32)throw new ArgumentException("Bounded public facts required");
  if(v.Any(x=>x is null||x.Scope!=scope||!Ref(x.Reference))||v.Select(x=>x.Reference).Distinct().Count()!=v.Length)throw new ArgumentException("Current visible refs must be unique and same scope");
  if(i.Any(x=>x is null||x.Scope!=scope||x.Slot<0||x.Slot>4095||x.Value<0||x.Count<0||x.Value==0&&x.Count>0)||i.Select(x=>x.Slot).Distinct().Count()!=i.Length)throw new ArgumentException("Current owned slots must be unique and same scope");
  if(m.Any(x=>x is null||x.Scope!=scope||x.RecipientActor!=scope.Actor||!Ref(x.Reference)||!double.IsFinite(x.ReceivedAt)||x.ReceivedAt<0||x.ReceivedAt>gameTime)||m.Select(x=>x.Reference).Distinct().Count()!=m.Length)throw new ArgumentException("Only this actor's actual received offers, no future/duplicate/cross actor");
  if(a.Any(x=>x is null||x.Scope!=scope||x.Offer is null)||a.Select(x=>x.Offer.Name).Distinct().Count()!=a.Length)throw new ArgumentException("Nested actions must be scoped current offers");
  Visible=Array.AsReadOnly(v);Inventory=Array.AsReadOnly(i);Inbox=Array.AsReadOnly(m);
  Actions=Array.AsReadOnly(a.Select(x=>x with{Offer=Clone(x.Offer)}).ToArray());
  ActorSupport=new ReadOnlyDictionary<string,bool?>(new Dictionary<string,bool?>(actorSupport));
 }
 static bool Ref(string? value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=160;
 static ToolOffer Clone(ToolOffer o)=>o with{Parameters=o.Parameters.Clone(),ReferenceValues=o.ReferenceValues is null?null:new ReadOnlyDictionary<string,IReadOnlyList<string>>(o.ReferenceValues.ToDictionary(x=>x.Key,x=>(IReadOnlyList<string>)Array.AsReadOnly(x.Value.ToArray()))),AssociatedParameterSchemas=o.AssociatedParameterSchemas is null?null:Array.AsReadOnly(o.AssociatedParameterSchemas.Select(x=>x.Clone()).ToArray()),ParameterBranches=o.ParameterBranches is null?null:Array.AsReadOnly(o.ParameterBranches.Select(b=>new ToolParameterBranch(Array.AsReadOnly(b.Required.ToArray()),Array.AsReadOnly(b.Forbidden.ToArray()),b.IntegerConstraints is null?null:new ReadOnlyDictionary<string,JsonElement>(b.IntegerConstraints.ToDictionary(p=>p.Key,p=>p.Value.Clone())))).ToArray())};
}

/// <summary>Binding/schema construction only. Never owns native objects or runs a queue.</summary>
public static class SocialTaskFamily {
 public const double NativeOfferLifetimeSeconds=15;
 public const int MaxPlanSteps=12;
 public const int MaxExplicitRetries=2;
 public const int MaxNativeAttempts=MaxPlanSteps*(MaxExplicitRetries+1);
 static JsonElement J(object value)=>JsonSerializer.SerializeToElement(value);
 static JsonObject Obj(JsonObject properties,params string[] required)=>new(){["type"]="object",["properties"]=properties,["required"]=JsonSerializer.SerializeToNode(required),["additionalProperties"]=false};
 static JsonObject Text(int maximum)=>new(){["type"]="string",["minLength"]=1,["maxLength"]=maximum};
 static JsonObject Choice(IEnumerable<string> values)=>new(){["type"]="string",["enum"]=JsonSerializer.SerializeToNode(values.ToArray())};
 static JsonObject Integer(int minimum,int maximum)=>new(){["type"]="integer",["minimum"]=minimum,["maximum"]=maximum};
 static JsonObject Exact(int value)=>new(){["type"]="integer",["const"]=value};
 static CapabilityDescriptor Descriptor(string name,params ParameterKind[] kinds)=>new(name,name=="submit_plan"?"finite_task":"social",Array.AsReadOnly(kinds),true);
 static ToolOffer Tool(string name,string description,JsonObject schema,IReadOnlyDictionary<string,IReadOnlyList<string>>? refs=null,IReadOnlyList<JsonElement>? associated=null)=>new(name,description,J(schema),refs,AssociatedParameterSchemas:associated);
 public static SocialTaskFamilyResult Build(SocialCaptureId expected,SocialTaskFacts facts,int planPage=0){
  if(expected!=facts.Scope)throw new InvalidOperationException("stale_or_wrong_social_capture");
  var entries=new List<FamilyCapability>();
  var targets=facts.Visible.Where(x=>!x.IsSelf).Select(x=>x.Reference).ToArray();
  var items=facts.Inventory.Where(x=>x.Count>0&&x.Value>0).ToArray();
  var offers=facts.Inbox.Where(x=>!x.KnownRetired&&facts.GameTime-x.ReceivedAt<NativeOfferLifetimeSeconds).Select(x=>x.Reference).ToArray();
  void Add(CapabilityDescriptor descriptor,ToolOffer? tool,FactState state,string reason,bool write){
   bool? supported=facts.ActorSupport.GetValueOrDefault(descriptor.Name);
   if(supported!=true){state=supported is null?FactState.Unknown:FactState.Unavailable;reason=supported is null?"actor_support_unknown":"actor_not_supported";tool=null;}
   else if(write&&facts.OwnerIdle!=true){state=facts.OwnerIdle is null?FactState.Unknown:FactState.Unavailable;reason=facts.OwnerIdle is null?"single_owner_idle_unknown":"single_owner_busy";tool=null;}
   JsonElement? effective=null;
   if(tool is not null){
    var compiled=new ToolDecisionPrompt(expected.Session,expected.Actor,expected.Capture,J(new{}),new[]{tool});
    using var document=JsonDocument.Parse(compiled.ResponseSchema);
    effective=document.RootElement.GetProperty("properties").GetProperty("steps").GetProperty("items").GetProperty("oneOf")[0].GetProperty("properties").GetProperty("arguments").Clone();
   }
   entries.Add(new(descriptor,new(descriptor.Name,supported,state,reason,tool is not null&&state==FactState.Available),effective,tool));
  }
  Add(Descriptor("read_social"),Tool("read_social","Read only this actor's actual received inbox. May drain native inbox unless cognition owns a peek; retain returned events before another read.",Obj(new())),FactState.Available,"actor_private_inbox_query",false);
  var speakProps=new JsonObject{["text"]=Text(512),["mode"]=Choice(new[]{"whisper","normal","shout"})};
  IReadOnlyDictionary<string,IReadOnlyList<string>>? targetBinding=null;
  if(facts.VisibleKnown==true&&targets.Length>0){speakProps["target"]=Choice(targets);targetBinding=new Dictionary<string,IReadOnlyList<string>>{{"target",Array.AsReadOnly(targets)}};}
  Add(Descriptor("act_Speak",ParameterKind.Scalar,ParameterKind.LiveTarget),Tool("act_Speak","Speak audibly: whisper 3m, normal 8m, shout 16m, native LOS/listeners apply. target is a directed address, NOT private: nearby hearers still hear. Success is not recipient acknowledgment.",Obj(speakProps,"text"),targetBinding),FactState.Available,"broadcast_available_target_requires_current_vision",true);
  ToolOffer? offerItem=null;FactState offerState=FactState.Unknown;string offerReason="visible_people_or_inventory_unknown";
  if(facts.VisibleKnown==true&&facts.InventoryKnown==true){
   offerState=targets.Length>0&&items.Length>0?FactState.Available:FactState.Unavailable;offerReason="no_visible_other_entity_or_owned_item";
   if(offerState==FactState.Available){
    // Same paired slot/full value/count grammar used by Eat; target uses visible refs only.
    var chosen=items.OrderBy(x=>x.Slot).Take(32).ToArray();
    var props=new JsonObject{["target"]=Choice(targets),["slot"]=Integer(0,4095),["value"]=Integer(1,int.MaxValue),["count"]=Integer(1,64)};
    var variants=chosen.Select(x=>J(Obj(new(){["target"]=Choice(targets),["slot"]=Exact(x.Slot),["value"]=Exact(x.Value),["count"]=Integer(1,Math.Min(x.Count,64))},"target","slot","value","count"))).ToArray();
    offerItem=Tool("act_OfferItem","Offer owned items to a currently visible entity; native participant, 2m LOS, inventory and 15s expiry checks still apply. Offer success is NOT transfer. At most 32 stacks exposed; coverage reports omissions.",Obj(props,"target","slot","value","count"),new Dictionary<string,IReadOnlyList<string>>{{"target",Array.AsReadOnly(targets)}},variants);
    offerReason=chosen.Length<items.Length?"partial_owned_stack_menu":"current_targets_and_paired_owned_slots";
   }
  }
  Add(Descriptor("act_OfferItem",ParameterKind.LiveTarget,ParameterKind.OwnedItem),offerItem,offerState,offerReason,true);
  var respond=facts.InboxKnown==true&&offers.Length>0?Tool("act_RespondToOffer","Respond only to an offer actually received by this actor. Native recipient, pending state, 15s lifetime, 2m LOS and inventory still validate. Successful accept may transfer only the affected count, not the whole request.",Obj(new(){["offer"]=Choice(offers),["accept"]=new JsonObject{["type"]="boolean"}},"offer","accept"),new Dictionary<string,IReadOnlyList<string>>{{"offer",Array.AsReadOnly(offers)}}):null;
  Add(Descriptor("act_RespondToOffer",ParameterKind.SocialOffer),respond,facts.InboxKnown!=true?FactState.Unknown:respond is null?FactState.Unavailable:FactState.Available,facts.InboxKnown!=true?"own_received_inbox_unknown":respond is null?"no_unexpired_received_offer":"own_received_reference_not_pending_or_delivery_guarantee",true);
  // Current social results replace older same-name candidates, including unavailable/null.
  var merged=facts.Actions.Where(x=>facts.ActorSupport.GetValueOrDefault(x.Offer.Name)==true).ToDictionary(x=>x.Offer.Name,StringComparer.Ordinal);
  foreach(var entry in entries.Where(x=>x.Descriptor.Name.StartsWith("act_",StringComparison.Ordinal))){merged.Remove(entry.Descriptor.Name);if(entry.Offer is not null)merged[entry.Descriptor.Name]=new(facts.Scope,entry.Offer);}
  var pages=FiniteNativePlan.BuildPages(facts.Scope,merged.Values.ToArray());
  if(planPage<0||planPage>=Math.Max(1,pages.Pages.Count))throw new ArgumentOutOfRangeException(nameof(planPage));
  var plan=pages.Pages.Count==0?new NativePlanSchemaResult(null,pages.Candidates,0,pages.Candidates):pages.Pages[planPage];
  Add(Descriptor("submit_plan",ParameterKind.NestedPlan),plan.Offer,plan.Offer is null?FactState.Unavailable:FactState.Available,plan.Offer is null?"no_bounded_current_native_action_schema":"one_native_task_owner_linear_1_to_12_steps",true);
  return new(entries.AsReadOnly(),new(targets.Length,items.Length,offerItem?.AssociatedParameterSchemas?.Count??0,items.Length-(offerItem?.AssociatedParameterSchemas?.Count??0),offers.Length,plan.Candidates,plan.Included,plan.Omitted)){PlanPage=planPage,PlanPages=pages.Pages.Count,PlanRejected=pages.Rejected};
 }
}

public sealed record NativePlanSchemaResult(ToolOffer? Offer,int Candidates,int Included,int Omitted);
public sealed record NativePlanPages(IReadOnlyList<NativePlanSchemaResult> Pages,int Candidates,IReadOnlyList<string> Rejected);
public static class FiniteNativePlan {
 // Only schemas, submitted once to existing ActorTools.SubmitPlan. No queue here.
 public static NativePlanSchemaResult Build(SocialCaptureId scope,IReadOnlyList<CurrentActionSchema> current)=>BuildPages(scope,current).Pages.FirstOrDefault()??new(null,current.Count(x=>x.Offer.Name.StartsWith("act_",StringComparison.Ordinal)),0,current.Count(x=>x.Offer.Name.StartsWith("act_",StringComparison.Ordinal)));
 public static NativePlanPages BuildPages(SocialCaptureId scope,IReadOnlyList<CurrentActionSchema> current){
  scope.Validate();if(current.Count>32||current.Any(x=>x.Scope!=scope)||current.Select(x=>x.Offer.Name).Distinct().Count()!=current.Count)throw new ArgumentException("Same capture unique current action schemas required");
  var candidates=current.Where(x=>x.Offer.Name.StartsWith("act_",StringComparison.Ordinal)).OrderBy(x=>x.Offer.Name,StringComparer.Ordinal).ToArray();
  var pages=new List<NativePlanSchemaResult>();var rejected=new List<string>();var variants=new JsonArray();var names=new HashSet<string>(StringComparer.Ordinal);
  ToolOffer Offer(JsonArray options)=>new("submit_plan","One native task: 1..12 linear actions, explicit safe retries 0..2 (at most 36 attempts). This is one bounded schema page; other pages need explicit selection and fresh facts. No next/fallback/item_from_step. Native task_status/cancel_task retain ownership.",JsonSerializer.SerializeToElement(new JsonObject{["type"]="object",["properties"]=new JsonObject{["steps"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=12,["items"]=new JsonObject{["oneOf"]=options.DeepClone()}}},["required"]=new JsonArray("steps"),["additionalProperties"]=false}));
  bool Fits(JsonArray options){try{_=new ToolDecisionPrompt(scope.Session,scope.Actor,scope.Capture,JsonSerializer.SerializeToElement(new{}),new[]{Offer(options)});return true;}catch(ArgumentException){return false;}}
  void Flush(){if(variants.Count==0)return;pages.Add(new(Offer(variants),candidates.Length,names.Count,candidates.Length-names.Count));variants=new();names.Clear();}
  foreach(var candidate in candidates){
   JsonElement effective;
   try{var prompt=new ToolDecisionPrompt(scope.Session,scope.Actor,scope.Capture,JsonSerializer.SerializeToElement(new{}),new[]{candidate.Offer});using var doc=JsonDocument.Parse(prompt.ResponseSchema);effective=doc.RootElement.GetProperty("properties").GetProperty("steps").GetProperty("items").GetProperty("oneOf")[0].GetProperty("properties").GetProperty("arguments").Clone();}
   catch(ArgumentException){rejected.Add(candidate.Offer.Name+":invalid_current_schema");continue;}
   // Flatten only provably disjoint top-level alternatives. Overlapping oneOf must not
   // become permissive when spread across pages. Correlated slot/value branches survive.
   var parts=effective.TryGetProperty("oneOf",out var alternatives)&&Disjoint(alternatives)?alternatives.EnumerateArray().Select(x=>x.Clone()).ToArray():new[]{effective};
   int index=0;
   foreach(var part in parts){
    var variant=new JsonObject{["type"]="object",["properties"]=new JsonObject{["tool"]=new JsonObject{["type"]="string",["const"]=candidate.Offer.Name},["arguments"]=JsonNode.Parse(part.GetRawText()),["retries"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=2},["minimum_affected"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=64}},["required"]=new JsonArray("tool","arguments"),["additionalProperties"]=false};
    var alone=new JsonArray(variant.DeepClone());if(!Fits(alone)){rejected.Add(candidate.Offer.Name+":fragment_"+index+"_exceeds_protocol");index++;continue;}
    var proposed=(JsonArray)variants.DeepClone();proposed.Add(variant.DeepClone());if(!Fits(proposed)){Flush();proposed=alone;}
    variants=proposed;names.Add(candidate.Offer.Name);index++;
   }
  }
  Flush();return new(pages.AsReadOnly(),candidates.Length,rejected.AsReadOnly());
 }
 static bool Disjoint(JsonElement alternatives){
  var branches=alternatives.EnumerateArray().ToArray();
  for(int i=0;i<branches.Length;i++)for(int j=i+1;j<branches.Length;j++)if(!Separate(branches[i],branches[j]))return false;
  return true;
 }
 static bool Separate(JsonElement a,JsonElement b){
  if(!a.TryGetProperty("properties",out var ap)||!b.TryGetProperty("properties",out var bp)||!a.TryGetProperty("required",out var ar)||!b.TryGetProperty("required",out var br))return false;
  foreach(var name in ar.EnumerateArray().Select(x=>x.GetString()!).Intersect(br.EnumerateArray().Select(x=>x.GetString()!))){
   if(!ap.TryGetProperty(name,out var av)||!bp.TryGetProperty(name,out var bv))continue;
   JsonElement[]? Values(JsonElement x)=>x.TryGetProperty("const",out var c)?new[]{c}:x.TryGetProperty("enum",out var e)?e.EnumerateArray().ToArray():null;
   var aa=Values(av);var bb=Values(bv);if(aa is not null&&bb is not null&&!aa.Any(x=>bb.Any(y=>JsonElement.DeepEquals(x,y))))return true;
  }
  return false;
 }
}
