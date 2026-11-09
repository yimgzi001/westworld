using System.Text.Json;
using Game.Westworld;

int checks=0;
void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
string root=Path.Combine(Path.GetTempPath(),"westworld-ledger-public-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Console.WriteLine("Evidence directory: "+root);
var source=new NoNetworkSource();
WorldCognitionScheduler Open(string path)=>new(source,"system:"+path,1,10000,1,1,"synthetic-v1");
string Seed(string name){
 string path=Path.Combine(root,name);Directory.CreateDirectory(path);
 var attempts=new[]{
 new {id="done",batchId="turn1",actorId=1,number=1,reserved=100,state="completed",sentAt=DateTimeOffset.UtcNow,charge=30},
 new {id="pending",batchId="turn2",actorId=2,number=1,reserved=200,state="inFlight",sentAt=DateTimeOffset.UtcNow,charge=0},
 new {id="never-sent",batchId="turn3",actorId=3,number=1,reserved=100,state="unsent",sentAt=DateTimeOffset.UtcNow,charge=0}};
 File.WriteAllText(Path.Combine(path,"ledger.json"),JsonSerializer.Serialize(new{format=1,experimentId="synthetic",budget=10000,inputPrice=1,outputPrice=1,priceVersion="synthetic-v1",sourceIdentity=typeof(NoNetworkSource).FullName,parallelism=1,batch=(object?)null,attempts}));return path;
}
async Task<WorldCognitionScheduler> Reopen(string path){
 var deadline=DateTime.UtcNow.AddSeconds(8);
 while(true){try{return Open(path);}catch(IOException) when(DateTime.UtcNow<deadline){await Task.Delay(10);}}
}
string good=Seed("recover");
var scheduler=Open(good);
bool locked=false;try{using var duplicate=Open(good);}catch(IOException){locked=true;}Check(locked,"exclusive ledger ownership");
using(var report=JsonDocument.Parse(await scheduler.RecoverObservationLedgerAsync().WaitAsync(TimeSpan.FromSeconds(8)))){
 var r=report.RootElement;Check(r.GetProperty("attempts").GetInt32()==3,"no new attempts");Check(r.GetProperty("spent").GetInt64()==30,"known costs preserved");Check(r.GetProperty("unknown").GetInt64()==200,"inflight liability preserved");}
using(var durable=JsonDocument.Parse(File.ReadAllText(Path.Combine(good,"ledger.json")))){
 var a=durable.RootElement.GetProperty("attempts");Check(a[0].GetProperty("state").GetString()=="completed","completed stays completed");Check(a[1].GetProperty("state").GetString()=="unknown","awaited recovery has durable unknown");Check(a[2].GetProperty("state").GetString()=="unsent","unsent stays unsent");Check(a[1].GetProperty("id").GetString()=="pending","attempt identity preserved");}
Check(source.Calls==0,"recovery never calls decision source");
bool repeated=false;try{_ = scheduler.RecoverObservationLedgerAsync();}catch(InvalidOperationException){repeated=true;}Check(repeated,"same instance recovery cannot restart");
scheduler.Dispose();scheduler.Dispose();
using(var reopened=await Reopen(good)){
 string before=File.ReadAllText(Path.Combine(good,"ledger.json"));await reopened.RecoverObservationLedgerAsync();Check(before==File.ReadAllText(Path.Combine(good,"ledger.json")),"second recovery idempotent bytes");}
string failure=Seed("write-failure");string original=File.ReadAllText(Path.Combine(failure,"ledger.json"));Directory.CreateDirectory(Path.Combine(failure,"ledger.json.tmp"));
var failed=Open(failure);bool faulted=false;
try{await failed.RecoverObservationLedgerAsync().WaitAsync(TimeSpan.FromSeconds(8));}catch(Exception e) when(e is IOException or UnauthorizedAccessException){faulted=true;}
Check(faulted,"background write failure reaches awaited public task");Check(File.ReadAllText(Path.Combine(failure,"ledger.json"))==original,"failed write leaves prior ledger intact");Check(source.Calls==0,"write failure does not send model request");failed.Dispose();
using(var afterFailure=await Reopen(failure)){Check(true,"dispose releases lock even after failed background write");}
string closing=Seed("dispose-during-recovery");var closingScheduler=Open(closing);var pending=closingScheduler.RecoverObservationLedgerAsync();closingScheduler.Dispose();await pending.WaitAsync(TimeSpan.FromSeconds(8));
using(var afterClose=await Reopen(closing)){Check(true,"dispose waits asynchronously for recovery then releases lock");}
string fresh=Path.Combine(root,"no-world");using(var noWorld=Open(fresh)){
 bool rejected=false;try{noWorld.StartObservation();}catch(InvalidOperationException){rejected=true;}Check(rejected,"unbound observation cannot start");Check(source.Calls==0,"no world no dispatch");}
Console.WriteLine($"{checks}/{checks} public-API offline ledger assertions passed. No world, model, GUI, or network; live send/settle confirmation ordering NOT exercised.");
sealed class NoNetworkSource:IActorDecisionSource{
 public int Calls;
 public Task<ActorModelReply> RequestAsync(string contextJson,string toolsJson,CancellationToken token){Interlocked.Increment(ref Calls);throw new InvalidOperationException("No network source may be invoked by these tests");}
}
