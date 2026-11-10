using System.Collections.Immutable;
namespace Westworld.Runtime.Core;

public enum GoalEvidenceState { NotYet,Verified,Unknown }
public sealed record GoalCriterion(string Name,GoalEvidenceState State,string Reason,Guid ObservationId,double ObservedAt,ImmutableArray<Guid> EvidenceIds);
public sealed record GoalItemFacts(int Value,int InitialCount,int? CurrentCount,int? AcquiredCount,int? ConsumedCount,
 float? Nutrition,bool NewSourceEligible,GoalEvidenceState CurrentlyOwnedEdible,
 GoalEvidenceState NewSourceVerified,GoalEvidenceState MealVerified,ImmutableArray<Guid> EvidenceIds);
public sealed record GoalBudgetFacts(int SentRequests,int? CancelledRequests,int MaximumRequests,int RemainingRequests,
 double WallSecondsRemaining,double? RequestSecondsRemaining,bool Speculative,Guid? PredecessorId,
 long? ReportedInputTokens,long? ReportedOutputTokens,bool UsageComplete);
public sealed record GoalExecutionContext(Guid GoalId,string OriginalGoal,string? CurrentModelSubgoal,
 Guid ObservationId,double ObservedAt,ImmutableArray<GoalItemFacts> Items,
 ImmutableArray<GoalCriterion> Criteria,GoalBudgetFacts Budget);

/// <summary>Only own observed inventory and validated own-event summaries. Never hidden world sources.
/// Bridge must derive MealVerified from NaturalFoodEvidence.Verified, not a model assertion.</summary>
public sealed record GoalItemInput(int Value,int InitialCount,int? CurrentCount,float? Nutrition,
 int AcquiredCount,int ConsumedCount,bool SourceVerified,bool MealVerified,
 ImmutableArray<Guid> EvidenceIds);
public sealed record GoalBudgetInput(int SentRequests,int? CancelledRequests,int MaximumRequests,
 double Now,double WallDeadline,double? RequestDeadline,bool Speculative,Guid? PredecessorId,
 long? ReportedInputTokens,long? ReportedOutputTokens,bool UsageComplete);
