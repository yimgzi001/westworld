using System.Text.Json;
using Westworld.Runtime.Core;
int count=0;void C(bool condition,string title){if(!condition)throw new Exception(title);count++;}
var session=Guid.NewGuid();var actor=Guid.NewGuid();var plan=Guid.NewGuid();var epoch=Guid.NewGuid();var obs=Guid.NewGuid();
var originalDecision=Guid.NewGuid();var speculativeDecision=Guid.NewGuid();
var move=new ContinuationStep("act_MoveTo",JsonSerializer.SerializeToElement(new{bearing=0,distance=3}));
var wait=new ContinuationStep("act_Wait",JsonSerializer.SerializeToElement(new{duration=.2}));
foreach(bool early in new[]{true,false}){
 var fence=new PlanContinuationFence(session,actor,plan,0,new[]{move,wait},45);var handoff=new AsyncDecisionHandoff<string>(session,actor,epoch);
 var run1=Guid.NewGuid();var run2=Guid.NewGuid();
 C(fence.Bind(session,actor,plan,0,0,move,10,run1,1).Allowed,"first actual bind");
 var lease=handoff.Begin(speculativeDecision,obs,0,10);
 if(early)C(handoff.Receive(lease,"new-plan"),"early candidate buffered");
 C(!handoff.TryAcquire(out _,out _),"never acquire before original tail");
 C(fence.Observe(session,actor,plan,0,0,10,run1,"succeeded",2).Allowed,"movement terminal");
 C(ContinuationScheduling.MayDispatchOriginalTail(true,false,1),"pending prefetch must not block old wait step");
 C(!ContinuationScheduling.IsOrdinaryResponse(true,true),"prefetch cannot enter ordinary response queue");
 C(!fence.TryAdmit(session,actor,plan,0,obs,Guid.NewGuid(),true,true,true,2).Allowed,"first success not complete tail");
 C(fence.Bind(session,actor,plan,0,1,wait,11,run2,3).Allowed,"original queued wait actually binds");
 C(fence.Observe(session,actor,plan,0,1,11,run2,"succeeded",4).Allowed,"old tail fully succeeds");
 C(!fence.TryAdmit(session,actor,plan,0,obs,Guid.NewGuid(),true,false,true,4).Allowed,"not yet physically settled");
 C(handoff.CompletePreceding(10,true),"only now complete original handoff barrier");
 if(!early)C(handoff.Receive(lease,"new-plan"),"late candidate buffered after real tail");
 C(handoff.TryAcquire(out _,out _),"candidate available but not yet validated");C(!handoff.TryTake(out _,out _),"candidate cannot dispatch before fresh validation");
 var fresh=Guid.NewGuid();C(fence.TryAdmit(session,actor,plan,0,obs,fresh,true,true,true,5).Allowed,"fresh actual validation");
 C(handoff.ConfirmValidation(lease.RequestId,fresh,true),"single slot validated");C(handoff.TryTake(out _,out var result)&&result=="new-plan","candidate taken once");C(!handoff.TryTake(out _,out _),"not twice");
}
var ownership=new PlanStepOwnership();ownership.Install(originalDecision,new object[]{move,wait});
C(ownership.For(move)==originalDecision,"original decision id");C(ownership.For(wait)==originalDecision,"new request does not overwrite queued old step id");
var next=new ContinuationStep("act_Wait",wait.Arguments);ownership.Install(speculativeDecision,new object[]{next});C(ownership.For(next)==speculativeDecision,"new accepted plan id");bool oldDenied=false;try{ownership.For(wait);}catch(InvalidOperationException){oldDenied=true;}C(oldDenied,"old plan cannot replay after install");
C(!ContinuationScheduling.MayDispatchOriginalTail(false,false,1),"invalidated fence not claimed active");
foreach(var reason in new[]{"cancelled","skipped","failed"}){
 var f=new PlanContinuationFence(session,actor,plan,0,new[]{move,wait},45);var id=Guid.NewGuid();f.Bind(session,actor,plan,0,0,move,10,id,1);f.Observe(session,actor,plan,0,0,10,id,reason,2);C(!f.TryAdmit(session,actor,plan,0,obs,Guid.NewGuid(),true,true,true,3).Allowed,"failed tail never dispatches candidate");}
var times=new PlanStepOwnership();times.Install(originalDecision,new object[]{move,wait},10,20);
var publishedNewRequestTime=30d;var oldTailDispatch=35d;
C(times.TimingFor(wait).RequestedAt==10&&times.TimingFor(wait).RepliedAt==20,"new request publish cannot replace old tail timing");
C(oldTailDispatch-times.TimingFor(wait).RequestedAt==25&&oldTailDispatch-times.TimingFor(wait).RepliedAt==15,"old tail measured against original decision not speculative request");
times.Install(speculativeDecision,new object[]{next},publishedNewRequestTime,40);
C(41-times.TimingFor(next).RequestedAt==11&&41-times.TimingFor(next).RepliedAt==1,"new candidate timing retained on acceptance");
bool invalidTime=false;try{times.Install(originalDecision,new object[]{move},40,30);}catch(ArgumentException){invalidTime=true;}C(invalidTime,"reverse timing rejected");
Console.WriteLine($"ContinuationScheduling {count}/{count} passed");
