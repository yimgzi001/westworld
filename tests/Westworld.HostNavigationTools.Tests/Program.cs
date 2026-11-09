using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;
int count=0;void Check(bool x,string why){if(!x)throw new Exception(why);count++;}void Reject(Action f,string why){try{f();}catch(ArgumentException){count++;return;}catch(InvalidOperationException){count++;return;}throw new Exception(why);}
JsonElement J(object o)=>JsonSerializer.SerializeToElement(o);var player=new object();var owner=new object();var nav=new Fake(player,owner);var router=new HostNavigationToolRouter(player,owner,nav);
Reject(()=>new HostNavigationToolRouter(new object(),owner,nav),"wrong actor");Reject(()=>new HostNavigationToolRouter(player,new object(),nav),"wrong owner");
var sid=Guid.NewGuid();var aid=Guid.NewGuid();var ledger=new ToolCallLedger(sid,aid);ToolRequest Request(long seq,string tool,object args)=>new(sid,aid,seq,tool,J(args),tool=="host_navigation_status",tool=="host_cancel_navigation");
var first=Request(1,"host_navigate_local",new{bearing=90,distance=3});var receipt=ledger.Invoke(first,router.Invoke);Check(nav.Starts==1&&receipt.State=="returned"&&nav.Busy,"start admitted");var id=nav.RequestId;
Check(ledger.Invoke(first,router.Invoke)==receipt&&nav.Starts==1,"same seq no duplicate Start");Reject(()=>ledger.Invoke(first with{Arguments=J(new{bearing=180,distance=3})},router.Invoke),"payload conflict");Check(nav.RequestId==id&&nav.Cancels==0,"conflict cannot cancel");
var busy=ledger.Invoke(Request(2,"host_navigate_local",new{bearing=0,distance=1}),router.Invoke);Check(busy.Data.GetProperty("status").GetString()=="rejected"&&nav.Starts==1&&!ledger.RequiresReconciliation.HasValue,"busy known no effects");
var stale=ledger.Invoke(Request(3,"host_cancel_navigation",new{request_id=Guid.NewGuid()}),router.Invoke);Check(stale.Data.GetProperty("status").GetString()=="rejected"&&nav.Cancels==0,"old cancel rejected");
var stop=Request(4,"host_cancel_navigation",new{request_id=id});ledger.Invoke(stop,router.Invoke);ledger.Invoke(stop,router.Invoke);Check(nav.Cancels==1&&nav.Busy,"cancel once still stopping");
Check(ledger.Invoke(Request(5,"host_navigation_status",new{}),router.Invoke).Data.GetProperty("status").GetString()=="stopping","actual stopping status");nav.Complete();Check(!router.Busy,"drained");
foreach(object invalid in new object[]{new{bearing=-1,distance=1},new{bearing=360,distance=1},new{bearing=90,distance=0},new{bearing=90,distance=5},new{bearing=90,distance=1,max_seconds=0},new{bearing=90,distance=1,max_seconds=13},new{bearing="90",distance=1},new{bearing=90,distance=1,extra=1},new{distance=1}}){var r=router.Invoke("host_navigate_local",J(invalid));Check(r.Ok&&r.Data.GetProperty("status").GetString()=="rejected"&&nav.Starts==1,"invalid no Start");}
using(var d=JsonDocument.Parse("{\"bearing\":90,\"bearing\":0,\"distance\":1}"))Check(router.Invoke("host_navigate_local",d.RootElement).Data.GetProperty("status").GetString()=="rejected","duplicate fields");
Check(router.Invoke("host_navigation_status",J(new{unknown=true})).Data.GetProperty("status").GetString()=="rejected","status closed");
Check(router.Invoke("host_cancel_navigation",J(new{})).Data.GetProperty("status").GetString()=="rejected","cancel requires id");
nav.ThrowOnStart=true;var error=ledger.Invoke(Request(6,"host_navigate_local",new{bearing=90,distance=1}),router.Invoke);Check(error.State=="uncertain"&&ledger.RequiresReconciliation==6,"real exception uncertain");Reject(()=>ledger.Invoke(Request(7,"host_navigate_local",new{bearing=90,distance=1}),router.Invoke),"uncertain write gate");
var read=ledger.Invoke(Request(7,"host_navigation_status",new{}),router.Invoke);Check(read.State=="returned","uncertain can observe");ledger.Reconcile(6,7);nav.ThrowOnStart=false;ledger.Invoke(Request(8,"host_navigate_local",new{bearing=0,distance=1,max_seconds=12}),router.Invoke);Check(nav.Starts==2&&nav.RequestId!=id,"new actual request identity");
Check(ledger.Invoke(Request(9,"host_cancel_navigation",new{request_id=id}),router.Invoke).Data.GetProperty("status").GetString()=="rejected"&&nav.Cancels==1,"old request cannot stop new");
Check(HostNavigationToolRouter.Definitions().Count==3,"explicit tool catalogue");
Exception? threadError=null;var t=new Thread(()=>{try{router.Invoke("host_navigation_status",J(new{}));}catch(Exception e){threadError=e;}});t.Start();t.Join();Check(threadError is InvalidOperationException,"game thread");
nav.Complete();nav.OwnerQuarantined=true;
Check(!nav.Busy&&router.Invoke("host_navigate_local",J(new{bearing=0,distance=1})).Data.GetProperty("reason").GetString()=="navigation_owner_quarantined","quarantine persists when idle");
count+=NativeChecks.Run();
Console.WriteLine($"PASS {count}/{count} host navigation router+ledger tests (fake native port)");
sealed class Fake(object player,object owner):IHostNavigationControl{
 public bool OwnerQuarantined{get;set;}
 public bool Busy{get;private set;}public Guid RequestId{get;private set;}public int Starts,Cancels;public bool ThrowOnStart;string status="idle";
 public bool Matches(object p,object o)=>ReferenceEquals(player,p)&&ReferenceEquals(owner,o);
 public JsonElement ReadStatus()=>JsonSerializer.SerializeToElement(new{status,request_id=RequestId,busy=Busy});
 public void Start(Guid request,double bearing,double distance,double maxSeconds){if(ThrowOnStart)throw new Exception("native partial failure");Starts++;RequestId=request;Busy=true;status="running";}
 public void Cancel(){Cancels++;status="stopping";}public void Complete(){Busy=false;status="cancelled";}
}
