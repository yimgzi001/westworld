using System.Text.Json;
using System.Text.Json.Nodes;
int checks=0;void Check(bool b,string n){checks++;if(!b)throw new Exception(n);Console.WriteLine("PASS "+n);}
JsonElement E(JsonNode n)=>JsonSerializer.SerializeToElement(n);
JsonObject Recipe(int count=1)=>new(){["reference"]="recipe1",["recipe"]=new JsonObject{["resultCount"]=count,["resultValue"]=122,["ingredients"]=new JsonArray(new JsonObject{["column"]=0,["row"]=1,["craftingId"]="wood",["data"]=null}),["requiredHeat"]=0,["unknownFutureRequirement"]=42,["description"]="public long description"}};
JsonObject Event(JsonObject recipe)=>new(){["tool"]="find_recipes",["ok"]=true,["data"]=new JsonObject{["projectionStatus"]="projected",["data"]=new JsonArray(recipe),["omitted"]=2,["hasMore"]=null}};
JsonObject Context()=>new(){["facts"]=new JsonObject{["learnedRecipes"]=new JsonArray(Recipe())},["recentResults"]=new JsonArray(Event(Recipe()))};
var raw=Context();string original=raw.ToJsonString();var output=ToolContextMemory.Compact(E(raw));
Check(raw.ToJsonString()==original,"input immutable");
var known=output.GetProperty("facts").GetProperty("learnedRecipes");
Check(known.GetArrayLength()==1,"one canonical recipe");
Check(known[0].GetProperty("recipe").GetProperty("unknownFutureRequirement").GetInt32()==42,"unknown requirement retained");
Check(known[0].GetProperty("recipe").GetProperty("ingredients")[0].GetProperty("row").GetInt32()==1,"ingredient coordinates retained");
Check(!known[0].GetProperty("recipe").TryGetProperty("description",out _),"only known verbose description omitted");
var ev=output.GetProperty("recentResults")[0].GetProperty("data");
Check(ev.GetProperty("recipeRefs")[0].GetString()=="recipe1","event points to exact ref");
Check(ev.GetProperty("omitted").GetInt32()==2&&ev.GetProperty("hasMore").ValueKind==JsonValueKind.Null,"omission and unknown pagination retained");
var conflict=Context();conflict["recentResults"]=new JsonArray(Event(Recipe(2)));var co=ToolContextMemory.Compact(E(conflict));
Check(co.GetProperty("recentResults")[0].GetProperty("data").TryGetProperty("data",out _),"conflicting event retained");
Check(co.GetProperty("memoryCompaction").GetProperty("conflictingRecipeRefs").GetArrayLength()==1,"conflict explicit");
Check(co.GetProperty("facts").GetProperty("learnedRecipes")[0].GetProperty("recipe").GetProperty("resultCount").GetInt32()==1,"conflict cannot overwrite canonical source");
var error=Context();error["recentResults"]![0]!["ok"]=false;var er=ToolContextMemory.Compact(E(error));
Check(er.GetProperty("recentResults")[0].GetProperty("data").TryGetProperty("data",out _),"failed query not mined");
var unknown=Context();unknown["recentResults"]![0]!["data"]!["data"]=new JsonArray(new JsonObject{["future"]=1});
Check(ToolContextMemory.Compact(E(unknown)).GetProperty("recentResults")[0].GetProperty("data").GetProperty("data")[0].GetProperty("future").GetInt32()==1,"unknown shape retained");
JsonObject Storage(int value)=>new(){["tool"]="read_storage",["ok"]=true,["storage"]="same",["data"]=new JsonObject{["projectionStatus"]="projected",["data"]=new JsonObject{["inventory"]=new JsonObject{["slots"]=new JsonArray(new JsonObject{["slot"]=0,["value"]=0,["count"]=0},new JsonObject{["slot"]=1,["value"]=value,["count"]=1})},["furnace"]=new JsonObject{["heat"]=1,["progress"]=0.5}}}};
var histories=Context();histories["recentResults"]=new JsonArray(Storage(88),Storage(89));var hist=ToolContextMemory.Compact(E(histories)).GetProperty("recentResults");
Check(hist.GetArrayLength()==2,"same storage different snapshots never merged");
Check(hist[0].GetProperty("data").GetProperty("data").GetProperty("inventory").GetProperty("slots")[0].GetProperty("value").GetInt32()==88&&hist[1].GetProperty("data").GetProperty("data").GetProperty("inventory").GetProperty("slots")[0].GetProperty("value").GetInt32()==89,"historical change retained");
Check(hist[0].GetProperty("data").GetProperty("data").GetProperty("inventory").GetProperty("emptySlotIds")[0].GetInt32()==0,"empty slot exactly reconstructible");
Check(hist[0].GetProperty("data").GetProperty("data").GetProperty("furnace").GetProperty("progress").GetDouble()==0.5,"furnace facts unchanged");
if(args.Length>0){
 foreach(var line in File.ReadLines(args[0])){
  using var doc=JsonDocument.Parse(line);var row=doc.RootElement;
  if(row.GetProperty("kind").GetString()!="model_prompt"||row.GetProperty("round").GetInt32()!=2)continue;
  string prompt=row.GetProperty("prompt").GetString()!,text=prompt.Split("OBSERVATION:\n")[1];using var source=JsonDocument.Parse(text);var compact=ToolContextMemory.Compact(source.RootElement);
  Check(compact.GetRawText().Length<text.Length,"real round2 shrinks");
  var recipes=compact.GetProperty("facts").GetProperty("learnedRecipes");
  foreach(var prior in source.RootElement.GetProperty("facts").GetProperty("learnedRecipes").EnumerateArray()){
   var match=recipes.EnumerateArray().Single(x=>x.GetProperty("reference").GetString()==prior.GetProperty("reference").GetString());
   Check(JsonElement.DeepEquals(match.GetProperty("recipe").GetProperty("ingredients"),prior.GetProperty("recipe").GetProperty("ingredients")),"replay all material coordinates intact");
  }
  Console.WriteLine("REPLAY context "+text.Length+" -> "+compact.GetRawText().Length+"; prompt "+prompt.Length+" -> "+(prompt.Length-text.Length+compact.GetRawText().Length));
 }
}
Console.WriteLine("TOTAL "+checks);


