// Host audit only. Origin observation is not proof this actor harvested/killed the origin.
public sealed record ResourceTarget(Guid WorldId,string Kind,int? X,int? Y,int? Z,int? OriginalBlockValue,Guid? OriginEntityId);
public sealed record ResourceOrigin(Guid EventId,Guid SourceId,ResourceTarget Target,int Value,int Count,double CreationGameTime,double GameTime,double Time);
public sealed record ResourceTransfer(Guid EventId,Guid SourceId,Guid ActorId,int Value,int SourceBefore,int SourceAfter,int? InventoryBefore,int? InventoryAfter,string Method,double Time);
public sealed record ResourceSourceState(Guid SourceId,int Value,int Produced,int Remaining,int Acquired);
public sealed record NaturalResourceState(bool Unknown,string Reason,IReadOnlyList<ResourceSourceState> Sources,IReadOnlyDictionary<int,int> ExpectedInventory,IReadOnlyDictionary<int,int> Consumed);
public sealed class NaturalResourceAcquisitionLedger {
 // Block identity excludes lighting only; content and all data bits remain intact.
 public static int ObservedBlockIdentity(int rawValue)=>rawValue & ~15360;
 readonly Guid world,actor;readonly long failures;readonly Dictionary<int,int> baseline,inventory,consumed=[];
 readonly Dictionary<Guid,(ResourceTarget Target,double Time)> observed=[];
 readonly Dictionary<Guid,ResourceOrigin> origins=[];readonly Dictionary<Guid,ResourceSourceState> sources=[];
 readonly Dictionary<Guid,object> events=[];double last;bool unknown;string reason="awaiting_origin";
 const int Limit=256;
 public NaturalResourceAcquisitionLedger(Guid worldId,Guid actorId,IReadOnlyDictionary<int,int> initialInventory,long auditFailures,double now){
  if(worldId==Guid.Empty||actorId==Guid.Empty||auditFailures<0||!double.IsFinite(now)||now<0||initialInventory.Count>Limit||initialInventory.Any(p=>p.Key<0||p.Value<0))throw new ArgumentException("invalid_scope");world=worldId;actor=actorId;failures=auditFailures;last=now;baseline=new(initialInventory);inventory=new(initialInventory);
 }
 public NaturalResourceState State=>new(unknown,reason,sources.Values.ToArray(),new System.Collections.ObjectModel.ReadOnlyDictionary<int,int>(new Dictionary<int,int>(inventory)),new System.Collections.ObjectModel.ReadOnlyDictionary<int,int>(new Dictionary<int,int>(consumed)));
 void Fault(string s){if(!unknown){unknown=true;reason=s;}}
 bool Time(double now){if(!double.IsFinite(now)||now<last){Fault("invalid_or_regressed_time");return false;}last=now;return !unknown;}
 bool TargetValid(ResourceTarget t)=>t is not null&&t.WorldId==world&&(t.Kind=="block_drop"&&t.X is >=-1000000 and <=1000000&&t.Y is >=0 and <=1024&&t.Z is >=-1000000 and <=1000000&&t.OriginalBlockValue is >=0&&t.OriginEntityId is null||t.Kind=="creature_loot"&&t.OriginEntityId is Guid id&&id!=Guid.Empty&&t.X is null&&t.Y is null&&t.Z is null&&t.OriginalBlockValue is null);
 bool Replay(Guid id,object fact){if(id==Guid.Empty){Fault("empty_event_id");return true;}if(events.TryGetValue(id,out var old)){if(!Equals(old,fact))Fault("event_conflict");return true;}if(events.Count>=Limit){Fault("event_capacity");return true;}return false;}
 public void ObserveTarget(Guid evidenceId,ResourceTarget target,double now){if(!Time(now))return;if(evidenceId==Guid.Empty||target is null||!TargetValid(target)){Fault("invalid_observation");return;}if(observed.TryGetValue(evidenceId,out var old)){if(old!=(target,now))Fault("observation_conflict");return;}if(observed.Count>=128){Fault("observation_capacity");return;}observed.Add(evidenceId,(target,now));}
 public void Origin(Guid evidenceId,ResourceOrigin fact){if(fact is null){Fault("missing_origin");return;}if(Replay(fact.EventId,fact))return;if(!Time(fact.Time))return;
  if(!observed.TryGetValue(evidenceId,out var o)||o.Target!=fact.Target||fact.Time-o.Time>30||!TargetValid(fact.Target)){Fault("origin_not_bound_to_recent_observation");return;}
  if(fact.SourceId==Guid.Empty||fact.Value<0||fact.Count<=0||fact.Count>1000000||!double.IsFinite(fact.CreationGameTime)||!double.IsFinite(fact.GameTime)||fact.CreationGameTime<0||fact.CreationGameTime>fact.GameTime||origins.ContainsKey(fact.SourceId)){Fault("invalid_or_reused_source");return;}
  if(!inventory.ContainsKey(fact.Value)&&inventory.Count>=Limit){Fault("inventory_capacity");return;}events.Add(fact.EventId,fact);origins.Add(fact.SourceId,fact);sources.Add(fact.SourceId,new(fact.SourceId,fact.Value,fact.Count,fact.Count,0));inventory.TryAdd(fact.Value,0);reason="origin_registered";
 }
 public void Transfer(ResourceTransfer f){if(f is null){Fault("missing_transfer");return;}if(f.ActorId!=actor&&!sources.ContainsKey(f.SourceId))return;if(Replay(f.EventId,f))return;if(!Time(f.Time))return;
  if(f.ActorId!=actor){Fault("tracked_source_transferred_to_other_actor");return;}if(!sources.TryGetValue(f.SourceId,out var s)){Fault("unattributed_actor_acquisition");return;}
  int before=inventory.GetValueOrDefault(f.Value);long gain=(long)f.SourceBefore-f.SourceAfter;
  if(f.Value!=s.Value||f.SourceBefore!=s.Remaining||f.SourceAfter<0||gain<=0||f.InventoryBefore!=before||f.InventoryAfter is not int after||(long)after-before!=gain||f.Method is not("automatic" or "explicit")){Fault("transfer_not_conserved");return;}
  events.Add(f.EventId,f);sources[f.SourceId]=s with{Remaining=f.SourceAfter,Acquired=checked(s.Acquired+(int)gain)};inventory[f.Value]=after;reason="partially_or_fully_acquired";
 }
 // Separate optional consumption evidence; does not assign a fungible item to one source.
 public void Consume(Guid eventId,int value,int count,int before,int after,bool nativeFoodEvent,double now){var f=(eventId,value,count,before,after,nativeFoodEvent,now);if(Replay(eventId,f))return;if(!Time(now))return;
  if(!nativeFoodEvent||baseline.GetValueOrDefault(value)!=0||count<=0||before!=inventory.GetValueOrDefault(value)||after<0||(long)before-after!=count||count>sources.Values.Where(s=>s.Value==value).Sum(s=>s.Acquired)-consumed.GetValueOrDefault(value)){Fault("consumption_not_attributed_or_conserved");return;}events.Add(eventId,f);inventory[value]=after;consumed[value]=consumed.GetValueOrDefault(value)+count;reason="acquired_pool_consumed";
 }
 public void Reconcile(IReadOnlyDictionary<int,int> actualInventory,IReadOnlyDictionary<Guid,(int Value,int Count)> actualSources,long auditFailures,double now){if(!Time(now))return;if(actualInventory is null||actualSources is null){Fault("missing_reconciliation");return;}if(auditFailures!=failures){Fault("audit_failure_count_changed");return;}
  if(actualInventory.Count>Limit||actualInventory.Any(p=>p.Key<0||p.Value<0)||inventory.Any(p=>actualInventory.GetValueOrDefault(p.Key)!=p.Value)||actualInventory.Any(p=>inventory.GetValueOrDefault(p.Key)!=p.Value)){Fault("unexplained_inventory_change");return;}
  foreach(var s in sources.Values){if(!actualSources.TryGetValue(s.SourceId,out var a)){if(s.Remaining!=0)Fault("source_missing_without_transfer");}else if(a.Value!=s.Value||a.Count!=s.Remaining)Fault("unexplained_source_change");}
 }
}
