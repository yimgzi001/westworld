using Capability39;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
int n=0;void C(bool b,string why){if(!b)throw new Exception(why);n++;}void Reject(Action f){try{f();}catch(ArgumentException){n++;return;}catch(InvalidOperationException){n++;return;}throw new Exception("expected fail closed");}
var opts=new JsonSerializerOptions();opts.Converters.Add(new JsonStringEnumConverter());
var all=JsonSerializer.Deserialize<CapabilityDescriptor[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"catalog.json")),opts)!;
var registry=new CapabilityRegistry(all);C(registry.Count==55,"52 native + 3 host");
var session=Guid.NewGuid();var actor=Guid.NewGuid();var id=Guid.NewGuid();
var states=all.Select((d,i)=>new CapabilityState(d.Name,i%3==0?null:i%3==1,false?FactState.Available:FactState.Unknown,"facts not yet bound",false)).ToArray();
var cap=registry.Capture(session,actor,id,states);C(cap.FullNameIndex().Split(' ').Length==55,"complete index");
var visited=new HashSet<string>();int? offset=0;int pages=0;while(offset is int off){var page=cap.Discover(session,actor,id,off,8);C(page.Entries.Count<=8,"bounded");foreach(var e in page.Entries)C(visited.Add(e.Descriptor.Name),"no duplicates");offset=page.NextOffset;pages++;}
C(visited.Count==55&&pages==7,"all states discoverable");C(cap.Discover(session,actor,id,55).Entries.Count==0,"last page");
Reject(()=>cap.Discover(Guid.NewGuid(),actor,id));Reject(()=>cap.Discover(session,Guid.NewGuid(),id));Reject(()=>cap.Discover(session,actor,Guid.NewGuid()));Reject(()=>cap.Discover(session,actor,id,-1));Reject(()=>cap.Discover(session,actor,id,0,9));
Reject(()=>registry.Capture(session,actor,id,states.Skip(1)));Reject(()=>registry.Capture(session,actor,id,states.Append(states[0])));
Reject(()=>registry.Capture(session,actor,id,states.Select((s,i)=>i==0?s with{InExecutionMenu=true}:s)));
Reject(()=>cap.FullNameIndex(64));
var schemas=all.ToDictionary(x=>x.Name,x=>JsonSerializer.SerializeToElement(new{type="object",properties=new{},additionalProperties=false}));
var tiny=cap.Describe(session,actor,id,0,8,schemas,512);C(tiny.ByteBudgetLimited&&tiny.Entries.Count<8&&tiny.NextOffset==tiny.Entries.Count,"explicit budget partial");C(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(tiny))<=512,"envelope final budget");
var full=cap.Describe(session,actor,id,0,8,schemas);C(full.Entries.All(x=>!x.GrantedExecutionPermission),"discovery never grants");
var missing=cap.Describe(session,actor,id,0,1,new Dictionary<string,JsonElement>());C(missing.Entries[0].Availability=="schema_not_registered","missing explicit");
schemas[all.OrderBy(x=>x.Name,StringComparer.Ordinal).First().Name]=JsonSerializer.SerializeToElement(new{description=new string('界',5000)});Reject(()=>cap.Describe(session,actor,id,0,1,schemas,256));
C(cap.Discover(session,actor,id,0,1).Entries[0].State.Facts==FactState.Unknown,"old capture immutable");
JsonElement J(string v)=>JsonDocument.Parse(v).RootElement.Clone();
var native=J("""{"type":"object","properties":{"workspace":{"type":"string","enum":["own","table"]},"value":{"type":"integer","minimum":0,"maximum":999},"settings":{"type":"object","properties":{"mode":{"type":"string","enum":["on","off"]}},"required":["mode"],"additionalProperties":false}},"required":["workspace","value"],"additionalProperties":false}""");
var pair=J("""{"type":"object","properties":{"workspace":{"type":"string","const":"own"},"value":{"type":"integer","const":4},"settings":{"type":"object","properties":{"mode":{"type":"string","const":"on"}},"required":["mode"],"additionalProperties":false}},"required":["workspace","value","settings"],"additionalProperties":false}""");
C(TypedAssociationSchema.Build(native,new[]{pair}).GetProperty("oneOf").GetArrayLength()==1,"nested typed association");
foreach(var bad in new[]{pair.GetRawText().Replace("\"own\"","\"unseen\""),pair.GetRawText().Replace("\"const\":4","\"const\":1000"),pair.GetRawText().Replace("\"mode\"","\"secret\""),pair.GetRawText().Replace("\"additionalProperties\":false","\"additionalProperties\":true")})C(!TypedAssociationSchema.IsSubset(native,J(bad)),"reject widening/unknown");
var opaque=J("""{"type":"object"}""");C(!TypedAssociationSchema.IsSubset(opaque,pair),"opaque settings need native template");
C(!TypedAssociationSchema.IsSubset(native,J(pair.GetRawText().Replace("\"const\":4","\"const\":4,\"multipleOf\":2"))),"unknown keywords reject");
Reject(()=>TypedAssociationSchema.Build(native,Array.Empty<JsonElement>()));
var offer=new Westworld.Decisions.Local.ToolOffer("act_Config","typed native template",native,new Dictionary<string,IReadOnlyList<string>>{{"workspace",new[]{"own","table"}}},AssociatedParameterSchemas:new[]{pair});
var prompt=new Westworld.Decisions.Local.ToolDecisionPrompt(session,actor,id,J("{}"),new[]{offer});
C(prompt.ParseResponse("""{"goal":"choose configuration","steps":[{"tool":"act_Config","arguments":{"workspace":"own","value":4,"settings":{"mode":"on"}}}]}""").Steps.Count==1,"associated parser accepted");
foreach(var argumentJson in new[]{"{\"workspace\":\"table\",\"value\":4,\"settings\":{\"mode\":\"on\"}}","{\"workspace\":\"own\",\"value\":5,\"settings\":{\"mode\":\"on\"}}","{\"workspace\":\"own\",\"value\":4,\"settings\":{\"mode\":\"off\"}}"}){
 try{prompt.ParseResponse("{\"goal\":\"test\",\"steps\":[{\"tool\":\"act_Config\",\"arguments\":"+argumentJson+"}]}");throw new Exception("bad association accepted");}catch(FormatException){n++;}
}
Console.WriteLine($"PASS {n}/{n}; completeIndexUtf8Bytes={Encoding.UTF8.GetByteCount(cap.FullNameIndex())}; fullCatalog=55; descriptorPages={pages}; tinySchemaPageBytes={Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(tiny))}");

