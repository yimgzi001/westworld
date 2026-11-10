using System.Diagnostics;
using System.Text.Json;
using Engine;
using Game;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;
using Westworld.Decisions.Local;
internal sealed partial class ToolAgentScenario {
 readonly Guid controllerEpoch=Guid.NewGuid();AsyncDecisionHandoff<ToolDecisionPlan> handoff;
 PendingDecisionLease prefetchLease;Guid precedingNavigation;long planRevision;readonly HashSet<Guid> prefetchedNavigation=[];
 bool inferenceIsPrefetch,prefetchAccepted,precedingSucceeded;CancellationTokenSource prefetchCancellation;
 double prefetchDeadline,prefetchStarted,sampleMono,overlapSeconds,overlapPath;Vector3 sampleBody,prefetchBody,navigationOrigin;bool sampleMoving;
 double? acceptedCandidateDeadline;Guid? acceptedCandidateRequest;int prefetchTaken;string invalidatedReason;double receivedPrefetchAt;
 bool CanPrefetchNavigation=>(ToolAgentGate.Mode!="handoff"||prefetchTaken==0)&&waiting is not null&&predecessor is not null&&predecessorSample is not null&&waitingReceipt.Sequence==predecessor.Binding.ReceiptSequence&&predecessor.CanPrefetch(CanPrepareContinuation?0:steps.Count,inference is not null||handoff.Occupied,rounds,ToolAgentGate.ElapsedSeconds,ToolAgentGate.Budget).Allowed&&!prefetchedNavigation.Contains(predecessor.Binding.RunId);
 string precedingKind=>predecessor?.Binding.Kind.ToString();

