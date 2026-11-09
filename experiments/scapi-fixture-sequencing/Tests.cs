using RecoveryFixtureNext;
int count=0;
void Check(bool ok,string name){count++;if(!ok)throw new Exception(name);}
Snapshot S(double t,double tick,bool loaded=true,bool visible=true,bool safe=true,bool target=true,bool wall=true,bool original=true,bool raw=true,bool light=true)=>new(t,tick,safe,loaded,target,wall,visible,original,raw,light);
FixtureSequencer Start(bool visible=true,bool wall=true){
 var q=new FixtureSequencer(visible,wall);var req=q.Step(S(0,0));
 Check(req?.Kind==WriteKind.PlaceTarget,"place target once");q.AcknowledgeWrite(req!.Sequence,true,0);return q;
}
var a=Start();
Check(a.Step(S(.1,1,false)) is null&&a.Phase==Phase.WaitTarget,"InvalidLight cannot measure");
Check(a.Step(S(.2,2)) is null,"first valid tick");
Check(a.Step(S(.3,2)) is null,"same game tick cannot count twice");
var w=a.Step(S(.4,3));Check(w?.Kind==WriteKind.PlaceWall,"two fresh valid ticks permit wall");
Check(a.Step(S(.5,4)) is null,"pending write never repeated");
a.AcknowledgeWrite(w!.Sequence,true,.5);
Check(a.Step(S(.6,5,false,false)) is null,"wall invalid chunk not success");
Check(a.Step(S(.7,6,true,false)) is null,"wall valid first tick");
var restore=a.Step(S(.8,7,true,false));Check(restore?.Kind==WriteKind.RestoreOriginalContents,"wall validated before restore");
a.AcknowledgeWrite(restore!.Sequence,true,.8);
Check(a.Step(S(.9,8,false,false,raw:false,light:false)) is null,"restore waits lighting");
Check(a.Step(S(1,9,true,false,raw:false,light:false)) is null,"restore valid first");
Check(a.Step(S(1.1,10,true,false,raw:false,light:false)) is null,"restore ends without rewrite");
Check(a.Result is {AssertionsPassed:true,ContentDataRestored:true,ChunksValid:true,RawMatches:false,LightMatches:false},"raw light mismatch explicitly preserved");
Check(a.Step(S(2,20)) is null,"completed cannot replay");
var b=Start(false,false);
Check(b.Step(S(.1,1,true,false)) is null,"behind first valid tick");
restore=b.Step(S(.2,2,true,false));Check(restore?.Kind==WriteKind.RestoreOriginalContents,"behind no wall");
b.AcknowledgeWrite(restore!.Sequence,true,.2);b.Step(S(.3,3));b.Step(S(.4,4));
Check(b.Result?.AssertionsPassed==true,"behind pass");
var c=Start();
c.Step(S(.1,1));c.Step(S(.2,2,false));Check(c.Step(S(.3,3)) is null,"invalid resets stability");Check(c.Step(S(.4,4))?.Kind==WriteKind.PlaceWall,"second stable after reset");
var d=Start();restore=d.Step(S(.1,1,safe:false));Check(restore?.Kind==WriteKind.RestoreOriginalContents,"safety cleanup");d.AcknowledgeWrite(restore!.Sequence,true,.1);d.Step(S(.2,2,safe:false));d.Step(S(.3,3,safe:false));Check(d.Result is{AssertionsPassed:false,ContentDataRestored:true},"cleanup allowed without new assertions");
var e=Start();restore=e.Step(S(16,1,false));Check(restore?.Kind==WriteKind.RestoreOriginalContents,"load timeout cleanup");
var f=Start();restore=f.Step(S(.1,1,target:false));Check(restore?.Kind==WriteKind.RestoreOriginalContents,"neighbor removes pumpkin fails");
var g=Start();g.Step(S(.1,1,visible:false));restore=g.Step(S(.2,2,visible:false));Check(restore?.Kind==WriteKind.RestoreOriginalContents&&g.StatusReason=="visibility_assertion_failed","front not visible fails");
var h=new FixtureSequencer(true,true);var pending=h.Step(S(0,0));h.AcknowledgeWrite(pending!.Sequence,false,.1);Check(h.Phase==Phase.NeedsReconcile,"uncertain write needs inspection");Check(h.Step(S(1,1)) is null,"uncertain never retry");
var i=Start();i.Step(S(1,1));Check(i.Step(S(.5,2)) is null&&i.Phase==Phase.NeedsReconcile,"wall time rewind");
var j=Start();j.Step(S(1,2));Check(j.Step(S(2,1)) is null&&j.Phase==Phase.NeedsReconcile,"game time rewind");
var k=Start();Check(k.Step(S(double.NaN,1)) is null&&k.Phase==Phase.NeedsReconcile,"NaN clock");
var l=new FixtureSequencer(true,true);l.Step(S(0,0));l.Step(S(16,1));Check(l.Phase==Phase.NeedsReconcile,"missing ack timeout");
var m=Start(false,false);m.Step(S(.1,1,visible:false));restore=m.Step(S(.2,2,visible:false));m.AcknowledgeWrite(restore!.Sequence,true,.2);m.Step(S(16,3,false,original:true,raw:false,light:false));Check(m.Result is{AssertionsPassed:false,ContentDataRestored:true,ChunksValid:false},"restore timeout not silent success");
var n=new FixtureSequencer(true,true);Check(n.Step(S(0,0,safe:false)) is null&&n.Result?.AssertionsPassed==false,"unsafe preflight no edits");
try{new FixtureSequencer(false,true);Check(false,"bad config");}catch(ArgumentException){Check(true,"invalid wall config rejected");}


