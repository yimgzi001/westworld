using Westworld.Scapi.Adapter;
int n=0;void C(bool v,string reason){if(!v)throw new Exception(reason);n++;}
var key=new NavigationSampleKey(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Moving","running",true,true,false);
var g=new NavigationSampleGate();C(g.Observe(0,key).Emit,"first");
C(!g.Observe(.01,key).Emit,"no per frame");C(!g.Observe(.099,key).Emit,"period below");
var p=g.Observe(.1,key);C(p.Emit&&p.Trigger=="periodic"&&p.SuppressedSincePrevious==2,"10hz");
C(!p.CoverageUnknown,"covered actual interval");
foreach(var changed in new[]{key with {State="Settling"},key with{Owner=Guid.NewGuid()},key with{Request=Guid.NewGuid()},key with{Segment=Guid.NewGuid()},key with{OwnsLease=false},key with{Reason="failed"},key with{KinematicallyStopped=true}}){var a=new NavigationSampleGate();a.Observe(1,key);C(a.Observe(1.001,changed).Trigger=="boundary","identity/status/stop immediate");}
var failed=key with{State="Failed",Busy=false,OwnsLease=false};C(g.Observe(.101,failed).Emit,"failed immediate");
for(int i=1;i<=1000;i++)C(!g.Observe(.101+i*.001,failed).Emit,"terminal no periodic");
var stop=g.Observe(1.102,failed with{KinematicallyStopped=true});C(stop.Emit&&stop.CoverageUnknown&&stop.SuppressedSincePrevious==1000,"real later stop preserved with gap unknown");
var gap=new NavigationSampleGate();gap.Observe(10,key);var after=gap.Observe(11,key);C(after.Emit&&after.IntervalSeconds==1&&after.CoverageUnknown,"no invented missing samples");
var backwards=gap.Observe(9,key);C(backwards.Emit&&backwards.CoverageUnknown&&backwards.Trigger=="clock_regressed","regression explicit");
foreach(var x in new[]{double.NaN,double.PositiveInfinity,-1}){try{gap.Observe(x,key);throw new Exception("invalid accepted");}catch(ArgumentOutOfRangeException){n++;}}
var idle=new NavigationSampleGate();C(idle.Observe(0,failed).Emit,"first failed logs");C(!idle.Observe(10,failed).Emit,"no failed heartbeat");C(idle.Observe(10.001,key).Emit,"new active boundary");
Console.WriteLine($"PASS {n}/{n}");
