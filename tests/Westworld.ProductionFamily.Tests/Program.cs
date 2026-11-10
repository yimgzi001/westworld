using System.Text.Json;
using Capability39;
using Westworld.Decisions.Local;
var scope=new ProductionScope(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());int checks=0;
void Check(bool b,string n){if(!b)throw new Exception(n);checks++;}
JsonElement J(string s)=>JsonDocument.Parse(s).RootElement.Clone();
JsonElement Schema(string n){var props=new Dictionary<string,object>();var req=new List<string>();
 void P(string k,string type,bool required=true){props[k]=new{type};if(required)req.Add(k);}
 if(n is "act_UseItem" or "act_UseAimedItem" or "act_ConfigureItem" or "act_BuildFurniture" or "act_Drop"){P("slot","integer");P("value","integer");}
 if(n is "act_UseItem" or "act_UseAimedItem" or "act_Interact" or "act_ConfigureBlock" or "act_BuildFurniture")P("target","string",n!="act_UseItem");
 if(n is "act_Drop" or "act_Craft")P("count","integer");
 if(n=="act_UseAimedItem")P("duration","number");
 if(n.StartsWith("act_Configure"))P("settings","object");
 if(n=="act_BuildFurniture")P("options","object",false);
 if(n=="act_Craft"){P("workspace","string");P("result_value","integer");}
 return JsonSerializer.SerializeToElement(new{type="object",properties=props,required=req,additionalProperties=false});}
