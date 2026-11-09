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
  var corridor=new LocalExploration(Session,Actor);Position cursor=P(0);var corridorCells=new HashSet<Position>{cursor};
  for(int step=0;step<60;step++){
   var choices=cursor.X==0?new[]{C(1)}:new[]{C(cursor.X-1),C(cursor.X+1)};
   var choice=corridor.Select(O(step,cursor,choices),step).Choice!;corridor.Report(choice.ChoiceId,step,choice.Destination,ExplorationOutcome.Arrived);cursor=choice.Destination;corridorCells.Add(cursor);
  }
  Check(cursor==P(60)&&corridorCells.Count==61,"long corridor retains forward coverage without immediate reversal");
  var ring=new[]{P(0,0),P(1,0),P(2,0),P(2,1),P(2,2),P(1,2),P(0,2),P(0,1)};
  var ringPlanner=new LocalExploration(Session,Actor);int ringIndex=0,previous=-1,reversals=0;var ringSeen=new HashSet<int>{0};
  for(int step=0;step<96;step++){
   int left=(ringIndex+7)%8,right=(ringIndex+1)%8;
   var choice=ringPlanner.Select(O(step,ring[ringIndex],new(Guid.NewGuid(),ring[left],true),new(Guid.NewGuid(),ring[right],true)),step).Choice!;
   int next=Array.IndexOf(ring,choice.Destination);if(next==previous)reversals++;ringPlanner.Report(choice.ChoiceId,step,choice.Destination,ExplorationOutcome.Arrived);previous=ringIndex;ringIndex=next;ringSeen.Add(next);
  }
  Check(ringSeen.Count==8&&reversals==0,"ninety-six-step ring avoids two-cell ping-pong without inventing exits");
  var deadEnd=new LocalExploration(Session,Actor);cursor=P(0);var deadEndPath=new List<Position>{cursor};
  for(int step=0;step<6;step++){
   var candidates=new List<ExplorationCandidate>();if(cursor.X>-2)candidates.Add(C(cursor.X-1));if(cursor.X<2)candidates.Add(C(cursor.X+1));
   var choice=deadEnd.Select(O(step,cursor,candidates.ToArray()),step).Choice!;deadEnd.Report(choice.ChoiceId,step,choice.Destination,ExplorationOutcome.Arrived);cursor=choice.Destination;deadEndPath.Add(cursor);
  }
  Check(deadEndPath.SequenceEqual(new[]{P(0),P(-1),P(-2),P(-1),P(0),P(1),P(2)}),"dead end permits return then reaches observed unvisited branch");
  LocalExploration FrontierHistory(){
   var f=new LocalExploration(Session,Actor);
   void Hop(double time,Position origin,Position expected,params ExplorationCandidate[] candidates){var choice=f.Select(O(time,origin,candidates),time).Choice!;Check(choice.Destination==expected,"frontier history hop "+time);f.Report(choice.ChoiceId,time,choice.Destination,ExplorationOutcome.Arrived);}
   Hop(0,P(2,0),P(2,1),C(3,0),C(2,1));
   Hop(1,P(2,1),P(2,2),C(2,0),C(2,2));
   Hop(2,P(2,2),P(1,2),C(2,1),C(1,2));
   Hop(3,P(1,2),P(1,1),C(2,2),C(1,1));
   Hop(4,P(1,1),P(0,1),C(1,2),C(0,1));return f;
  }
  var frontier=FrontierHistory();var toward=frontier.Select(O(5,P(0,1),C(1,1),C(2,1)),5).Choice!;
  Check(toward.Destination==P(2,1),"observed frontier beats old coordinate tie that would revisit ring");frontier.Report(toward.ChoiceId,5,toward.Destination,ExplorationOutcome.Arrived);
  var nextFrontier=frontier.Select(O(6,P(2,1),C(2,0),C(2,2)),6).Choice!;Check(nextFrontier.Destination==P(2,0),"known first hop leads back toward remembered branch");frontier.Report(nextFrontier.ChoiceId,6,nextFrontier.Destination,ExplorationOutcome.Arrived);
  var branch=frontier.Select(O(7,P(2,0),C(2,1),C(3,0)),7).Choice!;Check(branch.Destination==P(3,0),"fresh evidence finally admits remembered unvisited branch");
  var noStaleRoute=FrontierHistory();var onlyCurrent=noStaleRoute.Select(O(5,P(0,1),C(1,1)),5).Choice!;Check(onlyCurrent.Destination==P(1,1),"historical graph cannot invent missing current first-hop evidence");
  var blockedFrontier=FrontierHistory();var blockedChoice=blockedFrontier.Select(O(5,P(0,1),C(1,1),C(2,1)),5).Choice!;blockedFrontier.Report(blockedChoice.ChoiceId,5,P(0,1),ExplorationOutcome.Blocked);
  var fallback=blockedFrontier.Select(O(6,P(0,1),C(1,1),C(2,1)),6).Choice!;Check(fallback.Destination==P(1,1),"failed frontier first hop falls back to current safe alternative");blockedFrontier.Report(fallback.ChoiceId,6,P(0,1),ExplorationOutcome.Interrupted);
  Check(blockedFrontier.Select(O(15,P(0,1),C(2,1)),15).Choice!=null,"frontier failed edge retry returns only after backoff expires");
  var staleHints=FrontierHistory();var staleSelection=staleHints.Select(O(40,P(0,1),C(1,1),C(2,1)),40).Choice!;
  Check(staleSelection.Reason.Contains("frontier_hops=none"),"expired neighbor hints never count as a live frontier");
  Check(toward.Reason.Contains("frontier_hops=2"),"selection exposes bounded remembered frontier distance");
  Check(corridor.MemoryCount<=128&&corridor.NeighborHintCount<=corridor.MemoryCount*32,"neighbor hints bounded separately within retained visit records");
  Check(bounded.MemoryCount<=4&&bounded.NeighborHintCount<=128,"evicting visit records also evicts their graph hints");
  Console.WriteLine($"{count}/{count} checks passed; pure local heuristic, no native game, global map, model, or physical safety proof.");
 }
}
