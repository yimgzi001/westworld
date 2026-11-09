using Westworld.Runtime.Core;
int count=0;void Check(bool ok,string why){count++;if(!ok)throw new Exception(why);}
NavigationSettlingSample S(double t)=>new(t,true,true,false,0,0,0,0);
var gate=new NavigationSettling(0);
Check(gate.Update(S(0)).Status==NavigationSettlingStatus.Waiting,"zero velocity alone insufficient");
for(int i=1;i<=2;i++)Check(gate.Update(S(i*.1)).Status==NavigationSettlingStatus.Waiting,"residence pending");
Check(gate.Update(S(.25)).Status==NavigationSettlingStatus.Settled,"continuous residence");
Check(gate.Update(S(.3) with{Grounded=false}).Status==NavigationSettlingStatus.Settled,"terminal latched caller must create new gate");
gate=new(0);gate.Update(S(0));Check(gate.Update(S(.3)).Status==NavigationSettlingStatus.Waiting,"sparse gap resets");
gate.Update(S(.4));gate.Update(S(.5));Check(gate.Update(S(.55)).Status==NavigationSettlingStatus.Settled,"after gap residence");
foreach(var bad in new[]{S(.1) with{Grounded=false},S(.1) with{Immersion=.11},S(.1) with{VelocityY=.11},S(.1) with{VelocityX=.08,VelocityZ=.08}}){gate=new(0);gate.Update(S(0));Check(gate.Update(bad).StableFor==0,"unstable resets");Check(gate.Update(S(.2)).Status==NavigationSettlingStatus.Waiting,"must reconfirm");}
gate=new(0);Check(gate.Update(S(0) with{Alive=false}).Reason=="actor_dead","death");
gate=new(0);Check(gate.Update(S(0) with{Parented=true}).Reason=="actor_parented","parent");
gate=new(0);Check(gate.Update(S(3)).Status==NavigationSettlingStatus.Failed,"deadline no old stability");
gate=new(0);gate.Update(S(0));Check(gate.Update(S(4)).Reason=="settling_timeout","late update");
gate=new(0);gate.Update(S(.1));foreach(var invalid in new[]{S(0),S(double.NaN),S(.2) with{Immersion=2},S(.2) with{VelocityX=double.PositiveInfinity}}){bool rejected=false;try{gate.Update(invalid);}catch(ArgumentException){rejected=true;}Check(rejected,"invalid rejected");}
Check(gate.Update(S(.2)).Status==NavigationSettlingStatus.Waiting,"invalid samples atomic");
gate=new(0,.3,.2);gate.Update(S(.1));gate.Update(S(.2));Check(gate.Update(S(.3)).Status==NavigationSettlingStatus.Settled,"complete exactly deadline");
Console.WriteLine($"PASS {count}/{count}");
