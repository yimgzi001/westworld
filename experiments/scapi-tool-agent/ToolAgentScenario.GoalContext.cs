using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Westworld.Runtime.Core;
internal sealed partial class ToolAgentScenario {
 readonly Guid persistentGoalId=Guid.NewGuid();
 readonly Dictionary<int,Guid> ownExperienceIds=[];
 double goalSnapshotAt;
 static readonly JsonSerializerOptions goalJson=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)}};
 JsonElement GoalFacts(JsonElement inventory,OfferPurpose purpose,bool preparingRequest){
  double now=MonotonicSeconds;
  var current=inventory.GetProperty("slots").EnumerateArray().Where(s=>s.GetProperty("count").GetInt32()>0).GroupBy(s=>s.GetProperty("value").GetInt32()).ToDictionary(g=>g.Key,g=>g.Sum(s=>s.GetProperty("count").GetInt32()));
  var nutrition=new Dictionary<int,float>();foreach(var value in current.Keys.Concat(ownExperienceIds.Keys).Distinct())if(itemFacts.TryGetValue(value,out var item)&&item.TryGetProperty("nutrition",out var n)&&n.TryGetSingle(out var f)&&float.IsFinite(f)&&f>=0)nutrition[value]=f;
  bool speculative=purpose==OfferPurpose.AfterPrecedingSuccess;
  double? deadline=preparingRequest||speculative?null:acceptedCandidateDeadline;
  var budget=new GoalBudgetInput(rounds,null,ToolAgentGate.Budget.MaxRequests,now,now+Math.Max(0,ToolAgentGate.Budget.Remaining(ToolAgentGate.ElapsedSeconds)),deadline,speculative,speculative?predecessor?.Binding.RunId:null,null,null,false);
  GoalExecutionContext progress=null;
  if(IsNaturalMode){
   var state=naturalLedger.State;
   var verified=naturalSources.Where(x=>x.Value.TerminalStatus=="succeeded"&&x.Value.RunId.HasValue&&x.Value.ReceiptSequence.HasValue).Select(x=>x.Key).ToHashSet();
   foreach(var source in state.Sources.Where(s=>s.Acquired>0&&verified.Contains(s.SourceId)))if(!ownExperienceIds.ContainsKey(source.Value)&&ownExperienceIds.Count<256)ownExperienceIds.Add(source.Value,Guid.NewGuid());
   var evidence=ownExperienceIds.ToDictionary(x=>x.Key,x=>ImmutableArray.Create(x.Value));
   var meals=naturalMeals.Where(m=>m.Result is not null).Select(m=>m.Result).ToArray();
   var items=NaturalGoalProgressBridge.Project(naturalBaseline,current,nutrition,state,meals,evidence,verified);
   progress=GoalExecutionProjection.Build(persistentGoalId,Goal,currentPlanGoal,currentObservationId,goalSnapshotAt,true,naturalUnknown is null&&!state.Unknown,items,budget);
  }
  return JsonSerializer.SerializeToElement(new{progress=GoalContextPresentation.Progress(progress),budget=progress?.Budget??new GoalBudgetFacts(rounds,null,ToolAgentGate.Budget.MaxRequests,ToolAgentGate.Budget.MaxRequests-rounds,Math.Max(0,ToolAgentGate.Budget.Remaining(ToolAgentGate.ElapsedSeconds)),deadline is double end?Math.Max(0,end-now):null,speculative,speculative?predecessor?.Binding.RunId:null,null,null,false),requestLifecycle=preparingRequest||speculative?"prepared_not_sent":"validation_only_no_new_request",requestOrdinal=(int?)null,remainingBeforeDispatch=ToolAgentGate.Budget.MaxRequests-rounds,thisPreparedRequestWillCountIfSubmitted=preparingRequest||speculative,preparedRequestUpperBoundSeconds=preparingRequest||speculative?(double?)ToolAgentGate.Budget.RequestWindow(ToolAgentGate.ElapsedSeconds,speculative):null},goalJson);
 }
}
