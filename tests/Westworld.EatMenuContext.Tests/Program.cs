using System.Text.Json;
using System.Text.Json.Nodes;
int n=0;void Check(bool x){if(!x)throw new Exception("assert "+n);n++;}
void Throws(Action a){bool yes=false;try{a();}catch(ArgumentException){yes=true;}catch(InvalidOperationException){yes=true;}Check(yes);}
JsonElement J(string s)=>JsonDocument.Parse(s).RootElement.Clone();
var input=J("{\"projectionStatus\":\"projected\",\"facts\":{\"inventory\":{\"slots\":[{\"slot\":0,\"value\":226,\"count\":1}]},\"outcomeFeedback\":{\"inventoryDelta\":[]}},\"recentResults\":[]}");
var coverage=J("{\"Status\":\"partial\",\"KnownEdibleSlots\":33,\"OfferedSlots\":32,\"OmittedSlots\":1,\"UnknownNutritionSlots\":0,\"BranchCount\":32}");
var result=EatMenuContext.Attach(input,coverage);
Check(!input.GetProperty("facts").TryGetProperty("eatMenuCoverage",out _));
Check(result.GetProperty("facts").GetProperty("eatMenuCoverage").GetProperty("OmittedSlots").GetInt32()==1);
Check(JsonNode.DeepEquals(JsonNode.Parse(input.GetProperty("facts").GetProperty("inventory").GetRawText()),JsonNode.Parse(result.GetProperty("facts").GetProperty("inventory").GetRawText())));
Check(result.GetProperty("facts").GetProperty("outcomeFeedback").GetProperty("inventoryDelta").GetArrayLength()==0);
Check(result.GetProperty("recentResults").GetArrayLength()==0);
Throws(()=>EatMenuContext.Attach(input,coverage,14001));Throws(()=>EatMenuContext.Attach(input,coverage,maxUtf8Bytes:16001));
Throws(()=>EatMenuContext.Attach(J("{}"),coverage));Throws(()=>EatMenuContext.Attach(input,J("null")));
Throws(()=>EatMenuContext.Attach(input,coverage,256));
var large=J(JsonSerializer.Serialize(new{projectionStatus="projected",facts=new{text=new string('中',5000)},recentResults=Array.Empty<object>()}));
Throws(()=>EatMenuContext.Attach(large,coverage,maxUtf8Bytes:15000));
Check(!result.GetProperty("facts").GetProperty("eatMenuCoverage").TryGetProperty("recommendedAction",out _));
EatMenuContext.VerifyTransportBudget(new string('x',32768),"{}");Check(true);
Throws(()=>EatMenuContext.VerifyTransportBudget(new string('x',32769),"{}"));
Throws(()=>EatMenuContext.VerifyTransportBudget("x",new string('x',32769)));
Console.WriteLine($"PASS {n} Eat menu disclosure contracts; no strategy or native execution");
