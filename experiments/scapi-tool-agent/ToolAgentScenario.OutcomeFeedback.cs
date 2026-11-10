using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Decisions.Local;
internal sealed partial class ToolAgentScenario {
 readonly ToolOutcomeFeedback outcomeFeedback=new();
 static bool TracksOutcome(string tool)=>Capability39.CapabilityExecution.TracksOutcome(tool);
 ToolOutcomeSnapshot PublicOutcomeSnapshot(JsonElement observation,JsonElement inventory)=>new(gateway.SessionId,gateway.ActorId,observation.GetProperty("snapshotId").GetGuid(),inventory.Clone());
 void BeginOutcome(ToolDecisionStep step,JsonElement observation,JsonElement inventory){if(TracksOutcome(step.Tool))outcomeFeedback.Begin(PublicOutcomeSnapshot(observation,inventory),step.Tool,Goal);}
 void BindOutcome(ToolDecisionStep step,ToolReceipt receipt){if(!TracksOutcome(step.Tool))return;if(receipt.State=="returned")outcomeFeedback.Bind(receipt.Sequence);else outcomeFeedback.AbortDispatch("native_receipt_not_returned");}
 void CompleteOutcome(ToolReceipt original,string status){if(!TracksOutcome(original.Tool))return;try{var after=Snapshot();outcomeFeedback.Complete(original.Sequence,status,PublicOutcomeSnapshot(after.Observation,after.Inventory));}catch(Exception e){outcomeFeedback.Unavailable(Goal,"terminal_snapshot_unavailable",Array.Empty<ToolUnexecutedStep>());ToolAgentGate.Log(new{kind="outcome_feedback_unavailable",reason=e.Message});}}
 void UnexecutedOutcome(string reason,ToolDecisionStep failedStep=null){
  var remaining=(failedStep is null?steps: new[]{failedStep}.Concat(steps)).Take(4).Select(s=>new ToolUnexecutedStep(s.Tool,reason)).ToArray();if(remaining.Length==0)return;
  try{var after=Snapshot();outcomeFeedback.Unexecuted(Goal,PublicOutcomeSnapshot(after.Observation,after.Inventory),remaining);}catch(Exception e){outcomeFeedback.Unavailable(Goal,"unexecuted_snapshot_unavailable",remaining);ToolAgentGate.Log(new{kind="outcome_feedback_unavailable",reason=e.Message});}
 }
}
