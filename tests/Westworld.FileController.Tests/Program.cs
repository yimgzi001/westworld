using System.Text.Json;
using Westworld.Decisions.Local;
int passed=0;void Check(bool b,string why){if(!b)throw new Exception(why);passed++;}void Reject(string raw,ToolDecisionPrompt p,Guid s,Guid a,Guid o,Guid r){try{StructuredFileController.Validate(raw,p,s,a,o,r);throw new Exception("accepted invalid");}catch(Exception e)when(e.Message!="accepted invalid"){passed++;}}
Guid s=Guid.NewGuid(),a=Guid.NewGuid(),o=Guid.NewGuid(),r=Guid.NewGuid();
var prompt=new ToolDecisionPrompt(s,a,o,JsonSerializer.SerializeToElement(new{actual=true}),Array.Empty<ToolOffer>());
string Envelope(Guid ss,Guid aa,Guid oo,Guid rr,string decision="""{"goal":"wait","steps":[]}""")=>JsonSerializer.Serialize(new{sessionId=ss,actorId=aa,observationId=oo,requestId=rr,decision=JsonSerializer.Deserialize<JsonElement>(decision)});
string valid=Envelope(s,a,o,r);
Check(StructuredFileController.Validate(valid,prompt,s,a,o,r).Steps.Count==0,"empty valid plan");
foreach(int field in Enumerable.Range(0,4)){var ids=new[]{s,a,o,r};ids[field]=Guid.NewGuid();Reject(Envelope(ids[0],ids[1],ids[2],ids[3]),prompt,s,a,o,r);}
Reject(valid[..^1]+""","code":"run"}""",prompt,s,a,o,r);
Reject(valid[..^1]+",\"requestId\":\""+r+"\"}",prompt,s,a,o,r);
Reject(Envelope(s,a,o,r,"""{"goal":"execute","steps":[{"tool":"shell","arguments":{}}]}"""),prompt,s,a,o,r);
Reject(new string(' ',65537),prompt,s,a,o,r);
string dir=Path.Combine(Path.GetTempPath(),"ww-file-test-"+Guid.NewGuid().ToString("N"));var logs=new List<object>();var controller=new StructuredFileController(dir,x=>{lock(logs)logs.Add(x);});
using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(5));var task=controller.DecideAsync(prompt,s,a,o,JsonSerializer.SerializeToElement(new{actual=true}),Array.Empty<ToolOffer>(),1,cancel.Token);
string request=Directory.GetFiles(dir,"request.json",SearchOption.AllDirectories).Single();using var doc=JsonDocument.Parse(File.ReadAllText(request));Guid id=doc.RootElement.GetProperty("requestId").GetGuid();
Check(doc.RootElement.GetProperty("context").GetProperty("actual").GetBoolean(),"real context retained");
string response=Path.Combine(Path.GetDirectoryName(request)!,"response.json");File.WriteAllText(response,Envelope(s,a,o,Guid.NewGuid()));
await Task.Delay(180);Check(!task.IsCompleted,"wrong id never accepted");
File.WriteAllText(response+".tmp",Envelope(s,a,o,id));File.Move(response+".tmp",response);var result=await task;Check(result.ActorId==a,"correct async response");
Check(File.Exists(Path.Combine(Path.GetDirectoryName(request)!,"accepted.json")),"acceptance durable");
File.WriteAllText(response,Envelope(s,a,o,id));await Task.Delay(150);Check(task.IsCompletedSuccessfully&&Directory.GetFiles(Path.GetDirectoryName(request)!,"*.received.json").Length==2,"accepted request not replayed");
try{new StructuredFileController(dir,_=>{});throw new Exception("reused");}catch(InvalidOperationException){passed++;}
Console.WriteLine($"PASS {passed}/{passed} file protocol checks; no game/model/native actions");

