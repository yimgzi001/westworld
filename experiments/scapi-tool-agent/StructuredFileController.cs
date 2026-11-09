using System.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Westworld.Decisions.Local;

/// <summary>Data-only bridge. Never interprets code or dispatches native actions.</summary>
public sealed class StructuredFileController {
 readonly string root;readonly Action<object> log;long ordinal;
 public StructuredFileController(string directory,Action<object> audit){root=Path.GetFullPath(directory);log=audit;if(Directory.Exists(root))throw new InvalidOperationException("Fresh control directory required");Directory.CreateDirectory(root);}
 public async Task<ToolDecisionPlan> DecideAsync(ToolDecisionPrompt prompt,Guid session,Guid actor,Guid observation,JsonElement context,object offers,double gameTime,CancellationToken cancellation){
  cancellation.ThrowIfCancellationRequested();
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromSeconds(120));cancellation=deadline.Token;
  Guid request=Guid.NewGuid();long n=Interlocked.Increment(ref ordinal);string dir=Path.Combine(root,$"{n:D4}-{request:N}");Directory.CreateDirectory(dir);
  var published=DateTimeOffset.UtcNow;var payload=new{protocol="westworld-tool-file-v1",sessionId=session,actorId=actor,observationId=observation,requestId=request,publishedUtc=published,publishedGameTime=gameTime,context,offers,prompt=prompt.Prompt,responseSchema=JsonDocument.Parse(prompt.ResponseSchema).RootElement.Clone(),responseFormat=new{sessionId=session,actorId=actor,observationId=observation,requestId=request,decision=new{goal="your short goal",steps=Array.Empty<object>()}}};
  string tmp=Path.Combine(dir,"request.tmp"),ready=Path.Combine(dir,"request.json");File.WriteAllText(tmp,JsonSerializer.Serialize(payload),new UTF8Encoding(false));File.Move(tmp,ready);
  long publishedTicks=Stopwatch.GetTimestamp();
  log(new{kind="controller_request_published",controller="file",requestId=request,ordinal=n,sessionId=session,actorId=actor,observationId=observation,wallUtc=published,monotonicTicks=publishedTicks,monotonicFrequency=Stopwatch.Frequency,gameTime,requestPath=ready});
  string response=Path.Combine(dir,"response.json");int rejected=0;var seen=new HashSet<string>();
  try{while(true){cancellation.ThrowIfCancellationRequested();
    if(rejected>=32){log(new{kind="controller_request_failed",requestId=request,reason="response_attempt_budget_exhausted",receivedAttempts=rejected,monotonicTicks=Stopwatch.GetTimestamp(),elapsedSeconds=Stopwatch.GetElapsedTime(publishedTicks).TotalSeconds});throw new InvalidDataException("Response attempt budget exhausted");}
    if(File.Exists(response)){
    long responseObservedTicks=Stopwatch.GetTimestamp();
    // Claim once by rename. Writers should atomically rename response.tmp -> response.json.
    string claimed=Path.Combine(dir,$"response-{rejected+1:D4}.received.json");
    try{File.Move(response,claimed);rejected++;}catch(IOException){await Task.Delay(50,cancellation);continue;}
    try{var info=new FileInfo(claimed);if(info.Length>65536)throw new InvalidDataException("Response exceeds 64KiB");
     string raw=await ReadBoundedAsync(claimed,cancellation);var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));if(!seen.Add(hash))continue;var result=Validate(raw,prompt,session,actor,observation,request);
     cancellation.ThrowIfCancellationRequested();
     File.WriteAllText(Path.Combine(dir,"accepted.json"),JsonSerializer.Serialize(new{requestId=request,responseFile=Path.GetFileName(claimed),acceptedUtc=DateTimeOffset.UtcNow,monotonicTicks=Stopwatch.GetTimestamp()}));
     log(new{kind="controller_response_received",controller="file",requestId=request,wallUtc=DateTimeOffset.UtcNow,waitSeconds=Stopwatch.GetElapsedTime(publishedTicks).TotalSeconds,monotonicTicks=Stopwatch.GetTimestamp(),responseObservedTicks,monotonicFrequency=Stopwatch.Frequency,responseFile=claimed,decision=result});return result;
    }catch(Exception e)when(e is not OperationCanceledException){log(new{kind="controller_response_rejected",requestId=request,wallUtc=DateTimeOffset.UtcNow,responseFile=claimed,monotonicTicks=Stopwatch.GetTimestamp(),error=e.Message});}
   }await Task.Delay(100,cancellation);}
  }catch(OperationCanceledException){log(new{kind="controller_request_canceled",requestId=request,wallUtc=DateTimeOffset.UtcNow,monotonicTicks=Stopwatch.GetTimestamp(),elapsedSeconds=Stopwatch.GetElapsedTime(publishedTicks).TotalSeconds});throw;}
 }
 static async Task<string> ReadBoundedAsync(string path,CancellationToken cancellation){
  // Read bound is enforced during IO, not just via racy pre-read FileInfo.Length.
  await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,4096,true);
  byte[] bytes=new byte[65537];int count=0;
  while(count<bytes.Length){int n=await file.ReadAsync(bytes.AsMemory(count),cancellation);if(n==0)break;count+=n;}
  if(count>65536)throw new InvalidDataException("Response exceeds 64KiB");
  return new UTF8Encoding(false,true).GetString(bytes,0,count);
 }
 public static ToolDecisionPlan Validate(string raw,ToolDecisionPrompt prompt,Guid session,Guid actor,Guid observation,Guid request){
  if(Encoding.UTF8.GetByteCount(raw)>65536)throw new InvalidDataException("Response exceeds 64KiB");
  using var doc=JsonDocument.Parse(raw,new JsonDocumentOptions{MaxDepth=32});var e=doc.RootElement;
  if(e.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Envelope object required");
  var fields=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject())if(!fields.Add(p.Name)||p.Name is not("sessionId" or "actorId" or "observationId" or "requestId" or "decision"))throw new InvalidDataException("Unknown or duplicate envelope field");
  if(fields.Count!=5)throw new InvalidDataException("All five envelope fields required");
  foreach(var p in new[]{("sessionId",session),("actorId",actor),("observationId",observation),("requestId",request)})
   if(!e.GetProperty(p.Item1).TryGetGuid(out var id)||id==Guid.Empty||id!=p.Item2)throw new InvalidDataException("Stale or mismatched "+p.Item1);
  return prompt.ParseResponse(e.GetProperty("decision").GetRawText());
 }
}