var facts=new ProductionFacts(scope,[new("block",ProductionTargetKind.Block),new("entity",ProductionTargetKind.Entity),new("moving",ProductionTargetKind.MovingBlock)],[new(0,89,2),new(3,162,1)],[new("block",null,null,"BatteryBlock"),new(null,0,89,"BatteryBlock")],[new("crafting",21,3)]);
ProductionOffer Build(string n,ProductionFacts? f=null)=>ProductionFamily.Build(n,Schema(n),true,scope,f??facts);
bool Accept(ProductionOffer x,string args){try{var p=new ToolDecisionPrompt(scope.Session,scope.Actor,scope.Capture,J("{}"),[x.Offer!]);p.ParseResponse("{\"goal\":\"test\",\"steps\":[{\"tool\":\""+x.Descriptor.Name+"\",\"arguments\":"+args+"}]}");return true;}catch(FormatException){return false;}}
foreach(var n in ProductionFamily.Names){var x=Build(n);Check(x.Offer!=null,n);Check(!ProductionFamily.Build(n,Schema(n),false,scope,facts).State.InExecutionMenu,n+"unsupported");}
foreach(int slot in new[]{0,3})foreach(int value in new[]{89,162})foreach(int count in new[]{1,2,3}){Check(Accept(Build("act_Drop"),$"{{\"slot\":{slot},\"value\":{value},\"count\":{count}}}")==((slot==0&&value==89&&count<=2)||(slot==3&&value==162&&count==1)),"drop pairing");}
Check(Accept(Build("act_UseItem"),"{\"slot\":0,\"value\":89}"),"optional current facing");
Check(!Accept(Build("act_UseItem"),"{\"slot\":3,\"value\":89}"),"use wrong pair");
Check(!Accept(Build("act_Interact"),"{\"target\":\"entity\"}"),"entity not interact target");
Check(Accept(Build("act_Interact"),"{\"target\":\"moving\"}"),"moving interact");
foreach(var s in new[]{"{\"voltage\":0}","{\"voltage\":15}"})Check(Accept(Build("act_ConfigureBlock"),"{\"target\":\"block\",\"settings\":"+s+"}"),"voltage range");
foreach(var s in new[]{"{\"voltage\":16}","{\"isOn\":true}","{\"voltage\":1,\"script\":\"x\"}","{}"})Check(!Accept(Build("act_ConfigureBlock"),"{\"target\":\"block\",\"settings\":"+s+"}"),"reject settings");
Check(Build("act_ConfigureBlock",facts with{Settings=[]}).State.Facts==FactState.Unknown,"unknown settings");
Check(Build("act_ConfigureItem",facts with{Settings=[new(null,0,89,"MusketBlock")]}).Offer==null,"unsupported settings template");
Check(Build("act_Craft",facts with{Crafts=[new("a",21,3),new("b",22,3)]}).Offer!=null,"mixed craft correlated");
Check(!Accept(Build("act_Craft"),"{\"workspace\":\"crafting\",\"result_value\":22,\"count\":1}"),"wrong result");
Check(!Accept(Build("act_Craft"),"{\"workspace\":\"crafting\",\"result_value\":21,\"count\":4}"),"over craft");
Check(!Accept(Build("act_BuildFurniture"),"{\"target\":\"block\",\"slot\":0,\"value\":89,\"options\":{}}"),"unknown options not opened");
try{Build("act_Drop",facts with{Scope=scope with{Actor=Guid.NewGuid()}});throw new Exception("wrong scope");}catch(ArgumentException){checks++;}
try{Build("act_Drop",facts with{Slots=[new(0,89,2),new(0,162,1)]});throw new Exception("dupe slots");}catch(ArgumentException){checks++;}
var narrow=System.Text.Json.Nodes.JsonNode.Parse(Schema("act_Drop").GetRawText())!;
narrow["properties"]!["count"]!["maximum"]=1;
var bounded=ProductionFamily.Build("act_Drop",JsonSerializer.SerializeToElement(narrow),true,scope,facts);
Check(Accept(bounded,"{\"slot\":0,\"value\":89,\"count\":1}"),"native narrow count accepted");
Check(!Accept(bounded,"{\"slot\":0,\"value\":89,\"count\":2}"),"native narrow count preserved");
var many=facts with{Slots=Enumerable.Range(0,33).Select(i=>new ProductionSlot(i,100+i,1)).ToArray()};
var partial=Build("act_Drop",many);Check(partial.OmittedAlternatives==1&&partial.Offer!.Description.Contains("Partial"),"partial alternative explicit");
Check(!Accept(partial,"{\"slot\":32,\"value\":132,\"count\":1}"),"omitted pair not executable");
var targets=System.Text.Json.Nodes.JsonNode.Parse(Schema("act_Interact").GetRawText())!;
targets["properties"]!["target"]!["enum"]=new System.Text.Json.Nodes.JsonArray("moving");
var typed=ProductionFamily.Build("act_Interact",JsonSerializer.SerializeToElement(targets),true,scope,facts);
Check(!Accept(typed,"{\"target\":\"block\"}"),"native target enum preserved");
Check(Accept(typed,"{\"target\":\"moving\"}"),"native target enum supported");
var mixedCraft=Build("act_Craft",facts with{Crafts=[new("a",21,2),new("b",22,1)]});
foreach(string w in new[]{"a","b"})foreach(int val in new[]{21,22})foreach(int count in new[]{1,2,3})Check(Accept(mixedCraft,$"{{\"workspace\":\"{w}\",\"result_value\":{val},\"count\":{count}}}")==((w=="a"&&val==21&&count<=2)||(w=="b"&&val==22&&count==1)),"workspace/result/count correlation");
var mixed=facts with{Targets=[new("battery",ProductionTargetKind.Block),new("delay",ProductionTargetKind.Block)],Settings=[new("battery",null,null,"BatteryBlock"),new("delay",null,null,"AdjustableDelayGateBlock"),new(null,0,89,"BatteryBlock"),new(null,3,162,"AdjustableDelayGateBlock")]};
var mixedBlock=Build("act_ConfigureBlock",mixed);var mixedItem=Build("act_ConfigureItem",mixed);
foreach(string target in new[]{"battery","delay"})foreach(string key in new[]{"voltage","delaySteps"})foreach(int v in new[]{0,15,255,256})Check(Accept(mixedBlock,$"{{\"target\":\"{target}\",\"settings\":{{\"{key}\":{v}}}}}")==((target=="battery"&&key=="voltage"&&v<=15)||(target=="delay"&&key=="delaySteps"&&v<=255)),"typed target/settings correlation");
foreach(int slot in new[]{0,3})foreach(int val in new[]{89,162})foreach(string key in new[]{"voltage","delaySteps"})Check(Accept(mixedItem,$"{{\"slot\":{slot},\"value\":{val},\"settings\":{{\"{key}\":1}}}}")==((slot==0&&val==89&&key=="voltage")||(slot==3&&val==162&&key=="delaySteps")),"owned typed settings correlation");
Check(!Accept(mixedBlock,"{\"target\":\"battery\",\"settings\":{\"voltage\":1,\"delaySteps\":1}}"),"settings union not open object");
var partialSettings=Build("act_ConfigureBlock",mixed with{Settings=[new("battery",null,null,"BatteryBlock"),new("delay",null,null,"PistonBlock")]});
Check(partialSettings.OmittedAlternatives==1&&partialSettings.State.Reason=="partial_current_facts","unsupported type explicit partial");
Check(!Accept(partialSettings,"{\"target\":\"delay\",\"settings\":{\"voltage\":1}}"),"omitted type no borrowed permission");
try{Build("act_Craft",facts with{Crafts=[new("a",21,1),new("a",22,1)]});throw new Exception("conflict preview");}catch(ArgumentException){checks++;}
try{Build("act_ConfigureBlock",facts with{Settings=[new("block",null,null,"BatteryBlock"),new("block",null,null,"AdjustableDelayGateBlock")]});throw new Exception("conflict settings");}catch(ArgumentException){checks++;}
Console.WriteLine($"PASS {checks}; production family mapper; no GUI/API/native execution");