// Attempt cap includes identical repeats and oversized files; no 33rd claim.
async Task Budget(bool oversized){
 string root=Path.Combine(Path.GetTempPath(),"ww-file-budget-"+Guid.NewGuid().ToString("N"));
 var events=new System.Collections.Concurrent.ConcurrentQueue<JsonElement>();var bridge=new StructuredFileController(root,x=>events.Enqueue(JsonSerializer.SerializeToElement(x)));
 using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(15));var pending=bridge.DecideAsync(prompt,s,a,o,JsonSerializer.SerializeToElement(new{}),Array.Empty<ToolOffer>(),2,cts.Token);
 string sub=Path.GetDirectoryName(Directory.GetFiles(root,"request.json",SearchOption.AllDirectories).Single())!;string responsePath=Path.Combine(sub,"response.json");
 for(int i=0;i<33&&!pending.IsCompleted;i++){
  if(!File.Exists(responsePath)){File.WriteAllText(responsePath+".tmp",oversized?new string('x',65537):"{}");File.Move(responsePath+".tmp",responsePath);}
  for(int spin=0;spin<50&&File.Exists(responsePath)&&!pending.IsCompleted;spin++)await Task.Delay(10);
 }
 try{await pending;throw new Exception("budget did not stop");}catch(InvalidDataException){}
 Check(Directory.GetFiles(sub,"*.received.json").Length==32,"bounded claimed files");
 Check(!File.Exists(Path.Combine(sub,"accepted.json")),"reject budget no accept");
 Check(events.Any(e=>e.GetProperty("kind").GetString()=="controller_request_failed"),"explicit budget terminal");
 var publishedEvent=events.First(e=>e.GetProperty("kind").GetString()=="controller_request_published");
 Check(publishedEvent.GetProperty("monotonicTicks").GetInt64()>0&&publishedEvent.GetProperty("monotonicFrequency").GetInt64()>0,"monotonic publication");
}
await Budget(false);await Budget(true);
string cancelRoot=Path.Combine(Path.GetTempPath(),"ww-file-cancel-"+Guid.NewGuid().ToString("N"));
var cb=new StructuredFileController(cancelRoot,_=>{});using var cs=new CancellationTokenSource();
var ct=cb.DecideAsync(prompt,s,a,o,JsonSerializer.SerializeToElement(new{}),Array.Empty<ToolOffer>(),0,cs.Token);cs.Cancel();
try{await ct;throw new Exception("cancel failed");}catch(OperationCanceledException){Check(true,"cancel pending request");}
Check(!Directory.GetFiles(cancelRoot,"accepted.json",SearchOption.AllDirectories).Any(),"canceled not accepted");
string faultRoot=Path.Combine(Path.GetTempPath(),"ww-file-fault-"+Guid.NewGuid().ToString("N"));var fb=new StructuredFileController(faultRoot,_=>{});Directory.Delete(faultRoot);File.WriteAllText(faultRoot,"not directory");
try{await fb.DecideAsync(prompt,s,a,o,JsonSerializer.SerializeToElement(new{}),Array.Empty<ToolOffer>(),0,default);throw new Exception("fault accepted");}catch(IOException){Check(true,"publication fault terminal");}
Console.WriteLine($"PASS {passed}/{passed} complete file protocol checks including attempt cap/cancel/publication failure");

