using System.Text.Json;
int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
var session=Guid.NewGuid();var actor=Guid.NewGuid();var run=Guid.NewGuid();var obs=Guid.NewGuid();
foreach(var kind in Enum.GetValues<PrecedingExecutionKind>()) {
 var tool=kind==PrecedingExecutionKind.NativeMoveTo?"act_MoveTo":"host_navigate_local";
 var b=PrecedingExecutionBinding.Create(session,actor,7,run,kind,tool,JsonSerializer.SerializeToElement(new{distance=3}),"Find resources");
 var moving=new PrecedingExecutionSample(session,actor,7,run,kind,"running",true,false,false,.2);
 var t=new PrecedingExecutionTrack(b);var budget=ToolScenarioBudget.For("food_drop");
 Check(!t.CanPrefetch(0,false,0,0,budget).Allowed,"no state");
 Check(!t.Observe(moving with{Status="idle",RunId=Guid.Empty}).Allowed,"no-run idle rejected without poisoning");
 Check(!t.Observe(moving with{Status="idle"}).Allowed,"idle rejected");
 Check(t.Observe(moving).Allowed,"idle did not poison");
 Check(t.CanPrefetch(0,false,0,0,budget).Allowed,"moving admission");
 Check(!t.CanPrefetch(1,false,0,0,budget).Allowed,"queued pickup prevents prefetch");
 Check(!t.CanPrefetch(0,true,0,0,budget).Allowed,"single request");
 Check(!t.CanPrefetch(0,false,4,0,budget).Allowed,"four total");
 Check(!t.CanPrefetch(0,false,0,180,budget).Allowed,"wall deadline");
 Check(!t.CanPrefetch(0,false,0,double.NaN,budget).Allowed,"finite time");
 t.Observe(moving with{Displacement=.149});Check(!t.CanPrefetch(0,false,0,0,budget).Allowed,"real displacement");
 t.Observe(moving with{Displacement=double.NaN});Check(!t.CanPrefetch(0,false,0,0,budget).Allowed,"invalid distance");
 var success=moving with{Status=kind==PrecedingExecutionKind.NativeMoveTo?"succeeded":"arrived",Moving=false,Settled=true};
 Check(t.CanHandoff(success,obs,Guid.NewGuid(),2,3).Allowed,"success handoff");
 Check(!t.CanHandoff(success,obs,obs,2,3).Allowed,"fresh observation identity");
 Check(!t.CanHandoff(success,obs,Guid.NewGuid(),3,3).Allowed,"late");
 Check(!t.CanHandoff(success with{Settled=false},obs,Guid.NewGuid(),2,3).Allowed,"settle required");
 Check(!t.CanHandoff(moving,obs,Guid.NewGuid(),2,3).Allowed,"running cannot dispatch");
 foreach(var failed in new[]{moving with{Status="failed"},moving with{Status="cancelled"},moving with{CancelRequested=true},moving with{RunId=Guid.NewGuid()},moving with{ActorId=Guid.NewGuid()},moving with{SessionId=Guid.NewGuid()},moving with{ReceiptSequence=8}}) {
  var f=new PrecedingExecutionTrack(b);Check(!f.Observe(failed).Allowed,"failure rejected");
  Check(!f.CanHandoff(success,obs,Guid.NewGuid(),2,3).Allowed,"failure latched");
 }
 var once=new PrecedingExecutionTrack(b);once.Observe(moving);Check(once.TryClaimPrefetchSetup(),"first setup claim");Check(!once.TryClaimPrefetchSetup(),"failed setup cannot retry same run");Check(!once.CanPrefetch(0,false,0,0,budget).Allowed,"claimed setup remains ineligible");
 Check(PrecedingTerminalClassifier.Classify(b,run,"failed")==PrecedingTerminalEvidence.Failed,"failed evidence survives invalidation");
 Check(PrecedingTerminalClassifier.Classify(b,run,"cancelled")==PrecedingTerminalEvidence.Cancelled,"cancel evidence survives invalidation");
 Check(PrecedingTerminalClassifier.Classify(b,Guid.NewGuid(),"succeeded")==PrecedingTerminalEvidence.IdentityMismatch,"replacement never old success");
 Check(PrecedingTerminalClassifier.Classify(b,Guid.NewGuid(),"failed")==PrecedingTerminalEvidence.IdentityMismatch,"replacement never old failure");
 Check(PrecedingTerminalClassifier.Classify(b,Guid.Empty,"failed")==PrecedingTerminalEvidence.Unknown,"missing run not terminal proof");
 Check(PrecedingTerminalClassifier.Classify(b,run,success.Status)==PrecedingTerminalEvidence.Succeeded,"same actual success classified not dispatched");
 Check(PrecedingTerminalClassifier.Classify(b,run,"idle")==PrecedingTerminalEvidence.Unknown,"idle not prior terminal");
 var u=new PrecedingExecutionTrack(b);Check(!u.Observe(moving with{Status="mystery"}).Allowed,"unknown rejected");
 Check(u.Observe(moving).Allowed,"unknown no false failure latch");
}
Console.WriteLine($"PrecedingExecution: {checks}/{checks} passed");
