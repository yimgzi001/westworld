using System.Diagnostics;
using Engine;
using Game;
using Game.Westworld;
using Westworld.Runtime.Core;
namespace Westworld.Scapi.Adapter;

public enum LocalNavigationStatus { Idle, Observing, Looking, Aligning, Moving, Settling, Arrived, Failed, Cancelled }

/// <summary>One observed local segment, not a pathfinder or autonomous food policy.
/// Caller lends exclusive use of its EXISTING AtomicActions until terminal status.
/// No model call, second owner, terrain lookup or hidden entity tracking.</summary>
public sealed class LocalNavigationSkill {
 readonly ComponentPlayer player; readonly AtomicActions actions; readonly NativeActorSession senses;
 readonly LocalNavigationSkillLease lease;
 readonly Guid actor; readonly LocalObservationMemory memory; readonly Action<object> audit;
 readonly int thread=Environment.CurrentManagedThreadId;
 NavigationLookPlan look=new(16); AtomicActionRun? run; NavigationSettling? settling;
 IReadOnlyList<LocalObservedCell> proof=Array.Empty<LocalObservedCell>();ObservedStepRoute? step;
 Position origin=new(0,0,0), destination=new(0,0,0), bias=new(0,0,0);
 long started,proofStarted,settlingStarted; double initialAge; double lastGameTime; Vector3 previous;
 LocalNavigationStatus afterSettle; string? alignedCell; int preparations;bool cancelled;
 public LocalNavigationStatus Status {get;private set;}=LocalNavigationStatus.Idle;
 public string Reason {get;private set;}="idle";
 public Guid RequestId {get;private set;}
 public double ActualHorizontalPath {get;private set;}
 public bool Terminal=>Status is LocalNavigationStatus.Arrived or LocalNavigationStatus.Failed or LocalNavigationStatus.Cancelled;
 public LocalNavigationSkill(ComponentPlayer player,AtomicActions existingActions,NativeActorSession observationOnlySession,
     Guid actorId,LocalNavigationSkillLease hostLease,Action<object>? audit=null) {
  if(!existingActions.Queries.IsBoundTo(player.Entity)||observationOnlySession.ActionsEnabled)
   throw new ArgumentException("Bound existing owner and observation-only sensor required");
  if(!hostLease.Matches(existingActions,observationOnlySession))throw new ArgumentException("Lease owner/session mismatch");
  lease=hostLease;this.player=player;actions=existingActions;senses=observationOnlySession;actor=actorId;
  // Resolve actual binding before accepting any movement authority.
  var o=senses.Observe(senses.SessionId,actor);
  if(senses.Registry.Read(actor).EntityKey!="player:"+player.PlayerData.PlayerIndex||Distance(o.Position,Pos(player.ComponentBody.Position))>.001)throw new ArgumentException("Sensor binding mismatch");
  memory=new(senses.SessionId,actor);this.audit=audit??(_=>{});
 }
 void Owner(){if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("Game thread required");}
 public void Start(Guid requestId,Position requestedDirectionPoint) {
  Owner();if(Status!=LocalNavigationStatus.Idle&&!Terminal)throw new InvalidOperationException("Skill still owns action/settling");
  if(requestId==Guid.Empty||!Finite(requestedDirectionPoint))throw new ArgumentException("Invalid request");
  if(lease.ClockFaulted)throw new InvalidOperationException("Clock fault: new observation session required");
  var body=player.ComponentBody;var velocity=body.Velocity;
  if(!body.StandingOnValue.HasValue||body.ParentBody is not null||body.ImmersionFactor>.1
    ||Math.Abs(velocity.Y)>.1||velocity.XZ.Length()>.1||player.ComponentHealth.Health<=0)
   throw new InvalidOperationException("Stable living ground actor required before new segment");
  if(!lease.TryAcquire(requestId,GameTime))throw new InvalidOperationException("Native owner busy, quarantined or clock invalid");
  RequestId=requestId;bias=requestedDirectionPoint;started=Stopwatch.GetTimestamp();lastGameTime=GameTime;
  previous=player.ComponentBody.Position;ActualHorizontalPath=0;look=new(16);alignedCell=null;preparations=0;cancelled=false;
  run=null;settling=null;Status=LocalNavigationStatus.Observing;Reason="fresh_local_segment_requested";
  StopAndSettle(LocalNavigationStatus.Observing,"initial_stable_dwell_required");
 }
 double GameTime=>player.Project.FindSubsystem<SubsystemTime>(true).GameTime;
 public void Cancel(string reason="cancel_requested") {Owner();if(Status==LocalNavigationStatus.Idle||Terminal)return;cancelled=true;StopAndSettle(LocalNavigationStatus.Cancelled,reason);}
 public void Update() {
  Owner();if(Status==LocalNavigationStatus.Idle||Terminal)return;
  var body=player.ComponentBody;ActualHorizontalPath+=(body.Position-previous).XZ.Length();previous=body.Position;
  double now=GameTime;if(!lease.ValidateClock(now)){if(run?.Result.Status==AtomicActionStatus.Running)actions.Cancel(run);Status=LocalNavigationStatus.Failed;Reason="game_clock_regressed_new_session_required";lease.Release(RequestId,false);return;}lastGameTime=now;
  if(Status==LocalNavigationStatus.Settling){if(Stopwatch.GetElapsedTime(settlingStarted).TotalSeconds>5){Status=LocalNavigationStatus.Failed;Reason="settling_wall_timeout_not_stable";lease.Release(RequestId,false);return;}var v=body.Velocity;var s=settling!.Update(new(now,player.ComponentHealth.Health>0,body.StandingOnValue.HasValue,body.ParentBody is not null,body.ImmersionFactor,v.X,v.Y,v.Z));
   if(s.Status==NavigationSettlingStatus.Waiting)return;
   Status=s.Status==NavigationSettlingStatus.Settled?afterSettle:LocalNavigationStatus.Failed;Reason=s.Status==NavigationSettlingStatus.Settled?Reason:s.Reason;
   if(Terminal)lease.Release(RequestId,s.Status==NavigationSettlingStatus.Settled);
   audit(new{kind="local_navigation_settled",RequestId,Status,Reason,ActualHorizontalPath});return;
  }
  if(Stopwatch.GetElapsedTime(started).TotalSeconds>20||player.ComponentHealth.Health<=0||body.ParentBody is not null||body.ImmersionFactor>.1){StopAndSettle(LocalNavigationStatus.Failed,"budget_or_body_unavailable");return;}
  if(Status==LocalNavigationStatus.Observing){Prepare();return;}
  if(Status is LocalNavigationStatus.Moving or LocalNavigationStatus.Aligning){
   double age=initialAge+Stopwatch.GetElapsedTime(proofStarted).TotalSeconds;var b=body.BoundingBox;
   bool inside=step is not null?ObservedStepGuard.Check(step,Pos(b.Min),Pos(b.Max),body.IsSmoothRiseEnabled,body.MaxSmoothRiseHeight).Inside:LocalNavigationSkillGeometry.InObservedAir(proof,Pos(b.Min),Pos(b.Max));
   if(age>3||!inside){StopAndSettle(LocalNavigationStatus.Failed,age>3?"evidence_expired":"outside_observed_envelope");return;}
  }
  if(run?.Result.Status==AtomicActionStatus.Running)return;
  bool ok=run?.Result.Status==AtomicActionStatus.Succeeded;
  var next=ok?(Status==LocalNavigationStatus.Moving?LocalNavigationStatus.Arrived:LocalNavigationStatus.Observing):LocalNavigationStatus.Failed;
  StopAndSettle(next,ok?"native_segment_completed":"native_"+run?.Result.Failure);
 }
 void Prepare(){
  if(++preparations>18){StopAndSettle(LocalNavigationStatus.Failed,"preparation_budget_exhausted");return;}
  var w=memory.Observe(senses.Observe(senses.SessionId,actor));origin=w.Position;
  var flat=LocalRouteCandidates.Project(w,1,RouteGroundPolicy.ObservedFullCube);var steps=ObservedStepRoutes.Project(w);
  var centre=LocalNavigationAlignment.Project(w);string cell=$"{Math.Floor(origin.X)},{Math.Floor(origin.Y)},{Math.Floor(origin.Z)}";
  bool raised=w.Cells.Any(c=>c.Y==(int)Math.Round(origin.Y)&&(c.Value&1023) is 1 or 2 or 3 or 4 or 5 or 8&&Math.Abs(c.X-Math.Floor(origin.X))+Math.Abs(c.Z-Math.Floor(origin.Z))==1);
  if(centre is not null&&alignedCell!=cell&&(raised||flat.Candidates.Count+steps.Count==0)&&centre.MaximumAge<=1.5){
   alignedCell=cell;Begin(centre.Destination,centre.Evidence,null,new MoveTo(Vec(centre.Destination),.035f,3),LocalNavigationStatus.Aligning);return;}
  var choices=flat.Candidates.Select(c=>(c.Destination,Proof:flat.Provenance[c.EvidenceId],Step:(ObservedStepRoute?)null))
   .Concat(steps.Select(s=>(s.Destination,Proof:s.Evidence,Step:(ObservedStepRoute?)s)))
   .Where(c=>Distance(c.Destination,bias)<Distance(origin,bias)-.01)
   .OrderBy(c=>Distance(c.Destination,bias)).ToArray();
  if(choices.Length>0){var c=choices[0];if(c.Proof.Max(e=>e.Age)<=(c.Step is null?2:1.25)){
    Begin(c.Destination,c.Proof,c.Step,c.Step is null?new MoveTo(Vec(c.Destination),.25f,4):new StepTo(Vec(c.Destination)),LocalNavigationStatus.Moving);return;}}
  var eye=player.ComponentCreatureModel.EyePosition;var decision=look.Next(w,Pos(eye),LocalNavigationSkillGeometry.MissingCells(w));
  if(decision.Look is not {} request){StopAndSettle(LocalNavigationStatus.Failed,decision.Reason);return;}
  if(!lease.IsHeld(RequestId))throw new InvalidOperationException("Navigation lease lost");
  proof=Array.Empty<LocalObservedCell>();step=null;run=actions.Start(new FaceTowards(Vec(request.Target),4,(float)Math.Clamp(request.PitchRadians*180/Math.PI,-75,75)));Status=LocalNavigationStatus.Looking;
  audit(new{kind="local_navigation_look",RequestId,request});
 }
 void Begin(Position end,IReadOnlyList<LocalObservedCell> evidence,ObservedStepRoute? stair,AtomicAction command,LocalNavigationStatus state){
  if(!lease.IsHeld(RequestId))throw new InvalidOperationException("Navigation lease lost");
  destination=end;proof=evidence;step=stair;initialAge=evidence.Max(c=>c.Age);proofStarted=Stopwatch.GetTimestamp();run=actions.Start(command);Status=state;
  audit(new{kind="local_navigation_start",RequestId,Status,origin,destination,initialAge,evidence});
 }
 void StopAndSettle(LocalNavigationStatus next,string reason){
  if(run?.Result.Status==AtomicActionStatus.Running)actions.Cancel(run);
  if(Status==LocalNavigationStatus.Settling){if(cancelled)afterSettle=LocalNavigationStatus.Cancelled;Reason=reason;return;}
  afterSettle=cancelled?LocalNavigationStatus.Cancelled:next;Reason=reason;settlingStarted=Stopwatch.GetTimestamp();settling=new NavigationSettling(GameTime);Status=LocalNavigationStatus.Settling;
 }
 static Position Pos(Vector3 v)=>new(v.X,v.Y,v.Z);static Vector3 Vec(Position p)=>new((float)p.X,(float)p.Y,(float)p.Z);
 static bool Finite(Position p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)&&Math.Abs(p.X)<1e7&&Math.Abs(p.Z)<1e7&&p.Y>=0&&p.Y<=256;
 static double Distance(Position a,Position b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(a.Z-b.Z,2));
}
