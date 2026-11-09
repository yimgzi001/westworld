public sealed record FoodSourceTransfer(Guid SourceId,Guid ActorId,int Value,int SourceBefore,int SourceAfter,
 int? InventoryBefore,int? InventoryAfter,string Method,double MonotonicSeconds,double GameTime);
public sealed record FoodSourceState(bool Unknown,string Reason,bool Acquired,int AcquiredCount,int ConsumedCount,
 int ExpectedInventory,int SourceRemaining,bool ConsumptionPending,string? AcquisitionMethod,double? AcquisitionTime);
/// <summary>Host audit only. Conserves exact native values and target-source transfers; never authorizes actions.
/// Unknown is latched. Observation of a same-valued item elsewhere is deliberately not an acquisition event.</summary>
public sealed class FoodSourceLedger {
 readonly Guid source,actor;readonly int value,sourceInitial;readonly long failureBaseline;
 int expectedInventory,remaining,acquired,consumed,pending;double lastTime,pendingAt;bool unknown;string reason="awaiting_target_transfer";string? method;double? acquiredAt;
 public FoodSourceLedger(Guid sourceId,Guid actorId,int fullValue,int sourceCount,int baselineInventory,long listenerFailures,double now){
  if(sourceId==Guid.Empty||actorId==Guid.Empty||fullValue<0||sourceCount<1||baselineInventory<0||listenerFailures<0||!double.IsFinite(now)||now<0)throw new ArgumentException("valid initial audit scope required");
  source=sourceId;actor=actorId;value=fullValue;sourceInitial=remaining=sourceCount;expectedInventory=baselineInventory;failureBaseline=listenerFailures;lastTime=now;
  if(baselineInventory!=0)Fault("initial_food_not_new");
 }
 public FoodSourceState State=>new(unknown,reason,!unknown&&acquired==sourceInitial,acquired,consumed,expectedInventory,remaining,pending>0,method,acquiredAt);
 void Fault(string cause){if(!unknown){unknown=true;reason=cause;}}
 bool Time(double now){if(!double.IsFinite(now)||now<lastTime){Fault("audit_time_invalid_or_regressed");return false;}lastTime=now;return true;}
 public void Transfer(FoodSourceTransfer fact){
  if(fact is null){Fault("missing_transfer_event");return;}
  // Unrelated actors and sources cannot spoil provenance merely by possessing the same value.
  if(fact.ActorId!=actor&&fact.SourceId!=source)return;
  if(fact.ActorId==actor&&fact.SourceId!=source&&fact.Value!=value)return;
  if(!Time(fact.MonotonicSeconds)||unknown)return;
  if(fact.SourceId==source&&fact.Value!=value){Fault("target_value_changed");return;}
  if(fact.ActorId!=actor){Fault("target_transferred_to_other_actor");return;}
  if(fact.SourceId!=source){Fault("competing_source_acquired_by_actor");return;}
  if(!double.IsFinite(fact.GameTime)||fact.GameTime<0||fact.Method is not("automatic" or "explicit")
   ||fact.SourceBefore!=remaining||fact.SourceAfter<0||fact.SourceAfter>=fact.SourceBefore
   ||fact.InventoryBefore is not int before||fact.InventoryAfter is not int after||before!=expectedInventory
   ||after<before||after-before!=fact.SourceBefore-fact.SourceAfter||pending>0){Fault("transfer_not_conserved_or_missing_counts");return;}
  int gain=fact.SourceBefore-fact.SourceAfter;
  if(gain>sourceInitial-acquired){Fault("target_transfer_exceeds_source");return;}
  expectedInventory=after;remaining=fact.SourceAfter;acquired+=gain;method=method is null?fact.Method:method==fact.Method?method:"mixed";
  if(acquired==sourceInitial){acquiredAt=fact.MonotonicSeconds;reason="target_acquired";}else reason="target_partially_acquired";
 }
 public void FoodEaten(int fullValue,int count,int inventoryBefore,double now){
  if(fullValue!=value)return;
  if(!Time(now)||unknown)return;
  if(pending>0||count<1||(inventoryBefore!=expectedInventory&&inventoryBefore!=expectedInventory-count)||count>expectedInventory||acquired!=sourceInitial){Fault("consumption_without_owned_target");return;}
  if(inventoryBefore==expectedInventory-count){expectedInventory=inventoryBefore;consumed+=count;reason="target_consumption_conserved";return;}
  pending=count;pendingAt=now;reason="awaiting_native_inventory_decrement";
 }
 public void Observe(int fullValue,int sourceCount,int actualInventory,long listenerFailures,double now,bool sourceAvailable=true){
  if(!Time(now)||unknown)return;
  if(listenerFailures!=failureBaseline){Fault("native_audit_listener_failure");return;}
  if(fullValue!=value){Fault("target_value_changed");return;}
  if(!sourceAvailable&&remaining>0){Fault("source_disappeared_without_transfer_event");return;}
  if(sourceCount!=remaining){Fault("source_change_without_transfer_event");return;}
  if(pending>0){
   if(actualInventory==expectedInventory-pending){expectedInventory=actualInventory;consumed+=pending;pending=0;reason="target_consumption_conserved";return;}
   if(actualInventory==expectedInventory&&now-pendingAt<=.5)return;
   Fault("consumption_inventory_not_conserved");return;
  }
  if(actualInventory!=expectedInventory)Fault("inventory_change_without_transfer_event");
 }
}
