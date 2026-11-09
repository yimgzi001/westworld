using System.Text.Json;
using Game.Westworld;
using Westworld.Decisions.Local;
int checks=0;void Check(bool b,string n){checks++;if(!b)throw new Exception(n);Console.WriteLine("PASS "+n);}
JsonElement J(string s)=>JsonDocument.Parse(s).RootElement.Clone();
ActorToolDefinition Tool(string name,string properties,string required)=>new(name,"native description",J("{\"type\":\"object\",\"properties\":{"+properties+"},\"required\":["+required+"],\"additionalProperties\":false}"));
var tools=new[]{
Tool("act_MoveTo","\"destination\":{\"type\":\"string\"},\"bearing\":{\"type\":\"number\"},\"distance\":{\"type\":\"number\"},\"timeout\":{\"type\":\"number\"}",""),
Tool("act_FaceTowards","\"destination\":{\"type\":\"string\"},\"bearing\":{\"type\":\"number\"},\"pitch\":{\"type\":\"number\"},\"timeout\":{\"type\":\"number\"}",""),
Tool("act_Dig","\"target\":{\"type\":\"string\"},\"timeout\":{\"type\":\"number\"}","\"target\""),
Tool("act_MeleeAttack","\"target\":{\"type\":\"string\"}","\"target\""),
Tool("act_PickUp","\"target\":{\"type\":\"string\"},\"food_value\":{\"type\":\"integer\"},\"count\":{\"type\":\"integer\"}","\"count\""),
Tool("plan_craft","\"workspace\":{\"type\":\"string\"},\"recipe\":{\"type\":\"string\"},\"count\":{\"type\":\"integer\"}","\"workspace\",\"recipe\",\"count\""),
Tool("act_Eat","\"slot\":{\"type\":\"integer\"},\"value\":{\"type\":\"integer\"},\"count\":{\"type\":\"integer\"}","\"slot\",\"value\",\"count\""),
Tool("find_recipes","\"result_value\":{\"type\":\"integer\"},\"limit\":{\"type\":\"integer\"}",""),
Tool("observe","\"look_distance\":{\"type\":\"number\"},\"object_distance\":{\"type\":\"number\"},\"limit\":{\"type\":\"integer\"}",""),
Tool("act_Wait","\"duration\":{\"type\":\"number\"}","\"duration\""),Tool("shell","","")};
var obs=J("""{"food":1,"focus":{"kind":"block","reference":"block1","storageReference":"table1"},"entities":[{"reference":"pig1"}],"pickables":[{"reference":"drop1","value":88}],"inventoryReference":"inv","craftingReference":"ownGrid"}""");
var inventory=J("""{"slots":[{"slot":0,"value":162,"count":1},{"slot":1,"value":0,"count":0}]}""");
var recipes=J("""{"ok":true,"data":[{"reference":"recipe1","recipe":{"resultValue":122}}]}""");
var table=J("""{"ok":true,"storage":"table1","data":{"inventory":{"slots":[{"slot":0,"value":0,"count":0}]},"crafting":{"recipe":null}}}""");
var offers=NativeToolOffers.Build(tools,obs,inventory,[recipes,table]);
ToolOffer Find(string n)=>offers.Single(o=>o.Name==n);
Check(offers.All(o=>o.Name!="shell"),"non-profile arbitrary execution not offered");
Check(Find("act_MeleeAttack").ReferenceValues!["target"].SequenceEqual(new[]{"pig1"}),"hunting refs only observed entities");
Check(Find("act_Dig").ReferenceValues!["target"].SequenceEqual(new[]{"block1"}),"dig refs only visible blocks");
Check(Find("act_PickUp").ReferenceValues!["target"].SequenceEqual(new[]{"drop1"}),"pickup refs only visible drops");
Check(Find("plan_craft").ReferenceValues!["recipe"].Single()=="recipe1","recipe reference from actual recent result");
Check(Find("plan_craft").ReferenceValues!["workspace"].Contains("table1"),"observed storage classified as workbench only by preview");
Check(Find("act_Eat").Parameters.GetProperty("properties").GetProperty("value").GetProperty("enum").EnumerateArray().Any(v=>v.GetInt32()==0),"native empty inventory value retained");
Check(Find("act_MoveTo").Parameters.GetProperty("properties").GetProperty("timeout").GetProperty("maximum").GetDouble()==4,"short navigation timeout bounded");
Check(Find("act_Wait").Parameters.GetProperty("properties").GetProperty("duration").GetProperty("maximum").GetDouble()==10,"wait upper bound ten seconds");
Check(Find("find_recipes").Parameters.GetProperty("properties").GetProperty("limit").GetProperty("maximum").GetInt32()==4,"knowledge query result budget bounded");
var none=NativeToolOffers.Build(tools,J("{}"),J("{\"slots\":[]}"),[]);
Check(none.All(t=>t.Name is not ("act_Dig" or "act_MeleeAttack" or "act_PickUp" or "plan_craft" or "act_Eat")),"missing required facts omit unavailable calls");
Check(!none.Single(t=>t.Name=="act_MoveTo").Parameters.GetProperty("properties").TryGetProperty("destination",out _),"no references still permits relative exploration");
var failed=NativeToolOffers.Build(tools,obs,inventory,[J("""{"ok":false,"data":[{"reference":"fake","recipe":{}}]}""")]);
Check(failed.All(t=>t.Name!="plan_craft"),"failed query cannot create recipe capability");
var p=new ToolDecisionPrompt(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),obs,offers);
Check(p.ParseResponse("""{"goal":"观察到猪，尝试攻击","steps":[{"tool":"act_MeleeAttack","arguments":{"target":"pig1"}}]}""").Steps.Count==1,"factory schemas compile and parse in real pure protocol");
Check(offers.Any(t=>t.Name=="act_MoveTo")&&offers.Any(t=>t.Name=="act_Eat")&&offers.Any(t=>t.Name=="plan_craft"),"full actor retains parallel navigation food production choices");
Check(Find("act_Dig").Parameters.GetProperty("properties").GetProperty("timeout").GetProperty("maximum").GetDouble()==30,"dig receives thirty-second progress window");
Check(Find("act_Dig").Parameters.GetProperty("required").EnumerateArray().Any(x=>x.GetString()=="timeout"),"dig cannot omit timeout and bypass bound");
Check(Find("act_MoveTo").Parameters.GetProperty("required").EnumerateArray().Any(x=>x.GetString()=="timeout"),"move cannot silently use native fifteen-second default");
Check(Find("observe").Parameters.GetProperty("properties").GetProperty("look_distance").GetProperty("maximum").GetDouble()==16&&Find("observe").Parameters.GetProperty("properties").GetProperty("object_distance").GetProperty("maximum").GetDouble()==16,"native sixteen-metre observation range preserved");
var digPlan=p.ParseResponse("""{"goal":"采集花岗岩","steps":[{"tool":"act_Dig","arguments":{"target":"block1","timeout":30}}]}""");
Check(digPlan.Steps.Count==1,"thirty-second native dig offer accepted");
bool missingTimeout=false;try{p.ParseResponse("""{"goal":"挖掘","steps":[{"tool":"act_Dig","arguments":{"target":"block1"}}]}""");}catch(FormatException){missingTimeout=true;}
Check(missingTimeout,"omitted dig deadline rejected by actual protocol");
bool longMove=false;try{p.ParseResponse("""{"goal":"移动","steps":[{"tool":"act_MoveTo","arguments":{"bearing":0,"distance":1,"timeout":5}}]}""");}catch(FormatException){longMove=true;}
Check(longMove,"move still rejects over-four-second deadline");
Check(!Find("act_PickUp").Parameters.GetProperty("properties").TryGetProperty("food_value",out _),"visible item value alone does not prove nutrition");
var nutritionObs=J("""{"pickables":[{"reference":"club","value":122},{"reference":"xp","value":248,"nutrition":0},{"reference":"edible","value":88,"nutrition":0.3},{"reference":"negative","value":77,"nutrition":-1}]}""");
var nutritionOffers=NativeToolOffers.Build(tools,nutritionObs,inventory,[]);
var pickup=nutritionOffers.Single(t=>t.Name=="act_PickUp");
Check(pickup.Parameters.GetProperty("properties").GetProperty("food_value").GetProperty("enum").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(new[]{88}),"food_value needs explicit positive native nutrition");
Check(pickup.ReferenceValues!["target"].Contains("club")&&pickup.ReferenceValues["target"].Contains("xp"),"non-food target pickup remains available");
Check(p.Prompt.Contains("PickUp: target XOR food_value"),"pickup branch exclusivity communicated");
void PlanCheck(ToolDecisionPrompt prompt,string tool,string parameters,bool accepted,string label){
 bool ok=true;try{prompt.ParseResponse("{\"goal\":\"test\",\"steps\":[{\"tool\":\""+tool+"\",\"arguments\":"+parameters+"}]}");}catch(FormatException){ok=false;}Check(ok==accepted,label);
}
PlanCheck(p,"act_MoveTo","""{"destination":"block1","timeout":2}""",true,"move destination branch accepted");
PlanCheck(p,"act_MoveTo","""{"bearing":0,"distance":1,"timeout":2}""",true,"move relative branch accepted");
PlanCheck(p,"act_MoveTo","""{"destination":"block1","bearing":0,"distance":1,"timeout":2}""",false,"move both branches rejected");
PlanCheck(p,"act_MoveTo","""{"timeout":2}""",false,"move neither branch rejected");
PlanCheck(p,"act_MoveTo","""{"bearing":0,"timeout":2}""",false,"move missing distance rejected");
PlanCheck(p,"act_MoveTo","""{"distance":1,"timeout":2}""",false,"move missing bearing rejected");
PlanCheck(p,"act_FaceTowards","""{"destination":"block1","timeout":2}""",true,"face destination accepted");
PlanCheck(p,"act_FaceTowards","""{"bearing":45,"pitch":-20,"timeout":2}""",true,"face relative pitch accepted");
PlanCheck(p,"act_FaceTowards","""{"destination":"block1","pitch":-20,"timeout":2}""",false,"face destination plus pitch rejected");
PlanCheck(p,"act_FaceTowards","""{"destination":"block1","bearing":45,"timeout":2}""",false,"face both branches rejected");
PlanCheck(p,"act_FaceTowards","""{"timeout":2}""",false,"face no direction rejected");
var pp=new ToolDecisionPrompt(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),nutritionObs,nutritionOffers);
PlanCheck(pp,"act_PickUp","""{"target":"club","count":1}""",true,"pickup nonfood target branch accepted");
PlanCheck(pp,"act_PickUp","""{"food_value":88,"count":1}""",true,"pickup observed food shortcut accepted");
PlanCheck(pp,"act_PickUp","""{"target":"edible","food_value":88,"count":1}""",false,"pickup both selectors rejected");
PlanCheck(pp,"act_PickUp","""{"count":1}""",false,"pickup no selector rejected");
var noRefsPrompt=new ToolDecisionPrompt(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),J("{}"),none);
PlanCheck(noRefsPrompt,"act_MoveTo","""{"bearing":0,"distance":1,"timeout":2}""",true,"filtered destination retains relative branch");
PlanCheck(noRefsPrompt,"act_MoveTo","""{"timeout":2}""",false,"filtered branches cannot produce empty movement");
using(var grammar=JsonDocument.Parse(p.ResponseSchema)){
 var moveSchema=grammar.RootElement.GetProperty("properties").GetProperty("steps").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(b=>b.GetProperty("properties").GetProperty("tool").GetProperty("const").GetString()=="act_MoveTo").GetProperty("properties").GetProperty("arguments");
 Check(moveSchema.GetProperty("oneOf").GetArrayLength()==2,"strict generation grammar contains two disjoint movement objects");
}
var semanticTools=new[]{Tool("act_Craft","\"workspace\":{\"type\":\"string\"},\"result_value\":{\"type\":\"integer\"},\"count\":{\"type\":\"integer\"}","\"workspace\",\"result_value\",\"count\""),
Tool("act_TransferItems","\"source\":{\"type\":\"string\"},\"destination\":{\"type\":\"string\"}","\"source\",\"destination\""),
Tool("read_storage","\"storage\":{\"type\":\"string\"}","\"storage\"")};
var semantic=NativeToolOffers.Build(semanticTools,obs,inventory,[recipes,table]);
Check(semantic.Single(t=>t.Name=="act_Craft").Description.Contains("already correctly arranged")&&semantic.Single(t=>t.Name=="act_Craft").Description.Contains("cannot heat/cook"),"Craft prerequisite and non-cooking semantics retained");
Check(semantic.Single(t=>t.Name=="act_TransferItems").Description.Contains("destination slot CURRENT value (0 if empty)"),"transfer expected destination value explained without truncation");
Check(Find("plan_craft").Description.Contains("existing carried/grid materials")&&Find("plan_craft").Description.Contains("no heated/furnace recipes"),"plan_craft does not promise gather or cook");
Check(semantic.Single(t=>t.Name=="read_storage").Description.Contains("native heat/fuel over time"),"furnace native prerequisites explained");
Check(Find("find_recipes").Description.Contains("does not mean supplies"),"static recipe knowledge distinguished from resources");
Console.WriteLine($"{checks}/{checks} linked factory checks passed using native type-shape stub; no game execution.");