C(CapabilityExecution.StatusTool("submit_plan")=="task_status","native plan lifecycle is task");
C(CapabilityExecution.TracksOutcome("submit_plan"),"native plan tracks outcome");
C(CapabilityExecution.StatusTool("read_social")==null,"social read is not action");
Console.WriteLine($"PASS production registry total {n}");

foreach(var busy in new[]{(true,false,false),(false,true,false),(false,false,true)})foreach(var tool in new[]{"act_Eat","act_Dig","act_Place","act_TransferItems","plan_craft","submit_plan"})C(!CapabilityDispatchPolicy.MayOffer(tool,busy.Item1,busy.Item2,busy.Item3),"all owners block legacy and typed writes");
foreach(var tool in new[]{"cancel_task","cancel_action","action_status","task_status","read_inventory"})C(CapabilityDispatchPolicy.MayOffer(tool,true,true,true),"cancel and status retained");
var taskId=Guid.NewGuid();var original=JsonSerializer.SerializeToElement(new{taskId});
C(CapabilityDispatchPolicy.SameTask(original,original),"same task terminal");C(!CapabilityDispatchPolicy.SameTask(original,JsonSerializer.SerializeToElement(new{taskId=Guid.NewGuid()})),"replacement task rejected");C(!CapabilityDispatchPolicy.SameTask(original,J("{}")),"missing task id rejected");
C(CapabilityDispatchPolicy.ReadKey("read_item_settings",J("{\"slot\":1,\"value\":7}"))==CapabilityDispatchPolicy.ReadKey("read_item_settings",J("{\"value\":7,\"slot\":1}")),"canonical query identity");
Console.WriteLine($"PASS production registry total {n}");

C(!CapabilityDispatchPolicy.KnownIdle(J("{}")),"unknown state not idle");C(!CapabilityDispatchPolicy.KnownIdle(J("null")),"null task not idle");C(CapabilityDispatchPolicy.KnownIdle(J("null"),true),"native direct null is no last action");Console.WriteLine($"PASS production registry total {n}");

var legacyCraft=new Westworld.Decisions.Local.ToolOffer("act_Craft","legacy",J("{\"type\":\"object\",\"properties\":{}}"));
C(CapabilityExecution.MergeActions(new[]{legacyCraft},new[]{new KeyValuePair<string,Westworld.Decisions.Local.ToolOffer?>("act_Craft",null)}).Count==0,"typed unavailable removes legacy nested action");Console.WriteLine($"PASS production registry total {n}");
