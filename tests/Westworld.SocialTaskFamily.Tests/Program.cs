using System.Text.Json;
using Capability39;
using Capability39.SocialTasks;
using Westworld.Decisions.Local;
int checks=0;
void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
void Throws(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}catch(InvalidOperationException){Check(true,name);return;}catch(FormatException){Check(true,name);return;}throw new Exception("Expected reject: "+name);}
JsonElement J(object value)=>JsonSerializer.SerializeToElement(value);
var scope=new SocialCaptureId(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
var supported=new Dictionary<string,bool?>{{"read_social",true},{"act_Speak",true},{"act_OfferItem",true},{"act_RespondToOffer",true},{"submit_plan",true},{"act_Wait",true}};
ToolOffer Wait()=>new("act_Wait","Native bounded wait",J(new{type="object",properties=new{duration=new{type="number",minimum=.1,maximum=10}},required=new[]{"duration"},additionalProperties=false}));
SocialTaskFacts Facts(double time=10,bool? idle=true,bool? visible=true,bool? inventory=true,bool? inbox=true,VisibleSocialEntity[]? people=null,OwnSocialItem[]? slots=null,ReceivedSocialOffer[]? messages=null,CurrentActionSchema[]? actions=null,IReadOnlyDictionary<string,bool?>? support=null)=>new(scope,time,idle,visible,inventory,inbox,
 people??[new(scope,"visible_person",false),new(scope,"self_ref",true)],slots??[new(scope,0,89,1),new(scope,1,162,2)],messages??[new(scope,scope.Actor,"offer_self",0,false,"heard_not_visible")],actions??[new(scope,Wait())],support??supported);
SocialTaskFamilyResult Build(SocialTaskFacts? facts=null)=>SocialTaskFamily.Build(scope,facts??Facts());
FamilyCapability Entry(string name,SocialTaskFamilyResult? result=null)=>(result??Build()).Entries.Single(x=>x.Descriptor.Name==name);
ToolDecisionPrompt Prompt(string name,SocialTaskFamilyResult? result=null)=>new(scope.Session,scope.Actor,scope.Capture,J(new{}),new[]{Entry(name,result).Offer!});
ToolDecisionPlan Parse(string name,object arguments,SocialTaskFamilyResult? result=null)=>Prompt(name,result).ParseResponse(JsonSerializer.Serialize(new{goal="pure test",steps=new[]{new{tool=name,arguments}}}));
Check(Build().Entries.Count==5,"complete family descriptors social and finite task");
Check(Parse("read_social",new{}).Steps.Count==1,"read own social query closed schema");
Throws(()=>Parse("read_social",new{actor="other"}),"read social cannot name another inbox");
Check(Parse("act_Speak",new{text="你好",mode="whisper",target="visible_person"}).Steps.Count==1,"native recipient retained for current visible target");
Check(Entry("act_Speak").Offer!.Description.Contains("NOT private")&&Entry("act_Speak").Offer!.Description.Contains("3m")&&Entry("act_Speak").Offer!.Description.Contains("16m"),"audible ranges and directed address not private");
Check(Parse("act_Speak",new{text="broadcast"}).Steps.Count==1,"broadcast permitted without inventing target");
Throws(()=>Parse("act_Speak",new{text="x",target="heard_not_visible"}),"heard source does not grant current vision ref");
Throws(()=>Parse("act_Speak",new{text="x",target="self_ref"}),"self target rejected");
Throws(()=>Parse("act_Speak",new{text=new string('x',513)}),"native speech text finite bound");
Throws(()=>Parse("act_Speak",new{text="x",mode="global"}),"no global speech mode");
Check(Parse("act_OfferItem",new{target="visible_person",slot=1,value=162,count=2}).Steps.Count==1,"owned item pair count accepted");
Throws(()=>Parse("act_OfferItem",new{target="visible_person",slot=0,value=162,count=1}),"cross slot value rejected");
Throws(()=>Parse("act_OfferItem",new{target="visible_person",slot=0,value=89,count=2}),"over stack count rejected");
Throws(()=>Parse("act_OfferItem",new{target="heard_not_visible",slot=0,value=89,count=1}),"offer requires current visible entity");
Check(Entry("act_OfferItem").Schema!.Value.TryGetProperty("oneOf",out _),"registry gets effective schema not unpaired base");
Check(Parse("act_RespondToOffer",new{offer="offer_self",accept=true}).Steps.Count==1,"respond actual own offer reference");
Check(Parse("act_RespondToOffer",new{offer="offer_self",accept=false}).Steps.Count==1,"explicit decline supported");
Throws(()=>Parse("act_RespondToOffer",new{offer="invented",accept=true}),"unissued offer rejected");
Throws(()=>Parse("act_RespondToOffer",new{offer="offer_self",accept="true"}),"accept boolean typed");
Check(Entry("act_RespondToOffer",Build(Facts(time:15))).Offer is null,"exact native fifteen-second expiry no current authority");
Check(Entry("act_RespondToOffer",Build(Facts(messages:[new(scope,scope.Actor,"retired",9,true)]))).Offer is null,"known consumed or retired offer not revived");
Check(Entry("act_RespondToOffer",Build(Facts(inbox:null))).State.Facts==FactState.Unknown,"unknown inbox not empty");
Check(Entry("act_OfferItem",Build(Facts(visible:null))).State.Facts==FactState.Unknown,"unknown visibility not broad target permission");
Check(Entry("act_Speak",Build(Facts(visible:null))).Offer is not null,"unknown people does not hide legal native broadcast");
Throws(()=>Facts(messages:[new(scope,Guid.NewGuid(),"other_inbox",9)]),"cross-recipient inbox rejected");
Throws(()=>Facts(people:[new(scope with{Actor=Guid.NewGuid()},"other",false)]),"cross actor visible refs rejected");
Throws(()=>SocialTaskFamily.Build(scope with{Capture=Guid.NewGuid()},Facts()),"stale capture rejected");
Throws(()=>SocialTaskFamily.Build(scope with{World=Guid.NewGuid()},Facts()),"cross world rejected");
Throws(()=>Facts(messages:[new(scope,scope.Actor,"future",11)]),"future native message time rejected");
Throws(()=>Facts(slots:[new(scope,0,89,1),new(scope,0,162,2)]),"conflicting duplicate inventory rejected");
var unknownSupport=new Dictionary<string,bool?>(supported){["act_Speak"]=null};
Check(Entry("act_Speak",Build(Facts(support:unknownSupport))).State.ActorSupported is null,"unknown component support distinct from false");
var busy=Build(Facts(idle:false));Check(busy.Entries.Where(x=>x.Descriptor.Name!="read_social").All(x=>!x.State.InExecutionMenu)&&Entry("read_social",busy).Offer is not null,"busy retains read only and no second owner");
var data=new[]{new OwnSocialItem(scope,0,89,1)};var frozen=Facts(slots:data);data[0]=new(scope,0,162,99);
Check(Parse("act_OfferItem",new{target="visible_person",slot=0,value=89,count=1},Build(frozen)).Steps.Count==1,"input list mutation cannot alter capture");
Check(Entry("act_OfferItem",Build(Facts(slots:Enumerable.Range(0,40).Select(i=>new OwnSocialItem(scope,i,89,1)).ToArray()))).State.Reason=="partial_owned_stack_menu","bounded partial item menu explicit");
object Step(string name="act_Wait",double duration=.1,int retries=0)=>new{tool=name,arguments=new{duration},retries};
Check(Parse("submit_plan",new{steps=new[]{Step()}}).Steps.Count==1,"one outer submission to native task");
Check(Parse("submit_plan",new{steps=Enumerable.Range(0,12).Select(_=>Step(retries:2)).ToArray()}).Steps.Count==1,"twelve steps explicit two retries grammar");
Check(SocialTaskFamily.MaxNativeAttempts==36,"native attempt maximum thirty-six not infinite");
Throws(()=>Parse("submit_plan",new{steps=Array.Empty<object>()}),"empty plan rejected");
Throws(()=>Parse("submit_plan",new{steps=Enumerable.Range(0,13).Select(_=>Step()).ToArray()}),"thirteen steps rejected");
Throws(()=>Parse("submit_plan",new{steps=new[]{Step(retries:3)}}),"third retry rejected by public plan contract");
Throws(()=>Parse("submit_plan",new{steps=new[]{new{tool="act_Wait",arguments=new{duration=.1},next=0}}}),"backward graph or unadapted next forbidden");
Throws(()=>Parse("submit_plan",new{steps=new[]{new{tool="act_Wait",arguments=new{duration=.1},fallback=new{step=0,failure="busy"}}}}),"no arbitrary fallback graph");
Throws(()=>Parse("submit_plan",new{steps=new[]{new{tool="submit_plan",arguments=new{steps=new[]{Step()}}}}}),"recursive native task forbidden");
Throws(()=>Parse("submit_plan",new{steps=new[]{new{tool="host_navigate_local",arguments=new{bearing=90,distance=1}}}}),"host navigation not smuggled into native queue");
Throws(()=>Parse("submit_plan",new{steps=new[]{new{tool="read_social",arguments=new{}}}}),"queries not task actions");
Throws(()=>Parse("submit_plan",new{steps=new[]{Step(duration:99)}}),"nested action keeps real current bounds");
var social=Build();var socialActions=social.Entries.Where(x=>x.Offer is not null&&x.Descriptor.Name.StartsWith("act_")).Select(x=>new CurrentActionSchema(scope,x.Offer!)).ToArray();
var nested=Build(Facts(actions:socialActions));
Check(Parse("submit_plan",new{steps=new[]{new{tool="act_OfferItem",arguments=new{target="visible_person",slot=0,value=89,count=1}}}},nested).Steps.Count==1,"nested owned-item association preserved");
Throws(()=>Parse("submit_plan",new{steps=new[]{new{tool="act_OfferItem",arguments=new{target="visible_person",slot=0,value=162,count=1}}}},nested),"nested slot correlation cannot widen");
Check(Entry("submit_plan",Build(Facts(actions:[],support:new Dictionary<string,bool?>{{"submit_plan",true}}))).Offer is null,"no supported current actions yields explicit unavailable not any object");
var registry=new CapabilityRegistry(Build().Entries.Select(x=>x.Descriptor));var capture=registry.Capture(scope.Session,scope.Actor,scope.Capture,Build().Entries.Select(x=>x.State));
Check(capture.Discover(scope.Session,scope.Actor,scope.Capture).Total==5,"shared registry descriptors and tri-state integrate");
// Stage40: social actions are included by the family itself, not a second host pass.
Check(Parse("submit_plan",new{steps=new[]{new{tool="act_Speak",arguments=new{text="安排采集"}}}}).Steps.Count==1,"current speech automatically joins finite native plan");
Check(Parse("submit_plan",new{steps=new[]{new{tool="act_RespondToOffer",arguments=new{offer="offer_self",accept=true}}}}).Steps.Count==1,"own received offer automatically joins native plan");
Throws(()=>Parse("submit_plan",new{steps=new[]{new{tool="act_OfferItem",arguments=new{target="visible_person",slot=0,value=162,count=1}}}}),"automatic social nested plan retains slot correlation");
var priorOffer=Entry("act_OfferItem").Offer!;
var unavailable=Build(Facts(visible:false,actions:[new(scope,priorOffer)]));
Throws(()=>Parse("submit_plan",new{steps=new[]{new{tool="act_OfferItem",arguments=new{target="visible_person",slot=0,value=89,count=1}}}},unavailable),"unavailable current social action masks stale prior candidate");
var expired=Build(Facts(time:15,actions:[new(scope,Entry("act_RespondToOffer").Offer!)]));
Throws(()=>Parse("submit_plan",new{steps=new[]{new{tool="act_RespondToOffer",arguments=new{offer="offer_self",accept=true}}}},expired),"expired social offer cannot return through old nested candidate");
var largeFacts=Facts(people:[new(scope,new string('p',120),false)],slots:Enumerable.Range(0,32).Select(i=>new OwnSocialItem(scope,i,89+i,1)).ToArray());
var large=Build(largeFacts);
Check(large.PlanPages>1,"large correlated social schema exposes multiple explicit pages");
Check(large.PlanRejected.Count==0,"all social fragments fit bounded pages without silent loss");
int accepted=0;
for(int page=0;page<large.PlanPages;page++){
 var result=SocialTaskFamily.Build(scope,largeFacts,page);
 Check(Entry("submit_plan",result).Offer!.Parameters.GetRawText().Length<=8192,"page stays within protocol 8KB "+page);
 for(int slot=0;slot<32;slot++){
  try{Parse("submit_plan",new{steps=new[]{new{tool="act_OfferItem",arguments=new{target=new string('p',120),slot,value=89+slot,count=1}}}},result);accepted++;}catch(FormatException){}
 }
}
Check(accepted==32,"every correlated item variant is reachable on exactly one page");
Throws(()=>SocialTaskFamily.Build(scope,largeFacts,-1),"negative page rejected");
Throws(()=>SocialTaskFamily.Build(scope,largeFacts,large.PlanPages),"out of range page rejected");
var duplicate=new[]{new CurrentActionSchema(scope,Wait()),new CurrentActionSchema(scope,Wait())};
Throws(()=>FiniteNativePlan.BuildPages(scope,duplicate),"duplicate action names rejected before paging");
var queryOnly=FiniteNativePlan.BuildPages(scope,[new(scope,Entry("read_social").Offer!)]);
Check(queryOnly.Pages.Count==0&&queryOnly.Candidates==0,"queries do not enter native action pages");
var wrongScope=new[]{new CurrentActionSchema(scope with{Actor=Guid.NewGuid()},Wait())};
Throws(()=>FiniteNativePlan.BuildPages(scope,wrongScope),"paging cannot rebind cross actor schema");
// Ambiguous oneOf must never become valid merely by splitting pages.
var overlapSchema=J(new{type="object",properties=new{duration=new{type="number",minimum=.1,maximum=10}},required=new[]{"duration"},additionalProperties=false});
var overlapping=Wait() with{AssociatedParameterSchemas=new[]{overlapSchema,overlapSchema}};
var overlapPages=FiniteNativePlan.BuildPages(scope,[new(scope,overlapping)]);
if(overlapPages.Pages.Count>0){var op=new ToolDecisionPrompt(scope.Session,scope.Actor,scope.Capture,J(new{}),new[]{overlapPages.Pages[0].Offer!});Throws(()=>op.ParseResponse(JsonSerializer.Serialize(new{goal="reject ambiguous",steps=new[]{new{tool="submit_plan",arguments=new{steps=new[]{Step()}}}}})),"paging preserves overlapping oneOf rejection");}
else Check(overlapPages.Rejected.Count>0,"unpackable ambiguous schema explicitly rejected");
Check(large.PlanPage==0&&SocialTaskFamily.Build(scope,largeFacts,1).PlanPage==1,"explicit plan page selected without execution");
Console.WriteLine($"RESULT PASS {checks}");
