using System.Text.Json;

public enum PrecedingExecutionKind { NativeMoveTo, HostLocalNavigation }
public sealed record PrecedingExecutionBinding(Guid SessionId, Guid ActorId, long ReceiptSequence,
 Guid RunId, PrecedingExecutionKind Kind, string Tool, JsonElement Arguments, string Goal) {
 public static PrecedingExecutionBinding Create(Guid session,Guid actor,long sequence,Guid run,
  PrecedingExecutionKind kind,string tool,JsonElement arguments,string goal) {
  if(session==Guid.Empty||actor==Guid.Empty||run==Guid.Empty||sequence<=0||
   !Enum.IsDefined(kind)||tool!=(kind==PrecedingExecutionKind.NativeMoveTo?"act_MoveTo":"host_navigate_local")||
   arguments.ValueKind!=JsonValueKind.Object||string.IsNullOrWhiteSpace(goal)||goal.Length>1024)
   throw new ArgumentException("Actual predecessor binding and original intent required");
  return new(session,actor,sequence,run,kind,tool,arguments.Clone(),goal);
 }
}
public sealed record PrecedingExecutionSample(Guid SessionId,Guid ActorId,long ReceiptSequence,Guid RunId,
 PrecedingExecutionKind Kind,string Status,bool Moving,bool Settled,bool CancelRequested,double Displacement);
public sealed record PrecedingExecutionAdmission(bool Allowed,string Reason);

// Game-thread-owned admission evidence only. No model queue, clock, native calls,
// invented future inventory, or substitute for actual fresh snapshot/schema validation.
public sealed class PrecedingExecutionTrack {
 public PrecedingExecutionBinding Binding {get;}
 public string InvalidatedReason {get;private set;}
 PrecedingExecutionSample current;bool setupClaimed;
 public bool TryClaimPrefetchSetup(){if(setupClaimed||InvalidatedReason is not null)return false;setupClaimed=true;return true;}
 public PrecedingExecutionTrack(PrecedingExecutionBinding binding) {
  ArgumentNullException.ThrowIfNull(binding);
  Binding=PrecedingExecutionBinding.Create(binding.SessionId,binding.ActorId,binding.ReceiptSequence,
   binding.RunId,binding.Kind,binding.Tool,binding.Arguments,binding.Goal);
 }
 bool Matches(PrecedingExecutionSample s)=>s.SessionId==Binding.SessionId&&s.ActorId==Binding.ActorId&&
  s.ReceiptSequence==Binding.ReceiptSequence&&s.RunId==Binding.RunId&&s.Kind==Binding.Kind;
 public PrecedingExecutionAdmission Observe(PrecedingExecutionSample sample) {
  if(InvalidatedReason is not null)return new(false,InvalidatedReason);
  current=sample;
  if(sample is null)return new(false,"missing_state");
  if(sample.RunId==Guid.Empty&&sample.Status=="idle")return new(false,"no_active_run");
  if(!Matches(sample)){InvalidatedReason="identity_replaced";return new(false,InvalidatedReason);}
  if(sample.CancelRequested||sample.Status is "cancelled" or "failed") {
   InvalidatedReason=sample.CancelRequested?"cancel_requested":"preceding_"+sample.Status;
   return new(false,InvalidatedReason);
  }
  if(!double.IsFinite(sample.Displacement)||sample.Displacement<0)return new(false,"invalid_displacement");
  string success=Binding.Kind==PrecedingExecutionKind.NativeMoveTo?"succeeded":"arrived";
  return sample.Status=="running"||sample.Status==success?new(true,"observed"):new(false,"not_running_or_known_terminal");
 }
 public PrecedingExecutionAdmission CanPrefetch(int remainingSteps,bool decisionOccupied,
  int issued,double elapsed,ToolScenarioBudget budget) {
  if(InvalidatedReason is not null)return new(false,InvalidatedReason);
  if(current is null)return new(false,"missing_state");
  if(setupClaimed)return new(false,"setup_already_attempted");
  if(current.Status!="running"||!current.Moving||current.Settled)return new(false,"not_moving");
  if(!double.IsFinite(current.Displacement)||current.Displacement<.15)return new(false,"insufficient_displacement");
  if(remainingSteps!=0)return new(false,"remaining_plan_steps");
  if(decisionOccupied)return new(false,"decision_occupied");
  if(budget is null||!double.IsFinite(elapsed)||elapsed<0||!budget.CanRequest(issued,elapsed))return new(false,"request_budget");
  return new(true,"eligible");
 }
 public PrecedingExecutionAdmission CanHandoff(PrecedingExecutionSample sample,Guid planningObservation,
  Guid validationObservation,double now,double deadline) {
  var state=Observe(sample);if(!state.Allowed)return state;
  if(!double.IsFinite(now)||!double.IsFinite(deadline)||now<0||now>=deadline)return new(false,"decision_deadline");
  string success=Binding.Kind==PrecedingExecutionKind.NativeMoveTo?"succeeded":"arrived";
  if(sample.Status!=success||!sample.Settled||sample.Moving)return new(false,"not_successfully_settled");
  if(planningObservation==Guid.Empty||validationObservation==Guid.Empty||planningObservation==validationObservation)
   return new(false,"validation_observation_required");
  return new(true,"requires_fresh_schema_and_native_validation");
 }
}

// Receipt attribution, not permission to dispatch a new action. Failure/cancel
// evidence remains reportable after candidate invalidation, only for the same run.
public enum PrecedingTerminalEvidence { Unknown, IdentityMismatch, Running, Succeeded, Failed, Cancelled }
public static class PrecedingTerminalClassifier {
 public static PrecedingTerminalEvidence Classify(PrecedingExecutionBinding binding,Guid actualRun,string actualStatus){
  ArgumentNullException.ThrowIfNull(binding);
  if(actualRun==Guid.Empty)return PrecedingTerminalEvidence.Unknown;
  if(actualRun!=binding.RunId)return PrecedingTerminalEvidence.IdentityMismatch;
  if(actualStatus=="running"||actualStatus=="stopping")return PrecedingTerminalEvidence.Running;
  if(actualStatus=="failed")return PrecedingTerminalEvidence.Failed;
  if(actualStatus=="cancelled")return PrecedingTerminalEvidence.Cancelled;
  if(actualStatus==(binding.Kind==PrecedingExecutionKind.NativeMoveTo?"succeeded":"arrived"))return PrecedingTerminalEvidence.Succeeded;
  return PrecedingTerminalEvidence.Unknown;
 }
}