 void InvalidatePrefetch(string reason){continuation?.Invalidate(reason);acceptedCandidateDeadline=null;acceptedCandidateRequest=null;if(handoff is null)return;if(handoff.Occupied||inferenceIsPrefetch){handoff.Invalidate(++planRevision);prefetchCancellation?.Cancel();invalidatedReason=reason;ToolAgentGate.Log(new{kind="prefetch_invalidated",reason,requestId=prefetchLease?.RequestId,precedingNavigation,precedingRunId=precedingNavigation,precedingKind,planRevision,monotonicSeconds=MonotonicSeconds});}}
 void TickPrefetch(){
  SamplePredecessor();TickContinuation();
  if((handoff.Occupied||inferenceIsPrefetch)&&MonotonicSeconds>=prefetchDeadline&&invalidatedReason is null)InvalidatePrefetch("decision_deadline");
  if(inferenceIsPrefetch&&inference is not null){double now=MonotonicSeconds;bool moving=!inference.IsCompleted&&predecessorSample?.RunId==precedingNavigation&&predecessorSample.Moving&&predecessor.InvalidatedReason is null;
   if(moving&&sampleMoving){overlapSeconds+=now-sampleMono;overlapPath+=(p.ComponentBody.Position-sampleBody).XZ.Length();}sampleMono=now;sampleBody=p.ComponentBody.Position;sampleMoving=moving;
   if(inference.IsCompleted){try{var plan=inference.GetAwaiter().GetResult();receivedPrefetchAt=MonotonicSeconds;bool accepted=invalidatedReason is null&&receivedPrefetchAt<prefetchDeadline&&handoff.Receive(prefetchLease,plan);prefetchAccepted=accepted;ToolAgentGate.Log(new{kind="prefetch_response",requestId=prefetchLease.RequestId,precedingNavigation,precedingRunId=precedingNavigation,precedingKind,accepted,precedingSucceeded,monotonicSeconds=receivedPrefetchAt,overlapSeconds,overlapPath,netDuringDecision=(p.ComponentBody.Position-prefetchBody).XZ.Length(),plan});}
    catch(Exception e){ToolAgentGate.Log(new{kind="prefetch_failure",requestId=prefetchLease.RequestId,error=e.GetType().Name,monotonicSeconds=MonotonicSeconds,overlapSeconds,overlapPath});if(invalidatedReason is null)InvalidatePrefetch("decision_failed_or_cancelled");}
    finally{inference=null;inferenceIsPrefetch=false;prefetchCancellation?.Dispose();prefetchCancellation=null;}}
  }
  if(CheckGoal()){continuation?.GoalAlreadySatisfied();InvalidatePrefetch("goal_already_satisfied");return;}
  if(!CanPrefetchNavigation||!predecessor.TryClaimPrefetchSetup())return;
  try{precedingNavigation=predecessor.Binding.RunId;prefetchedNavigation.Add(precedingNavigation);var snap=Snapshot();prefetchStarted=sampleMono=MonotonicSeconds;prefetchDeadline=prefetchStarted+Math.Min(ToolAgentGate.Mode=="handoff"&&ToolAgentGate.HandoffCase=="expired"?.35:45,ToolAgentGate.Budget.RequestWindow(ToolAgentGate.ElapsedSeconds,true));
   BeginContinuation();precedingNavigation=predecessor.Binding.RunId;activePrompt=Prompt(snap.Observation,snap.Inventory,out var offers,obs=>ContinuationPhase(obs),OfferPurpose.AfterPrecedingSuccess);
   Guid request=Guid.NewGuid();prefetchedNavigation.Add(precedingNavigation);prefetchLease=handoff.Begin(request,currentObservationId,planRevision,waitingReceipt.Sequence);prefetchBody=sampleBody=p.ComponentBody.Position;overlapPath=overlapSeconds=0;sampleMoving=true;prefetchAccepted=precedingSucceeded=false;invalidatedReason=null;
   double remaining=prefetchDeadline-MonotonicSeconds;if(remaining<=0)throw new TimeoutException();prefetchCancellation=CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);prefetchCancellation.CancelAfter(TimeSpan.FromSeconds(remaining));rounds++;inferenceIsPrefetch=true;prefetchRequestStarted=requestMono=MonotonicSeconds;
   var prompt=activePrompt;ToolAgentGate.Log(new{kind="prefetch_requested",lease=prefetchLease,precedingNavigation,precedingRunId=precedingNavigation,precedingKind,deadline=prefetchDeadline,monotonicSeconds=prefetchStarted,round=rounds,prompt=prompt.Prompt,schema=prompt.ResponseSchema,gameTime=p.Project.FindSubsystem<SubsystemTime>(true).GameTime});
   inference=fileController is not null?fileController.DecideAsync(prompt,gateway.SessionId,gateway.ActorId,currentObservationId,currentContext,offers,p.Project.FindSubsystem<SubsystemTime>(true).GameTime,prefetchCancellation.Token,requestId:request,remainingBudget:TimeSpan.FromSeconds(remaining)):Task.Run(()=>model.CompleteStructuredAsync(prompt.Prompt,prompt.ResponseSchema,prompt.ParseResponse,prefetchCancellation.Token));
  }catch(Exception e){InvalidatePrefetch("prefetch_setup_failed");if(inference is null)inferenceIsPrefetch=false;ToolAgentGate.Log(new{kind="prefetch_setup_failure",error=e.ToString()});}
 }
 void ObservePrecedingTerminal(ToolReceipt receipt,bool success){if(continuation is not null)return;if(prefetchLease is null||prefetchLease.PrecedingReceiptSequence!=receipt.Sequence)return;
  success=success&&PredecessorSettled&&predecessor.Binding.RunId==precedingNavigation;
  precedingSucceeded=success;fixtureFirstArrived=success;if(!success)InvalidatePrefetch("preceding_failed");handoff.CompletePreceding(receipt.Sequence,success);ToolAgentGate.Log(new{kind="prefetch_preceding_terminal",requestId=prefetchLease.RequestId,precedingNavigation,precedingRunId=precedingNavigation,precedingKind,sequence=receipt.Sequence,success,monotonicSeconds=MonotonicSeconds});
 }
 bool ConsumePrefetch(){if(ContinuationScheduling.MayDispatchOriginalTail(ContinuingTail,continuationSucceeded,steps.Count))return false;if(inferenceIsPrefetch)return true;if(!handoff.Occupied)return false;if(!handoff.TryAcquire(out var lease,out var candidate))return true;
  if(MonotonicSeconds>=prefetchDeadline||(continuation is not null?(!continuationSucceeded||!ContinuationPhysicalIdle()):(!PredecessorSettled||predecessor.Binding.RunId!=precedingNavigation))){InvalidatePrefetch("handoff_stale");return true;}
  try{var s=Snapshot();var validation=Prompt(s.Observation,s.Inventory,out var offers);validation.ParseResponse(JsonSerializer.Serialize(new{goal=candidate.Goal,steps=candidate.Steps.Select(v=>new{tool=v.Tool,arguments=v.Arguments})}));
   if(!DecisionDispatchWindow.IsOpen(MonotonicSeconds,prefetchDeadline)){InvalidatePrefetch("validation_crossed_deadline");return true;}
   if(!(continuation is not null?continuation.TryAdmit(gateway.SessionId,gateway.ActorId,continuationPlanId,continuationRevision,lease.PlanningObservationId,currentObservationId,!actions.IsActionRunning&&!navigation.Busy,ContinuationPhysicalIdle(),true,MonotonicSeconds).Allowed:predecessor.CanHandoff(predecessorSample,lease.PlanningObservationId,currentObservationId,MonotonicSeconds,prefetchDeadline).Allowed)){InvalidatePrefetch("predecessor_validation_failed");return true;}handoff.ConfirmValidation(lease.RequestId,currentObservationId,true);if(!DecisionDispatchWindow.IsOpen(MonotonicSeconds,prefetchDeadline)){InvalidatePrefetch("take_crossed_deadline");return true;}if(!handoff.TryTake(out _,out var plan))throw new InvalidOperationException("No validated candidate");continuation=null;stepOwnership.Install(lease.RequestId,plan.Steps,prefetchRequestStarted,receivedPrefetchAt);currentPlanGoal=plan.Goal;foreach(var step in plan.Steps)steps.Enqueue(step);prefetchTaken++;acceptedCandidateDeadline=DecisionDispatchWindow.ForPlan(plan.Steps.Count,prefetchDeadline);acceptedCandidateRequest=acceptedCandidateDeadline.HasValue?lease.RequestId:null;replyMono=receivedPrefetchAt;requestMono=prefetchStarted;
   ToolAgentGate.Log(new{kind="prefetch_handoff_taken",requestId=lease.RequestId,precedingNavigation,precedingRunId=precedingNavigation,precedingKind,validationObservationId=currentObservationId,monotonicSeconds=MonotonicSeconds,overlapSeconds,overlapPath,prefetchTaken});
  }catch(Exception e){InvalidatePrefetch("fresh_validation_failed");ToolAgentGate.Log(new{kind="prefetch_validation_failed",requestId=lease.RequestId,error=e.Message});}return true;
 }
}