// Host identity must match the published envelope and remain retired forever.
string hostRoot=Path.Combine(Path.GetTempPath(),"ww-file-host-"+Guid.NewGuid().ToString("N"));
var host=new StructuredFileController(hostRoot,_=>{});Guid hostId=Guid.NewGuid();
var hp=host.DecideAsync(prompt,s,a,o,JsonSerializer.SerializeToElement(new{}),Array.Empty<ToolOffer>(),0,default,requestId:hostId,remainingBudget:TimeSpan.FromSeconds(2));
string hr=Directory.GetFiles(hostRoot,"request.json",SearchOption.AllDirectories).Single();
using(var hj=JsonDocument.Parse(File.ReadAllText(hr))){Check(hj.RootElement.GetProperty("requestId").GetGuid()==hostId,"host id preserved");Check(hj.RootElement.GetProperty("budgetSeconds").GetDouble()==2,"host budget published");}
File.WriteAllText(Path.Combine(Path.GetDirectoryName(hr)!,"response.json"),Envelope(s,a,o,hostId));await hp;
try{await host.DecideAsync(prompt,s,a,o,JsonSerializer.SerializeToElement(new{}),Array.Empty<ToolOffer>(),0,default,hostId);throw new Exception("reused host id");}catch(InvalidOperationException){Check(true,"host id cannot reuse");}
foreach(var bad in new[]{TimeSpan.Zero,TimeSpan.FromSeconds(-1),TimeSpan.FromSeconds(120.01)}){
 try{await host.DecideAsync(prompt,s,a,o,JsonSerializer.SerializeToElement(new{}),Array.Empty<ToolOffer>(),0,default,Guid.NewGuid(),bad);throw new Exception("bad budget accepted");}catch(ArgumentOutOfRangeException){Check(true,"invalid budget rejected");}
}
try{await host.DecideAsync(prompt,s,a,o,JsonSerializer.SerializeToElement(new{}),Array.Empty<ToolOffer>(),0,default,Guid.Empty);throw new Exception("empty host id");}catch(ArgumentException){Check(true,"empty id rejected");}
Guid expiredId=Guid.NewGuid();var elapsed=System.Diagnostics.Stopwatch.StartNew();
try{await host.DecideAsync(prompt,s,a,o,JsonSerializer.SerializeToElement(new{}),Array.Empty<ToolOffer>(),0,default,expiredId,TimeSpan.FromMilliseconds(30));throw new Exception("deadline ignored");}catch(OperationCanceledException){Check(elapsed.Elapsed<TimeSpan.FromSeconds(2),"short remaining budget expires");}
try{await host.DecideAsync(prompt,s,a,o,JsonSerializer.SerializeToElement(new{}),Array.Empty<ToolOffer>(),0,default,expiredId);throw new Exception("expired id reused");}catch(InvalidOperationException){Check(true,"expired id retired");}
string slowRoot=Path.Combine(Path.GetTempPath(),"ww-file-slow-"+Guid.NewGuid().ToString("N"));
var slow=new StructuredFileController(slowRoot,item=>{var j=JsonSerializer.SerializeToElement(item);if(j.GetProperty("kind").GetString()=="controller_response_received")Thread.Sleep(80);});
Guid slowId=Guid.NewGuid();var sp=slow.DecideAsync(prompt,s,a,o,JsonSerializer.SerializeToElement(new{}),Array.Empty<ToolOffer>(),0,default,slowId,TimeSpan.FromMilliseconds(160));
string sr=Directory.GetFiles(slowRoot,"request.json",SearchOption.AllDirectories).Single();File.WriteAllText(Path.Combine(Path.GetDirectoryName(sr)!,"response.json"),Envelope(s,a,o,slowId));
try{await sp;throw new Exception("late return");}catch(OperationCanceledException){Check(!Directory.GetFiles(slowRoot,"accepted.json",SearchOption.AllDirectories).Any(),"late callback cannot return plan");}
Console.WriteLine($"PASS {passed}/{passed} file controller checks with host identity/deadline");

string canceledMarker=Directory.GetFiles(cancelRoot,"canceled.json",SearchOption.AllDirectories).Single();
using(var marker=JsonDocument.Parse(File.ReadAllText(canceledMarker))){Check(marker.RootElement.GetProperty("reason").GetString()=="canceled","explicit cancel marker");Check(marker.RootElement.EnumerateObject().Count()==4,"marker no prompt/context");}
string expiredDir=Directory.GetDirectories(hostRoot).Single(p=>p.EndsWith(expiredId.ToString("N")));
using(var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(expiredDir,"canceled.json"))))Check(marker.RootElement.GetProperty("requestId").GetGuid()==expiredId&&marker.RootElement.GetProperty("reason").GetString()=="deadline_expired","deadline marker bound identity");
using(var requestDoc=JsonDocument.Parse(File.ReadAllText(hr))){var pub=requestDoc.RootElement.GetProperty("publishedUtc").GetDateTimeOffset();var end=requestDoc.RootElement.GetProperty("deadlineUtc").GetDateTimeOffset();Check(end>pub&&end-pub<=TimeSpan.FromSeconds(2),"conservative UTC hint");}
string markerFaultRoot=Path.Combine(Path.GetTempPath(),"ww-file-markerfault-"+Guid.NewGuid().ToString("N"));var markerFault=new StructuredFileController(markerFaultRoot,_=>{});using var markerCancel=new CancellationTokenSource();
var markerTask=markerFault.DecideAsync(prompt,s,a,o,JsonSerializer.SerializeToElement(new{}),Array.Empty<ToolOffer>(),0,markerCancel.Token);
string markerDir=Directory.GetDirectories(markerFaultRoot).Single();Directory.CreateDirectory(Path.Combine(markerDir,"canceled.tmp"));markerCancel.Cancel();
try{await markerTask;throw new Exception("cancel swallowed");}catch(OperationCanceledException){Check(true,"marker write failure preserves cancellation");}
Console.WriteLine($"PASS {passed}/{passed} file controller checks including lifecycle markers");
