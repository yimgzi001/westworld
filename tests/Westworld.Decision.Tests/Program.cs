using System.Diagnostics;
using Westworld.Runtime.Core;

static class Tests {
 static int checks;
 static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
 static ActorObservation Snapshot() => new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),0,new Position(0,64,0),1,1,[new(0,1,1)],[new(0,64,1,"known",1)]);
 static HighLevelDecision Decision(ActorObservation o) => new(o.SessionId,o.ActorId,o.ObservationId,Guid.NewGuid(),"survive","eat",0,1);
 static (HighLevelDecision? Decision,string? Error) Take(AsyncDecisionScheduler s,Guid actor) {
  var until=Stopwatch.StartNew();while(until.Elapsed.TotalSeconds<4){if(s.TryTake(actor,out var d,out var e))return(d,e);Thread.Sleep(5);}throw new Exception("No result");
 }
 static void Eventually(Func<bool> condition) {var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed.TotalSeconds>4)throw new Exception("Timed out");Thread.Sleep(5);}}
 static void Main() {
  var o=Snapshot();
  using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
   ActorObservation? received=null;
   var provider=new Provider((x,ct)=>{received=x;entered.Set();release.Wait();return Task.FromResult(Decision(x));});
   using var s=new AsyncDecisionScheduler(provider,TimeSpan.FromSeconds(2));var w=Stopwatch.StartNew();
   Check(s.Request(o,out _),"accepted");Check(w.ElapsedMilliseconds<500,"Request does not block on synchronous provider");Check(entered.Wait(2000),"provider started on worker");
   Check(!s.TryTake(o.ActorId,out _,out _),"unfinished result not published");Check(!s.Request(o,out var busy)&&busy=="actor_busy","one request per actor");
   o.Inventory[0]=new(0,99,1);o.Cells[0]=new(0,64,1,"unknown",null);
   Check(received!.Inventory[0].Value==1&&received.Cells[0].Value==1,"snapshot arrays detached");
   release.Set();var result=Take(s,o.ActorId);Check(result.Decision?.ActorId==o.ActorId&&result.Error==null,"delayed valid result");Check(!s.TryTake(o.ActorId,out _,out _),"mailbox consumed once");
  }
  using(var s=new AsyncDecisionScheduler(new Provider((x,ct)=>Task.FromResult(Decision(x) with { ActorId=Guid.NewGuid() })),TimeSpan.FromSeconds(2))){s.Request(o,out _);Check(Take(s,o.ActorId).Error=="invalid_decision_scope","wrong scope rejected");}
  using(var s=new AsyncDecisionScheduler(new Provider((x,ct)=>throw new Exception("private details")),TimeSpan.FromSeconds(2))){s.Request(o,out _);Check(Take(s,o.ActorId).Error=="provider_failed","provider exception sanitized");}
  using(var s=new AsyncDecisionScheduler(new Provider((x,ct)=>Task.FromResult(Decision(x) with {Skill=""})),TimeSpan.FromSeconds(2))){s.Request(o,out _);Check(Take(s,o.ActorId).Error=="invalid_decision_fields","invalid fields rejected");}
  var stuck=new TaskCompletionSource<HighLevelDecision>(TaskCreationOptions.RunContinuationsAsynchronously);var requests=new List<ActorObservation>();int started=0;
  using(var s=new AsyncDecisionScheduler(new Provider((x,ct)=>{lock(requests)requests.Add(x);Interlocked.Increment(ref started);return stuck.Task;}),TimeSpan.FromMilliseconds(100))){
   var a=Snapshot();var b=Snapshot();var c=Snapshot();Check(s.Request(a,out _)&&s.Request(b,out _),"two concurrent slots");Eventually(()=>Volatile.Read(ref started)==2);
   Check(!s.Request(c,out var cap)&&cap=="concurrency_full","third request rejected");
   Check(Take(s,a.ActorId).Error=="deadline_exceeded"&&Take(s,b.ActorId).Error=="deadline_exceeded","timeouts reported without provider completing");
   Check(!s.Request(a,out var same)&&same=="actor_busy","timed out actor remains blocked until provider finishes");
   Check(!s.Request(c,out cap)&&cap=="concurrency_full","cancel-ignoring providers retain capacity");
   stuck.SetResult(Decision(a));Eventually(()=>s.Request(c,out _));
   Check(!s.TryTake(a.ActorId,out _,out _),"late result retired permanently");
  }
  using(var s=new AsyncDecisionScheduler(new Provider((x,ct)=>Task.FromResult(Decision(x))),TimeSpan.FromMilliseconds(100))){var x=Snapshot();s.Request(x,out _);Thread.Sleep(250);var r=Take(s,x.ActorId);Check(r.Decision!=null,"timely completion survives late polling");}
  using(var s=new AsyncDecisionScheduler(new Provider((x,ct)=>Task.FromResult(Decision(x))),TimeSpan.FromSeconds(2))){bool rejected=Task.Run(()=>{try{s.Request(Snapshot(),out _);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult();Check(rejected,"owner thread enforced");}
  using(var s=new AsyncDecisionScheduler(new Provider((x,ct)=>Task.FromResult(Decision(x))),TimeSpan.FromSeconds(2),1,1)){
   var a=Snapshot();s.Request(a,out _);Thread.Sleep(100);
   Check(!s.Request(Snapshot(),out var e)&&e=="mailbox_full","untaken completed results enforce mailbox capacity");Take(s,a.ActorId);
   Check(s.Request(Snapshot(),out _),"taking result frees mailbox capacity");
  }
  using(var startedEvent=new ManualResetEventSlim()){
   var delayed=new TaskCompletionSource<HighLevelDecision>(TaskCreationOptions.RunContinuationsAsynchronously);var a=Snapshot();
   using var s=new AsyncDecisionScheduler(new Provider((x,ct)=>{startedEvent.Set();return delayed.Task;}),TimeSpan.FromMilliseconds(100));
   s.Request(a,out _);Check(startedEvent.Wait(2000),"late provider started");Thread.Sleep(150);delayed.SetResult(Decision(a));Thread.Sleep(30);
   Check(Take(s,a.ActorId).Error=="deadline_exceeded","late completion rejected even before first timeout poll");
  }
  using(var enteredCancel=new ManualResetEventSlim())using(var releaseCancel=new ManualResetEventSlim())using(var startedEvent=new ManualResetEventSlim()){
   var pending=new TaskCompletionSource<HighLevelDecision>(TaskCreationOptions.RunContinuationsAsynchronously);var a=Snapshot();
   using var s=new AsyncDecisionScheduler(new Provider((x,ct)=>{ct.Register(()=>{enteredCancel.Set();releaseCancel.Wait();});startedEvent.Set();return pending.Task;}),TimeSpan.FromMilliseconds(100),1);
   s.Request(a,out _);Check(startedEvent.Wait(2000),"callback test provider started");Thread.Sleep(150);var timer=Stopwatch.StartNew();
   Check(s.TryTake(a.ActorId,out _,out var e)&&e=="deadline_exceeded"&&timer.ElapsedMilliseconds<500,"blocking cancel callback does not block game thread");
   Check(enteredCancel.Wait(2000),"cancel callback dispatched");pending.SetResult(Decision(a));Thread.Sleep(30);
   Check(!s.Request(Snapshot(),out e)&&e=="concurrency_full","unfinished cancel callback retains capacity even after provider return");releaseCancel.Set();
  }
  static HighLevelDecision Forage(ActorObservation x) => new(x.SessionId,x.ActorId,x.ObservationId,Guid.NewGuid(),"SatisfyHunger","ForageOne",-1,0,new CellTarget(0,64,1,1));
  void TargetCase(string name, ActorObservation observation, Func<ActorObservation,HighLevelDecision> make, bool valid=false) {
   using var scheduler=new AsyncDecisionScheduler(new Provider((x,ct)=>Task.FromResult(make(x))),TimeSpan.FromSeconds(2));
   scheduler.Request(observation,out _);var result=Take(scheduler,observation.ActorId);
   Check(valid?result.Decision!=null&&result.Error==null:result.Error=="invalid_decision_target",name);
  }
  TargetCase("known matching forage target accepted",Snapshot(),Forage,true);
  TargetCase("unknown target rejected",Snapshot() with {Cells=[new(0,64,1,"unknown",null)]},Forage);
  TargetCase("unknown label with numeric value rejected",Snapshot() with {Cells=[new(0,64,1,"unknown",1)]},Forage);
  TargetCase("missing target cell rejected",Snapshot() with {Cells=[]},Forage);
  TargetCase("duplicate target evidence rejected",Snapshot() with {Cells=[new(0,64,1,"known",1),new(0,64,1,"known",1)]},Forage);
  TargetCase("full encoded value mismatch rejected",Snapshot(),x=>Forage(x) with {Target=new(0,64,1,1025)});
  TargetCase("forage missing target rejected",Snapshot(),x=>Forage(x) with {Target=null});
  TargetCase("forage wrong goal rejected",Snapshot(),x=>Forage(x) with {Goal="Observe"});
  TargetCase("target on another skill rejected",Snapshot(),x=>Forage(x) with {Skill="EatOne"});
  TargetCase("forage slot ambiguity rejected",Snapshot(),x=>Forage(x) with {Slot=0});
  TargetCase("forage value ambiguity rejected",Snapshot(),x=>Forage(x) with {ExpectedValue=1});
  TargetCase("out of envelope target rejected even when observed",Snapshot() with {Cells=[new(int.MaxValue,64,1,"known",1)]},x=>Forage(x) with {Target=new(int.MaxValue,64,1,1)});
  TargetCase("provider cannot forge observation by editing input arrays",Snapshot() with {Cells=[new(0,64,1,"unknown",null)]},x=>{x.Cells[0]=new(0,64,1,"known",1);return Forage(x);});
  foreach(var target in new[]{new CellTarget(0,-1,0,1),new CellTarget(0,256,0,1),new CellTarget(int.MinValue,64,0,1),new CellTarget(0,64,10_000_001,1)}) {
   bool rejected=false;try{target.Validate();}catch(ArgumentOutOfRangeException){rejected=true;}Check(rejected,"target coordinate bounds "+target);
  }
  new CellTarget(-10_000_000,0,10_000_000,int.MinValue).Validate();Check(true,"coordinate boundary allowed; encoded values retain all native bits");
  Console.WriteLine($"{checks}/{checks} checks passed; deterministic providers only, no native game or network.");
 }
 sealed class Provider(Func<ActorObservation,CancellationToken,Task<HighLevelDecision>> decide):IDecisionProvider { public Task<HighLevelDecision> DecideAsync(ActorObservation o,CancellationToken ct)=>decide(o,ct); }
}
