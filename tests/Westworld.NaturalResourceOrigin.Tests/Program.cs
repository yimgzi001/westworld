using Game.Westworld;
int n=0;void Check(bool v){if(!v)throw new Exception("Failed check "+n);n++;}
object source=new(),world=new(),entity=new(),otherOwner=new();var facts=new List<NaturalResourceOriginFact>();
Action<NaturalResourceOriginFact> good=facts.Add,bad=_=>throw new InvalidOperationException("fixture listener");
NaturalResourceOriginAudit.Recorded+=bad;NaturalResourceOriginAudit.Recorded+=good;
try{
 long failures=NaturalResourceOriginAudit.ListenerFailures;
 NaturalResourceOriginAudit.Record(source,world,"block_drop",262300,2,-2,64,5,123,null,null,4,5);
 Check(facts.Count==1);var f=facts[0];
 Check(f.SourceId==NativeAcquisitionAudit.IdentityFor(source));Check(f.WorldId==NativeAcquisitionAudit.IdentityFor(world));
 Check(f.Kind=="block_drop"&&f.Value==262300&&f.Count==2);Check(f.CellX==-2&&f.CellY==64&&f.CellZ==5&&f.OriginalBlockValue==123);
 Check(f.OwnerEntityId==null&&f.OriginEntityId==null);Check(f.CreationGameTime==4&&f.GameTime==5&&double.IsFinite(f.MonotonicSeconds));
 Check(NaturalResourceOriginAudit.ListenerFailures==failures+1);
 NaturalResourceOriginAudit.Record(source,world,"creature_loot",89,1,null,null,null,null,entity,otherOwner,5,6);
 Check(facts.Count==2);var g=facts[1];Check(g.SourceId==f.SourceId&&g.EventId!=f.EventId);
 Check(g.OriginEntityId==NativeAcquisitionAudit.IdentityFor(entity));Check(g.OwnerEntityId==NativeAcquisitionAudit.IdentityFor(otherOwner));
 Check(g.OwnerEntityId!=g.OriginEntityId);Check(g.CellX==null&&g.OriginalBlockValue==null);
 long before=NaturalResourceOriginAudit.RecordFailures;
 NaturalResourceOriginAudit.Record(null,world,"block_drop",1,1,0,1,2,3,null,null,0,1);
 NaturalResourceOriginAudit.Record(source,null,"block_drop",1,1,0,1,2,3,null,null,0,1);
 NaturalResourceOriginAudit.Record(source,world,"unknown",1,1,0,1,2,3,null,null,0,1);
 NaturalResourceOriginAudit.Record(source,world,"block_drop",1,0,0,1,2,3,null,null,0,1);
 NaturalResourceOriginAudit.Record(source,world,"block_drop",1,1,null,1,2,3,null,null,0,1);
 NaturalResourceOriginAudit.Record(source,world,"creature_loot",1,1,null,null,null,null,null,null,0,1);
 NaturalResourceOriginAudit.Record(source,world,"creature_loot",1,1,0,null,null,null,entity,entity,0,1);
 NaturalResourceOriginAudit.Record(source,world,"block_drop",1,1,0,1,2,3,null,null,2,1);
 NaturalResourceOriginAudit.Record(source,world,"block_drop",1,1,0,1,2,3,null,null,0,double.NaN);
 NaturalResourceOriginAudit.Record(source,world,"block_drop",1,1,0,1,2,3,null,null,-1,1);
 Check(NaturalResourceOriginAudit.RecordFailures==before+10);Check(facts.Count==2);
 NaturalResourceOriginAudit.RecordBlock(null,null,0,0,0,0);Check(NaturalResourceOriginAudit.RecordFailures==before+11);
 NaturalResourceOriginAudit.RecordCreature(null,null);Check(NaturalResourceOriginAudit.RecordFailures==before+12);
 NaturalResourceOriginAudit.Record(new object(),world,"block_drop",1,1,0,0,0,3,null,null,0,0);Check(facts.Count==3);Check(facts[2].SourceId!=f.SourceId);
}finally{NaturalResourceOriginAudit.Recorded-=bad;NaturalResourceOriginAudit.Recorded-=good;}
NaturalResourceOriginAudit.Record(source,world,"block_drop",1,1,0,0,0,3,null,null,0,1);Check(facts.Count==3);
Console.WriteLine($"PASS {n} pure provenance capture checks; no native drop execution");
