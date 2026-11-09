using Game.Westworld;
using Westworld.Runtime.Core;
using Westworld.Scapi.Adapter;
using Engine;
using Game;
using System.Text.Json;
internal sealed partial class ToolAgentScenario{
 readonly Guid foodSourceId=Guid.NewGuid();Guid foodRequestId;readonly List<object> foodDispatches=[];object foodAcquisitionAudit,foodEatAudit,foodPickupReceipt;int foodLateDispatches;double? foodStopTime,foodDrainedTime;int? foodEventValue;ToolReceipt foodEatReceipt;
 Pickable foodDrop;int foodValue;bool foodReady,foodAcquired,foodSourceAmbiguous,foodExplicitPickup,foodNativeEvent;double foodFixtureStarted;
 readonly Dictionary<Point3,int> foodOriginalCells=[];
 AtomicActionRun foodDismount;int foodPreparation;
 bool IsFoodFixture=>ToolAgentGate.Mode=="food_drop";
 int ExactCount(int value){var inv=p.ComponentMiner.Inventory;return Enumerable.Range(0,inv.SlotsCount).Where(i=>inv.GetSlotValue(i)==value).Sum(inv.GetSlotCount);}
 void SetupFoodFixture(){if(IsFoodFixture)foodFixtureStarted=MonotonicSeconds;}
 void PlaceFoodFixture(){foodValue=Terrain.MakeBlockValue(CookedMeatBlock.Index);if(ExactCount(foodValue)!=0)throw new InvalidOperationException("Fixture food must not exist in initial inventory");
  var t=p.Project.FindSubsystem<SubsystemTerrain>(true);var body=p.ComponentBody;int x=Terrain.ToCell(body.Position.X),y=Terrain.ToCell(body.Position.Y),z=Terrain.ToCell(body.Position.Z);
  void Set(int a,int b,int c,int value){var pos=new Point3(a,b,c);foodOriginalCells.TryAdd(pos,t.Terrain.GetCellValue(a,b,c));t.ChangeCell(a,b,c,value);}
  for(int a=x-5;a<=x+5;a++)for(int c=z-5;c<=z+5;c++){Set(a,y-1,c,DirtBlock.Index);for(int b=y;b<=y+3;b++)Set(a,b,c,0);}
  Set(x+2,y,z+2,OakWoodBlock.Index);body.Position=new(x+.5f,y,z+.5f);body.Velocity=Vector3.Zero;body.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI/2);
  foodDrop=p.Project.FindSubsystem<SubsystemPickables>(true).AddPickable(foodValue,1,body.Position+new Vector3(2.75f,.2f,0),Vector3.Zero,null);
  float sourceDistance=(foodDrop.Position-body.Position).Length();if(sourceDistance<=foodDrop.DistanceToFlyToTarget||sourceDistance<=foodDrop.DistanceToPick)throw new InvalidOperationException("Fixture source inside native attraction radius");origin=previous=body.Position;path=0;
  p.ComponentVitalStats.FoodEaten+=RecordFixtureFoodEvent;
  ToolAgentGate.Log(new{kind="food_fixture",artificial=true,worldDrop=true,plantHarvest=false,fixtureFullReady=32,fixtureContent=48,sourceDistance,attractionRadius=foodDrop.DistanceToFlyToTarget,pickupRadius=foodDrop.DistanceToPick,inventoryInjection=false,healthOrFoodEdited=false,sourceId=foodSourceId,baselineCount=0,value=foodValue,count=1,position=NativeLogCoordinates.Position(foodDrop.Position),foodDrop.CreationTime,note="Host audit only; model receives normal actual observations, not fixture coordinates or a prescribed action sequence."});
 }
 void RecordFixtureFoodEvent(int value){if(IsFoodFixture&&value==foodValue){foodNativeEvent=true;foodEventValue=value;ToolAgentGate.Log(new{kind="food_event",sourceId=foodSourceId,requestId=foodRequestId,time=MonotonicSeconds,value,food=p.ComponentVitalStats.Food,statsAtEvent=p.PlayerStats.FoodItemsEaten,note="Native event occurs before food-items statistic increment; terminal validation follows."});}}
 bool TickFoodFixture(){if(!IsFoodFixture)return true;
  if(foodPreparation<2){
   if(MonotonicSeconds-foodFixtureStarted>20){Fail("food_fixture_dismount_timeout");return false;}
   if(foodPreparation==0){if(p.Entity.FindComponent<ComponentRider>(true).m_isAnimating)return false;foodDismount=actions.Start(new Dismount());foodPreparation=1;return false;}
   if(actions.IsActionRunning)return false;ToolAgentGate.Log(new{kind="food_fixture_dismount",result=foodDismount.Result,parented=p.ComponentBody.ParentBody is not null});
   if(foodDismount.Result.Status!=AtomicActionStatus.Succeeded||p.ComponentBody.ParentBody is not null){Fail("food_fixture_dismount_failed");return false;}PlaceFoodFixture();foodPreparation=2;return false;
  }
  var drops=p.Project.FindSubsystem<SubsystemPickables>(true).Pickables;var others=drops.Where(d=>!ReferenceEquals(d,foodDrop)&&!d.ToRemove&&d.Count>0&&d.Value==foodValue).ToArray();foodSourceAmbiguous|=others.Length>0;
  int owned=ExactCount(foodValue);bool gone=foodDrop.ToRemove||!drops.Contains(foodDrop);
  if(!foodAcquired&&FoodAcquisitionEvidence.Acquired(0,owned,1,gone,foodDrop.Count,foodSourceAmbiguous)){foodAcquired=true;foodAcquisitionAudit=new{sourceId=foodSourceId,value=foodValue,inventoryBefore=0,inventoryAfter=owned,worldBefore=1,worldAfter=0,pickupReceipt=foodPickupReceipt,originalRemaining=foodDrop.Count,method=foodExplicitPickup?"model_pickup_native_receipt":"native_automatic_collection",time=MonotonicSeconds};ToolAgentGate.Log(new{kind="food_acquired",value=foodValue,owned,originalRemoved=gone,remaining=foodDrop.Count,method=foodExplicitPickup?"model_pickup_native_receipt":"native_automatic_collection",competingSource=foodSourceAmbiguous});}
  ToolAgentGate.Log(new{kind="food_source_audit",sourceId=foodSourceId,time=MonotonicSeconds,owned,foodDrop.Count,foodDrop.ToRemove,removed=gone,flyToThisActor=foodDrop.FlyToGatherer?.Entity==p.Entity,foodAcquired,foodSourceAmbiguous});
  if(foodReady)return true;
  if(MonotonicSeconds-foodFixtureStarted>40){Fail("food_fixture_observation_not_ready");return false;}
  var t=p.Project.FindSubsystem<SubsystemTerrain>(true);var at=p.ComponentBody.Position;int bx=Terrain.ToCell(at.X),bz=Terrain.ToCell(at.Z);
  for(int x=bx-5;x<=bx+5;x+=1)for(int z=bz-5;z<=bz+5;z+=1)if(t.Terrain.GetChunkAtCell(x,z)?.State!=TerrainChunkState.Valid)return false;
  var snapshot=Snapshot();if(!snapshot.Observation.GetProperty("pickables").EnumerateArray().Any(d=>d.GetProperty("value").GetInt32()==foodValue))return false;
  foodReady=true;ToolAgentGate.Log(new{kind="food_fixture_ready",observation=snapshot.Observation,inventory=snapshot.Inventory});return true;
 }
 bool PickupTargetsFood(JsonElement args,JsonElement observation){
  if(args.TryGetProperty("food_value",out var value)&&value.TryGetInt32(out int v)&&v==foodValue)return true;
  return args.TryGetProperty("target",out var target)&&target.ValueKind==JsonValueKind.String&&observation.GetProperty("pickables").EnumerateArray().Any(d=>d.GetProperty("reference").GetString()==target.GetString()&&d.GetProperty("value").GetInt32()==foodValue);
 }
 void RecordFoodEatEvidence(){if(!IsFoodFixture)return;foodEatAudit=new{requestId=foodRequestId,sequence=foodEatReceipt.Sequence,value=eatValue,inventoryBefore=eatCountBefore,inventoryAfter=ExactCount(foodValue),foodBefore=eatBefore,foodAfter=p.ComponentVitalStats.Food,statsBefore=eatenBefore,statsAfter=p.PlayerStats.FoodItemsEaten,eventValue=foodEventValue,nativeStatus="succeeded",time=MonotonicSeconds};}
 void LogFoodEvidence(){if(!IsFoodFixture)return;ToolAgentGate.Log(new{kind="food_chain_evidence",schema=1,sessionId=gateway.SessionId,actorId=gateway.ActorId,sourceId=foodSourceId,value=foodValue,initialFishValue=162,baselineCount=0,sourceCount=1,uniqueSourceThroughout=!foodSourceAmbiguous,inventoryInjection=false,acquisition=foodAcquisitionAudit,eat=foodEatAudit,shutdown=new{stopTime=foodStopTime,drainedTime=foodDrainedTime,budgetSeconds=ToolScenarioBudget.DrainSeconds,runBudgetSeconds=ToolAgentGate.Budget.WallSeconds,idle=foodDrainedTime.HasValue,lateDispatches=foodLateDispatches},dispatches=foodDispatches,note="Host fixture audit, not cryptographic proof or natural-resource provenance. Missing stages remain null."});}
 void FinishFoodFixture(){if(!IsFoodFixture)return;p.ComponentVitalStats.FoodEaten-=RecordFixtureFoodEvent;var t=p.Project.FindSubsystem<SubsystemTerrain>(true);foreach(var c in foodOriginalCells)t.ChangeCell(c.Key.X,c.Key.Y,c.Key.Z,c.Value);if(foodDrop!=null&&!foodDrop.ToRemove)foodDrop.ToRemove=true;}
}
