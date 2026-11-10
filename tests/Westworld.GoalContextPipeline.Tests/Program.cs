using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Westworld.Runtime.Core;
int count=0;void Check(bool b){if(!b)throw new Exception("pipeline "+count);count++;}
var options=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)}};
var goal=Guid.NewGuid();var evidence=Guid.NewGuid();var observation=Guid.NewGuid();
var input=new GoalItemInput(226,0,1,.05f,1,0,true,false,ImmutableArray.Create(evidence));
var budget=new GoalBudgetInput(7,null,8,100,150,null,false,null,null,null,false);
var progress=GoalExecutionProjection.Build(goal,"Get new food and survive",null,observation,100,true,true,new[]{input,new GoalItemInput(162,1,1,.2f,0,0,false,false,ImmutableArray<Guid>.Empty)},budget);
var oldDelta=JsonSerializer.SerializeToElement(new{inventoryDelta=Array.Empty<object>(),status="succeeded"});
foreach(var phase in new[]{"prepared_not_sent","validation_only_no_new_request"}){
 var facts=JsonSerializer.SerializeToElement(new{goalExecution=new{progress,requestLifecycle=phase,remainingBeforeDispatch=1},outcomeFeedback=oldDelta,inventory=new{slots=new[]{new{value=226,count=1}},emptySlotIds=new[]{1,2,3}}},options);
 var projected=ToolResultProjection.BuildContext(facts,Array.Empty<JsonElement>());
 Check(projected.GetProperty("projectionStatus").GetString()!="facts_too_large");
 var final=ToolContextMemory.CompactBounded(projected);var preserved=final.GetProperty("facts").GetProperty("goalExecution");
 Check(preserved.GetRawText()==facts.GetProperty("goalExecution").GetRawText());
 Check(final.GetProperty("facts").GetProperty("outcomeFeedback").GetRawText()==oldDelta.GetRawText());
 Check(preserved.GetProperty("progress").GetProperty("budget").GetProperty("cancelledRequests").ValueKind==JsonValueKind.Null);
 Check(preserved.GetProperty("progress").GetProperty("budget").GetProperty("reportedInputTokens").ValueKind==JsonValueKind.Null);
 Check(!final.GetRawText().Contains("sourceId"));
}
var late=GoalExecutionProjection.Build(goal,"Get new food and survive",null,Guid.NewGuid(),101,true,true,new[]{input with{CurrentCount=0}},budget with{Now=101});
Check(late.Items[0].NewSourceVerified==GoalEvidenceState.Verified&&late.Items[0].CurrentlyOwnedEdible==GoalEvidenceState.NotYet&&late.Items[0].MealVerified==GoalEvidenceState.NotYet);
Console.WriteLine($"PASS {count}; synthetic pipeline only");
var replays=new List<(string Name,JsonElement Context)>();
if(args.Length>0){foreach(string path in args){using var doc=JsonDocument.Parse(File.ReadAllText(path));replays.Add((path,doc.RootElement.GetProperty("context").Clone()));}}
else{using var envelope=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures/stage34-contexts.json")));byte[] compressed=Convert.FromBase64String(envelope.RootElement.GetProperty("gzipBase64").GetString()!);using var zip=new System.IO.Compression.GZipStream(new MemoryStream(compressed),System.IO.Compression.CompressionMode.Decompress);using var bytes=new MemoryStream();zip.CopyTo(bytes);byte[] rawBytes=bytes.ToArray();Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rawBytes)).ToLowerInvariant()==envelope.RootElement.GetProperty("sha256").GetString());using var entries=JsonDocument.Parse(rawBytes);foreach(var entry in entries.RootElement.EnumerateArray())replays.Add((entry.GetProperty("case").GetString()!,entry.GetProperty("context").Clone()));}
foreach(var replay in replays){string path=replay.Name;var context=replay.Context;var factsNode=System.Text.Json.Nodes.JsonNode.Parse(context.GetProperty("facts").GetRawText())!.AsObject();factsNode["goalExecution"]=JsonSerializer.SerializeToNode(new{progress=GoalContextPresentation.Progress(progress),budget=progress.Budget,requestLifecycle="prepared_not_sent",remainingBeforeDispatch=1,thisPreparedRequestWillCountIfSubmitted=true,preparedRequestUpperBoundSeconds=45,requestOrdinal=(int?)null},options);
 var candidate=ToolResultProjection.BuildContext(JsonSerializer.SerializeToElement(factsNode),context.GetProperty("recentResults").EnumerateArray().Select(x=>x.Clone()).ToArray(),maxChars:13600,maxUtf8Bytes:15600);
 if(candidate.GetProperty("projectionStatus").GetString()=="facts_too_large")throw new Exception("Historical facts plus representative goal summary exceed budget: "+path);
 var compact=ToolContextMemory.CompactBounded(candidate);Check(compact.GetProperty("facts").TryGetProperty("goalExecution",out _));Console.WriteLine("REPLAY_BUDGET "+Path.GetFileName(Path.GetDirectoryName(path))+" chars="+compact.GetRawText().Length);
}
Console.WriteLine("PIPELINE FINAL "+count);
