namespace Westworld.Runtime.Core;

public sealed record PendingDecisionLease(Guid RequestId,Guid SessionId,Guid ActorId,Guid ControllerEpoch,long PlanRevision,Guid PlanningObservationId,long? PrecedingReceiptSequence);

// Admission/handoff only: no model tasks, native calls, clocks or implicit revalidation.
// T must be an immutable host-owned snapshot. One occupied slot (request OR result)
// is a conservative subset of a one-in-flight plus one-candidate capacity budget.
public sealed class AsyncDecisionHandoff<T> where T:notnull {
 readonly Guid session,actor;readonly int owner=Environment.CurrentManagedThreadId,capacity;
 readonly HashSet<Guid> used=new(),epochs=new();Guid epoch;long revision;PendingDecisionLease? pending;
 T? candidate;bool received,precedingSucceeded,validated;Guid? validationObservation;
 public AsyncDecisionHandoff(Guid session,Guid actor,Guid controllerEpoch,long planRevision=0,int requestCapacity=4096){
  if(session==Guid.Empty||actor==Guid.Empty||controllerEpoch==Guid.Empty||planRevision<0||requestCapacity is <1 or >65536)throw new ArgumentException("Invalid handoff scope");
  this.session=session;this.actor=actor;epoch=controllerEpoch;revision=planRevision;capacity=requestCapacity;epochs.Add(controllerEpoch);
 }
 void Owner(){if(Environment.CurrentManagedThreadId!=owner)throw new InvalidOperationException("Owner/game thread required");}
 public bool Occupied{get{Owner();return pending is not null;}}
 public Guid? ValidationObservationId{get{Owner();return validationObservation;}}
 public PendingDecisionLease Begin(Guid requestId,Guid planningObservationId,long planRevision,long? precedingReceiptSequence=null){
  Owner();if(pending is not null)throw new InvalidOperationException("Handoff slot occupied");
  if(requestId==Guid.Empty||planningObservationId==Guid.Empty||planRevision!=revision||precedingReceiptSequence is <=0)throw new ArgumentException("Invalid request binding");
  if(used.Contains(requestId))throw new InvalidOperationException("Request already used");
  if(used.Count>=capacity)throw new InvalidOperationException("Request history full; establish a new controller epoch");
  used.Add(requestId);pending=new(requestId,session,actor,epoch,revision,planningObservationId,precedingReceiptSequence);
  precedingSucceeded=precedingReceiptSequence is null;received=validated=false;validationObservation=null;candidate=default;return pending;
 }
 public bool Receive(PendingDecisionLease lease,T result){
  Owner();ArgumentNullException.ThrowIfNull(result);
  if(pending is null||lease!=pending||received)return false;
  candidate=result;received=true;return true;
 }
 public bool CompletePreceding(long receiptSequence,bool succeeded){
  Owner();if(pending is null||pending.PrecedingReceiptSequence!=receiptSequence||precedingSucceeded)return false;
  if(!succeeded){Retire();return true;}precedingSucceeded=true;return true;
 }
 public bool TryAcquire(out PendingDecisionLease? lease,out T? result){
  Owner();lease=null;result=default;if(pending is null||!received||!precedingSucceeded)return false;
  lease=pending;result=candidate;return true;
 }
 public bool ConfirmValidation(Guid requestId,Guid currentObservationId,bool valid){
  Owner();if(currentObservationId==Guid.Empty)throw new ArgumentException("Actual validation observation required");
  if(pending?.RequestId!=requestId||!received||!precedingSucceeded||validated)return false;
  if(!valid){Retire();return true;}validated=true;validationObservation=currentObservationId;return true;
 }
 public bool TryTake(out PendingDecisionLease? lease,out T? result){
  Owner();lease=null;result=default;if(!validated||!TryAcquire(out lease,out result))return false;Retire();return true;
 }
 public void Invalidate(long newPlanRevision){
  Owner();if(newPlanRevision<=revision)throw new ArgumentException("Plan revision must advance");revision=newPlanRevision;Retire();
 }
 public void SwitchController(Guid newEpoch,long newPlanRevision){
  Owner();if(newEpoch==Guid.Empty||epochs.Contains(newEpoch)||newPlanRevision<revision)throw new ArgumentException("New controller epoch required");
  if(epochs.Count>=65536)throw new InvalidOperationException("Controller epoch history full; new session required");
  epochs.Add(newEpoch);epoch=newEpoch;revision=newPlanRevision;Retire();used.Clear();
 }
 void Retire(){pending=null;candidate=default;received=precedingSucceeded=validated=false;validationObservation=null;}
}
