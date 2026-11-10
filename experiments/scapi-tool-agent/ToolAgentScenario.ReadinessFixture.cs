using Engine;
using System.Text.Json;
using Westworld.Scapi.Adapter;

// ONLY handoff/file/direct_readiness. Controlled capability evidence, not survival policy.
// The file driver chooses all writes. This fixture observes through the existing owner,
// never waits inside Jump, never retries navigation and never edits the body's landing.
internal sealed partial class ToolAgentScenario {
 double readinessNextSample,readinessStableSince=-1;string readinessAuditState;
 Guid readinessJumpRun;bool readinessJumpSucceeded,readinessJumpSucceededAirborne;
 bool EndReadinessFixture(){
  var now=MonotonicSeconds;var snapshot=navigation.Snapshot;var body=snapshot.Readiness;
  bool attempted=snapshot.RequestId!=Guid.Empty;
  // Read the actual same owner. Logging uses its normal ledger; no second native owner.
  var action=Call("action_status",new{},false);
  bool jump=action.State=="returned"&&action.Data.ValueKind==JsonValueKind.Object
   &&action.Data.TryGetProperty("action",out var name)&&name.GetString()=="Jump"
   &&action.Data.TryGetProperty("runId",out var run)&&run.TryGetGuid(out var id)&&id!=Guid.Empty;
  if(jump&&Status(action.Data)=="succeeded"){
   action.Data.GetProperty("runId").TryGetGuid(out var actual);
   if(readinessJumpRun==Guid.Empty)readinessJumpRun=actual;
   if(readinessJumpRun==actual){readinessJumpSucceeded=true;readinessJumpSucceededAirborne|=body is not null&&!body.Grounded;}
  }
  bool settled=body?.Ready==true&&!actions.IsActionRunning&&!navigation.Busy&&!navigation.OwnerQuarantined
   &&(p.ComponentLocomotion.WalkOrder??Vector2.Zero).LengthSquared()<=.000001f
   &&p.ComponentLocomotion.TurnOrder.LengthSquared()<=.000001f;
  string state=$"{attempted}:{snapshot.Status}:{body?.Ready}:{body?.Grounded}:{readinessJumpSucceeded}:{settled}";
  if(now>=readinessNextSample||state!=readinessAuditState){
   readinessNextSample=now+.05;readinessAuditState=state;
   var status=Call("host_navigation_status",new{},false);var v=p.ComponentBody.Velocity;
   var walk=p.ComponentLocomotion.WalkOrder??Vector2.Zero;var turn=p.ComponentLocomotion.TurnOrder;
   ToolAgentGate.Log(new{kind="n35_readiness_fixture_sample",sessionId=gateway.SessionId,actorId=gateway.ActorId,
    monotonicSeconds=now,stage=attempted?"after_attempt":"before_attempt",navigation=status.Data,action=action.Data,
    nativeRunning=actions.IsActionRunning,localRequestId=navigationSkill.RequestId,localStatus=navigationSkill.Status.ToString(),
    position=NativeLogCoordinates.Position(p.ComponentBody.Position),velocity=NativeLogCoordinates.Position(v),
    walk=new[]{walk.X,walk.Y},turn=new[]{turn.X,turn.Y},health=p.ComponentHealth.Health,hasViewport=p.GameWidget is not null});
  }
  if(!attempted)return false; // normal execution of the driver's one accepted plan
  // Hold subsequent decision requests while collecting real landing. No Wait action/input.
  // If the attempt missed the airborne window, preserve it as uncovered, not a fake pass.
  if(!settled){readinessStableSince=-1;return true;}
  if(readinessStableSince<0)readinessStableSince=now;
  if(now-readinessStableSince<.2||waiting is not null||steps.Count>0||inference is not null)return true;
  bool airborneRejected=snapshot.Status==BoundedNavigationStatus.Failed&&snapshot.Reason=="body_not_ready"
   &&snapshot.StartFailureReadiness is {Ready:false,Grounded:false} failed
   &&failed.Blockers.Contains("not_grounded")&&snapshot.Segments==0&&snapshot.Path==0
   &&navigationSkill.RequestId==Guid.Empty;
  bool safe=p.GameWidget is null&&p.ComponentHealth.Health>=.25f;
  Passed=readinessJumpSucceeded&&readinessJumpSucceededAirborne&&airborneRejected&&fixtureNavigationStarts==1&&safe;
  string outcome=Passed?"jump_pulse_airborne_rejection_then_stable":
   snapshot.StartFailureReadiness is null||snapshot.StartFailureReadiness.Grounded?"airborne_window_not_covered":"readiness_contract_failed";
  var final=Call("host_navigation_status",new{},false);
  ToolAgentGate.Log(new{kind="n35_readiness_fixture_result",sessionId=gateway.SessionId,actorId=gateway.ActorId,
   monotonicSeconds=now,Passed,reason=outcome,readinessJumpRun,readinessJumpSucceeded,readinessJumpSucceededAirborne,
   fixtureNavigationStarts,airborneRejected,stableSince=readinessStableSince,stableSeconds=now-readinessStableSince,
   navigation=final.Data,nativeRunning=actions.IsActionRunning,localRequestId=navigationSkill.RequestId,
   localStatus=navigationSkill.Status.ToString(),health=p.ComponentHealth.Health,hasViewport=p.GameWidget is not null,
   note="Artificial flat fixture; driver chose Jump and one navigation attempt. No automatic retry, no changed Jump completion, no natural survival claim."});
  RequestStop(outcome);return true;
 }
}
