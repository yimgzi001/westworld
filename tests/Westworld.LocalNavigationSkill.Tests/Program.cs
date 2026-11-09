using Westworld.Scapi.Adapter;
using Westworld.Runtime.Core;
int count=0;
void Check(bool ok){if(!ok)throw new Exception($"assertion {count+1}");count++;}
var id=Guid.NewGuid();var evidence=new[]{new LocalObservedCell(0,64,0,0,id,1,0,true),new LocalObservedCell(0,65,0,0,id,1,0,true)};
Check(LocalNavigationSkillGeometry.InObservedAir(evidence,new(.25,64,.25),new(.75,65.77,.75)));
Check(!LocalNavigationSkillGeometry.InObservedAir(evidence,new(.75,64,.25),new(1.25,65.77,.75)));
Check(!LocalNavigationSkillGeometry.InObservedAir(evidence,new(.25,63.9,.25),new(.75,65.67,.75)));
Check(!LocalNavigationSkillGeometry.InObservedAir(evidence,new(.25,64,.25),new(.85,65.77,.75)));
Check(!LocalNavigationSkillGeometry.InObservedAir(evidence,new(double.NaN,64,.25),new(.75,65.77,.75)));
Check(!LocalNavigationSkillGeometry.InObservedAir(new[]{evidence[0]},new(.25,64,.25),new(.75,65.77,.75)));
Check(!LocalNavigationSkillGeometry.InObservedAir(new[]{evidence[0] with{Value=2},evidence[1]},new(.25,64,.25),new(.75,65.77,.75)));
Check(!LocalNavigationSkillGeometry.InObservedAir(new[]{evidence[0] with{Value=-1024},evidence[1]},new(.25,64,.25),new(.75,65.77,.75)));
Check(!LocalNavigationSkillGeometry.InObservedAir(new[]{evidence[0] with{Age=4},evidence[1]},new(.25,64,.25),new(.75,65.77,.75)));
Check(!LocalNavigationSkillGeometry.InObservedAir(new[]{evidence[0],evidence[0],evidence[1]},new(.25,64,.25),new(.75,65.77,.75)));
var settling=new NavigationSettling(1);
foreach(double time in new[]{1d,1.05,1.1,1.15,1.2})Check(settling.Update(new(time,true,true,false,0,0,0,0)).Status==NavigationSettlingStatus.Waiting);
Check(settling.Update(new(1.25,true,true,false,0,0,0,0)).Status==NavigationSettlingStatus.Settled);
var airborne=new NavigationSettling(0);Check(airborne.Update(new(.01,true,false,false,0,0,-1,0)).Status==NavigationSettlingStatus.Waiting);
Check(airborne.Update(new(3,true,false,false,0,0,-1,0)).Status==NavigationSettlingStatus.Failed);
// Host arbitration contracts; no native runtime is instantiated here.
object nativeOwner=new(), sensor=new();bool directIdle=true,taskIdle=true;
var gate=new LocalNavigationSkillLease(nativeOwner,sensor,()=>new(directIdle,taskIdle));
var request=Guid.NewGuid();directIdle=false;Check(!gate.TryAcquire(request,10));
directIdle=true;taskIdle=false;Check(!gate.TryAcquire(request,10));
taskIdle=true;Check(gate.TryAcquire(request,10));Check(gate.IsHeld(request));
var rival=new LocalNavigationSkillLease(nativeOwner,sensor,()=>new(true,true));
Check(!rival.TryAcquire(Guid.NewGuid(),10));
bool wrongRelease=false;try{rival.Release(Guid.NewGuid(),true);}catch(InvalidOperationException){wrongRelease=true;}Check(wrongRelease);
gate.Release(request,true);Check(!gate.IsHeld(request));
Check(rival.TryAcquire(request,10.1));rival.Release(request,false);
Check(gate.Quarantined);Check(!gate.TryAcquire(Guid.NewGuid(),10.2));
var rebuiltOwnerGate=new LocalNavigationSkillLease(nativeOwner,new object(),()=>new(true,true));
Check(!rebuiltOwnerGate.TryAcquire(Guid.NewGuid(),10.3)); // new wrapper cannot clear quarantine
var otherOwner=new object();var clockGate=new LocalNavigationSkillLease(otherOwner,sensor,()=>new(true,true));
Check(!clockGate.ValidateClock(9));Check(clockGate.ClockFaulted);
var replacedWrapper=new LocalNavigationSkillLease(new object(),sensor,()=>new(true,true));
Check(!replacedWrapper.TryAcquire(Guid.NewGuid(),20)); // same sensor cannot reset poisoned clock
var freshSession=new LocalNavigationSkillLease(new object(),new object(),()=>new(true,true));
Check(freshSession.TryAcquire(request,0));freshSession.Release(request,true);
Check(!freshSession.ValidateClock(double.NaN));Check(!freshSession.ValidateClock(1));
var singleFrame=new NavigationSettling(0);
Check(singleFrame.Update(new(0,true,true,false,0,0,0,0)).Status==NavigationSettlingStatus.Waiting);
Check(singleFrame.Update(new(.3,true,true,false,0,0,0,0)).Status==NavigationSettlingStatus.Waiting); // sparse sampling not dwell
Console.WriteLine($"PASS {count}/{count}: geometry/settling/lease pure contracts; native adapter compiled, NOT executed");
