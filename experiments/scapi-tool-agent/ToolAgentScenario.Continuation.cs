using Engine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Westworld.Runtime.Core;
using Westworld.Decisions.Local;
internal sealed partial class ToolAgentScenario {
 PlanContinuationFence continuation;Guid continuationPlanId;long continuationRevision;bool continuationSucceeded;
 int continuationActiveIndex;ToolReceipt continuationReceipt;Guid continuationRun;string continuationTool;
 bool CanPrepareContinuation=>steps.Count>0&&steps.Count<=3&&steps.All(s=>ContinuationSupported(s.Tool));
 static bool ContinuationSupported(string tool)=>tool=="host_navigate_local"||tool.StartsWith("act_",StringComparison.Ordinal);
 bool ContinuingTail=>continuation is not null&&continuation.InvalidatedReason is null&&!continuationSucceeded;
 void BeginContinuation(){
  continuation=null;continuationSucceeded=false;continuationReceipt=null;
  if(steps.Count==0)return;
  continuationPlanId=Guid.NewGuid();continuationRevision=planRevision;
  var tail=new[]{waitingStep}.Concat(steps).Select(s=>new ContinuationStep(s.Tool,s.Arguments)).ToArray();
  continuation=new(gateway.SessionId,gateway.ActorId,continuationPlanId,continuationRevision,tail,prefetchDeadline);
  BindContinuation(waitingStep,waitingReceipt);
  ToolAgentGate.Log(new{kind="continuation_bound",planId=continuationPlanId,planRevision=continuationRevision,stepCount=tail.Length,firstReceipt=waitingReceipt.Sequence,deadline=prefetchDeadline,steps=tail,monotonicSeconds=MonotonicSeconds});
 }
 void BindContinuation(ToolDecisionStep step,ToolReceipt receipt){
  if(continuation is null||continuation.InvalidatedReason is not null||continuationSucceeded)return;
  string field=step.Tool=="host_navigate_local"?"request_id":"runId";Guid run=Guid.Empty;
  if(receipt.State=="returned"&&receipt.Data.ValueKind==JsonValueKind.Object&&receipt.Data.TryGetProperty(field,out var value))value.TryGetGuid(out run);
  int index=continuation.CompletedSteps;
  var accepted=continuation.Bind(gateway.SessionId,gateway.ActorId,continuationPlanId,continuationRevision,index,new(step.Tool,step.Arguments),receipt.Sequence,run,MonotonicSeconds);
  if(!accepted.Allowed){InvalidatePrefetch("continuation_"+accepted.Reason);return;}
  continuationActiveIndex=index;continuationReceipt=receipt;continuationRun=run;continuationTool=step.Tool;
  ToolAgentGate.Log(new{kind="continuation_step_bound",planId=continuationPlanId,index,sequence=receipt.Sequence,runId=run,tool=step.Tool,monotonicSeconds=MonotonicSeconds});
 }
 bool ContinuationIdentityValid(ToolReceipt original,JsonElement actual){
  if(continuation is null||continuation.InvalidatedReason is not null||continuationReceipt?.Sequence!=original.Sequence)return true;
  string field=continuationTool=="host_navigate_local"?"request_id":"runId";Guid run=Guid.Empty;
  if(actual.ValueKind==JsonValueKind.Object&&actual.TryGetProperty(field,out var value))value.TryGetGuid(out run);
  if(run==continuationRun)return true;
  continuation.Invalidate(run==Guid.Empty?"missing_execution_identity":"execution_identity_changed");
  InvalidatePrefetch("continuation_execution_identity_changed");
  ToolAgentGate.Log(new{kind="continuation_identity_rejected",planId=continuationPlanId,sequence=original.Sequence,expectedRunId=continuationRun,actualRunId=run,monotonicSeconds=MonotonicSeconds});
  return false;
 }
 void CompleteContinuation(ToolReceipt original,JsonElement actual){
  if(continuation is null||continuation.InvalidatedReason is not null||continuationReceipt?.Sequence!=original.Sequence)return;
  if(!ContinuationIdentityValid(original,actual))return;
  var result=continuation.Observe(gateway.SessionId,gateway.ActorId,continuationPlanId,continuationRevision,continuationActiveIndex,original.Sequence,continuationRun,Status(actual),MonotonicSeconds);
  ToolAgentGate.Log(new{kind="continuation_step_terminal",planId=continuationPlanId,index=continuationActiveIndex,sequence=original.Sequence,runId=continuationRun,status=Status(actual),accepted=result.Allowed,reason=result.Reason,monotonicSeconds=MonotonicSeconds});
  if(continuation.InvalidatedReason is not null)InvalidatePrefetch("continuation_"+result.Reason);
 }
 bool ContinuationPhysicalIdle(){var locomotion=p.ComponentLocomotion;return !actions.IsActionRunning&&!navigation.Busy&&!navigation.OwnerQuarantined&&p.ComponentBody.Velocity.LengthSquared()<=.0064f&&(locomotion.WalkOrder??Vector2.Zero).LengthSquared()<=.000001f&&locomotion.TurnOrder.LengthSquared()<=.000001f;}
 void TickContinuation(){
  if(!ContinuingTail||continuation.CompletedSteps!=continuation.StepCount||steps.Count!=0||waiting is not null)return;
  var actual=Call(continuationTool=="host_navigate_local"?"host_navigation_status":"action_status",new{},false);
  if(actual.State!="returned"||!ContinuationIdentityValid(continuationReceipt,actual.Data))return;
  if(Status(actual.Data)!=(continuationTool=="host_navigate_local"?"arrived":"succeeded")||!ContinuationPhysicalIdle())return;
  continuationSucceeded=true;precedingSucceeded=true;
  handoff.CompletePreceding(prefetchLease.PrecedingReceiptSequence.Value,true);
  ToolAgentGate.Log(new{kind="continuation_tail_settled",planId=continuationPlanId,firstReceipt=prefetchLease.PrecedingReceiptSequence,lastReceipt=continuationReceipt.Sequence,lastRunId=continuationRun,monotonicSeconds=MonotonicSeconds});
 }
 JsonElement ContinuationPhase(Guid observationId){
  var phase=PlanningPhaseContext.AfterExecutionSuccess(predecessor.Binding,observationId,predecessorSample,predecessorRaw,prefetchDeadline-MonotonicSeconds);
  if(continuation is null)return phase;
  var node=JsonNode.Parse(phase.GetRawText()).AsObject();
  node["instruction"]="Plan actions ONLY AFTER the entire listed original plan tail really succeeds and settles. The current movement alone does not complete that tail. Do not replace or cancel queued original steps. Choose your own subsequent goal; empty plan is legal. All observations and inventory are CURRENT, not predicted results. Never invent future items or references.";
  node["originalPlanTail"]=JsonSerializer.SerializeToNode(new{planId=continuationPlanId,planRevision=continuationRevision,currentStep=waitingStep,remainingSteps=steps.ToArray(),allStepsMustActuallySucceed=true});
  return JsonSerializer.SerializeToElement(node);
 }
}
