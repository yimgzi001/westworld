// The original tail continues while its candidate is pending. A speculative
// response never enters the ordinary-response queue.
public static class ContinuationScheduling {
 public static bool MayDispatchOriginalTail(bool fenceActive,bool tailSucceeded,int queuedOriginalSteps)=>fenceActive&&!tailSucceeded&&queuedOriginalSteps>0;
 public static bool IsOrdinaryResponse(bool inferenceExists,bool inferenceIsPrefetch)=>inferenceExists&&!inferenceIsPrefetch;
}
public sealed record PlanDecisionTiming(Guid DecisionId,double RequestedAt,double RepliedAt);
public sealed class PlanStepOwnership {
 PlanDecisionTiming timing;
 readonly Dictionary<object,Guid> owners=new(ReferenceEqualityComparer.Instance);
 public void Install(Guid decisionId,IEnumerable<object> steps){
  if(decisionId==Guid.Empty)throw new ArgumentException("Actual request identity required");
  var array=steps.ToArray();if(array.Length>4||array.Any(s=>s is null)||array.Distinct(ReferenceEqualityComparer.Instance).Count()!=array.Length)throw new ArgumentException("Bounded distinct plan steps required");
  timing=null;owners.Clear();foreach(var step in array)owners.Add(step,decisionId);
 }
 public void Install(Guid decisionId,IEnumerable<object> steps,double requestedAt,double repliedAt){
  if(!double.IsFinite(requestedAt)||!double.IsFinite(repliedAt)||requestedAt<0||repliedAt<requestedAt)throw new ArgumentException("Actual ordered decision timestamps required");
  Install(decisionId,steps);timing=new(decisionId,requestedAt,repliedAt);
 }
 public PlanDecisionTiming TimingFor(object step){var id=For(step);return timing is not null&&timing.DecisionId==id?timing:throw new InvalidOperationException("Step has no actual decision timing");}
 public Guid For(object step)=>owners.TryGetValue(step,out var id)?id:throw new InvalidOperationException("Step has no admitted decision owner");
}
