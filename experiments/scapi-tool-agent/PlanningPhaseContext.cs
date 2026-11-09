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
}
