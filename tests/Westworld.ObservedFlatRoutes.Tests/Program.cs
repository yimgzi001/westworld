using Westworld.Scapi.Adapter;
using Westworld.Runtime.Core;
int count=0;void Check(bool v){if(!v)throw new Exception($"assertion {count+1}");count++;}
var sid=Guid.NewGuid();var actor=Guid.NewGuid();
var all=new List<CellObservation>();for(int x=-4;x<=5;x++)for(int z=-4;z<=5;z++)for(int y=63;y<=66;y++)all.Add(new(x,y,z,"known",y==63?2:0));
LocalObservationWindow Window(IEnumerable<CellObservation> cells,double t=1,Position? p=null,float health=1)=>new LocalObservationMemory(sid,actor).Observe(new(sid,actor,Guid.NewGuid(),t,p??new(.5,64,.5),health,1,[],cells.ToArray()));
var w=Window(all);
foreach(var end in new[]{new Position(3.5,64,.5),new(-2.5,64,.5),new(.5,64,3.5),new(.5,64,-2.5)}){var r=ObservedFlatRoutes.Project(w,end);Check(r is not null);Check(r!.Points.Count==3);Check(r.Points[^1]==end);Check(r.SessionId==sid&&r.ActorId==actor);}
Check(ObservedFlatRoutes.Project(w,new(4.5,64,.5)) is not null);
Check(ObservedFlatRoutes.Project(w,new(4.6,64,.5)) is null);
Check(ObservedFlatRoutes.Project(w,new(1,64,.5)) is null);
Check(ObservedFlatRoutes.Project(w,new(3.5,65,.5)) is null);
Check(ObservedFlatRoutes.Project(w,new(2,64,2)) is null);
Check(ObservedFlatRoutes.Project(w,new(double.NaN,64,.5)) is null);
Check(ObservedFlatRoutes.Project(Window(all,health:0),new(3.5,64,.5)) is null);
foreach(var missing in new[]{(2,63,0),(2,64,0),(2,65,0)}){
 var cells=all.Where(c=>(c.X,c.Y,c.Z)!=missing);Check(ObservedFlatRoutes.Project(Window(cells),new(3.5,64,.5)) is null);
}
Check(ObservedFlatRoutes.Project(Window(all.Select(c=>c.X==2&&c.Y==65&&c.Z==0?c with{Value=2}:c)),new(3.5,64,.5)) is null);
Check(ObservedFlatRoutes.Project(Window(all.Select(c=>c.X==2&&c.Y==63&&c.Z==0?c with{Value=0}:c)),new(3.5,64,.5)) is null);
Check(ObservedFlatRoutes.Project(Window(all.Select(c=>c.X==2&&c.Y==64&&c.Z==0?c with{Visibility="unknown",Value=null}:c)),new(3.5,64,.5)) is null);
var memory=new LocalObservationMemory(sid,actor);memory.Observe(new(sid,actor,Guid.NewGuid(),1,new(.5,64,.5),1,1,[],all.ToArray()));
var historical=memory.Observe(new(sid,actor,Guid.NewGuid(),3.5,new(.5,64,.5),1,1,[],[]));
Check(ObservedFlatRoutes.Project(historical,new(3.5,64,.5))?.MaximumAge==2.5);
var expired=memory.Observe(new(sid,actor,Guid.NewGuid(),4.01,new(.5,64,.5),1,1,[],[]));Check(ObservedFlatRoutes.Project(expired,new(3.5,64,.5)) is null);
Check(ObservedFlatRoutes.Project(w,new(1.4999,64,.5)) is not null);
Check(ObservedFlatRoutes.Project(w,new(1.498,64,.5)) is null);
Check(ObservedFlatRoutes.Project(w,new(3.5,64,.5009)) is not null);
Check(ObservedFlatRoutes.Project(w,new(3.5,64,.502)) is null);
var cornerOrigin=new Position(.5,64,.7498);var cornerTarget=new Position(3.5,64,.7502);
Check(ObservedFlatRoutes.Project(Window(all,p:cornerOrigin),cornerTarget) is not null);
var cornerMissing=ObservedFlatRoutes.Inspect(Window(all.Where(c=>(c.X,c.Y,c.Z)!=(1,65,1)),p:cornerOrigin),cornerTarget);
Check(cornerMissing.Route is null);
Check(cornerMissing.Issues.Any(c=>c.X==1&&c.Y==65&&c.Z==1&&c.Reason=="missing_or_unknown"));
Check(ObservedFlatRoutes.Inspect(w,new(2,64,2)).Reason=="not_axis_aligned");
Check(ObservedFlatRoutes.Inspect(w,new(4.6,64,.5)).Reason=="distance_out_of_range");
var hole=ObservedFlatRoutes.Inspect(Window(all.Select(c=>c.X==2&&c.Y==63&&c.Z==0?c with{Value=0}:c)),new(3.5,64,.5));
Check(hole.Issues.Any(c=>c.Reason=="unsupported_ground"&&c.Support));
var wall=ObservedFlatRoutes.Inspect(Window(all.Select(c=>c.X==2&&c.Y==65&&c.Z==0?c with{Value=2}:c)),new(3.5,64,.5));
Check(wall.Issues.Any(c=>c.Reason=="observed_solid"&&!c.Support));
Check(ObservedFlatRoutes.Inspect(w,new(3.5,64,.5)).RequiredCount==12);
// Dispatch freshness is stricter than the unchanged three-second evidence TTL.
var refresh=ObservedFlatRoutes.Inspect(historical,new(3.5,64,.5),2);
Check(refresh.Route is null);
Check(refresh.Issues.Count==refresh.RequiredCount&&refresh.Issues.All(c=>c.Reason=="refresh_needed"));
Check(historical.Cells.All(c=>c.ObservedAt==1&&c.Age==2.5));
Check(ObservedFlatRoutes.Inspect(historical,new(3.5,64,.5),3).Route is not null);
foreach(var limit in new[]{double.NaN,-1d,3.001})Check(ObservedFlatRoutes.Inspect(w,new(3.5,64,.5),limit).Reason=="invalid_evidence_age_limit");
var boundaryMemory=new LocalObservationMemory(sid,actor);
boundaryMemory.Observe(new(sid,actor,Guid.NewGuid(),1,new(.5,64,.5),1,1,[],all.ToArray()));
var two=boundaryMemory.Observe(new(sid,actor,Guid.NewGuid(),3,new(.5,64,.5),1,1,[],[]));
Check(ObservedFlatRoutes.Inspect(two,new(3.5,64,.5),2).Route is not null);
var overTwo=boundaryMemory.Observe(new(sid,actor,Guid.NewGuid(),3.001,new(.5,64,.5),1,1,[],[]));
Check(ObservedFlatRoutes.Inspect(overTwo,new(3.5,64,.5),2).Issues.All(c=>c.Reason=="refresh_needed"));
var newSeen=boundaryMemory.Observe(new(sid,actor,Guid.NewGuid(),3.01,new(.5,64,.5),1,1,[],all.ToArray()));
Check(ObservedFlatRoutes.Inspect(newSeen,new(3.5,64,.5),2).Route?.MaximumAge==0);
Check(ObservedFlatRoutes.Inspect(expired,new(3.5,64,.5),2).Route is null);
Console.WriteLine($"PASS {count}/{count} pure observed flat corridor checks; no native movement executed");