void Reject(Action action,string label){try{action();Check(false,label);}catch(InvalidOperationException){Check(true,label);}}
var late=new FixtureSequencer(true,true);
var lateWrite=late.Step(S(0,0));
late.Step(S(16,1));
Reject(()=>late.AcknowledgeWrite(lateWrite!.Sequence,true,16),"late acknowledgement cannot revive timed-out sequence");
Check(late.Phase==Phase.NeedsReconcile&&late.Step(S(17,2)) is null,"terminal reconciliation remains terminal");
var rewind=new FixtureSequencer(true,true);
var rewindWrite=rewind.Step(S(1,1));
rewind.Step(S(.5,2));
Reject(()=>rewind.AcknowledgeWrite(rewindWrite!.Sequence,true,2),"ack cannot revive invalid-clock sequence");
var deadline=new FixtureSequencer(true,true);
var deadlineWrite=deadline.Step(S(0,0));
deadline.AcknowledgeWrite(deadlineWrite!.Sequence,true,16);
Check(deadline.Phase==Phase.NeedsReconcile,"late ack rejected even without intermediate Step");
var fresh=Start();
Check(fresh.Step(S(.1,0)) is null,"write tick not a fresh observation");
Check(fresh.Step(S(.2,1)) is null,"one post-write tick insufficient");
Check(fresh.Step(S(.3,2))?.Kind==WriteKind.PlaceWall,"two post-write ticks required");
var reset=Start();
reset.Step(S(.1,1,false));
Check(reset.Step(S(.2,1)) is null,"same invalidated tick cannot count");
Check(reset.Step(S(.3,2)) is null,"first fresh tick after invalidation");
Check(reset.Step(S(.4,3))?.Kind==WriteKind.PlaceWall,"second fresh tick after invalidation");
var pendingTick=new FixtureSequencer(true,true);
var pt=pendingTick.Step(S(0,0));
pendingTick.Step(S(.1,5));
pendingTick.AcknowledgeWrite(pt!.Sequence,true,.2);
Check(pendingTick.Step(S(.3,5)) is null,"latest observed pending tick is excluded");
Check(pendingTick.Step(S(.4,6)) is null,"first post-ack advancing tick");
Check(pendingTick.Step(S(.5,7))?.Kind==WriteKind.PlaceWall,"second post-ack advancing tick");



FixtureSequencer Restoring(){
 var q=Start(false,false);q.Step(S(.1,1,visible:false));
 var r=q.Step(S(.2,2,visible:false));q.AcknowledgeWrite(r!.Sequence,true,.2);return q;
}
var restoreDeadline=Restoring();
restoreDeadline.Step(S(.3,3));
restoreDeadline.Step(S(16,4));
Check(restoreDeadline.Result is{AssertionsPassed:false,ContentDataRestored:true,ChunksValid:true,Reason:"restore_timeout"},"late second restore sample cannot pass");
var restoreTicks=Restoring();
restoreTicks.Step(S(.3,2));
Check(restoreTicks.Result is null,"restore write tick excluded");
restoreTicks.Step(S(.4,3));
Check(restoreTicks.Result is null,"restore needs two advancing ticks");
restoreTicks.Step(S(.5,4));
Check(restoreTicks.Result?.AssertionsPassed==true,"restore two advancing ticks pass");
var restoreReset=Restoring();
restoreReset.Step(S(.3,3,false));
restoreReset.Step(S(.4,3));
restoreReset.Step(S(.5,4));
Check(restoreReset.Result is null,"restore invalid tick cannot be reused");
restoreReset.Step(S(.6,5));
Check(restoreReset.Result?.AssertionsPassed==true,"restore fresh ticks after invalidation pass");
Console.WriteLine($"{count}/{count} multi-tick sequencing checks passed.");
