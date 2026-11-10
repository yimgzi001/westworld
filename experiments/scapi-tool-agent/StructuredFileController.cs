using System.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Westworld.Decisions.Local;

/// <summary>Data-only bridge. Never interprets code or dispatches native actions.</summary>
public sealed class StructuredFileController {
 readonly string root;readonly Action<object> log;readonly int? identityLimit;long ordinal;readonly HashSet<Guid> issuedRequests=new();readonly object requestLock=new();
 public StructuredFileController(string directory,Action<object> audit,int? identityLimit=4096){root=Path.GetFullPath(directory);log=audit;this.identityLimit=identityLimit;if(identityLimit is <=0)throw new ArgumentOutOfRangeException(nameof(identityLimit));if(Directory.Exists(root))throw new InvalidOperationException("Fresh control directory required");Directory.CreateDirectory(root);}
 public async Task<ToolDecisionPlan> DecideAsync(ToolDecisionPrompt prompt,Guid session,Guid actor,Guid observation,JsonElement context,object offers,double gameTime,CancellationToken cancellation,Guid? requestId=null,TimeSpan? remainingBudget=null){
  long startedTicks=Stopwatch.GetTimestamp();
  TimeSpan budget=remainingBudget??TimeSpan.FromSeconds(120);
  if(budget<=TimeSpan.Zero||budget>TimeSpan.FromSeconds(120))throw new ArgumentOutOfRangeException(nameof(remainingBudget));
  if(requestId==Guid.Empty)throw new ArgumentException("Nonempty host request ID required",nameof(requestId));
  cancellation.ThrowIfCancellationRequested();
  CancellationToken callerCancellation=cancellation;
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(budget);cancellation=deadline.Token;
  string CancellationReason()=>ClassifyCancellation(callerCancellation.IsCancellationRequested,deadline.IsCancellationRequested,Stopwatch.GetElapsedTime(startedTicks)>=budget);
  void CheckDeadline(){cancellation.ThrowIfCancellationRequested();if(Stopwatch.GetElapsedTime(startedTicks)>=budget)throw new OperationCanceledException("Controller request deadline expired",cancellation);}
  Guid request=requestId??Guid.NewGuid();
  lock(requestLock){if(identityLimit is int limit&&issuedRequests.Count>=limit)throw new InvalidOperationException("Controller request identity budget exhausted");if(!issuedRequests.Add(request))throw new InvalidOperationException("Controller request ID already used");}
  CheckDeadline();long n=Interlocked.Increment(ref ordinal);string dir=Path.Combine(root,$"{n:D4}-{request:N}");Directory.CreateDirectory(dir);
  long publishedTicks=Stopwatch.GetTimestamp();
  try{
  var published=DateTimeOffset.UtcNow;var deadlineUtc=published+TimeSpan.FromSeconds(Math.Max(0,(budget-Stopwatch.GetElapsedTime(startedTicks)).TotalSeconds));var payload=new{protocol="westworld-tool-file-v1",sessionId=session,actorId=actor,observationId=observation,requestId=request,deadlineUtc,budgetSeconds=budget.TotalSeconds,startedMonotonicTicks=startedTicks,monotonicFrequency=Stopwatch.Frequency,publishedUtc=published,publishedGameTime=gameTime,context,offers,prompt=prompt.Prompt,responseSchema=JsonDocument.Parse(prompt.ResponseSchema).RootElement.Clone(),responseFormat=new{sessionId=session,actorId=actor,observationId=observation,requestId=request,decision=new{goal="your short goal",steps=Array.Empty<object>()}}};
  string tmp=Path.Combine(dir,"request.tmp"),ready=Path.Combine(dir,"request.json");File.WriteAllText(tmp,JsonSerializer.Serialize(payload),new UTF8Encoding(false));try{CheckDeadline();File.Move(tmp,ready);}finally{if(File.Exists(tmp))File.Delete(tmp);}
  publishedTicks=Stopwatch.GetTimestamp();
  log(new{kind="controller_request_published",controller="file",requestId=request,ordinal=n,sessionId=session,actorId=actor,observationId=observation,wallUtc=published,monotonicTicks=publishedTicks,monotonicFrequency=Stopwatch.Frequency,gameTime,requestPath=ready});
  CheckDeadline();
  }catch(OperationCanceledException){WriteCancellation(dir,request,CancellationReason());throw;}
  string response=Path.Combine(dir,"response.json");int rejected=0;var seen=new HashSet<string>();
  try{while(true){CheckDeadline();
    if(rejected>=32){log(new{kind="controller_request_failed",requestId=request,reason="response_attempt_budget_exhausted",receivedAttempts=rejected,monotonicTicks=Stopwatch.GetTimestamp(),elapsedSeconds=Stopwatch.GetElapsedTime(publishedTicks).TotalSeconds});throw new InvalidDataException("Response attempt budget exhausted");}
    if(File.Exists(response)){
    long responseObservedTicks=Stopwatch.GetTimestamp();
    // Claim once by rename. Writers should atomically rename response.tmp -> response.json.
    string claimed=Path.Combine(dir,$"response-{rejected+1:D4}.received.json");
    try{File.Move(response,claimed);rejected++;}catch(IOException){await Task.Delay(50,cancellation);continue;}
    try{var info=new FileInfo(claimed);if(info.Length>65536)throw new InvalidDataException("Response exceeds 64KiB");
     string raw=await ReadBoundedAsync(claimed,cancellation);var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));if(!seen.Add(hash))continue;var result=Validate(raw,prompt,session,actor,observation,request);
     CheckDeadline();
     File.WriteAllText(Path.Combine(dir,"accepted.json"),JsonSerializer.Serialize(new{requestId=request,responseFile=Path.GetFileName(claimed),acceptedUtc=DateTimeOffset.UtcNow,monotonicTicks=Stopwatch.GetTimestamp()}));
     log(new{kind="controller_response_received",controller="file",requestId=request,wallUtc=DateTimeOffset.UtcNow,waitSeconds=Stopwatch.GetElapsedTime(publishedTicks).TotalSeconds,monotonicTicks=Stopwatch.GetTimestamp(),responseObservedTicks,monotonicFrequency=Stopwatch.Frequency,responseFile=claimed,decision=result});
     try{CheckDeadline();}catch(OperationCanceledException){File.Delete(Path.Combine(dir,"accepted.json"));throw;}return result;
    }catch(Exception e)when(e is not OperationCanceledException){log(new{kind="controller_response_rejected",requestId=request,wallUtc=DateTimeOffset.UtcNow,responseFile=claimed,monotonicTicks=Stopwatch.GetTimestamp(),error=e.Message});}
   }await Task.Delay(100,cancellation);}
  }catch(OperationCanceledException){WriteCancellation(dir,request,CancellationReason());log(new{kind="controller_request_canceled",requestId=request,wallUtc=DateTimeOffset.UtcNow,monotonicTicks=Stopwatch.GetTimestamp(),elapsedSeconds=Stopwatch.GetElapsedTime(publishedTicks).TotalSeconds});throw;}
 }
 // Timer cancellation is authoritative even when OS timer granularity fires just
 // before the exact Stopwatch boundary. Never confuse it with caller cancellation.
 public static string ClassifyCancellation(bool callerCanceled,bool linkedCanceled,bool elapsedExpired)=>
  callerCanceled?"canceled":linkedCanceled||elapsedExpired?"deadline_expired":"canceled";
 static void WriteCancellation(string directory,Guid request,string reason){
  // Best effort lifecycle hint for external controllers, never an API cancellation guarantee.
  string temporary=Path.Combine(directory,"canceled.tmp"),ready=Path.Combine(directory,"canceled.json");
  try{File.WriteAllText(temporary,JsonSerializer.Serialize(new{requestId=request,reason,canceledUtc=DateTimeOffset.UtcNow,monotonicTicks=Stopwatch.GetTimestamp()}),new UTF8Encoding(false));File.Move(temporary,ready,true);}catch{ /* preserve the original cancellation even on disk failure */ }
  finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
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
