using System.Diagnostics;
using Engine;
using Game;
using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

public enum BoundedNavigationStatus { Idle, Running, Stopping, Arrived, Failed, Cancelled }
public sealed record BoundedNavigationSnapshot(Guid RequestId,BoundedNavigationStatus Status,string Reason,bool Busy,int Segments,double Path,double Remaining) {
 public NavigationReadiness? Readiness {get;init;}
 public NavigationReadiness? StartFailureReadiness {get;init;}
 public double ElapsedSeconds {get;init;}
 public double BudgetSeconds {get;init;}
 public string CompletionMeaning {get;init;}=NavigationCompletionSemantics.Navigation;
}
/// <summary>Pure budget rules. Distance is an endpoint objective, never permission to cross unknown cells.</summary>
public static class BoundedNavigationRules {
 public static void Validate(double bearing,double distance,double seconds){if(!double.IsFinite(bearing)||bearing<0||bearing>=360||!double.IsFinite(distance)||distance<1||distance>4||!double.IsFinite(seconds)||seconds<1||seconds>12)throw new ArgumentOutOfRangeException("Finite bearing[0,360), distance[1,4], seconds[1,12] required");}
 public static string Next(double remaining,double elapsed,double budget,int segments,bool segmentSucceeded){
  if(!double.IsFinite(remaining)||remaining<0||!double.IsFinite(elapsed)||elapsed<0)return "invalid_state";
  if(!segmentSucceeded)return "segment_failed";
  if(elapsed>=budget)return "wall_budget";
  if(remaining<=.4)return "arrived";
  return segments>=4?"segment_budget":"continue";
 }
}
/// <summary>Bounded multi-segment navigation. Host MUST block all other writes while Busy,
/// including between segment leases. Uses the supplied sole native owner; never calls a model.
/// Cancellation remains Busy until the native skill settles or quarantines. No world lookup.</summary>
public sealed class BoundedLocalNavigation {
 readonly NavigationSampleGate sampleGate=new();
 NavigationReadiness? startFailureReadiness;
 readonly Guid telemetryOwnerId=Guid.NewGuid();
 readonly ComponentPlayer player;readonly LocalNavigationSkill skill;readonly LocalNavigationSkillLease lease;readonly Action<object> audit;
 readonly int thread=Environment.CurrentManagedThreadId;long started;double budget,path;int segments;Position target=new(0,0,0);Guid request,segmentRequest;string reason="idle";BoundedNavigationStatus status;BoundedNavigationStatus stoppingResult;bool counted,ownershipLost;
 public BoundedLocalNavigation(ComponentPlayer player,LocalNavigationSkill skill,LocalNavigationSkillLease lease,Action<object>? audit=null){ArgumentNullException.ThrowIfNull(player);ArgumentNullException.ThrowIfNull(skill);ArgumentNullException.ThrowIfNull(lease);if(!skill.Matches(player,lease))throw new ArgumentException("Player, skill and lease must have identical binding");this.player=player;this.skill=skill;this.lease=lease;this.audit=audit??(_=>{});}
 public bool Matches(ComponentPlayer candidate,Game.Westworld.AtomicActions candidateActions)=>ReferenceEquals(player,candidate)&&skill.Matches(candidate,candidateActions);
 public bool OwnerQuarantined=>ownershipLost||lease.Quarantined||lease.ClockFaulted;
 public bool Busy=>status is BoundedNavigationStatus.Running or BoundedNavigationStatus.Stopping;
 public bool Terminal=>status is BoundedNavigationStatus.Arrived or BoundedNavigationStatus.Failed or BoundedNavigationStatus.Cancelled;
 public BoundedNavigationSnapshot Snapshot=>new(request,status,reason,Busy,segments,path+(Busy&&!counted?skill.ActualHorizontalPath:0) ,Remaining){Readiness=skill.ReadReadiness(),StartFailureReadiness=startFailureReadiness,ElapsedSeconds=request==Guid.Empty?0:Stopwatch.GetElapsedTime(started).TotalSeconds,BudgetSeconds=budget};
 double Remaining{get{var b=player.ComponentBody.Position;return Math.Sqrt(Math.Pow(b.X-target.X,2)+Math.Pow(b.Z-target.Z,2));}}
 void Owner(){if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("Game thread required");}
 public void Start(Guid requestId,double bearing,double distance,double maxSeconds=12){Owner();if(Busy||(!skill.Terminal&&skill.Status!=LocalNavigationStatus.Idle))throw new InvalidOperationException("Navigation owner busy");BoundedNavigationRules.Validate(bearing,distance,maxSeconds);if(requestId==Guid.Empty||OwnerQuarantined||lease.ClockFaulted)throw new InvalidOperationException("Invalid request or quarantined owner");
  var p=player.ComponentBody.Position;double radians=bearing*Math.PI/180;target=new(p.X+Math.Sin(radians)*distance,p.Y,p.Z+Math.Cos(radians)*distance);request=requestId;budget=maxSeconds;started=Stopwatch.GetTimestamp();segments=0;path=0;startFailureReadiness=null;status=BoundedNavigationStatus.Running;reason="started";Begin();Sample();}
 void Begin(){counted=true;segmentRequest=Guid.NewGuid();try{skill.Start(segmentRequest,target);counted=false;segments++;audit(new{kind="bounded_navigation_segment",request,segments,target,elapsedSeconds=Stopwatch.GetElapsedTime(started).TotalSeconds,monotonicTicks=Stopwatch.GetTimestamp(),monotonicFrequency=Stopwatch.Frequency});}catch(Exception e){startFailureReadiness=(e as NavigationNotReadyException)?.Readiness;reason=startFailureReadiness is null?"segment_start_"+e.GetType().Name:"body_not_ready";if(skill.RequestId==segmentRequest&&!skill.Terminal&&skill.Status!=LocalNavigationStatus.Idle){counted=false;stoppingResult=BoundedNavigationStatus.Failed;status=BoundedNavigationStatus.Stopping;skill.Cancel(reason);}else status=BoundedNavigationStatus.Failed;}}
 public void Cancel(string why="cancel_requested"){Owner();if(!Busy)return;if(skill.RequestId!=segmentRequest){ownershipLost=true;status=BoundedNavigationStatus.Failed;reason="request_ownership_lost";Sample();throw new InvalidOperationException("Navigation request ownership lost");}reason=why;stoppingResult=BoundedNavigationStatus.Cancelled;status=BoundedNavigationStatus.Stopping;skill.Cancel(why);Sample();}
 public void Update(){Owner();if(!Busy){if(request!=Guid.Empty)Sample();return;}try{if(skill.RequestId!=segmentRequest){ownershipLost=true;status=BoundedNavigationStatus.Failed;reason="request_ownership_lost";return;}skill.Update();if(skill.Terminal&&!counted){path+=skill.ActualHorizontalPath;counted=true;}
  if(status==BoundedNavigationStatus.Stopping){if(skill.Terminal||skill.Status==LocalNavigationStatus.Idle){status=lease.Quarantined?BoundedNavigationStatus.Failed:stoppingResult;audit(new{kind="bounded_navigation_terminal",snapshot=Snapshot,lease.Quarantined,elapsedSeconds=Stopwatch.GetElapsedTime(started).TotalSeconds,monotonicTicks=Stopwatch.GetTimestamp(),monotonicFrequency=Stopwatch.Frequency});}return;}
  double elapsed=Stopwatch.GetElapsedTime(started).TotalSeconds;if(elapsed>=budget){reason="wall_budget";stoppingResult=BoundedNavigationStatus.Failed;status=BoundedNavigationStatus.Stopping;skill.Cancel(reason);return;}
  if(!skill.Terminal)return;
  string next=BoundedNavigationRules.Next(Remaining,elapsed,budget,segments,skill.Status==LocalNavigationStatus.Arrived&&!lease.Quarantined);
  if(next=="continue"){Begin();return;}status=next=="arrived"?BoundedNavigationStatus.Arrived:BoundedNavigationStatus.Failed;reason=next=="segment_failed"?"segment_"+skill.Reason:next;audit(new{kind="bounded_navigation_terminal",snapshot=Snapshot,lease.Quarantined,elapsedSeconds=Stopwatch.GetElapsedTime(started).TotalSeconds,monotonicTicks=Stopwatch.GetTimestamp(),monotonicFrequency=Stopwatch.Frequency});
  }finally{Sample();}


 }
 void Sample(){var b=player.ComponentBody;var v=b.Velocity;var pos=b.Position;
  double now=(double)Stopwatch.GetTimestamp()/Stopwatch.Frequency;
  bool stopped=v.LengthSquared()<=.0001f; // kinematic <=0.01m/s, not an ownership/settling assertion
  var pacing=sampleGate.Observe(now,new(telemetryOwnerId,request,segmentRequest,status.ToString()+"/"+skill.Status.ToString(),reason,Busy,lease.IsHeld(skill.RequestId),stopped));
  if(!pacing.Emit)return;
  audit(new{kind="navigation_sample",schemaVersion=2,samplePolicy="10hz_periodic_plus_boundaries",sampleTrigger=pacing.Trigger,
   sampleIntervalSeconds=pacing.IntervalSeconds,coverageUnknown=pacing.CoverageUnknown,suppressedSamples=pacing.SuppressedSincePrevious,
   kinematicallyStopped=stopped,boundedState=status.ToString(),segmentRequestId=segmentRequest,monotonicSeconds=now,
   requestId=request,ownerId=telemetryOwnerId,state=Busy?skill.Status.ToString():status.ToString(),
   busy=Busy,ownsLease=lease.IsHeld(skill.RequestId),horizontalSpeed=v.XZ.Length(),
   position=new[]{pos.X,pos.Y,pos.Z},velocity=new[]{v.X,v.Y,v.Z},grounded=b.StandingOnValue.HasValue,segmentCount=segments,reason});
 }
}
