using Capability39.Cooperation;
using static Capability39.Cooperation.CooperativeEvidence45;
int checks=0;
void Pass(Action action){action();checks++;}
void Reject(Action action){try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException){checks++;return;}throw new Exception("Expected contract rejection");}
var world=Guid.NewGuid();var a=new CoopBinding45(world,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());var b=new CoopBinding45(world,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
Pass(()=>ValidateIdentity(a,a));
Reject(()=>ValidateIdentity(a,b));Reject(()=>ValidateIdentity(a,a with{Request=Guid.NewGuid()}));Reject(()=>ValidateIdentity(a,a with{Session=Guid.NewGuid()}));Reject(()=>ValidateIdentity(a,a with{World=Guid.NewGuid()}));Reject(()=>ValidateIdentity(a,a with{Actor=Guid.Empty}));
var initial=new CoopInventory45(world,a.Actor,b.Actor,162,2,0);var delivered=initial with{CountA=1,CountB=1};
Pass(()=>ValidateDelivery(initial,delivered,1));Reject(()=>ValidateDelivery(initial,delivered,0));Reject(()=>ValidateDelivery(initial,delivered with{CountA=2},1));Reject(()=>ValidateDelivery(initial,delivered with{CountB=2},1));Reject(()=>ValidateDelivery(initial,delivered with{ActorB=a.Actor},1));Reject(()=>ValidateDelivery(initial,delivered with{ItemValue=89},1));Reject(()=>ValidateDelivery(initial,delivered with{CountA=-1},1));
Pass(()=>ValidateEating(delivered,delivered with{CountB=0},b.Actor,1));Pass(()=>ValidateEating(delivered,delivered with{CountA=0},a.Actor,1));Reject(()=>ValidateEating(delivered,delivered with{CountA=0,CountB=0},b.Actor,1));Reject(()=>ValidateEating(delivered,delivered,b.Actor,1));Reject(()=>ValidateEating(delivered,delivered with{CountB=0},Guid.NewGuid(),1));Reject(()=>ValidateEating(delivered,delivered,b.Actor,0));
var wait=new CoopActionEvidence45(a,"act_Wait",Guid.NewGuid(),1,5,"succeeded",0);var eat=new CoopActionEvidence45(b,"act_Eat",Guid.NewGuid(),2,3,"succeeded",1);
Pass(()=>ValidateOverlap(wait,eat,3.1));Reject(()=>ValidateOverlap(wait,eat,5));Reject(()=>ValidateOverlap(wait,eat,2.9));Reject(()=>ValidateOverlap(wait,eat,double.NaN));Reject(()=>ValidateOverlap(wait,eat with{Binding=a},3.1));Reject(()=>ValidateOverlap(wait,eat with{NativeRunId=wait.NativeRunId},3.1));Reject(()=>ValidateOverlap(wait,eat with{AffectedCount=0},3.1));Reject(()=>ValidateOverlap(wait,eat with{Status="failed"},3.1));Reject(()=>ValidateOverlap(wait,eat with{StartedAt=.5},3.1));Reject(()=>ValidateOverlap(wait,eat with{EndedAt=double.PositiveInfinity},3.1));Reject(()=>ValidateOverlap(wait,eat with{Tool="act_Speak"},3.1));
Console.WriteLine($"{checks} cooperative evidence assertions passed; pure contracts, no game or API");
