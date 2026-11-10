using Game;
using Game.Westworld;
using System.Text.Json;
using Westworld.Runtime.Core;
using Westworld.Decisions.Local;
internal sealed partial class ToolAgentScenario {
 bool IsNaturalFood=>ToolAgentGate.Mode=="natural_food";
 sealed class NaturalMealAttempt {
  public Guid RequestId{get;init;} public long? ReceiptSequence{get;set;} public Guid? RunId{get;set;} public int Value{get;init;} public int Before{get;init;} public float FoodBefore{get;init;} public long StatsBefore{get;init;} public int Acquired{get;init;} public float Nutrition{get;init;}
  public int Events{get;set;} public bool Matching{get;set;}=true; public string NativeStatus{get;set;} public NaturalFoodMeal Result{get;set;}
 }
 readonly List<NaturalMealAttempt> naturalMeals=[];readonly List<object> naturalMealEvents=[];NaturalMealAttempt naturalMeal;
 void BeginNaturalMeal(ToolDecisionStep step,Guid? request){if(!IsNaturalFood||step.Tool!="act_Eat")return;if(naturalMeals.Count>=128){naturalUnknown="meal_capacity";return;}int value=step.Arguments.GetProperty("value").GetInt32();naturalMeal=new(){RequestId=request??naturalRequest,Value=value,Before=ExactCount(value),FoodBefore=p.ComponentVitalStats.Food,StatsBefore=p.PlayerStats.FoodItemsEaten,Acquired=naturalLedger.State.Sources.Where(s=>s.Value==value&&naturalSources.TryGetValue(s.SourceId,out var a)&&a.TerminalStatus=="succeeded").Sum(s=>s.Acquired),Nutrition=BlocksManager.Blocks[Terrain.ExtractContents(value)].GetNutritionalValue(value)};naturalMeals.Add(naturalMeal);}
 void NaturalFoodEvent(int value){if(!IsNaturalFood)return;var m=naturalMeal;if(m is null||naturalMealEvents.Count>=256){naturalUnknown="unbound_or_overflow_food_event";return;}m.Events++;m.Matching&=value==m.Value;int after=ExactCount(value);if(naturalBaseline.GetValueOrDefault(value)>0)naturalLedger.ConsumeInitial(Guid.NewGuid(),value,after+1,after,true,MonotonicSeconds);else naturalLedger.Consume(Guid.NewGuid(),value,1,after+1,after,true,MonotonicSeconds);var e=new{requestId=m.RequestId,value,time=MonotonicSeconds,inventoryAfter=after,food=p.ComponentVitalStats.Food,statsBeforeIncrement=p.PlayerStats.FoodItemsEaten};naturalMealEvents.Add(e);ToolAgentGate.Log(new{kind="natural_food_event",fact=e,hostOnly=true});}
 void BindNaturalMeal(ToolDecisionStep step,ToolReceipt receipt){if(!IsNaturalFood||step.Tool!="act_Eat"||naturalMeal is null)return;naturalMeal.ReceiptSequence=receipt.Sequence;if(receipt.State=="returned"&&receipt.Data.ValueKind==JsonValueKind.Object&&receipt.Data.TryGetProperty("runId",out var run)&&run.TryGetGuid(out var id))naturalMeal.RunId=id;else naturalUnknown="eat_missing_native_run";}
 void NaturalMealTerminal(ToolReceipt receipt,JsonElement actual){if(!IsNaturalFood||receipt.Tool!="act_Eat")return;var m=naturalMeals.LastOrDefault(x=>x.ReceiptSequence==receipt.Sequence);if(m is null||!actual.TryGetProperty("runId",out var id)||!id.TryGetGuid(out var run)||run!=m.RunId){naturalUnknown="eat_terminal_run_mismatch";return;}m.NativeStatus=Status(actual);m.Result=new(m.RequestId,m.ReceiptSequence,m.RunId,m.Value,naturalBaseline.GetValueOrDefault(m.Value),m.Acquired,m.Before,ExactCount(m.Value),m.Events,m.Matching,m.Nutrition,m.FoodBefore,p.ComponentVitalStats.Food,m.StatsBefore,p.PlayerStats.FoodItemsEaten,m.NativeStatus);ToolAgentGate.Log(new{kind="natural_meal_terminal",meal=m.Result,verified=NaturalFoodEvidence.Verified(m.Result)});if(ReferenceEquals(m,naturalMeal))naturalMeal=null;}
 bool NaturalMealGoal()=>naturalMeals.Any(m=>m.Result is not null&&NaturalFoodEvidence.Verified(m.Result));
}