void Reject(Action action,Type type,string name){bool rejected=false;try{action();}catch(Exception e){if(e.GetType()!=type)throw;rejected=true;}Check(rejected,name);}
var boundary=E(new JsonObject{["facts"]=new JsonObject{["note"]=new string('x',400)},["recentResults"]=new JsonArray()});
int finalLength=ToolContextMemory.Compact(boundary).GetRawText().Length;
Check(ToolContextMemory.CompactBounded(boundary,finalLength,16000).GetRawText().Length==finalLength,"inclusive exact final character budget");
Reject(()=>ToolContextMemory.CompactBounded(boundary,finalLength-1,16000),typeof(InvalidOperationException),"one character over final budget rejected");
Check(boundary.GetRawText().Length<finalLength,"metadata increases context before budget check");
Reject(()=>ToolContextMemory.CompactBounded(boundary,boundary.GetRawText().Length,16000),typeof(InvalidOperationException),"original-fit metadata overflow rejected");
using var unicodeDoc=JsonDocument.Parse("{\"future\":\""+new string('中',180)+"\"}");
var unicode=unicodeDoc.RootElement;int unicodeChars=unicode.GetRawText().Length,unicodeBytes=System.Text.Encoding.UTF8.GetByteCount(unicode.GetRawText());
Check(unicodeChars<256&&unicodeBytes>256,"multibyte input has independent byte cost");
Reject(()=>ToolContextMemory.CompactBounded(unicode,256,256),typeof(InvalidOperationException),"multibyte utf8 overflow despite character fit rejected");
Check(ToolContextMemory.CompactBounded(unicode,256,unicodeBytes).GetRawText()==unicode.GetRawText(),"inclusive exact utf8 budget");
Reject(()=>ToolContextMemory.CompactBounded(boundary,255,16000),typeof(ArgumentOutOfRangeException),"minimum character parameter");
Reject(()=>ToolContextMemory.CompactBounded(boundary,16001,16000),typeof(ArgumentOutOfRangeException),"maximum character parameter");
Reject(()=>ToolContextMemory.CompactBounded(boundary,14000,255),typeof(ArgumentOutOfRangeException),"minimum byte parameter");
Reject(()=>ToolContextMemory.CompactBounded(boundary,14000,16001),typeof(ArgumentOutOfRangeException),"maximum byte parameter");
Check(ToolContextMemory.CompactBounded(boundary).ValueKind==JsonValueKind.Object,"default budgets accepted");
Console.WriteLine("FINAL TOTAL "+checks);
