using Westworld.Decisions.Local;
int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;}
void Reject(Action run,string name){try{run();}catch(ArgumentOutOfRangeException){passed++;return;}throw new Exception(name);}
// Constructor-only: this existing assembly acts as a file-existence fixture.
// No executable process or model inference is started.
string fixture=typeof(LlamaProcessProvider).Assembly.Location;
LlamaProcessProvider Make(int context,int output,TimeSpan? timeout=null)=>new(fixture,fixture,_=>false,timeout,contextTokens:context,maxOutputTokens:output);
foreach(int c in new[]{2048,2049,4096,8191,8192})foreach(int o in new[]{64,65,96,511,512})Check(Make(c,o)!=null,$"accept {c}/{o}");
foreach(int c in new[]{int.MinValue,-1,0,2047,8193,int.MaxValue})Reject(()=>Make(c,96),$"context {c}");
foreach(int o in new[]{int.MinValue,-1,0,63,513,int.MaxValue})Reject(()=>Make(2048,o),$"output {o}");
Reject(()=>Make(2048,96,TimeSpan.Zero),"zero timeout");
Reject(()=>Make(2048,96,TimeSpan.FromTicks(-1)),"negative timeout");
Reject(()=>Make(2048,96,TimeSpan.FromSeconds(120)+TimeSpan.FromTicks(1)),"timeout over cap");
Check(Make(2048,96,TimeSpan.FromSeconds(120))!=null,"timeout upper inclusive");
Check(Make(2048,96,TimeSpan.FromMilliseconds(1))!=null,"positive timeout");
Console.WriteLine($"PASS {passed}/{passed} provider constructor bounds; no inference executed");
