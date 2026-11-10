using System.Text.Json;
using Capability39;
using Westworld.Decisions.Local;
int count=0;
void Check(bool b){count++;if(!b)throw new Exception("assertion "+count);}
void Reject(Action a){bool failed=false;try{a();}catch(Exception e)when(e is ArgumentException or InvalidOperationException){failed=true;}Check(failed);}
var s=Guid.NewGuid();var a=Guid.NewGuid();var c=Guid.NewGuid();
var schema=JsonSerializer.SerializeToElement(new{type="object",properties=new{},required=Array.Empty<string>(),additionalProperties=false});
var ds=Enumerable.Range(0,55).Select(i=>new CapabilityDescriptor("tool_"+i,i<20?"body":"other",Array.Empty<ParameterKind>(),true)).ToArray();
var cs=ds.Take(54).Select(d=>new CapabilityMenuCandidate(d.Name,true,FactState.Available,"actual",new ToolOffer(d.Name,"x",schema))).ToArray();
var menu=new CapabilityMenu(s,a,c,ds,cs,Array.Empty<CapabilityMenuCandidate>());
Check(menu.Build().Offers.Count==2);Check(menu.Build().CompleteIndex.Split(' ').Length==55);
var seen=new HashSet<string>();for(int i=0;i<55;i+=8){var page=menu.Discover(s,a,c,i);foreach(var e in page.Entries)Check(seen.Add(e.Descriptor.Name));}Check(seen.Count==55);
Check(Enumerable.Range(0,7).SelectMany(i=>menu.Discover(s,a,c,i*8).Entries).Single(e=>e.Descriptor.Name=="tool_54").State.Facts==FactState.Unknown);
Reject(()=>menu.Discover(s,a,Guid.NewGuid()));Reject(()=>menu.Select(s,Guid.NewGuid(),c,new(new[]{"tool_1"},Array.Empty<string>())));
var view=menu.Select(s,a,c,new(Array.Empty<string>(),new[]{"body"}));Check(view.Offers.Count==22);Check(view.Capture.Discover(s,a,c).Entries.Any(e=>e.State.InExecutionMenu));
Reject(()=>menu.Select(s,a,c,new(Array.Empty<string>(),new[]{"other"})));Check(menu.Build().Offers.Count==22);
Reject(()=>menu.Select(s,a,c,new(new[]{"bad"},Array.Empty<string>())));Reject(()=>menu.Select(s,a,c,new(new[]{"tool_1","tool_1"},Array.Empty<string>())));
view=menu.Select(s,a,c,new(new[]{"tool_54"},Array.Empty<string>()));Check(view.Offers.Count==2);Check(view.RequestedButUnavailable.Single()=="tool_54");
Check(menu.Describe(s,a,c,0,1).Entries.Single().GrantedExecutionPermission==false);
Check(menu.HandleDiscovery(s,a,c,CapabilityMenu.DiscoverTool,JsonSerializer.SerializeToElement(new{offset=54,limit=1})).GetProperty("Entries").GetArrayLength()==1);
Reject(()=>menu.HandleDiscovery(s,a,c,CapabilityMenu.DiscoverTool,JsonSerializer.SerializeToElement(new{fake=1})));
Reject(()=>menu.HandleDiscovery(s,a,c,"act_Dig",schema));Reject(()=>menu.Discover(s,a,c,0,9));
Reject(()=>new CapabilityMenu(s,a,c,ds,cs,new[]{cs[0] with {ActorSupported=false}}));
var replaced=new CapabilityMenu(s,a,c,ds,cs,new[]{cs[0] with {ActorSupported=null,Facts=FactState.Unknown,Reason="typed facts absent",Offer=null}});
Check(replaced.Select(s,a,c,new(new[]{"tool_0"},Array.Empty<string>())).Offers.Count==2);
Reject(()=>new CapabilityMenu(Guid.Empty,a,c,ds,cs,Array.Empty<CapabilityMenuCandidate>()));
var chosen=menu.HandleDiscovery(s,a,c,CapabilityMenu.SchemaTool,JsonSerializer.SerializeToElement(new{select_tool="tool_2"}));
Check(chosen.GetProperty("selectionApplied").GetBoolean());Check(!chosen.GetProperty("executionGranted").GetBoolean());Check(menu.Build().Offers.Count==3);
Reject(()=>menu.HandleDiscovery(s,a,c,CapabilityMenu.DiscoverTool,JsonSerializer.SerializeToElement(new{select_tool="tool_2"})));
Reject(()=>menu.HandleDiscovery(s,a,c,CapabilityMenu.SchemaTool,JsonSerializer.SerializeToElement(new{select_tool="tool_2",select_family="body"})));
Reject(()=>menu.HandleDiscovery(s,a,Guid.NewGuid(),CapabilityMenu.SchemaTool,JsonSerializer.SerializeToElement(new{select_tool="tool_2"})));
var newCapture=Guid.NewGuid();var fresh=new CapabilityMenu(s,a,newCapture,ds,Array.Empty<CapabilityMenuCandidate>(),Array.Empty<CapabilityMenuCandidate>());
Check(fresh.Select(s,a,newCapture,new(new[]{"tool_2"},Array.Empty<string>())).Offers.Count==2); // No old offer/target follows selection.
Reject(()=>fresh.HandleDiscovery(s,a,c,CapabilityMenu.SchemaTool,JsonSerializer.SerializeToElement(new{select_tool="tool_2"})));
var prompt=new ToolDecisionPrompt(s,a,c,JsonSerializer.SerializeToElement(new{}),menu.Build().Offers);
var parsed=prompt.ParseResponse("{\"goal\":\"select body\",\"steps\":[{\"tool\":\"read_capability_schema\",\"arguments\":{\"select_family\":\"body\"}}]}");
Check(parsed.Steps.Single().Tool==CapabilityMenu.SchemaTool);
Check(menu.HandleDiscovery(s,a,c,parsed.Steps[0].Tool,parsed.Steps[0].Arguments).GetProperty("nextMenu").GetArrayLength()==22);
var planMenu=new CapabilityMenu(s,a,c,CapabilityCatalog.All,Array.Empty<CapabilityMenuCandidate>(),Array.Empty<CapabilityMenuCandidate>());
Check(planMenu.HandleDiscovery(s,a,c,CapabilityMenu.SchemaTool,JsonSerializer.SerializeToElement(new{select_tool="submit_plan",plan_page=2})).GetProperty("executionGranted").GetBoolean()==false);
foreach(var bad in new[]{"{\"plan_page\":0}","{\"select_tool\":\"act_Eat\",\"plan_page\":0}","{\"select_tool\":\"submit_plan\",\"plan_page\":-1}","{\"select_tool\":\"submit_plan\",\"plan_page\":128}"}){
 bool denied=false;try{planMenu.HandleDiscovery(s,a,c,CapabilityMenu.SchemaTool,JsonDocument.Parse(bad).RootElement);}catch(ArgumentException){denied=true;}Check(denied);
}
Console.WriteLine($"PASS {count} capability menu assertions; no native/model execution");
