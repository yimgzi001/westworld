int checks=0;void Check(bool b){if(!b)throw new Exception("check "+checks);checks++;}
var world=Guid.NewGuid();var actor=Guid.NewGuid();var target=new ResourceTarget(world,"block_drop",1,64,2,9,null);
NaturalResourceAcquisitionLedger New()=>new(world,actor,new Dictionary<int,int>(),0,0);
(Guid,ResourceOrigin) Add(NaturalResourceAcquisitionLedger l,double t,int count=2){var evidence=Guid.NewGuid();l.ObserveTarget(evidence,target,t);var o=new ResourceOrigin(Guid.NewGuid(),Guid.NewGuid(),target,9,count,t,t,t);l.Origin(evidence,o);return(evidence,o);}
var l=New();var(a,o)=Add(l,1);Check(!l.State.Unknown);l.Origin(a,o);Check(l.State.Sources.Count==1);
var f=new ResourceTransfer(Guid.NewGuid(),o.SourceId,actor,9,2,1,0,1,"automatic",2);l.Transfer(f);Check(l.State.Sources[0].Acquired==1);Check(l.State.Sources[0].Remaining==1);l.Transfer(f);Check(l.State.ExpectedInventory[9]==1);
var(b,o2)=Add(l,3,1);l.Transfer(new(Guid.NewGuid(),o2.SourceId,actor,9,1,0,1,2,"explicit",4));Check(l.State.ExpectedInventory[9]==2);l.Consume(Guid.NewGuid(),9,1,2,1,true,5);Check(l.State.Consumed[9]==1);Check(!l.State.Unknown);
l.Reconcile(new Dictionary<int,int>{{9,1}},new Dictionary<Guid,(int,int)>{{o.SourceId,(9,1)}},0,6);Check(!l.State.Unknown);
void Bad(Action<NaturalResourceAcquisitionLedger,ResourceOrigin> action){var q=New();var(_,s)=Add(q,1);action(q,s);Check(q.State.Unknown);}
Bad((q,s)=>q.Transfer(new(Guid.NewGuid(),Guid.NewGuid(),actor,9,1,0,0,1,"automatic",2)));
Bad((q,s)=>q.Transfer(new(Guid.NewGuid(),s.SourceId,actor,9,2,1,0,2,"automatic",2)));
Bad((q,s)=>q.Transfer(new(Guid.NewGuid(),s.SourceId,Guid.NewGuid(),9,2,1,0,1,"automatic",2)));
Bad((q,s)=>q.Reconcile(new Dictionary<int,int>{{9,1}},new Dictionary<Guid,(int,int)>{{s.SourceId,(9,2)}},0,2));
Bad((q,s)=>q.Reconcile(new Dictionary<int,int>(),new Dictionary<Guid,(int,int)>{{s.SourceId,(9,2)}},1,2));
Bad((q,s)=>q.Reconcile(new Dictionary<int,int>(),new Dictionary<Guid,(int,int)>(),0,2));
Bad((q,s)=>q.ObserveTarget(Guid.NewGuid(),target,0));
Bad((q,s)=>q.Consume(Guid.NewGuid(),9,1,0,0,true,2));
var missing=New();missing.Origin(Guid.NewGuid(),o);Check(missing.State.Unknown);
var stale=New();stale.ObserveTarget(a,target,0);stale.Origin(a,o with{Time=31});Check(stale.State.Unknown);
var dup=New();var(de,d)=Add(dup,1);dup.Origin(de,d with{Count=3});Check(dup.State.Unknown);
var distant=New();Add(distant,1);distant.Transfer(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),9,1,0,0,1,"automatic",2));Check(!distant.State.Unknown);
var snapshot=l.State;Check(snapshot.Sources.Count==2);Check(snapshot.Consumed[9]==1);
var budget=ToolScenarioBudget.For("natural");Check(budget.MaxRequests==8);Check(budget.WallSeconds==180&&budget.RequestSeconds==45);Check(!budget.CanRequest(8,1));Check(!budget.CanRequest(0,180));Check(ToolScenarioBudget.For("food_drop").MaxRequests==4);
for(int light=0;light<16;light++)Check(NaturalResourceAcquisitionLedger.ObservedBlockIdentity(2|(light<<10)|(321<<14))==(2|(321<<14)));
Check(NaturalResourceAcquisitionLedger.ObservedBlockIdentity(0)==0);
Check(NaturalResourceAcquisitionLedger.ObservedBlockIdentity(int.MaxValue)==(int.MaxValue&~15360));
Check(NaturalResourceAcquisitionLedger.ObservedBlockIdentity(9|(1<<14))!=NaturalResourceAcquisitionLedger.ObservedBlockIdentity(9|(2<<14)));
Console.WriteLine($"PASS {checks}");
