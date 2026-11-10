using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed record ContinuationStep(string Tool,JsonElement Arguments);
public sealed record ContinuationRun(long ReceiptSequence,Guid RunId);
public sealed record ContinuationAdmission(bool Allowed,string Reason);

// Pure plan-tail barrier. Does not execute, rebind references, predict inventory,
// allocate model requests or replace AsyncDecisionHandoff's one occupied slot.
public sealed class PlanContinuationFence {
 readonly Guid session,actor,plan;readonly long revision;readonly string[] fingerprints;
 readonly HashSet<Guid> runs=new();readonly HashSet<long> receipts=new();
 readonly double deadline;double lastNow=-1;readonly string[] tools;int completed;long lastReceipt;ContinuationRun active;bool readyClaimed;
 public string InvalidatedReason {get;private set;}
 public int CompletedSteps=>completed;
 public int StepCount=>fingerprints.Length;
 public PlanContinuationFence(Guid sessionId,Guid actorId,Guid planId,long planRevision,
  IReadOnlyList<ContinuationStep> remainingSteps,double monotonicDeadline){
  if(sessionId==Guid.Empty||actorId==Guid.Empty||planId==Guid.Empty||planRevision<0||
   remainingSteps is null||remainingSteps.Count is <1 or >4||!double.IsFinite(monotonicDeadline)||monotonicDeadline<=0)
   throw new ArgumentException("Bounded original plan and deadline required");
  session=sessionId;actor=actorId;plan=planId;revision=planRevision;deadline=monotonicDeadline;
  fingerprints=remainingSteps.Select(Fingerprint).ToArray();tools=remainingSteps.Select(s=>s.Tool).ToArray();
 }
 public static string Fingerprint(ContinuationStep step){
  if(step is null||string.IsNullOrWhiteSpace(step.Tool)||step.Tool.Length>64||
   step.Tool.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_')||step.Arguments.ValueKind!=JsonValueKind.Object)
   throw new ArgumentException("Original named tool and object arguments required");
  // Canonical object key order, array order preserved. Numeric lexical differences
  // conservatively differ; no coercion of references or values.
  using var stream=new MemoryStream();using(var writer=new Utf8JsonWriter(stream))WriteCanonical(writer,step.Arguments);
  if(stream.Length>16384)throw new ArgumentException("Arguments too large");
  return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(step.Tool+"\n"+Encoding.UTF8.GetString(stream.ToArray()))));
 }
 static void WriteCanonical(Utf8JsonWriter writer,JsonElement value){
  if(value.ValueKind==JsonValueKind.Object){writer.WriteStartObject();var props=value.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal).ToArray();
   if(props.Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=props.Length)throw new ArgumentException("Duplicate argument field");
   foreach(var p in props){writer.WritePropertyName(p.Name);WriteCanonical(writer,p.Value);}writer.WriteEndObject();
  }else if(value.ValueKind==JsonValueKind.Array){writer.WriteStartArray();foreach(var v in value.EnumerateArray())WriteCanonical(writer,v);writer.WriteEndArray();}
  else value.WriteTo(writer);
 }
 ContinuationAdmission Reject(string reason){InvalidatedReason??=reason;return new(false,InvalidatedReason);}
 ContinuationAdmission Check(Guid sessionId,Guid actorId,Guid planId,long planRevision,double now){
  if(InvalidatedReason is not null)return new(false,InvalidatedReason);
  if(sessionId!=session||actorId!=actor||planId!=plan||planRevision!=revision)return Reject("plan_identity_changed");
  if(!double.IsFinite(now)||now<0)return Reject("invalid_clock");
  if(now<lastNow)return Reject("clock_reversed");lastNow=now;
  if(now>=deadline)return Reject("deadline_expired");
  return new(true,"bound");
 }
 public ContinuationAdmission Bind(Guid sessionId,Guid actorId,Guid planId,long planRevision,int stepIndex,
  ContinuationStep actualStep,long receiptSequence,Guid runId,double now){
  var check=Check(sessionId,actorId,planId,planRevision,now);if(!check.Allowed)return check;
  if(active is not null||stepIndex!=completed||completed>=StepCount)return Reject("step_order_or_overlap");
  if(receiptSequence<=lastReceipt||runId==Guid.Empty||receipts.Contains(receiptSequence)||runs.Contains(runId))return Reject("invalid_or_reused_execution_identity");
  string fingerprint;try{fingerprint=Fingerprint(actualStep);}catch(ArgumentException){return Reject("invalid_step");}
  if(fingerprint!=fingerprints[stepIndex])return Reject("original_step_changed");
  lastReceipt=receiptSequence;receipts.Add(receiptSequence);runs.Add(runId);active=new(receiptSequence,runId);return new(true,"execution_bound");
 }
 public ContinuationAdmission Observe(Guid sessionId,Guid actorId,Guid planId,long planRevision,int stepIndex,
  long receiptSequence,Guid runId,string status,double now){
  var check=Check(sessionId,actorId,planId,planRevision,now);if(!check.Allowed)return check;
  if(active is null||stepIndex!=completed||active.ReceiptSequence!=receiptSequence||active.RunId!=runId)return Reject("execution_identity_changed");
  if(status is "failed" or "cancelled" or "skipped" or "not_executed" or "stopping")return Reject("step_"+status);
  if(status is "running" or "waiting" or "blocked")return new(false,"execution_pending");
  if(status!=(tools[completed]=="host_navigate_local"?"arrived":"succeeded"))return new(false,"unknown_execution_status");
  completed++;active=null;return new(true,"step_completed");
 }
 public ContinuationAdmission Invalidate(string reason){if(string.IsNullOrWhiteSpace(reason)||reason.Length>160)throw new ArgumentException("Reason required");return Reject(reason);}
 public ContinuationAdmission GoalAlreadySatisfied()=>Reject("goal_already_satisfied");
 public ContinuationAdmission TryAdmit(Guid sessionId,Guid actorId,Guid planId,long planRevision,
  Guid planningObservationId,Guid actualObservationId,bool ownerIdle,bool physicallySettled,bool freshValidationSucceeded,double now){
  var check=Check(sessionId,actorId,planId,planRevision,now);if(!check.Allowed)return check;
  if(readyClaimed)return new(false,"already_admitted");
  if(active is not null||completed!=StepCount)return new(false,"plan_tail_not_completed");
  if(!ownerIdle||!physicallySettled)return new(false,"owner_or_body_not_settled");
  if(planningObservationId==Guid.Empty||actualObservationId==Guid.Empty||planningObservationId==actualObservationId)return new(false,"fresh_observation_required");
  if(!freshValidationSucceeded)return Reject("fresh_validation_failed");
  readyClaimed=true;return new(true,"admitted_once");
 }
}
