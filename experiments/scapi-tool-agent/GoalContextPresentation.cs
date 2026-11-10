using Westworld.Runtime.Core;
// Compact semantic projection. Original goal is already present in the enclosing facts.
// Does not drop items, change states, or infer a recommended action.
public static class GoalContextPresentation {
 public const string BudgetNote="Counts precede submission; cancelled requests are not refunded. Unknown cancellation totals/usage are null. Evidence IDs denote own experience, not world sources. Historical deltas remain unchanged.";
 public static object? Progress(GoalExecutionContext? p)=>p is null?null:new{
  p.GoalId,p.CurrentModelSubgoal,p.ObservationId,p.ObservedAt,
  items=p.Items.Select(i=>new{i.Value,i.InitialCount,i.CurrentCount,i.AcquiredCount,i.ConsumedCount,i.Nutrition,i.NewSourceEligible,i.CurrentlyOwnedEdible,i.NewSourceVerified,i.MealVerified,i.EvidenceIds}),
  criteria=p.Criteria.Select(c=>new{c.Name,c.State})
 };
}
