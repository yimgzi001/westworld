using System.Collections.Immutable;
using Westworld.Runtime.Core;
/// <summary>Host-only projection boundary. Publishes no source IDs/positions/target health.
/// Public evidence IDs refer to this actor's experience summaries, not hidden origin audit objects.</summary>
public static class NaturalGoalProgressBridge {
 public static ImmutableArray<GoalItemInput> Project(IReadOnlyDictionary<int,int> initial,
  IReadOnlyDictionary<int,int>? inventory,IReadOnlyDictionary<int,float> knownNutrition,NaturalResourceState ledger,
  IReadOnlyList<NaturalFoodMeal> meals,IReadOnlyDictionary<int,ImmutableArray<Guid>> publicEvidence,
  IReadOnlySet<Guid> verifiedHarvestSources){
  ArgumentNullException.ThrowIfNull(initial);ArgumentNullException.ThrowIfNull(knownNutrition);ArgumentNullException.ThrowIfNull(ledger);
  ArgumentNullException.ThrowIfNull(meals);ArgumentNullException.ThrowIfNull(publicEvidence);ArgumentNullException.ThrowIfNull(verifiedHarvestSources);
  if(verifiedHarvestSources.Contains(Guid.Empty))throw new ArgumentException("Invalid verified source identity");
  // Uncollected drops may be invisible: never enumerate their values as actor facts.
  var keys=initial.Keys.Concat(inventory?.Keys??Enumerable.Empty<int>()).Concat(ledger.Sources.Where(s=>s.Acquired>0&&verifiedHarvestSources.Contains(s.SourceId)).Select(s=>s.Value)).Distinct().Order();
  return keys.Select(value=>{
   var sources=ledger.Sources.Where(s=>s.Value==value&&s.Acquired>0).ToArray();
   int acquired=sources.Where(s=>verifiedHarvestSources.Contains(s.SourceId)).Sum(s=>s.Acquired);
   bool allSourcesVerified=sources.All(s=>verifiedHarvestSources.Contains(s.SourceId));
   int consumed=allSourcesVerified?ledger.Consumed.GetValueOrDefault(value):0;
   var ids=publicEvidence.TryGetValue(value,out var e)?e:ImmutableArray<Guid>.Empty;
   // Reuse exact acceptance contract, including actual food increase; never infer eating from disappearance.
   bool verified=!ledger.Unknown&&allSourcesVerified&&meals.Any(m=>m.Value==value&&m.InitialCount==initial.GetValueOrDefault(value)
    &&m.AcquiredCount<=acquired&&m.EventCount<=consumed&&NaturalFoodEvidence.Verified(m));
   return new GoalItemInput(value,initial.GetValueOrDefault(value),inventory?.GetValueOrDefault(value),
    knownNutrition.TryGetValue(value,out var nutrition)?nutrition:null,acquired,consumed,!ledger.Unknown&&allSourcesVerified&&acquired>0,verified,ids);
  }).ToImmutableArray();
 }
}
