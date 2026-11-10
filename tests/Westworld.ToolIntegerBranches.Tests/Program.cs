using System.Text.Json;
using Westworld.Decisions.Local;
int n=0;void C(bool ok){if(!ok)throw new Exception("assert "+n);n++;}
JsonElement J(string s)=>JsonDocument.Parse(s).RootElement.Clone();
var schema=J("""{"type":"object","properties":{"slot":{"type":"integer","minimum":0,"maximum":4095},"value":{"type":"integer","minimum":0,"maximum":2147483647},"count":{"type":"integer","minimum":1,"maximum":64}},"required":["slot","value","count"],"additionalProperties":false}""");
ToolDecisionPrompt P(JsonElement slots){var b=EatPairing.Build(slots,schema);return new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),J("{}"),b.Offer is null?Array.Empty<ToolOffer>():new[]{b.Offer});}
var inventory=J("""[{"slot":0,"value":226,"count":1,"nutrition":0.05},{"slot":1,"value":162,"count":2,"nutrition":0.5},{"slot":2,"value":226,"count":3,"nutrition":0.05},{"slot":3,"value":122,"count":8},{"slot":4,"value":0,"count":0}]""");
var prompt=P(inventory);
string Plan(int slot,int value,int count)=>JsonSerializer.Serialize(new{goal="choose freely",steps=new[]{new{tool="act_Eat",arguments=new{slot,value,count}}}});
foreach(var a in new[]{(0,226,1),(1,162,2),(2,226,3)})C(prompt.ParseResponse(Plan(a.Item1,a.Item2,a.Item3)).Steps.Count==1);
foreach(var a in new[]{(0,162,1),(1,226,1),(0,226,2),(2,226,4),(3,122,1),(4,0,1),(5,226,1),(0,226,0)}){try{prompt.ParseResponse(Plan(a.Item1,a.Item2,a.Item3));throw new Exception("accepted invalid");}catch(FormatException){n++;}}
C(prompt.ParseResponse("""{"goal":"do nothing","steps":[]}""").Steps.Count==0);
var fresh=P(J("""[{"slot":0,"value":1048802,"count":1,"nutrition":0.05}]"""));try{fresh.ParseResponse(Plan(0,226,1));throw new Exception();}catch(FormatException){n++;}
C(EatPairing.Build(J("[]"),schema).Offer is null);
C(EatPairing.Build(J("""[{"slot":0,"value":226,"count":1}]"""),schema).Offer is null);
try{EatPairing.Build(J("""[{"slot":0,"value":1,"count":1},{"slot":0,"value":2,"count":1}]"""),schema);throw new Exception();}catch(ArgumentException){n++;}
var many=JsonSerializer.SerializeToElement(Enumerable.Range(0,33).Select(i=>new{slot=i,value=226,count=1,nutrition=.05}));var cap=EatPairing.Build(many,schema);C(cap.Omitted==0&&cap.Status=="complete"&&cap.Offer!.ParameterBranches!.Count==1);
C(prompt.Prompt.Contains("const=226")&&prompt.Prompt.Contains("[1..1]"));
C(P(many).ParseResponse(Plan(31,226,1)).Steps.Count==1);
// Fresh dispatch never rebinds an old pair or assumes future acquisition.
var reduced=P(J("""[{"slot":1,"value":162,"count":1,"nutrition":0.5}]"""));
try{reduced.ParseResponse(Plan(1,162,2));throw new Exception();}catch(FormatException){n++;}
try{P(J("[]")).ParseResponse(Plan(0,226,1));throw new Exception();}catch(FormatException){n++;}
foreach(var bad in new[]{"{\"maximum\":65}","{\"const\":-1}"}){
 var branch=new ToolParameterBranch(new[]{"slot","value","count"},Array.Empty<string>(),new Dictionary<string,JsonElement>{{bad.Contains("maximum")?"count":"slot",J(bad)}});
 try{new ToolDecisionPrompt(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),J("{}"),new[]{new ToolOffer("act_Eat","",schema,ParameterBranches:new[]{branch})});throw new Exception();}catch(ArgumentException){n++;}
}
for(int slot=0;slot<=4;slot++)foreach(int value in new[]{226,162,122})for(int count=1;count<=4;count++){
 bool actual;try{prompt.ParseResponse(Plan(slot,value,count));actual=true;}catch(FormatException){actual=false;}
 bool expected=slot==0&&value==226&&count<=1||slot==1&&value==162&&count<=2||slot==2&&value==226&&count<=3;
 C(actual==expected);
}
var distinct=JsonSerializer.SerializeToElement(Enumerable.Range(0,33).Select(i=>new{slot=i,value=226+i,count=1,nutrition=.05}));
var limited=EatPairing.Build(distinct,schema);C(limited.Omitted==1&&limited.Status=="partial"&&limited.Offer!.ParameterBranches!.Count==32);
C(P(many).ParseResponse(Plan(32,226,1)).Steps.Count==1);
void Invalid(string key,string constraint,JsonElement? parameters=null){
 var branch=new ToolParameterBranch(new[]{"slot","value","count"},Array.Empty<string>(),new Dictionary<string,JsonElement>{{key,J(constraint)}});
 try{new ToolDecisionPrompt(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),J("{}"),new[]{new ToolOffer("act_Eat","",parameters??schema,ParameterBranches:new[]{branch})});throw new Exception("accepted invalid branch");}catch(ArgumentException){n++;}
}
Invalid("slot","{\"enum\":[]}");Invalid("slot","{\"enum\":[1,1]}");Invalid("slot","{\"enum\":[1,-1]}");Invalid("slot","{\"enum\":[1,1.5]}");Invalid("slot","{\"enum\":[\"1\"]}");
Invalid("slot","{\"minimum\":1}");Invalid("slot","{\"const\":1,\"maximum\":2}");Invalid("slot","{\"const\":1.5}");Invalid("not_a_field","{\"const\":1}");
Invalid("slot","{\"enum\":"+JsonSerializer.Serialize(Enumerable.Range(0,129))+"}");
var enumSchema=J(schema.GetRawText().Replace("\"minimum\":0,\"maximum\":4095","\"enum\":[0,2]"));
Invalid("slot","{\"const\":1}",enumSchema);Invalid("slot","{\"enum\":[0,1]}",enumSchema);
var unsupported=J(schema.GetRawText().Replace("\"maximum\":4095","\"maximum\":4095,\"multipleOf\":2"));Invalid("slot","{\"const\":2}",unsupported);
Console.WriteLine($"distinct32schemaChars={P(distinct).ResponseSchema.Length}; same33schemaChars={P(many).ResponseSchema.Length}");
Console.WriteLine($"PASS {n}/{n}; promptChars={prompt.Prompt.Length}; schemaChars={prompt.ResponseSchema.Length}; branches32schemaChars={P(many).ResponseSchema.Length}");
