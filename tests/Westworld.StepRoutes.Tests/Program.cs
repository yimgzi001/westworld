using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;
int checks=0;void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
var session=Guid.NewGuid();var actor=Guid.NewGuid();
ActorObservation Scene(int step) {
 var cells=new List<CellObservation>();
 for(int x=-2;x<=3;x++)for(int z=-2;z<=2;z++)for(int y=61;y<=68;y++) {
  int floor=x>=1?63+step:63;cells.Add(new(x,y,z,"known",y<=floor?3:0));
 }
 return new(session,actor,Guid.NewGuid(),1,new(.5,64,.5),1,.5f,[],cells.ToArray());
}
IReadOnlyList<ObservedStepRoute> Routes(ActorObservation o)=>ObservedStepRoutes.Project(new LocalObservationMemory(session,actor).Observe(o));
var up=Scene(1);var down=Scene(-1);
Check(Routes(up).Any(r=>r.Kind==StepKind.Up&&r.Destination==new Position(1.5,65,.5)),"up one cube");
Check(Routes(down).Any(r=>r.Kind==StepKind.Down&&r.Destination==new Position(1.5,63,.5)),"down one cube");
Check(Routes(up with{Position=new(.9,64,.5)}).Count==0,"origin intersects riser refused");
Check(Routes(Scene(0)).Count==0,"flat is not step");
Check(Routes(Scene(2)).Count==0,"two cube wall refused");
Check(Routes(Scene(-2)).Count==0,"two cube drop refused");
Check(Routes(up with{Position=new(.5,64.2,.5)}).Count==0,"ungrounded height refused");
Check(Routes(up with{Cells=up.Cells.Select(c=>c.X==1&&c.Y==66?c with{Value=3}:c).ToArray()}).Count==0,"head obstacle refused");
Check(Routes(up with{Cells=up.Cells.Select(c=>c.X==0&&c.Y==67?c with{Visibility="unknown",Value=null}:c).ToArray()}).Count==0,"unseen jump ceiling refused");
Check(Routes(up with{Cells=up.Cells.Select(c=>c.X==1&&c.Y==64?c with{Visibility="unknown",Value=null}:c).ToArray()}).Count==0,"unseen landing support refused");
Check(Routes(up with{Cells=up.Cells.Select(c=>c.X==1&&c.Y==64?c with{Value=18}:c).ToArray()}).Count==0,"water landing refused");
Check(Routes(up with{Cells=up.Cells.Select(c=>c.X==1&&c.Y==64?c with{Value=7}:c).ToArray()}).Count==0,"falling sand landing refused");
Check(Routes(up with{Health=0}).Count==0,"dead refused");
Check(Routes(up).All(r=>r.Evidence.All(c=>up.Cells.Any(o=>o.X==c.X&&o.Y==c.Y&&o.Z==c.Z&&o.Value==c.Value))),"proof only observed");
Check(Routes(up).All(r=>r.MaximumAge==0&&r.SessionId==session&&r.ActorId==actor),"scope age retained");
var memory=new LocalObservationMemory(session,actor);memory.Observe(up);
var later=up with{ObservationId=Guid.NewGuid(),Time=2,Cells=up.Cells.Select(c=>c with{Visibility="unknown",Value=null}).ToArray()};
Check(ObservedStepRoutes.Project(memory.Observe(later)).Any(r=>r.MaximumAge==1&&r.Evidence.All(c=>!c.IsCurrent)),"historical proof labelled");
Check(ObservedStepRoutes.Project(memory.Observe(later with{ObservationId=Guid.NewGuid(),Time=5})).Count==0,"expired proof removed");
Console.WriteLine($"{checks}/{checks} synthetic step geometry checks passed; native jumping not tested");
