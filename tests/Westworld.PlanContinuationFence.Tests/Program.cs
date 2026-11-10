using System.Text.Json;
int n=0;void Check(bool b,string name){if(!b)throw new Exception(name);n++;}
JsonElement J(string s)=>JsonDocument.Parse(s).RootElement.Clone();
var session=Guid.NewGuid();var actor=Guid.NewGuid();var plan=Guid.NewGuid();var r1=Guid.NewGuid();var r2=Guid.NewGuid();var obs=Guid.NewGuid();
var move=new ContinuationStep("act_MoveTo",J("{\"destination\":\"real-ref\",\"timeout\":4}"));
var pickup=new ContinuationStep("act_PickUp",J("{\"target\":\"real-ref\",\"count\":1}"));
PlanContinuationFence New()=>new(session,actor,plan,2,new[]{move,pickup},45);
ContinuationAdmission Bind(PlanContinuationFence f,int index=0,double now=1)=>f.Bind(session,actor,plan,2,index,index==0?move:pickup,index+10,index==0?r1:r2,now);
ContinuationAdmission End(PlanContinuationFence f,int index=0,string status="succeeded",double now=2)=>f.Observe(session,actor,plan,2,index,index+10,index==0?r1:r2,status,now);
ContinuationAdmission Admit(PlanContinuationFence f,double now=5,bool idle=true,bool settled=true,bool valid=true)=>f.TryAdmit(session,actor,plan,2,obs,Guid.NewGuid(),idle,settled,valid,now);
void Finish(PlanContinuationFence f){Check(Bind(f).Allowed,"bind first");Check(End(f).Allowed,"finish first");Check(Bind(f,1,3).Allowed,"bind second");Check(End(f,1,now:4).Allowed,"finish second");}
var good=New();Check(!Admit(good,0).Allowed,"no predicted success");Check(Bind(good).Allowed,"start first");Check(!End(good,status:"running").Allowed,"running pending");Check(End(good,now:2).Allowed,"first terminal");Check(!Admit(good,2).Allowed,"first success not entire plan");Check(Bind(good,1,3).Allowed,"actual second execution");Check(End(good,1,now:4).Allowed,"second terminal");Check(!Admit(good,5,settled:false).Allowed,"physical settle required");Check(Admit(good,6).Allowed,"full tail success");Check(!Admit(good,7).Allowed,"single admission");
foreach(var status in new[]{"failed","cancelled","skipped","not_executed","stopping"}){var f=New();Bind(f);Check(!End(f,status:status).Allowed,"negative terminal");Check(!End(f,now:3).Allowed,"later success cannot wash failure");Check(!Admit(f).Allowed,"failed tail never admits");}
var changed=New();Check(!changed.Bind(session,actor,plan,2,0,move with{Arguments=J("{\"destination\":\"future-ref\",\"timeout\":4}")},10,r1,1).Allowed,"no changed future ref");
var reused=New();Bind(reused);End(reused);Check(!reused.Bind(session,actor,plan,2,1,pickup,11,r1,3).Allowed,"run reuse rejected");
var seq=New();Bind(seq);End(seq);Check(!seq.Bind(session,actor,plan,2,1,pickup,10,r2,3).Allowed,"receipt reuse rejected");
var backwards=New();Bind(backwards);End(backwards);Check(!backwards.Bind(session,actor,plan,2,1,pickup,9,r2,3).Allowed,"receipt order cannot reverse");
var wrong=New();Bind(wrong);Check(!wrong.Observe(session,actor,plan,2,0,10,r2,"succeeded",2).Allowed,"wrong old run");Check(!End(wrong,now:3).Allowed,"identity replacement permanent");
var order=New();Check(!Bind(order,1).Allowed,"out of order");var parallel=New();Bind(parallel);Check(!Bind(parallel,1,2).Allowed,"no overlapping owner");
var revision=New();Check(!revision.Bind(session,actor,plan,3,0,move,10,r1,1).Allowed,"revision changed");
foreach(var field in new[]{0,1,2}){var f=New();Check(!f.Bind(field==0?Guid.NewGuid():session,field==1?Guid.NewGuid():actor,field==2?Guid.NewGuid():plan,2,0,move,10,r1,1).Allowed,"scope mismatch");}
var late=New();Finish(late);Check(!Admit(late,45).Allowed,"exact deadline rejected");
var fresh=New();Finish(fresh);Check(!fresh.TryAdmit(session,actor,plan,2,obs,obs,true,true,true,5).Allowed,"same observation insufficient");Check(Admit(fresh,6).Allowed,"later real fresh validation possible");
var invalid=New();Finish(invalid);Check(!Admit(invalid,valid:false).Allowed,"stale menu rejection");Check(!Admit(invalid,6).Allowed,"cannot wash rejected validation");
var achieved=New();Bind(achieved);achieved.GoalAlreadySatisfied();Check(!Admit(achieved).Allowed,"auto pickup goal satisfied cancels unnecessary candidate");Check(achieved.CompletedSteps==0,"goal satisfied not fake pickup success");
var clock=New();Bind(clock,now:2);Check(!End(clock,now:1).Allowed,"clock reversal");
var unknown=New();Bind(unknown);Check(!End(unknown,status:"mystery").Allowed,"unknown not success");Check(End(unknown,now:3).Allowed,"unknown not invented failure");
Check(PlanContinuationFence.Fingerprint(move)==PlanContinuationFence.Fingerprint(move with{Arguments=J("{\"timeout\":4,\"destination\":\"real-ref\"}")}),"key order canonical");
bool duplicate=false;try{PlanContinuationFence.Fingerprint(move with{Arguments=J("{\"x\":1,\"x\":2}")});}catch(ArgumentException){duplicate=true;}Check(duplicate,"duplicate JSON refused");
foreach(var count in new[]{0,5}){bool bad=false;try{_ =new PlanContinuationFence(session,actor,plan,2,Enumerable.Repeat(move,count).ToArray(),45);}catch(ArgumentException){bad=true;}Check(bad,"bounded tail");}
Console.WriteLine($"PlanContinuationFence {n}/{n} passed");
