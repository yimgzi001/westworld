using Westworld.Scapi.Adapter;
using System.Text.Json;
int n=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);n++;}
NavigationReadiness E(bool g=true,bool a=false,double i=0,double v=0,double h=0,double life=1)=>NavigationReadiness.Evaluate(g,a,i,v,h,life);
Check(E().Ready,"stable");
Check(E(i:.1,v:.1,h:.1).Ready,"inclusive thresholds");
Check(E(v:-.1).Ready,"down boundary");
foreach(var (sample,code) in new[]{(E(g:false),"not_grounded"),(E(a:true),"attached_to_parent"),(E(i:.10001),"immersed"),(E(v:.10001),"vertical_motion"),(E(v:-.10001),"vertical_motion"),(E(h:.10001),"horizontal_motion"),(E(life:0),"not_alive"),(E(life:-1),"not_alive")}){Check(!sample.Ready,"not ready");Check(sample.Blockers.Contains(code),code);}
foreach(var s in new[]{E(i:double.NaN),E(v:double.PositiveInfinity),E(h:-1),E(life:double.NaN),E(i:-1)})Check(!s.Ready&&s.Blockers.Contains("invalid_body_sample"),"fail closed invalid");
var jump=E(g:false,v:3.573086);Check(!jump.Ready&&jump.Blockers.Length==2,"real jump airborne");
Check(E().Ready&&!jump.Ready,"later grounded does not rewrite previous");
var exception=new NavigationNotReadyException(jump);Check(exception is InvalidOperationException&&ReferenceEquals(exception.Readiness,jump),"compatible typed exception");
Check(NavigationCompletionSemantics.Jump.Contains("not landing"),"jump semantic");
Check(NavigationCompletionSemantics.Jump.Contains("no automatic wait"),"no policy");
var json=JsonSerializer.Serialize(jump);Check(json.Contains("3.573086")&&json.Contains("not_grounded"),"observable exact reasons");
Check(NavigationCompletionSemantics.Navigation.Contains("partial progress"),"failure != zero progress");
Check(JsonSerializer.Serialize(E(v:double.NaN)).Contains("null"),"invalid sample safely serialized, not ready");
Console.WriteLine($"PASS {n}/{n}");
