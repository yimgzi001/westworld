using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
int count=0;void Check(bool v){if(!v)throw new Exception("check "+count);count++;}
var session=Guid.NewGuid();var actor=Guid.NewGuid();var b=Guid.NewGuid();var a=Guid.NewGuid();
JsonElement Inv(int quantity)=>JsonSerializer.SerializeToElement(new{slots=new[]{new{slot=0,value=226,count=quantity},new{slot=1,value=162,count=1}}});
JsonElement Build(int before,int after,int max=3000)=>ToolOutcomeDelta.Build(new(session,actor,b,Inv(before)),new(session,actor,a,Inv(after)),"Acquire new food and eat",new("act_Dig",12,"succeeded"),[],maxChars:max);
JsonElement Old(int before,int after,int max=3000)=>ToolOutcomeDeltaBaseline.Build(new(session,actor,b,Inv(before)),new(session,actor,a,Inv(after)),"Acquire new food and eat",new("act_Dig",12,"succeeded"),[],maxChars:max);
foreach(var pair in new[]{(0,0),(0,1),(2,1)}){
 var old=Old(pair.Item1,pair.Item2);var next=Build(pair.Item1,pair.Item2);
 var on=JsonNode.Parse(old.GetRawText())!.AsObject();var nn=JsonNode.Parse(next.GetRawText())!.AsObject();on.Remove("note");nn.Remove("note");Check(JsonNode.DeepEquals(on,nn));
 Check(next.GetProperty("beforeObservationId").GetGuid()==b&&next.GetProperty("afterObservationId").GetGuid()==a);
 Check(next.GetProperty("note").GetString()!.Contains("Historical action-result snapshot"));
}
var unchanged=Build(0,0);Check(unchanged.GetProperty("inventoryDelta").GetArrayLength()==0);
var later=Inv(1);Check(later.GetProperty("slots")[0].GetProperty("count").GetInt32()==1);Check(unchanged.GetProperty("inventoryDelta").GetArrayLength()==0);
var note=unchanged.GetProperty("note").GetString()!;Check(!note.Contains("act_Eat")&&!note.Contains("226"));Check(note.Contains("No next action prescribed"));
int oldSize=Old(0,0).GetRawText().Length;Check(Old(0,0,oldSize).GetRawText().Length==oldSize);
bool rejected=false;try{Build(0,0,oldSize);}catch(InvalidOperationException){rejected=true;}Check(rejected); // bounded payload still refuses; no hidden budget increase.
int newSize=unchanged.GetRawText().Length;Check(Build(0,0,newSize).GetRawText().Length==newSize);
Check(Encoding.UTF8.GetByteCount(unchanged.GetRawText())<=6000);
Console.WriteLine($"PASS {count} C# historical-delta checks; note growth {newSize-oldSize} ASCII chars/UTF8 bytes, no API/GUI");
