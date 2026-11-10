using Game.Westworld;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
int checks=0;void Check(bool b){if(!b)throw new Exception("failed "+checks);checks++;}
var tools=(ActorTools)RuntimeHelpers.GetUninitializedObject(typeof(ActorTools));
var field=typeof(ActorTools).GetField("m_direct",BindingFlags.Instance|BindingFlags.NonPublic)!;
var read=typeof(ActorTools).GetMethod("ReadDirect",BindingFlags.Instance|BindingFlags.NonPublic)!;
ActorActionView View()=> (ActorActionView)read.Invoke(tools,null)!;
AtomicActionRun Run(AtomicActionStatus status,AtomicActionFailure failure=AtomicActionFailure.None)=>(AtomicActionRun)Activator.CreateInstance(typeof(AtomicActionRun),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{new Wait(1),new AtomicActionResult(status,failure)},null)!;
Check(View()==null);var first=Run(AtomicActionStatus.Running);field.SetValue(tools,first);var initial=View();Check(initial.RunId!=Guid.Empty);Check(View().RunId==initial.RunId);Check(View().Status==AtomicActionStatus.Running);
foreach(var status in new[]{AtomicActionStatus.Succeeded,AtomicActionStatus.Cancelled,AtomicActionStatus.Failed}){typeof(AtomicActionRun).GetProperty("Result")!.SetValue(first,new AtomicActionResult(status));Check(View().RunId==initial.RunId);Check(View().Status==status);}
var rejected=Run(AtomicActionStatus.Failed,AtomicActionFailure.PreconditionsNotMet);field.SetValue(tools,rejected);Check(View().RunId!=initial.RunId);Check(View().Failure==AtomicActionFailure.PreconditionsNotMet);var rejectedId=View().RunId;Check(View().RunId==rejectedId);
var immediate=Run(AtomicActionStatus.Succeeded);field.SetValue(tools,immediate);Check(View().RunId!=rejectedId);var id=View().RunId;
var json=JsonSerializer.Serialize(View(),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});Check(JsonDocument.Parse(json).RootElement.GetProperty("runId").GetGuid()==id);
field.SetValue(tools,null);Check(View()==null);field.SetValue(tools,first);Check(View().RunId==initial.RunId);
Console.WriteLine($"PASS {checks}");
