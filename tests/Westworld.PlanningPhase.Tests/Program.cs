using System.Text.Json;
using Westworld.Decisions.Local;
int checks=0;void Check(bool b,string label){if(!b)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
JsonElement J(string s)=>JsonDocument.Parse(s).RootElement.Clone();
var session=Guid.NewGuid();var actor=Guid.NewGuid();var obs=Guid.NewGuid();var nav=Guid.NewGuid();
var status=JsonSerializer.SerializeToElement(new{request_id=nav,status="running",busy=true,path=.15,remaining=2.85});
var step=new ToolDecisionStep("host_navigate_local",J("{\"bearing\":90,\"distance\":3,\"max_seconds\":12}"));
JsonElement Make(JsonElement s,double seconds=40)=>PlanningPhaseContext.AfterPrecedingSuccess(session,actor,obs,nav,11,"Explore observed terrain",step,s,seconds);
var c=Make(status);var p=c.GetProperty("predecessor");
Check(c.GetProperty("phase").GetString()=="AfterPrecedingSuccess","explicit future planning phase");
Check(c.GetProperty("planningObservationId").GetGuid()==obs&&c.GetProperty("sessionId").GetGuid()==session&&c.GetProperty("actorId").GetGuid()==actor,"scope preserved");
Check(p.GetProperty("navigationRequestId").GetGuid()==nav&&p.GetProperty("receiptSequence").GetInt64()==11,"predecessor identity preserved");
Check(p.GetProperty("actualStatus").GetRawText()==status.GetRawText(),"real busy facts preserved without arrival prediction");
Check(p.GetProperty("step").GetProperty("arguments").GetRawText()==step.Arguments.GetRawText(),"original parameters preserved");
Check(c.GetProperty("instruction").GetString()!.Contains("Current busy status alone is not a reason")&&c.GetProperty("instruction").GetString()!.Contains("empty plan remains legal"),"not forced waiting or forced action");
Check(c.GetProperty("evidenceRule").GetString()!.Contains("fresh observation/schema/native validation")&&c.GetProperty("evidenceRule").GetString()!.Contains("Do not invent future"),"no future evidence minted");
foreach(var seconds in new[]{0,-1,double.NaN,double.PositiveInfinity,120.01}){bool bad=false;try{Make(status,seconds);}catch(ArgumentException){bad=true;}Check(bad,"invalid remaining budget rejected");}
foreach(var s in new[]{J("{}"),JsonSerializer.SerializeToElement(new{request_id=nav,status="stopping",busy=true}),JsonSerializer.SerializeToElement(new{request_id=Guid.NewGuid(),status="running",busy=true}),JsonSerializer.SerializeToElement(new{request_id=nav,status="running",busy=true,owner_quarantined=true})}){bool bad=false;try{Make(s);}catch(ArgumentException){bad=true;}Check(bad,"bad predecessor rejected");}
if(args.Length==1){var historical=J(File.ReadAllText(args[0]));var n=historical.GetProperty("context").GetProperty("facts").GetProperty("terminal").GetProperty("navigation");var result=PlanningPhaseContext.AfterPrecedingSuccess(session,actor,obs,n.GetProperty("request_id").GetGuid(),11,"Original replay intent supplied by test",step,n,40);Check(result.GetProperty("predecessor").GetProperty("actualStatus").GetRawText()==n.GetRawText(),"Terra12 round2 status replay preserved, no model call");}
foreach(var kind in Enum.GetValues<PrecedingExecutionKind>()) {
 string tool=kind==PrecedingExecutionKind.NativeMoveTo?"act_MoveTo":"host_navigate_local";
 var bind=PrecedingExecutionBinding.Create(session,actor,11,nav,kind,tool,step.Arguments,new string('x',300));
 var sample=new PrecedingExecutionSample(session,actor,11,nav,kind,"running",true,false,false,.3);
 var raw=kind==PrecedingExecutionKind.NativeMoveTo?JsonSerializer.SerializeToElement(new{runId=nav,status="running"}):status;
 var result=PlanningPhaseContext.AfterExecutionSuccess(bind,obs,sample,raw,40);
 Check(result.GetProperty("predecessor").GetProperty("runId").GetGuid()==nav,"unified native identity");
 Check(result.GetProperty("predecessor").GetProperty("goal").GetString()!.Length==300,"full original goal preserved");
 Check(result.GetProperty("predecessor").GetProperty("actualStatus").GetRawText()==raw.GetRawText(),"actual predecessor status retained");
 Check(result.GetProperty("instruction").GetString()!.Contains("physically stops"),"success alone not dispatch permission");
 foreach(var bad in new[]{sample with{RunId=Guid.NewGuid()},sample with{Status="failed"},sample with{CancelRequested=true}}){bool rejected=false;try{PlanningPhaseContext.AfterExecutionSuccess(bind,obs,bad,raw,40);}catch(ArgumentException){rejected=true;}Check(rejected,"bad unified sample rejected");}
 bool mismatch=false;try{PlanningPhaseContext.AfterExecutionSuccess(bind,obs,sample,J("{}"),40);}catch(ArgumentException){mismatch=true;}Check(mismatch,"raw missing identity rejected");
}
Console.WriteLine($"TOTAL {checks}/{checks}");
