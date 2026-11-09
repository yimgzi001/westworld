using Westworld.Scapi.Adapter;
int checks = 0;
void Check(AttackReadiness want, double now=10, double last=9, double interval=.66,
    double start=10, double end=14, double? previous=null)
{
    var got = LocalAttackReadiness.Evaluate(now,last,interval,start,end,previous);
    if(got!=want) throw new Exception($"check {checks+1}: wanted {want}, got {got}");
    checks++;
}
Check(AttackReadiness.Ready);
Check(AttackReadiness.Wait,last:10);
Check(AttackReadiness.Wait,last:9.5,interval:.5); // exact equality
Check(AttackReadiness.Ready,now:double.BitIncrement(10),last:9.5,interval:.5);
Check(AttackReadiness.Wait,last:10,interval:0);
Check(AttackReadiness.Ready,now:double.BitIncrement(10),last:10,interval:0);
Check(AttackReadiness.Expired,now:14); // ready cannot override deadline
Check(AttackReadiness.Expired,now:15,last:15);
Check(AttackReadiness.Wait,last:10,interval:100);
Check(AttackReadiness.Ready,last:-1); // finite native initial/sentinel hit time
Check(AttackReadiness.Invalid,now:9);
Check(AttackReadiness.Invalid,last:11);
Check(AttackReadiness.Invalid,interval:-1);
Check(AttackReadiness.Invalid,start:-1);
Check(AttackReadiness.Invalid,end:10);
Check(AttackReadiness.Invalid,end:9);
Check(AttackReadiness.Invalid,end:14.01);
Check(AttackReadiness.Invalid,previous:10.1);
Check(AttackReadiness.Invalid,previous:9.9);
Check(AttackReadiness.Ready,previous:10);
foreach(var bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
{
    Check(AttackReadiness.Invalid,now:bad);
    Check(AttackReadiness.Invalid,last:bad);
    Check(AttackReadiness.Invalid,interval:bad);
    Check(AttackReadiness.Invalid,start:bad);
    Check(AttackReadiness.Invalid,end:bad);
    Check(AttackReadiness.Invalid,previous:bad);
}
// Same immutable deadline across a sequence, no model call or mutation of native state.
Check(AttackReadiness.Wait,now:10,last:10,previous:10);
Check(AttackReadiness.Wait,now:10.5,last:10,previous:10);
Check(AttackReadiness.Ready,now:10.75,last:10,previous:10.5);
Check(AttackReadiness.Invalid,now:10.6,last:10,previous:10.75);
Check(AttackReadiness.Expired,now:14,last:13.9,previous:13.9);
Console.WriteLine($"PASS {checks}/{checks} pure attack readiness contracts (not native runtime validation)");
