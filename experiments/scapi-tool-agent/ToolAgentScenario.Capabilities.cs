using Westworld.Runtime.Core;
using System.Text.Json;
using System.Text.Json.Nodes;
using Capability39;
using Capability39.SocialTasks;
using Westworld.Decisions.Local;
using Game;

internal sealed partial class ToolAgentScenario {
 // Explicit stage39 runner switch preserves fixed stage38 regression drivers.
 static bool CapabilityMode=>Environment.GetEnvironmentVariable("WESTWORLD_CAPABILITY_MENU")=="1";
 readonly Guid capabilityWorldScope=Guid.NewGuid();
 CapabilityMenu capabilityMenu;
 CapabilityMenuSelection capabilitySelection=new(Array.Empty<string>(),Array.Empty<string>());
 readonly Dictionary<string,JsonElement> capabilityReadArguments=new(StringComparer.Ordinal);
 readonly List<JsonElement> retainedSocialEvents=[];
 bool socialInboxRead;
 void RememberCapabilityRead(string name,object args,ToolReceipt r){
  if(!CapabilityMode||r.State!="returned")return;
  var a=JsonSerializer.SerializeToElement(args);
  if(name is "read_block_settings" or "read_item_settings" or "read_storage" or "read_map"){
   string key=CapabilityDispatchPolicy.ReadKey(name,a);capabilityReadArguments[key]=a.Clone();
   while(capabilityReadArguments.Count>6)capabilityReadArguments.Remove(capabilityReadArguments.Keys.First());
  }
  if(name=="read_social"&&r.Data.ValueKind==JsonValueKind.Array){socialInboxRead=true;foreach(var e in r.Data.EnumerateArray())retainedSocialEvents.Add(e.Clone());while(retainedSocialEvents.Count>64)retainedSocialEvents.RemoveAt(0);}
  if(name=="act_RespondToOffer"&&Status(r.Data)=="succeeded"&&a.TryGetProperty("offer",out var offer))retainedSocialEvents.RemoveAll(e=>e.TryGetProperty("offerReference",out var reference)&&reference.GetString()==offer.GetString());
 }
 IReadOnlyList<ToolOffer> ExpandedCapabilityMenu(JsonElement o,JsonElement i,IReadOnlyList<ToolOffer> legacy,bool afterPrecedingSuccess=false){
  var scope=new SocialCaptureId(capabilityWorldScope,gateway.SessionId,gateway.ActorId,currentObservationId);
  var native=gateway.GetTools();var names=native.Select(t=>t.Name).ToArray();
  var empty=JsonSerializer.SerializeToElement(new{});
  var reads=new List<CapabilityRead>{new(scope,"observe",empty,o),new(scope,"read_inventory",empty,i)};
  void Read(string tool,JsonElement args){if(!names.Contains(tool))return;var r=Call(tool,args,false);reads.Add(new(scope,tool,args,r.Data,r.State=="returned"));}
  Read("read_equipment",empty);
  // Re-query previously selected settings/storage/map; never relabel old world facts.
  var liveBlocks=o.TryGetProperty("visibleBlocks",out var blocks)?blocks.EnumerateArray().Select(b=>b.GetProperty("reference").GetString()).ToHashSet():new HashSet<string>();
  if(o.TryGetProperty("focus",out var focus)&&focus.ValueKind==JsonValueKind.Object&&focus.TryGetProperty("kind",out var kind)&&kind.ToString() is "block" or "Block" or "0"&&focus.TryGetProperty("reference",out var fr))liveBlocks.Add(fr.GetString());
  foreach(var pair in capabilityReadArguments.ToArray()){
   string tool=pair.Key[..pair.Key.IndexOf(':')];var args=pair.Value;
   if(tool=="read_block_settings"&&(!args.TryGetProperty("target",out var target)||!liveBlocks.Contains(target.GetString())))continue;
   if(tool=="read_item_settings"&&!i.GetProperty("slots").EnumerateArray().Any(slot=>slot.GetProperty("slot").GetInt32()==args.GetProperty("slot").GetInt32()&&slot.GetProperty("value").GetInt32()==args.GetProperty("value").GetInt32()&&slot.GetProperty("count").GetInt32()>0))continue;
   Read(tool,args);
  }
  if(o.TryGetProperty("craftingReference",out var own)&&!reads.Any(r=>r.Tool=="read_storage"&&r.Arguments.TryGetProperty("storage",out var s)&&s.GetString()==own.GetString()))Read("read_storage",JsonSerializer.SerializeToElement(new{storage=own.GetString()}));
  if(socialInboxRead){
   // This is an actor-owned retained mailbox, not a second native read/drain.
   double now=o.GetProperty("gameTime").GetDouble();
   retainedSocialEvents.RemoveAll(e=>e.TryGetProperty("gameTime",out var at)&&now-at.GetDouble()>=SocialTaskFamily.NativeOfferLifetimeSeconds);
   var events=retainedSocialEvents.GroupBy(e=>e.TryGetProperty("offerReference",out var f)&&f.ValueKind==JsonValueKind.String?f.GetString():e.GetRawText()).Select(g=>g.Last()).ToArray();
   reads.Add(new(scope,"read_social",empty,JsonSerializer.SerializeToElement(events)));
  }
  bool directIdle=CapabilityDispatchPolicy.KnownIdle(actionState,true)&&!actions.IsActionRunning,taskIdle=CapabilityDispatchPolicy.KnownIdle(taskState),navIdle=CapabilityDispatchPolicy.KnownIdle(navigationState);bool idle=directIdle&&taskIdle&&navIdle;
  var facts=CapabilityFacts.Build(scope,reads,names,idle||afterPrecedingSuccess);
  var typed=new List<CapabilityMenuCandidate>();
  void Unadapted(IEnumerable<string> failed,Exception error){foreach(var name in failed)typed.Add(new(name,names.Contains(name),FactState.Unknown,"schema_adapter_error_requires_review",null));ToolAgentGate.Log(new{kind="capability_adapter_unavailable",tools=failed.ToArray(),error=error.Message});}
  try{foreach(var b in BodyFamily.Project(facts.Body))typed.Add(new(b.Descriptor.Name,b.State.ActorSupported,b.State.Facts,b.State.Reason,b.Offer,b.Schema));}catch(Exception e)when(e is ArgumentException or InvalidOperationException or NotSupportedException){Unadapted(CapabilityCatalog.All.Where(d=>d.Family=="body_transport_equipment").Select(d=>d.Name),e);}
  foreach(var name in ProductionFamily.Names){var definition=native.FirstOrDefault(t=>t.Name==name);if(definition==null){typed.Add(new(name,false,FactState.Unavailable,"native_actor_unsupported",null));continue;}
   try{var b=ProductionFamily.Build(name,definition.Parameters,true,facts.Production.Scope,facts.Production);typed.Add(new(name,b.State.ActorSupported,b.State.Facts,b.State.Reason,b.Offer));}catch(Exception e)when(e is ArgumentException or InvalidOperationException or NotSupportedException){Unadapted(new[]{name},e);}}
  var candidateActions=CapabilityExecution.MergeActions(legacy,typed.Select(t=>new KeyValuePair<string,ToolOffer>(t.Name,t.ActorSupported==true&&t.Facts==FactState.Available?t.Offer:null))).ToArray();
  // 31 native actions maximum. Plan compilation uses these same-capture bounded schemas.
  facts=CapabilityFacts.Build(scope,reads,names,idle||afterPrecedingSuccess,candidateActions);
  if(facts.Social is not null)try{
   var social=SocialTaskFamily.Build(scope,facts.Social);
   foreach(var b in social.Entries.Where(b=>b.Descriptor.Name!="submit_plan"))typed.Add(new(b.Descriptor.Name,b.State.ActorSupported,b.State.Facts,b.State.Reason,b.Offer,b.Schema));
   // Include actual social actions in the task schema, not just the earlier physical families.
   candidateActions=CapabilityExecution.MergeActions(legacy,typed.Select(t=>new KeyValuePair<string,ToolOffer>(t.Name,t.ActorSupported==true&&t.Facts==FactState.Available?t.Offer:null))).ToArray();
   facts=CapabilityFacts.Build(scope,reads,names,idle||afterPrecedingSuccess,candidateActions);
   var task=SocialTaskFamily.Build(scope,facts.Social).Entries.Single(b=>b.Descriptor.Name=="submit_plan");typed.Add(new(task.Descriptor.Name,task.State.ActorSupported,task.State.Facts,task.State.Reason,task.Offer,task.Schema));
  }catch(Exception e)when(e is ArgumentException or InvalidOperationException or NotSupportedException){typed.RemoveAll(t=>t.Name is "read_social" or "act_Speak" or "act_OfferItem" or "act_RespondToOffer" or "submit_plan");Unadapted(new[]{"read_social","act_Speak","act_OfferItem","act_RespondToOffer","submit_plan"},e);}
  if(!idle&&!afterPrecedingSuccess)typed=typed.Select(c=>CapabilityExecution.TracksOutcome(c.Name)?c with{Facts=FactState.Unavailable,Reason="native_owner_busy",Offer=null}:c).ToList();
  var old=CapabilityCatalog.All.Select(d=>{var offer=legacy.FirstOrDefault(x=>x.Name==d.Name);return new CapabilityMenuCandidate(d.Name,names.Contains(d.Name),offer is null?FactState.Unknown:FactState.Available,offer is null?"current_parameters_not_bound":"legacy_same_capture_binding",offer);});
  CapabilityMenuCandidate OwnerFilter(CapabilityMenuCandidate c)=>CapabilityDispatchPolicy.MayOffer(c.Name,!directIdle,!taskIdle,!navIdle,afterPrecedingSuccess)?c:c with{Facts=FactState.Unavailable,Reason="native_owner_busy",Offer=null};
  capabilityMenu=new(scope.Session,scope.Actor,scope.Capture,CapabilityCatalog.All,old.Select(OwnerFilter),typed.Select(OwnerFilter));
  var view=capabilityMenu.Select(scope.Session,scope.Actor,scope.Capture,capabilitySelection);
  var context=JsonNode.Parse(currentContext.GetRawText()).AsObject();
  context["capabilities"]=JsonSerializer.SerializeToNode(new{completeIndex=view.CompleteIndex,families=CapabilityCatalog.All.Select(x=>x.Family).Distinct(),requestedButUnavailable=view.RequestedButUnavailable,missingFacts=facts.Missing,verification="55 catalog entries; templates and current executability are separate. Stage39 real game verification is pending; discovery is not execution.",instruction="Use read_capabilities for state pages. Use read_capability_schema select_tool/select_family to choose the next execution menu; selection terminates this plan. No action is run by selection."});
  currentContext=JsonSerializer.SerializeToElement(context);
  return view.Offers;
 }
 bool HandleCapabilityDiscovery(ToolDecisionStep step){
  if(!CapabilityMode||capabilityMenu is null||!capabilityMenu.IsDiscovery(step.Tool))return false;
  var c=capabilityMenu.Build().Capture;
  var result=capabilityMenu.HandleDiscovery(c.Session,c.Actor,c.CaptureId,step.Tool,step.Arguments);
  if(step.Arguments.TryGetProperty("select_tool",out var tool))capabilitySelection=new(new[]{tool.GetString()},Array.Empty<string>());
  if(step.Arguments.TryGetProperty("select_family",out var family))capabilitySelection=new(Array.Empty<string>(),new[]{family.GetString()});
  recent.Add(JsonSerializer.SerializeToElement(new{tool=step.Tool,ok=true,data=result}));while(recent.Count>8)recent.RemoveAt(0);
  ToolAgentGate.Log(new{kind="capability_discovery",tool=step.Tool,result,executionGranted=false});
  steps.Clear();nextPlan=time;return true;
 }
}
