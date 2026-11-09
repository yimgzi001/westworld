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
