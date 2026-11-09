using Westworld.Runtime.Core;
int n=0;void Check(bool x,string s){if(!x)throw new Exception(s);n++;}
void Reject(Action f,string s){try{f();}catch(ArgumentException){n++;return;}catch(InvalidOperationException){n++;return;}throw new Exception(s);}
Guid G()=>Guid.NewGuid();var session=G();var actor=G();var epoch=G();
AsyncDecisionHandoff<string> New(int capacity=4096)=>new(session,actor,epoch,requestCapacity:capacity);
var h=New();var l=h.Begin(G(),G(),0,10);
Check(h.Receive(l,"next"),"early received");Check(!h.TryAcquire(out _,out _),"no early acquire");Check(!h.TryTake(out _,out _),"no early take");Check(!h.ConfirmValidation(l.RequestId,G(),true),"no early validation");
Check(!h.Receive(l,"replacement"),"duplicate ignored");Reject(()=>h.Begin(G(),G(),0),"occupied");Check(!h.CompletePreceding(9,true),"wrong predecessor");Check(h.CompletePreceding(10,true),"success predecessor");Check(h.TryAcquire(out var same,out var value)&&same==l&&value=="next","ready same");Check(!h.TryTake(out _,out _),"explicit validation mandatory");
var observed=G();Check(h.ConfirmValidation(l.RequestId,observed,true),"validate");Check(h.ValidationObservationId==observed,"separate current observation");Check(h.TryTake(out same,out value)&&same!.PlanningObservationId==l.PlanningObservationId,"planning obs retained");Check(!h.TryTake(out _,out _),"take once");Check(!h.Receive(l,"late"),"late ignored");Reject(()=>h.Begin(l.RequestId,G(),0),"id tombstone");
foreach(bool receiveFirst in new[]{false,true}){h=New();l=h.Begin(G(),G(),0,20);if(receiveFirst)h.Receive(l,"x");Check(h.CompletePreceding(20,false),"failed predecessor");Check(!h.Receive(l,"late")&&!h.TryAcquire(out _,out _)&&!h.TryTake(out _,out _),"failed never handoff");}
h=New();l=h.Begin(G(),G(),0,30);Check(h.CompletePreceding(30,true),"terminal before reply");Check(h.Receive(l,"x")&&h.TryAcquire(out _,out _),"late ready");Check(h.ConfirmValidation(l.RequestId,G(),false)&&!h.Occupied,"stale reject retires");
h=New();l=h.Begin(G(),G(),0);foreach(var bad in new[]{l with{SessionId=G()},l with{ActorId=G()},l with{ControllerEpoch=G()},l with{PlanRevision=1},l with{PlanningObservationId=G()},l with{RequestId=G()},l with{PrecedingReceiptSequence=1}})Check(!h.Receive(bad,"x"),"exact binding");
h.Invalidate(1);Check(!h.Receive(l,"late"),"cancel revision");Reject(()=>h.Begin(G(),G(),0),"stale revision");l=h.Begin(G(),G(),1);h.Receive(l,"x");h.ConfirmValidation(l.RequestId,G(),true);h.Invalidate(2);Check(!h.TryTake(out _,out _),"invalidate validated");
l=h.Begin(G(),G(),2);h.SwitchController(G(),2);Check(!h.Receive(l,"late"),"controller change");Reject(()=>h.SwitchController(Guid.Empty,2),"empty epoch");Reject(()=>h.SwitchController(epoch,2),"old epoch reuse forbidden");Reject(()=>h.Invalidate(2),"nonadvancing revision");
h=New(1);l=h.Begin(G(),G(),0);h.Receive(l,"x");h.ConfirmValidation(l.RequestId,G(),true);h.TryTake(out _,out _);Reject(()=>h.Begin(G(),G(),0),"history budget");h.SwitchController(G(),0);Check(h.Begin(G(),G(),0)!=null,"new epoch fresh budget");
h=New();Reject(()=>h.Begin(G(),Guid.Empty,0),"empty obs");Reject(()=>h.Begin(G(),G(),0,0),"zero predecessor");Reject(()=>h.ConfirmValidation(G(),Guid.Empty,true),"no validation obs");
Exception? failure=null;var t=new Thread(()=>{try{h.Begin(G(),G(),0);}catch(Exception e){failure=e;}});t.Start();t.Join();Check(failure is InvalidOperationException,"owner thread");
Console.WriteLine($"PASS {n}/{n} decision handoff tests");