public static class GoalExecutionProjection {
 public static GoalExecutionContext Build(Guid goalId,string goal,string? subgoal,Guid observationId,double observedAt,
 bool inventoryComplete,bool auditComplete,IReadOnlyList<GoalItemInput> items,GoalBudgetInput budget){
  if(goalId==Guid.Empty||observationId==Guid.Empty||string.IsNullOrWhiteSpace(goal)||goal.Length>4000||subgoal?.Length>2000
   ||!double.IsFinite(observedAt)||observedAt<0||items is null||items.Count>256)throw new ArgumentException("Invalid goal snapshot");
  ArgumentNullException.ThrowIfNull(budget);
  if(!double.IsFinite(budget.Now)||!double.IsFinite(budget.WallDeadline)||budget.Now<observedAt||budget.Now<0
   ||budget.WallDeadline<0||budget.RequestDeadline is double deadline&&(!double.IsFinite(deadline)||deadline<0)
   ||budget.SentRequests<0||budget.MaximumRequests<0||budget.SentRequests>budget.MaximumRequests
   ||budget.CancelledRequests<0||budget.CancelledRequests>budget.SentRequests||budget.ReportedInputTokens<0||budget.ReportedOutputTokens<0
   ||budget.UsageComplete&&(!budget.ReportedInputTokens.HasValue||!budget.ReportedOutputTokens.HasValue)
   ||budget.Speculative&&(budget.PredecessorId is not Guid prior||prior==Guid.Empty))throw new ArgumentException("Invalid authoritative budget");
  var output=ImmutableArray.CreateBuilder<GoalItemFacts>();var values=new HashSet<int>();
  foreach(var i in items){
   if(i is null||i.Value<0||!values.Add(i.Value)||i.InitialCount<0||i.CurrentCount<0||i.AcquiredCount<0||i.ConsumedCount<0||i.ConsumedCount>i.AcquiredCount
    ||i.Nutrition is float n&&(!float.IsFinite(n)||n<0)||i.EvidenceIds.IsDefault||i.EvidenceIds.Length>256||i.EvidenceIds.Any(id=>id==Guid.Empty))throw new ArgumentException("Invalid own item facts");
   bool eligible=i.InitialCount==0;
   bool knownSource=auditComplete&&i.SourceVerified&&i.EvidenceIds.Length>0;
   var owned=!inventoryComplete||i.CurrentCount is null||i.Nutrition is null?GoalEvidenceState.Unknown:
    i.CurrentCount>0&&i.Nutrition>0?GoalEvidenceState.Verified:GoalEvidenceState.NotYet;
   // Known current possession is distinct from proof of new-source acquisition.
   var acquired=!eligible?GoalEvidenceState.NotYet:!auditComplete||!knownSource&&(i.AcquiredCount>0||inventoryComplete&&i.CurrentCount>i.InitialCount)?GoalEvidenceState.Unknown:
    knownSource&&i.AcquiredCount>0?GoalEvidenceState.Verified:GoalEvidenceState.NotYet;
   var meal=!eligible?GoalEvidenceState.NotYet:!auditComplete?GoalEvidenceState.Unknown:
    knownSource&&i.ConsumedCount>0&&i.MealVerified?GoalEvidenceState.Verified:
    i.ConsumedCount>0&&!knownSource?GoalEvidenceState.Unknown:GoalEvidenceState.NotYet;
   output.Add(new(i.Value,i.InitialCount,inventoryComplete?i.CurrentCount:null,knownSource?i.AcquiredCount:null,
    knownSource?i.ConsumedCount:null,i.Nutrition,eligible,owned,acquired,meal,i.EvidenceIds.Distinct().ToImmutableArray()));
  }
  var facts=output.ToImmutable();var criteria=ImmutableArray.CreateBuilder<GoalCriterion>();
  void Add(string name,Func<GoalItemFacts,GoalEvidenceState> select,bool complete,string reason){
   var states=facts.Select(select).ToArray();var status=states.Contains(GoalEvidenceState.Verified)?GoalEvidenceState.Verified:
    !complete||states.Contains(GoalEvidenceState.Unknown)?GoalEvidenceState.Unknown:GoalEvidenceState.NotYet;
   criteria.Add(new(name,status,reason,observationId,observedAt,facts.Where(f=>select(f)==GoalEvidenceState.Verified).SelectMany(f=>f.EvidenceIds).Distinct().ToImmutableArray()));
  }
  Add("currently_owned_edible",f=>f.CurrentlyOwnedEdible,inventoryComplete,"Current own inventory only; does not establish new source or eating.");
  Add("new_source_verified",f=>f.NewSourceVerified,auditComplete,"Any-resource historical own acquisition evidence, NOT a food criterion; initial same-value supplies excluded.");
  Add("new_source_currently_held",f=>f.NewSourceVerified==GoalEvidenceState.Verified?
   !inventoryComplete||f.CurrentCount is null?GoalEvidenceState.Unknown:f.CurrentCount>0?GoalEvidenceState.Verified:GoalEvidenceState.NotYet:
   f.NewSourceVerified,inventoryComplete&&auditComplete,"Any-resource current holding, NOT a food criterion; loss never implies consumption.");
  GoalEvidenceState EdibleSource(GoalItemFacts f,bool requireHeld){
   if(f.NewSourceVerified!=GoalEvidenceState.Verified)return f.NewSourceVerified;
   if(f.Nutrition is null)return GoalEvidenceState.Unknown;
   if(f.Nutrition<=0)return GoalEvidenceState.NotYet;
   if(!requireHeld)return GoalEvidenceState.Verified;
   return !inventoryComplete||f.CurrentCount is null?GoalEvidenceState.Unknown:
    f.CurrentCount>0?GoalEvidenceState.Verified:GoalEvidenceState.NotYet;
  }
  Add("new_edible_source_verified",f=>EdibleSource(f,false),auditComplete,
   "Verified new acquisition of positive-nutrition food; nonfood resource acquisition does not satisfy this condition.");
  Add("new_edible_source_currently_held",f=>EdibleSource(f,true),inventoryComplete&&auditComplete,
   "Verified new positive-nutrition food is currently held; unknown nutrition remains unknown.");
  Add("meal_verified",f=>f.MealVerified,auditComplete,"Native event, exact quantities, successful receipt/run and positive food/stat changes required.");
  return new(goalId,goal,subgoal,observationId,observedAt,facts,criteria.ToImmutable(),new(budget.SentRequests,budget.CancelledRequests,
   budget.MaximumRequests,budget.MaximumRequests-budget.SentRequests,Math.Max(0,budget.WallDeadline-budget.Now),
   budget.RequestDeadline is double d?Math.Max(0,Math.Min(d,budget.WallDeadline)-budget.Now):null,budget.Speculative,
   budget.PredecessorId,budget.ReportedInputTokens,budget.ReportedOutputTokens,budget.UsageComplete));
 }
}
