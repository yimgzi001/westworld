using System.Text.Json;
using Westworld.Runtime.Core;
using Engine;
using Westworld.Scapi.Adapter;
using Westworld.Decisions.Local;
internal sealed partial class ToolAgentScenario {
 bool RequiresNativeDrain=>IsFoodFixture||IsDirectFixture||IsNaturalMode;
 PrecedingExecutionTrack predecessor;PrecedingExecutionSample predecessorSample;
 Vector3 predecessorOrigin;JsonElement predecessorRaw;double nextPredecessorAudit;Guid settledLogged;string lastPredecessorAuditState;
 void BindPredecessor(ToolDecisionStep step,ToolReceipt receipt) {
  if(step.Tool is not ("act_MoveTo" or "host_navigate_local")){predecessor=null;predecessorSample=null;return;}
  if(receipt.State!="returned"||Status(receipt.Data)!="running")return;
  bool direct=step.Tool=="act_MoveTo";string field=direct?"runId":"request_id";
  if(!receipt.Data.TryGetProperty(field,out var id)||!id.TryGetGuid(out var run)||run==Guid.Empty)
   throw new InvalidOperationException("Running movement lacks native execution identity");
  predecessor=new(PrecedingExecutionBinding.Create(gateway.SessionId,gateway.ActorId,receipt.Sequence,run,
   direct?PrecedingExecutionKind.NativeMoveTo:PrecedingExecutionKind.HostLocalNavigation,step.Tool,step.Arguments,currentPlanGoal));
  predecessorOrigin=p.ComponentBody.Position;predecessorSample=null;nextPredecessorAudit=0;settledLogged=Guid.Empty;
  var b=predecessor.Binding;ToolAgentGate.Log(new{kind="predecessor_bound",sessionId=b.SessionId,actorId=b.ActorId,receiptSequence=b.ReceiptSequence,runId=b.RunId,precedingKind=b.Kind.ToString(),tool=b.Tool,arguments=b.Arguments,monotonicSeconds=MonotonicSeconds,position=NativeLogCoordinates.Position(predecessorOrigin)});
 }
 void SamplePredecessor() {
  if(predecessor is null)return;var b=predecessor.Binding;bool direct=b.Kind==PrecedingExecutionKind.NativeMoveTo;
  var receipt=Call(direct?"action_status":"host_navigation_status",new{},false);predecessorRaw=receipt.Data;
  Guid run=Guid.Empty;string field=direct?"runId":"request_id";
  if(receipt.State=="returned"&&receipt.Data.ValueKind==JsonValueKind.Object&&receipt.Data.TryGetProperty(field,out var id))id.TryGetGuid(out run);
  string status=receipt.State=="returned"?Status(receipt.Data):"unknown";
  // Missing identity/status is unknown, not evidence that another run replaced this one.
  if(run==Guid.Empty){predecessorSample=null;return;}
  var velocity=p.ComponentBody.Velocity;var loco=p.ComponentLocomotion;var walk=loco.WalkOrder??Vector2.Zero;var turn=loco.TurnOrder;
  bool stopped=velocity.LengthSquared()<=.0064f&&walk.LengthSquared()<=.000001f&&turn.LengthSquared()<=.000001f;
  bool settled=stopped&&!actions.IsActionRunning&&!navigation.Busy&&!navigation.OwnerQuarantined;
  bool moving=status=="running"&&actions.IsActionRunning&&velocity.XZ.LengthSquared()>.0001f;
  bool cancel=status is "cancelled" or "stopping";
  predecessorSample=new(gateway.SessionId,gateway.ActorId,b.ReceiptSequence,run,b.Kind,status,moving,settled,cancel,(p.ComponentBody.Position-predecessorOrigin).XZ.Length());
  var verdict=predecessor.Observe(predecessorSample);
  if(predecessor.InvalidatedReason is not null)InvalidatePrefetch(predecessor.InvalidatedReason);
  string auditState=$"{run}:{status}:{settled}:{cancel}";
  if(MonotonicSeconds>=nextPredecessorAudit||auditState!=lastPredecessorAuditState){lastPredecessorAuditState=auditState;nextPredecessorAudit=MonotonicSeconds+.1;
   ToolAgentGate.Log(new{kind="predecessor_physical_sample",sessionId=b.SessionId,actorId=b.ActorId,receiptSequence=b.ReceiptSequence,runId=run,precedingKind=b.Kind.ToString(),status,moving,settled,cancelRequested=cancel,displacement=predecessorSample.Displacement,position=NativeLogCoordinates.Position(p.ComponentBody.Position),velocity=NativeLogCoordinates.Position(velocity),walk=new[]{walk.X,walk.Y},turn=new[]{turn.X,turn.Y},monotonicSeconds=MonotonicSeconds,admission=verdict.Reason});}
  if(settled&&status==(direct?"succeeded":"arrived")&&settledLogged!=run){settledLogged=run;ToolAgentGate.Log(new{kind="predecessor_settled",sessionId=b.SessionId,actorId=b.ActorId,receiptSequence=b.ReceiptSequence,runId=run,precedingKind=b.Kind.ToString(),monotonicSeconds=MonotonicSeconds});}
 }
 bool IsTrackedPredecessor(ToolReceipt receipt)=>predecessor?.Binding.ReceiptSequence==receipt.Sequence;
 bool PredecessorSettled=>predecessorSample is not null&&predecessor.InvalidatedReason is null&&predecessorSample.RunId==predecessor.Binding.RunId&&predecessorSample.Settled&&!predecessorSample.Moving;
}
