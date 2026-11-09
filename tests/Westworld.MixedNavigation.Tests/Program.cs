using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;
int checks=0;
void Check(bool value,string name){checks++;if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
void Reject(Action action,string name){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,name);}
var session=Guid.NewGuid();var actor=Guid.NewGuid();
// Piecewise constant, full-cube terraces: west down, east up, north/south flat.
int Floor(int x)=>x<0?62:x>0?64:63;
Position At(int x,int z)=>new(x+.5,Floor(x)+1,z+.5);
ActorObservation Scene(Position origin,double time){
 var cells=new List<CellObservation>();
 for(int x=-4;x<=5;x++)for(int z=-4;z<=5;z++)for(int y=61;y<=69;y++)
  cells.Add(new(x,y,z,"known",y<=Floor(x)?3:0));
 return new(session,actor,Guid.NewGuid(),time,origin,1,.4f,[],cells.ToArray());
}
(ExplorationObservation Observation,IReadOnlyList<ObservedStepRoute> Steps) Mix(ActorObservation source){
 var flat=LocalRouteCandidates.Project(source);
 var steps=ObservedStepRoutes.Project(new LocalObservationMemory(session,actor).Observe(source));
 var candidates=flat.Candidates.Concat(steps.Select(s=>new ExplorationCandidate(s.Id,s.Destination,true))).ToArray();
 return(new(session,actor,source.ObservationId,source.Time,source.Position,candidates),steps);
}
var origin=At(0,0);var scene=Scene(origin,1);var mixed=Mix(scene);var input=mixed.Observation;
Check(mixed.Steps.Any(r=>r.Kind==StepKind.Up&&r.Destination==At(1,0)),"east up from observed full cubes");
Check(mixed.Steps.Any(r=>r.Kind==StepKind.Down&&r.Destination==At(-1,0)),"west down from observed full cubes");
Check(input.Candidates.Any(c=>c.Destination==At(0,1)),"north flat coexists with steps");
Check(input.Candidates.Select(c=>c.Destination).Distinct().Count()==input.Candidates.Length,"mixed destinations unique");
Check(mixed.Steps.All(r=>r.Evidence.All(c=>scene.Cells.Any(s=>s.X==c.X&&s.Y==c.Y&&s.Z==c.Z&&s.Value==c.Value))),"step proofs subset of supplied observation");
var planner=new LocalExploration(session,actor);var first=planner.Select(input,1).Choice!;
Check(input.Candidates.Any(c=>c.EvidenceId==first.EvidenceId&&c.Destination==first.Destination),"choice retains actual candidate evidence");
Check(planner.Select(input,1).Reason=="actor_busy","mixed selection single in-flight");
Reject(()=>planner.Report(Guid.NewGuid(),1,origin,ExplorationOutcome.Arrived),"wrong completion id refused");
Check(planner.HasPendingChoice,"wrong completion cannot clear pending");
Reject(()=>planner.Report(first.ChoiceId,1,origin,ExplorationOutcome.Arrived),"arrival requires positional result");
planner.Report(first.ChoiceId,1,origin,ExplorationOutcome.Blocked);
var fallback=planner.Select(input,1).Choice!;
Check(fallback.Destination!=first.Destination,"blocked mixed candidate yields alternative");
planner.Report(fallback.ChoiceId,1,origin,ExplorationOutcome.TimedOut);
var third=planner.Select(input,1).Choice!;
Check(third.Destination!=first.Destination&&third.Destination!=fallback.Destination,"timeout also backs off directed edge");
planner.Report(third.ChoiceId,1,origin,ExplorationOutcome.Interrupted);
Check(planner.Select(input,1).Choice!.Destination==third.Destination,"interruption does not invent blocked terrain");
// Fresh observations are required even when cooldown expires.
var fresh=new LocalExploration(session,actor);
Reject(()=>fresh.Select(input,4),"stale mixed observation refused");
Reject(()=>fresh.Select(input with{ActorId=Guid.NewGuid()},1),"cross actor mixed observation refused");
Reject(()=>fresh.Select(input with{SessionId=Guid.NewGuid()},1),"cross session mixed observation refused");
var only=input with{Candidates=[input.Candidates.Single(c=>c.Destination==At(1,0))]};
var failed=fresh.Select(only,1).Choice!;fresh.Report(failed.ChoiceId,1,origin,ExplorationOutcome.Blocked);
Check(fresh.Select(only,1).Reason=="all_safe_edges_in_backoff","sole step backs off");
var retry=Mix(Scene(origin,12)).Observation;
Check(fresh.Select(retry with{Candidates=retry.Candidates.Where(c=>c.Destination==At(1,0)).ToArray()},12).Choice!=null,"fresh re-observed step retries after cooldown");
var unknown=scene with{Cells=scene.Cells.Select(c=>c.X==1&&c.Y==64?c with{Visibility="unknown",Value=null}:c).ToArray()};
Check(!Mix(unknown).Observation.Candidates.Any(c=>c.Destination==At(1,0)),"unknown step support never enters mixed set");
var memory=new LocalObservationMemory(session,actor);memory.Observe(scene);
var blind=scene with{ObservationId=Guid.NewGuid(),Time=5,Cells=scene.Cells.Select(c=>c with{Visibility="unknown",Value=null}).ToArray()};
Check(ObservedStepRoutes.Project(memory.Observe(blind)).Count==0,"expired historical step evidence excluded");
// Frontier preference across terraces. Every offered hop below is projected anew
// from supplied synthetic geometry, not inserted as an unverified destination.
var frontier=new LocalExploration(session,actor);double clock=1;
ExplorationChoice Choose(Position from,params Position[] allowed){
 var projected=Mix(Scene(from,clock)).Observation;
 var filtered=projected.Candidates.Where(c=>allowed.Contains(c.Destination)).ToArray();
 Check(filtered.Length==allowed.Length,"all requested fixture hops have current geometric evidence");
 return frontier.Select(projected with{Candidates=filtered},clock).Choice!;
}
void Arrive(ExplorationChoice choice){frontier.Report(choice.ChoiceId,clock,choice.Destination,ExplorationOutcome.Arrived);clock+=.5;}
// A and D on high terrace; G and C straddle a down step.
var a=At(2,0);var b=At(3,0);var d=At(2,1);var e=At(2,2);var f=At(1,2);var g=At(1,1);var c=At(0,1);
var choice=Choose(a,b,d);Check(choice.Destination==d,"deterministic initial branch leaves observed frontier B");Arrive(choice);
Arrive(Choose(d,e));Arrive(Choose(e,f));Arrive(Choose(f,g));Arrive(Choose(g,c));
choice=Choose(c,g);Check(choice.Destination==g,"history cannot synthesize unavailable shortcut across terrace");Arrive(choice);
choice=Choose(g,c,d);Check(choice.Destination==d,"remembered frontier wins over recently explored dead branch");Arrive(choice);
choice=Choose(d,a);Arrive(choice);choice=Choose(a,b);
Check(choice.Destination==b&&choice.PriorVisits==0,"fresh evidence reaches remembered unvisited frontier");Arrive(choice);
Check(!frontier.HasPendingChoice,"mixed sequence releases final pending choice");
Check(frontier.MemoryCount<=128&&frontier.NeighborHintCount<=32*128,"mixed history bounded");
// Frozen observed-envelope guard is a geometry check, not native collision simulation.
var upRoute=mixed.Steps.Single(r=>r.Kind==StepKind.Up&&r.Destination==At(1,0));
var downRoute=mixed.Steps.Single(r=>r.Kind==StepKind.Down&&r.Destination==At(-1,0));
StepEnvelopeCheck Up(double feet,bool smooth=true,float rise=.5f)=>ObservedStepGuard.Check(upRoute,new(1.25,feet,.25),new(1.75,feet+1.77,.75),smooth,rise);
Check(Up(64.5) is {Inside:true,KnownSupportContactUsed:true},"up allows known landing support at exactly half metre smooth rise");
Check(!Up(64.499).Inside,"up rejects support penetration exceeding half metre");
Check(!Up(64.5,false).Inside,"up support contact requires smooth-rise mode");
Check(!Up(64.5,true,.25f).Inside,"up respects configured smaller smooth-rise allowance");
Check(!Up(64.49,true,2).Inside,"up allowance capped at half metre even if configured larger");
Check(Up(65) is {Inside:true,KnownSupportContactUsed:false},"up fully clear body uses observed air only");
StepEnvelopeCheck Down(double feet)=>ObservedStepGuard.Check(downRoute,new(-.1,feet,.25),new(.4,feet+1.77,.75),false,0);
Check(Down(63.98) is {Inside:true,KnownSupportContactUsed:true},"down allows exactly two centimetres of known source support contact");
Check(!Down(63.979).Inside,"down rejects deeper source-support contact");
Check(Down(64) is {Inside:true,KnownSupportContactUsed:false},"down clear source height needs no support exception");
var missingUp=upRoute with{Evidence=upRoute.Evidence.Where(c=>!(c.X==1&&c.Y==64&&c.Z==0)).ToArray()};
Check(ObservedStepGuard.Check(missingUp,new(1.25,64.5,.25),new(1.75,66.27,.75),true,.5f).Reason=="outside_observed_envelope","unknown landing support cannot use smooth-rise exception");
var missingDown=downRoute with{Evidence=downRoute.Evidence.Where(c=>!(c.X==0&&c.Y==63&&c.Z==0)).ToArray()};
Check(!ObservedStepGuard.Check(missingDown,new(-.1,63.98,.25),new(.4,65.75,.75),false,0).Inside,"unknown source support cannot use descent exception");
var waterUp=upRoute with{Evidence=upRoute.Evidence.Select(c=>c.X==1&&c.Y==64&&c.Z==0?c with{Value=18}:c).ToArray()};
Check(!ObservedStepGuard.Check(waterUp,new(1.25,64.5,.25),new(1.75,66.27,.75),true,.5f).Inside,"water is not known solid support");
Check(!ObservedStepGuard.Check(upRoute,new(8,65,.25),new(8.5,66.77,.75),true,.5f).Inside,"body outside frozen proof rejected");
Check(ObservedStepGuard.Check(upRoute,new(1.2,65,.25),new(1.8,66.77,.75),true,.5f).Reason=="invalid_body_bounds","oversized body width rejected");
Check(!ObservedStepGuard.Check(upRoute,new(1.25,65,.25),new(1.75,67,.75),true,.5f).Inside,"oversized body height rejected");
Check(!ObservedStepGuard.Check(upRoute,new(double.NaN,65,.25),new(1.75,66.77,.75),true,.5f).Inside,"nonfinite body rejected");
Check(!ObservedStepGuard.Check(upRoute,new(1.75,65,.25),new(1.25,66.77,.75),true,.5f).Inside,"inverted body rejected");
Check(!Up(65,true,float.NaN).Inside,"nonfinite smooth-rise configuration rejected");
Check(!Up(65,true,-1).Inside,"negative smooth-rise configuration rejected");
Console.WriteLine($"{checks}/{checks} synthetic mixed-navigation checks passed; no native movement, model, global visibility or live terrain tested.");
