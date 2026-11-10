using System.Text.Json;
using Westworld.Decisions.Local;

// Declarative planning context only. This is NOT dispatch permission, a predicted
// world snapshot, or a change to the predecessor's actual execution deadline.
public static class PlanningPhaseContext {
 public static JsonElement AfterPrecedingSuccess(Guid sessionId,Guid actorId,Guid planningObservationId,
  Guid precedingNavigationId,long precedingReceiptSequence,string precedingGoal,ToolDecisionStep precedingStep,
  JsonElement actualNavigationStatus,double remainingDecisionSeconds){
  if(sessionId==Guid.Empty||actorId==Guid.Empty||planningObservationId==Guid.Empty||precedingNavigationId==Guid.Empty||precedingReceiptSequence<=0)
   throw new ArgumentException("Actual planning and predecessor identities required");
  if(string.IsNullOrWhiteSpace(precedingGoal)||precedingGoal.Length>160||precedingStep is null||precedingStep.Tool!="host_navigate_local"||precedingStep.Arguments.ValueKind!=JsonValueKind.Object)
   throw new ArgumentException("Original navigation plan intent and arguments required");
  if(!double.IsFinite(remainingDecisionSeconds)||remainingDecisionSeconds<=0||remainingDecisionSeconds>120)
   throw new ArgumentOutOfRangeException(nameof(remainingDecisionSeconds));
  if(!ToolResultProjection.ValidNavigationStatus(actualNavigationStatus)||
   actualNavigationStatus.GetProperty("status").GetString()!="running"||!actualNavigationStatus.GetProperty("busy").GetBoolean()||
   actualNavigationStatus.GetProperty("request_id").GetGuid()!=precedingNavigationId||
   (actualNavigationStatus.TryGetProperty("owner_quarantined",out var q)&&q.ValueKind!=JsonValueKind.False))
   throw new ArgumentException("Actual running predecessor status must match identity");
  return JsonSerializer.SerializeToElement(new{
   phase="AfterPrecedingSuccess",sessionId,actorId,planningObservationId,
   instruction="Plan what to do AFTER the identified ongoing navigation succeeds. These are deferred candidates, not commands to execute now. Current busy status alone is not a reason to wait or return no steps. Choose the next goal/actions yourself; an empty plan remains legal if justified by the goal or evidence. Do not cancel the predecessor in this plan.",
   evidenceRule="All supplied observations, references and inventory describe the actual current world, NOT a predicted arrival state. Do not invent future items, positions, references or successful outcomes. The host executes candidates only after actual predecessor success and fresh observation/schema/native validation; stale or failed plans are discarded.",
   predecessor=new{navigationRequestId=precedingNavigationId,receiptSequence=precedingReceiptSequence,goal=precedingGoal,step=new{tool=precedingStep.Tool,arguments=precedingStep.Arguments.Clone()},actualStatus=actualNavigationStatus.Clone()},
   remainingDecisionSeconds,deadlineRule="This remaining decision budget does not extend navigation or guarantee execution. Host monotonic deadline and cancellation remain authoritative."
  });
 }
 public static JsonElement AfterExecutionSuccess(PrecedingExecutionBinding binding,Guid planningObservationId,
  PrecedingExecutionSample sample,JsonElement actualStatus,double remainingDecisionSeconds) {
  var track=new PrecedingExecutionTrack(binding);
  if(planningObservationId==Guid.Empty||!track.Observe(sample).Allowed||sample.Status!="running"||
   !double.IsFinite(remainingDecisionSeconds)||remainingDecisionSeconds<=0||remainingDecisionSeconds>120||actualStatus.ValueKind!=JsonValueKind.Object)
   throw new ArgumentException("Actual running predecessor and remaining decision budget required");
  string identityField=binding.Kind==PrecedingExecutionKind.NativeMoveTo?"runId":"request_id";
  if(!actualStatus.TryGetProperty(identityField,out var id)||!id.TryGetGuid(out var run)||run!=binding.RunId||
   !actualStatus.TryGetProperty("status",out var status)||status.GetString()!="running")throw new ArgumentException("Raw predecessor state identity mismatch");
  return JsonSerializer.SerializeToElement(new{
   phase="AfterPrecedingSuccess",sessionId=binding.SessionId,actorId=binding.ActorId,planningObservationId,
   instruction="Plan what to do AFTER this identified ongoing movement succeeds and physically stops. These are deferred candidates, not commands to execute now. Current busy status alone is not a reason to return no steps. Choose goals/actions yourself; an empty plan remains legal. Do not cancel the predecessor in this plan.",
   evidenceRule="Observations, references and inventory describe the actual current world, NOT predicted arrival. Do not invent future items, positions, references or outcomes. Execution requires real predecessor success, stopped motion, fresh observation/schema and native validation; stale or failed plans are discarded.",
   predecessor=new{kind=binding.Kind.ToString(),runId=binding.RunId,navigationRequestId=binding.Kind==PrecedingExecutionKind.HostLocalNavigation?(Guid?)binding.RunId:null,receiptSequence=binding.ReceiptSequence,goal=binding.Goal,step=new{tool=binding.Tool,arguments=binding.Arguments},actualStatus=actualStatus.Clone()},
   remainingDecisionSeconds,deadlineRule="Decision budget does not extend the preceding action. Host monotonic deadline and cancellation remain authoritative."
  });
 }
}
