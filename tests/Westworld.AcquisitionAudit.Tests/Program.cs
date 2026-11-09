using Game.Westworld;
int checks=0;void Check(bool b){if(!b)throw new Exception("check "+checks);checks++;}
var source=new object();var actor=new object();Check(NativeAcquisitionAudit.IdentityFor(source)==NativeAcquisitionAudit.IdentityFor(source));Check(NativeAcquisitionAudit.IdentityFor(actor)!=NativeAcquisitionAudit.IdentityFor(source));
int seen=0;NativeAcquisitionFact observed=null;Action<NativeAcquisitionFact> bad=f=>throw new Exception("listener failure");Action<NativeAcquisitionFact> good=f=>{seen++;observed=f;};
long before=NativeAcquisitionAudit.ListenerFailures;NativeAcquisitionAudit.Recorded+=bad;NativeAcquisitionAudit.Recorded+=good;
try{NativeAcquisitionAudit.Record(source,actor,89,1,0,0,1,"automatic",2);Check(seen==1);Check(NativeAcquisitionAudit.ListenerFailures==before+1);Check(observed.SourceId==NativeAcquisitionAudit.IdentityFor(source));Check(observed.ActorId==NativeAcquisitionAudit.IdentityFor(actor));Check(observed.SourceBefore==1&&observed.SourceAfter==0);Check(observed.InventoryBefore==0&&observed.InventoryAfter==1);Check(observed.Value==89&&observed.Method=="automatic");Check(double.IsFinite(observed.MonotonicSeconds));Check(observed.GameTime==2);Check(NativeAcquisitionAudit.InventoryCount(null,89)==null);}
finally{NativeAcquisitionAudit.Recorded-=bad;NativeAcquisitionAudit.Recorded-=good;}
NativeAcquisitionAudit.Record(source,actor,89,1,0,null,null,"explicit",3);Check(seen==1);Console.WriteLine($"PASS {checks}");
