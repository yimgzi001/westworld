using Westworld.Runtime.Core;
int n=0;void Check(bool ok){if(!ok)throw new Exception("check "+n);n++;}void Reject(Action a){try{a();}catch(Exception){n++;return;}throw new Exception("accepted invalid case "+n);}
Guid world=Guid.NewGuid(),sender=Guid.NewGuid(),receiver=Guid.NewGuid(),source=Guid.NewGuid(),origin=Guid.NewGuid();
var spec=new ResourceTaskSpec(world,Guid.NewGuid(),receiver,89,5);var task=new ResourceDeliveryTask(spec);var claimId=Guid.NewGuid();
var claim=task.Claim(claimId,sender);Check(task.Claim(claimId,sender)==claim);Reject(()=>task.Claim(claimId,Guid.NewGuid()));Reject(()=>task.Claim(Guid.NewGuid(),Guid.NewGuid()));
ResourceDeliveryIntent Intent(int count)=>new(Guid.NewGuid(),sender,receiver,source,origin,89,count);
ResourceDeliveryProof Proof(ResourceDeliveryIntent i,int before,int after)=>new(Guid.NewGuid(),i.DeliveryId,source,origin,sender,receiver,89,before,after,before,after,5-before,5-after);
var first=Intent(3);task.Prepare(first);Check(task.Prepare(first)==first);Reject(()=>task.Prepare(first with{Count=2}));Reject(()=>task.Prepare(Intent(1)));
var p1=Proof(first,5,3); // Native partial transfer: asked3, actually moved2.
task.Accept(p1);Check(task.Status.Delivered==2&&!task.Status.Complete);Check(task.Accept(p1)==p1&&task.Status.Delivered==2);Reject(()=>task.Accept(p1 with{NativeEventId=Guid.NewGuid()}));
var second=Intent(3);task.Prepare(second);Reject(()=>task.Accept(Proof(second,5,2))); // source lot cannot replenish
Reject(()=>task.Accept(Proof(second,3,0) with{NativeEventId=p1.NativeEventId}));
Reject(()=>task.Accept(Proof(second,3,0) with{RecipientAfter=99}));Reject(()=>task.Accept(Proof(second,3,0) with{OriginEvidenceId=Guid.NewGuid()}));
var cp=task.Capture("world-save-a");var restored=ResourceDeliveryTask.Restore(cp,world,"world-save-a");
Check(restored.Status.Delivered==2&&restored.Status.NeedsReconciliation&&restored.Status.Pending.Length==1);
Reject(()=>restored.Claim(claimId,sender));Reject(()=>restored.Prepare(Intent(1)));Reject(()=>restored.Accept(Proof(second,3,0)));Reject(()=>restored.Capture("b"));
Reject(()=>restored.Reconcile("wrong",[]));Reject(()=>restored.Reconcile("world-save-a",[]));Check(restored.Status.NeedsReconciliation&&restored.Status.Delivered==2);
var p2=Proof(second,3,0);restored.Reconcile("world-save-a",[p2]);Check(restored.Status.Complete&&restored.Status.Delivered==5&&!restored.Status.NeedsReconciliation);
Check(restored.Accept(p2)==p2&&restored.Status.Delivered==5);Reject(()=>restored.Prepare(Intent(1)));
var again=ResourceDeliveryTask.Restore(restored.Capture("save-b"),world,"save-b");Check(again.Status.NeedsReconciliation);again.Reconcile("save-b",[]);Check(again.Status.Complete);
Reject(()=>ResourceDeliveryTask.Restore(cp,Guid.NewGuid(),"world-save-a"));Reject(()=>ResourceDeliveryTask.Restore(cp,world,"wrong"));
Reject(()=>ResourceDeliveryTask.Restore(cp.Replace("\"Schema\":1","\"Schema\":1,\"Schema\":1"),world,"world-save-a"));
Reject(()=>ResourceDeliveryTask.Restore(cp.Replace("\"Count\":3","\"Count\":-1"),world,"world-save-a"));
var empty=new ResourceDeliveryTask(spec);Reject(()=>empty.Prepare(Intent(1)));Reject(()=>empty.Claim(Guid.NewGuid(),receiver));
Reject(()=>new ResourceDeliveryTask(spec with{RequiredCount=0}));Reject(()=>empty.Accept(p1));
var json=task.Capture("world-save-a");Check(json==cp); // restored instance never mutates original checkpoint.
Reject(()=>new ResourceDeliveryTask(spec with{Value=-1}));
Reject(()=>ResourceDeliveryTask.Restore(cp.Replace("\"Claims\":[", "\"Claims\":[null,"),world,"world-save-a"));
Console.WriteLine($"PASS {n} pure task delivery/recovery checks; no native transfer, disk atomicity or multiplayer claimed");
