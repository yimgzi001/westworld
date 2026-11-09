using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Westworld.Decisions.Local;

// One historical inference only. No game assembly, tool dispatcher or generated-code runner.
if(args.Length!=4){Console.Error.WriteLine("Usage: LocalContextReplay <llama06-jsonl> <llama-completion> <gguf> <fresh-work-output-dir>");return 2;}
string input=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[3]);
if(!output.Contains(Path.DirectorySeparatorChar+"work"+Path.DirectorySeparatorChar)||Directory.Exists(output)||File.Exists(output))throw new ArgumentException("A fresh directory beneath work is required");
JsonElement? selected=null;int lineCount=0;
foreach(string line in File.ReadLines(input)){
 if(++lineCount>100000||line.Length>1048576)throw new ArgumentException("Input log bound exceeded");
 using var document=JsonDocument.Parse(line);var row=document.RootElement;
 if(row.TryGetProperty("kind",out var kind)&&kind.GetString()=="model_prompt"&&row.GetProperty("round").GetInt32()==2){if(selected is not null)throw new ArgumentException("Ambiguous round 2");selected=row.Clone();}
}
if(selected is null)throw new ArgumentException("Historical model_prompt round 2 missing");
string original=selected.Value.GetProperty("prompt").GetString()!;
string schema=selected.Value.GetProperty("schema").GetString()!;
const string marker="OBSERVATION:\n";
int split=original.IndexOf(marker,StringComparison.Ordinal);
if(split<0||original.IndexOf(marker,split+marker.Length,StringComparison.Ordinal)>=0)throw new ArgumentException("Expected one observation boundary");
using var oldContext=JsonDocument.Parse(original[(split+marker.Length)..]);
var compact=ToolContextMemory.CompactBounded(oldContext.RootElement);
string prompt=original[..(split+marker.Length)]+compact.GetRawText();
string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
Directory.CreateDirectory(output);
void Save(string name,object value)=>File.WriteAllText(Path.Combine(output,name),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
Save("manifest.json",new{kind="historical_context_inference_replay",historicalRound=2,gameActionsDispatched=0,liveObservation=false,goalCompletionClaimed=false,originalPromptChars=original.Length,compactedPromptChars=prompt.Length,originalContextChars=oldContext.RootElement.GetRawText().Length,compactedContextChars=compact.GetRawText().Length,originalPromptSha256=Hash(original),compactedPromptSha256=Hash(prompt),unchangedSchemaSha256=Hash(schema),contextTokens=8192,maxOutputTokens=512,timeoutWallSeconds=120,threads=4,gpuLayers=0,seed=42,temperature=0});
File.WriteAllText(Path.Combine(output,"prompt.txt"),prompt);File.WriteAllText(Path.Combine(output,"response-schema.json"),schema);
var provider=new LlamaProcessProvider(args[1],args[2],_=>false,TimeSpan.FromSeconds(120),contextTokens:8192,maxOutputTokens:512);
string status="not_started";string? error=null;JsonElement? response=null;
try{
 response=await provider.CompleteStructuredAsync(prompt,schema,text=>{
  using var d=JsonDocument.Parse(text);
  if(d.RootElement.ValueKind!=JsonValueKind.Object)throw new FormatException("Expected JSON object");
  return d.RootElement.Clone();
 },CancellationToken.None);
 status="json_response_received";
}catch(Exception e){status="inference_failed";error=e.GetType().Name+": "+e.Message;}
finally{
 Save("result.json",new{status,error,response,rawResponse=provider.LastResponse,audit=provider.LastAudit,metrics=provider.LastMetrics,failure=provider.LastFailure,validation="Original schema supplied to grammar; response JSON parsed only. No live-reference validation or action execution.",gameActionsDispatched=0});
}
Console.WriteLine(JsonSerializer.Serialize(new{status,originalPromptChars=original.Length,compactedPromptChars=prompt.Length,audit=provider.LastAudit}));
return status=="json_response_received"?0:1;
