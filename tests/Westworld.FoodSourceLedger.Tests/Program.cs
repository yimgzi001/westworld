int n=0;void Check(bool p,string message){n++;if(!p)throw new Exception(message);}
var source=Guid.NewGuid();var actor=Guid.NewGuid();
FoodSourceLedger Ledger(int count=1,int baseline=0)=>new(source,actor,89,count,baseline,0,0);
FoodSourceTransfer Transfer(int before=1,int after=0,int? invBefore=0,int? invAfter=1,double t=1)=>new(source,actor,89,before,after,invBefore,invAfter,"automatic",t,t);
var ok=Ledger();ok.Transfer(Transfer());Check(ok.State.Acquired,"native target acquired");Check(ok.State.ExpectedInventory==1,"actual count");ok.Observe(89,0,1,0,1.1);Check(!ok.State.Unknown,"source and inventory conserved");
ok.FoodEaten(89,1,1,2);ok.Observe(89,0,1,0,2.01);Check(ok.State.ConsumptionPending,"event before decrement");ok.Observe(89,0,0,0,2.02);Check(ok.State.ConsumedCount==1&&!ok.State.Unknown,"native eat decrement");
var remote=Ledger();remote.Transfer(Transfer() with{SourceId=Guid.NewGuid(),ActorId=Guid.NewGuid()});remote.Observe(89,1,0,0,2);Check(!remote.State.Unknown&&!remote.State.Acquired,"remote same value harmless");
var competitor=Ledger();competitor.Transfer(Transfer() with{SourceId=Guid.NewGuid()});Check(competitor.State.Unknown,"actor competing source unknown");
var missing=Ledger();missing.Observe(89,0,1,0,1);Check(missing.State.Unknown&&!missing.State.Acquired,"missing event cannot infer ownership");
var unknownInventory=Ledger();unknownInventory.Observe(89,1,1,0,1);Check(unknownInventory.State.Unknown,"unknown inventory growth");
var changed=Ledger();changed.Observe(89+262144,1,0,0,1);Check(changed.State.Unknown,"full value metadata change");
var listener=Ledger();listener.Observe(89,1,0,1,1);Check(listener.State.Unknown,"listener failure detected");
var part=Ledger(3);part.Transfer(Transfer(3,2,0,1));Check(!part.State.Acquired&&part.State.AcquiredCount==1,"partial not complete");part.Transfer(Transfer(2,0,1,3,2));part.Observe(89,0,3,0,2.1);Check(part.State.Acquired&&!part.State.Unknown,"partial transfers conserve total");
Check(Ledger(baseline:1).State.Unknown,"birth inventory not new food");
var other=Ledger();other.Transfer(Transfer() with{ActorId=Guid.NewGuid()});Check(other.State.Unknown,"target given to different actor");
foreach(var bad in new[]{Transfer(invBefore:null),Transfer(invAfter:null),Transfer(invAfter:2),Transfer(after:1),Transfer() with{Method="invented"},Transfer() with{Value=162},Transfer(t:double.NaN)}){var x=Ledger();x.Transfer(bad);Check(x.State.Unknown,"invalid transfer fail closed");}
var repeat=Ledger();repeat.Transfer(Transfer());repeat.Transfer(Transfer(t:2));Check(repeat.State.Unknown,"duplicate cannot replay acquisition");
var lost=Ledger();lost.Transfer(Transfer());lost.Observe(89,0,0,0,2);Check(lost.State.Unknown,"decrease without FoodEaten unknown");
var timeout=Ledger();timeout.Transfer(Transfer());timeout.FoodEaten(89,1,1,2);timeout.Observe(89,0,1,0,2.51);Check(timeout.State.Unknown,"eat without decrement expires");
var initialFish=Ledger();initialFish.FoodEaten(162,1,1,1);initialFish.Observe(89,1,0,0,2);Check(!initialFish.State.Acquired&&!initialFish.State.Unknown,"initial fish ignored not new meal");
var latched=Ledger();latched.Observe(89,1,1,0,1);latched.Transfer(Transfer(t:2));Check(latched.State.Unknown&&!latched.State.Acquired,"unknown never clears");
var nativeEat=Ledger();nativeEat.Transfer(Transfer());nativeEat.FoodEaten(89,1,0,2);nativeEat.Observe(89,0,0,0,2.1);Check(nativeEat.State.ConsumedCount==1&&!nativeEat.State.Unknown,"native removes slot before FoodEaten");
var disappeared=Ledger();disappeared.Observe(89,1,0,0,1,sourceAvailable:false);Check(disappeared.State.Unknown&&!disappeared.State.Acquired,"source disappears without transfer");
Console.WriteLine($"PASS {n}/{n} pure source ledger checks; no native world executed");
