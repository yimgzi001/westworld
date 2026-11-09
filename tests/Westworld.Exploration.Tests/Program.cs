using Westworld.Runtime.Core;
static class Tests {
 static int count;static readonly Guid Session=Guid.NewGuid(),Actor=Guid.NewGuid();
 static Position P(double x,double z=0,double y=64)=>new(x,y,z);
 static ExplorationCandidate C(double x,double z=0,bool safe=true)=>new(Guid.NewGuid(),P(x,z),safe);
 static ExplorationObservation O(double time,Position origin,params ExplorationCandidate[] c)=>new(Session,Actor,Guid.NewGuid(),time,origin,c);
 static void Check(bool x,string name){if(!x)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static bool Reject(Action f){try{f();return false;}catch(Exception e)when(e is ArgumentException or InvalidOperationException){return true;}}
 static void Main(){
  var planner=new LocalExploration(Session,Actor,4,10);
  var a=C(1);var hidden=C(2,safe:false);var picked=planner.Select(O(0,P(0),a,hidden),0).Choice!;
  Check(picked.EvidenceId==a.EvidenceId&&picked.Destination==a.Destination,"unknown not treated as safe");
  Check(picked.SessionId==Session&&picked.ActorId==Actor&&picked.PriorVisits==0&&picked.Reason.Contains("lowest_visits"),"scoped explainable choice");
  Check(planner.Select(O(0,P(0),a),0).Reason=="actor_busy","one choice at a time");
  Check(Reject(()=>planner.Report(Guid.NewGuid(),0,P(1),ExplorationOutcome.Arrived))&&planner.HasPendingChoice,"forged choice receipt rejected");
  Check(Reject(()=>planner.Report(picked.ChoiceId,0,P(0),ExplorationOutcome.Arrived))&&planner.HasPendingChoice,"false arrival rejected without losing pending");
  planner.Report(picked.ChoiceId,1,P(1),ExplorationOutcome.Arrived);
  Check(!planner.HasPendingChoice&&planner.MemoryCount==2,"actual reached cells recorded");
  Check(Reject(()=>planner.Report(picked.ChoiceId,1,P(1),ExplorationOutcome.Arrived)),"replayed receipt rejected");
  var back=planner.Select(O(1,P(1),C(0)),1).Choice!;planner.Report(back.ChoiceId,2,P(0),ExplorationOutcome.Arrived);
  var fresh=planner.Select(O(2,P(0),C(1),C(0,1)),2).Choice!;
  Check(fresh.Destination==P(0,1)&&fresh.PriorVisits==0,"unvisited candidate preferred over visited");
  planner.Report(fresh.ChoiceId,3,P(0),ExplorationOutcome.Blocked);
  var alternative=planner.Select(O(3,P(0),C(1),C(0,1)),3).Choice!;
  Check(alternative.Destination==P(1),"blocked directed edge avoided");
  planner.Report(alternative.ChoiceId,4,P(0),ExplorationOutcome.Interrupted);
  Check(planner.Select(O(4,P(0),C(0,1)),4).Reason=="all_safe_edges_in_backoff","blocked-only observation returns reason");
  var expired=planner.Select(O(13,P(0),C(0,1)),13).Choice!;
  Check(expired!=null,"backoff expires at deadline");planner.Report(expired!.ChoiceId,13,P(0),ExplorationOutcome.Interrupted);
  var again=planner.Select(O(13,P(0),C(0,1)),13).Choice!;Check(again!=null,"interrupt is not a fabricated blocked edge");planner.Report(again!.ChoiceId,13,P(0),ExplorationOutcome.Interrupted);
  Check(planner.Select(O(13,P(0)),13).Reason=="no_known_safe_local_candidate","empty candidates do not invent routes");
  var isolate=new LocalExploration(Session,Guid.NewGuid());Check(isolate.MemoryCount==0,"private actor memory starts isolated");
  Check(Reject(()=>isolate.Select(O(13,P(0),C(1)),13)),"cross actor scope rejected");
  Check(Reject(()=>planner.Select(O(13,P(0),C(1)) with {SessionId=Guid.NewGuid()},13)),"cross session rejected");
  Check(Reject(()=>planner.Select(O(10,P(0),C(1)),13)),"stale evidence rejected");
  Check(Reject(()=>planner.Select(O(14,P(0),C(1)),13)),"future evidence rejected");
  Check(Reject(()=>planner.Select(O(12,P(0),C(1)),12)),"clock rollback rejected");
  Check(Reject(()=>planner.Select(O(13,P(0),C(1)),double.NaN)),"nonfinite clock rejected");
  Check(Reject(()=>planner.Select(O(13,P(double.NaN),C(1)),13)),"nonfinite origin rejected");
  Check(Reject(()=>planner.Select(O(13,P(0),C(double.PositiveInfinity)),13)),"nonfinite destination rejected");
  Check(Reject(()=>planner.Select(O(13,P(0),C(5)),13)),"nonlocal destination rejected");
  Check(Reject(()=>planner.Select(O(13,P(0),C(1),C(1.5)),13)),"duplicate target cell rejected");
  Check(Reject(()=>planner.Select(O(13,P(0),a,a with {Destination=P(2)}),13)),"duplicate evidence id rejected");
  Check(Reject(()=>planner.Select(O(13,P(0),a with {EvidenceId=Guid.Empty}),13)),"missing evidence rejected");
  Check(Reject(()=>planner.Select(O(13,P(0),Enumerable.Range(0,33).Select(i=>C(i)).ToArray()),13)),"candidate count bounded");
  var bounded=new LocalExploration(Session,Actor,4);
  for(int i=0;i<50;i++){var choice=bounded.Select(O(i,P(i),C(i+1)),i).Choice!;bounded.Report(choice.ChoiceId,i,P(i+1),ExplorationOutcome.Arrived);Check(bounded.MemoryCount<=4,"bounded visit memory "+i);}
  var still=new LocalExploration(Session,Actor);for(int i=0;i<10;i++)still.Select(O(i,P(0)),i);
  var outbound=still.Select(O(10,P(0),C(1)),10).Choice!;still.Report(outbound.ChoiceId,10,P(1),ExplorationOutcome.Arrived);
  var inbound=still.Select(O(11,P(1),C(0)),11).Choice!;Check(inbound.PriorVisits==1,"polling in place does not inflate visits");
  Check(Reject(()=>still.Report(inbound.ChoiceId,11,P(0),(ExplorationOutcome)999)),"invalid outcome rejected");
  Check(Task.Run(()=>Reject(()=>still.Select(O(11,P(1)),11))).GetAwaiter().GetResult(),"game thread ownership enforced");
  var source=new[]{C(1)};var copyTest=new LocalExploration(Session,Actor);var preserved=copyTest.Select(O(0,P(0),source),0).Choice!;source[0]=C(2);Check(preserved.Destination==P(1),"source-array changes cannot rewrite signed choice");
  Check(Reject(()=>new LocalExploration(Guid.Empty,Actor)),"invalid instance scope rejected");
  Check(Reject(()=>new LocalExploration(Session,Actor,3)),"invalid memory capacity rejected");
  var diagonal=new LocalExploration(Session,Actor);var d=diagonal.Select(O(0,P(0.5,0.5),C(0.5+Math.Sqrt(0.5),0.5+Math.Sqrt(0.5))),0).Choice;Check(d!=null,"one meter diagonal tolerates floating point rounding");
  var timed=new LocalExploration(Session,Actor);var t=timed.Select(O(0,P(0),C(1)),0).Choice!;timed.Report(t.ChoiceId,1,P(0),ExplorationOutcome.TimedOut);Check(timed.Select(O(1,P(0),C(1)),1).Reason=="all_safe_edges_in_backoff","timeout feedback backs off edge");
  var reverse=timed.Select(O(2,P(1),C(0)),2).Choice;Check(reverse!=null,"blocked edge memory is directional");
  var failureBounded=new LocalExploration(Session,Actor,4,300);for(int i=0;i<20;i++){var c=failureBounded.Select(O(i,P(i*2),C(i*2+1)),i).Choice!;failureBounded.Report(c.ChoiceId,i,P(i*2),ExplorationOutcome.Blocked);}
  Check(failureBounded.MemoryCount<=4,"combined failed-edge and visit memory bounded");
  var unchanged=new LocalExploration(Session,Actor);Check(Reject(()=>unchanged.Select(O(100,P(0),C(5)),100)),"invalid future request rejected");Check(unchanged.Select(O(0,P(0),C(1)),0).Choice!=null,"rejected input does not advance internal clock");
  Console.WriteLine($"{count}/{count} checks passed; pure local heuristic, no native game, global map, model, or physical safety proof.");
 }
}
