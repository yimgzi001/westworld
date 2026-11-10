#!/usr/bin/env python3
"""Independent offline natural-food log consistency gate; never a model/autonomy claim."""
import argparse,json,math,pathlib,uuid

def number(x):return type(x) in (int,float) and math.isfinite(x)
def guid(x):
 try:return isinstance(x,str) and uuid.UUID(x).int!=0
 except (ValueError,AttributeError):return False

def validate_log(rows):
 failures=[]
 def check(ok,label):
  if not ok:failures.append(label)
 def one(items,label):
  check(len(items)==1,label)
  if len(items)!=1:raise ValueError(label)
  return items[0]
 try:
  e=one([r for r in rows if r.get('kind')=='natural_resource_evidence'],'one_final_evidence')
  check(e['schema']==1 and e['mode']=='natural_food','natural_food_mode')
  for k in ['worldId','nativeActorId','gatewaySessionId','gatewayActorId']:check(guid(e[k]),'identity_'+k)
  check(e['unknownReason'] is None and e['ledger']['Unknown'] is False,'unknown_provenance')
  check(set(e['fixture'])=={'terrainEdited','positionEdited','inventoryInjected','foodInjected','healthEdited'} and all(v is False for v in e['fixture'].values()),'injection')
  check(not any(r.get('kind') in ['food_fixture','direct_fixture_prepared','artificial_fixture','hunt_fixture'] for r in rows),'artificial_event')
  check(e['actorGameWidgetNull'] is True and e['aiViewportCount']==0,'viewport')
  check(e['animalAttributionSupported'] is False and e['conversionAttributionSupported'] is False,'unsupported_provenance_scope')
  for k in ['originRecord','originListener','transferListener']:
   a=e['auditFailures'];check(type(a[k+'Before']) is int and a[k+'Before']>=0 and a[k+'After']==a[k+'Before'],'audit_'+k)
  stop=e['shutdown'];check(stop['idle'] is True and stop['lateDispatches']==0 and number(stop['stopTime']) and number(stop['drainedTime']) and 0<=stop['drainedTime']-stop['stopTime']<=7,'shutdown')
  end=one([r for r in rows if r.get('kind')=='result'],'one_host_result');check(end.get('passed') is True and end.get('updateFailures')==0,'host_result')
  scope=one([r for r in rows if r.get('kind')=='natural_scope'],'one_natural_scope')
  check(all(scope.get(k)==e[k] for k in ['worldId','nativeActorId','gatewaySessionId','gatewayActorId','baselineInventory']),'scope_identity_baseline')
  check(all(scope.get(k) is False for k in e['fixture']),'scope_injection')
  baseline=e['baselineInventory'];inventory=dict(baseline);check(all(type(v) is int and v>=0 for v in inventory.values()),'baseline')
  obs={o['evidenceId']:o for o in e['observations']};attempts={a['BindingId']:a for a in e['attempts']}
  check(len(obs)==len(e['observations'])<=128 and len(attempts)==len(e['attempts'])<=128,'capacity_or_duplicates')
  sources={};events=set()
  for origin in e['origins']:
   f=origin['fact'];a=attempts[origin['bindingId']];o=obs[a['EvidenceId']];t=o['target'];sid=f['SourceId']
   check(guid(sid) and sid not in sources and guid(f['EventId']) and f['EventId'] not in events,'origin_identity');events.add(f['EventId'])
   check(a['Target']==t and a['Tool']=='act_Dig' and a['TerminalStatus']=='succeeded' and guid(a['RunId']) and guid(a['RequestId']) and type(a['ReceiptSequence']) is int and a['ReceiptSequence']>0,'dig_binding')
   check(f['WorldId']==e['worldId']==t['WorldId'] and f['Kind']==t['Kind']=='block_drop' and f['OriginEntityId'] is None,'origin_scope')
   for fk,tk in [('CellX','X'),('CellY','Y'),('CellZ','Z')]:check(f[fk]==t[tk],'origin_cell')
   check(type(f['OriginalBlockValue']) is int and f['OriginalBlockValue']&~15360==t['OriginalBlockValue'],'block_identity')
   times=[o['observedAt'],a['StartedAt'],f['MonotonicSeconds'],a['TerminalAt'],stop['stopTime']]
   check(all(number(v) for v in times) and times==sorted(times) and f['MonotonicSeconds']-o['observedAt']<=30,'origin_time')
   check(number(f['CreationGameTime']) and number(f['GameTime']) and 0<=f['CreationGameTime']<=f['GameTime'],'origin_game_time')
   check(type(f['Value']) is int and f['Value']>=0 and type(f['Count']) is int and f['Count']>0,'origin_count')
   check(sum(r.get('kind')=='natural_origin_bound' and r.get('bindingId')==origin['bindingId'] and r.get('fact')==f for r in rows)==1,'raw_origin')
   check(any(r.get('kind')=='native_call_returned' and r.get('sequence')==a['ReceiptSequence'] and r.get('tool')=='act_Dig' and r.get('state')=='returned' and (r.get('data') or {}).get('runId')==a['RunId'] for r in rows),'raw_dig_receipt')
   check(any(r.get('kind')=='tool_receipt' and r.get('receipt',{}).get('Tool') in ['act_Dig','action_status'] and (r['receipt'].get('Data') or {}).get('runId')==a['RunId'] and (r['receipt'].get('Data') or {}).get('status')=='succeeded' for r in rows),'raw_dig_terminal')
   sources[sid]={'SourceId':sid,'Value':f['Value'],'Produced':f['Count'],'Remaining':f['Count'],'Acquired':0,'time':f['MonotonicSeconds']}
  operations=[(x['fact']['MonotonicSeconds'],'transfer',x) for x in e['transfers']]+[(x['time'],'eat',x) for x in e['foodEvents']]
  check(len(operations)<=512 and all(number(x[0]) for x in operations),'operation_bound')
  consumed={};acquired={};event_groups={};meal_inventory={}
  for at,kind,x in sorted(operations,key=lambda x:x[0]):
   check(at<=stop['stopTime'],'operation_after_stop')
   if kind=='transfer':
    f=x['fact'];sid=f['SourceId'];s=sources[sid];value=str(f['Value']);event=x['eventId']
    check(guid(event) and event not in events,'transfer_identity');events.add(event)
    check(f['ActorId']==e['nativeActorId'] and f['Value']==s['Value'] and f['Method'] in ['automatic','explicit'] and at>=s['time'],'transfer_scope')
    gain=f['SourceBefore']-f['SourceAfter'];check(type(gain) is int and gain>0 and f['SourceBefore']==s['Remaining'] and f['SourceAfter']>=0,'source_conservation')
    check(f['InventoryBefore']==inventory.get(value,0) and f['InventoryAfter']-f['InventoryBefore']==gain,'transfer_inventory')
    check(sum(r.get('kind')=='natural_transfer' and r.get('eventId')==event and r.get('fact')==f for r in rows)==1,'raw_transfer')
    inventory[value]=f['InventoryAfter'];s['Remaining']=f['SourceAfter'];s['Acquired']+=gain;acquired[value]=acquired.get(value,0)+gain
   else:
    value=str(x['value']);group=(x['requestId'],x['value']);check(guid(x['requestId']),'food_request')
    check(sum(r.get('kind')=='natural_food_event' and r.get('fact')==x for r in rows)==1,'raw_food_event')
    before=inventory.get(value,0);check(type(x['inventoryAfter']) is int and before>0 and x['inventoryAfter']==before-1,'food_inventory')
    if group not in meal_inventory:meal_inventory[group]=(before,acquired.get(value,0))
    inventory[value]=x['inventoryAfter'];event_groups.setdefault(group,[]).append(x)
    if baseline.get(value,0)==0:
     consumed[value]=consumed.get(value,0)+1;check(consumed[value]<=acquired.get(value,0),'consumption_acquisition')
  check({k:v for k,v in inventory.items() if v}=={k:v for k,v in e['actualInventory'].items() if v}=={k:v for k,v in e['ledger']['ExpectedInventory'].items() if v},'final_inventory')
  check(consumed=={k:v for k,v in e['ledger']['Consumed'].items() if v},'ledger_consumption')
  ledger={s['SourceId']:s for s in e['ledger']['Sources']};check(len(ledger)==len(e['ledger']['Sources']) and set(ledger)==set(sources),'ledger_sources')
  for sid,s in sources.items():check(all(ledger[sid][k]==s[k] for k in ['Value','Produced','Remaining','Acquired']),'ledger_source_counts')
  valid_meals=0;meal_groups=set();meal_runs=set()
  for wrapper in e['meals']:
   m=wrapper['Result']
   if m is None:check(False,'incomplete_meal');continue
   group=(m['RequestId'],m['Value']);value=str(m['Value']);fs=event_groups.get(group,[])
   check(group not in meal_groups and m['RunId'] not in meal_runs,'ambiguous_meal_group');meal_groups.add(group);meal_runs.add(m['RunId'])
   check(all(type(m[k]) is int and m[k]>=0 for k in ['Value','InitialCount','AcquiredCount','InventoryBefore','InventoryAfter','EventCount','StatsBefore','StatsAfter']),'meal_integer_fields')
   check(guid(m['RequestId']) and guid(m['RunId']) and type(m['ReceiptSequence']) is int and m['ReceiptSequence']>0,'meal_identity')
   check(m['NativeStatus']=='succeeded' and m['MatchingEvents'] is True and type(m['EventCount']) is int and m['EventCount']>0 and len(fs)==m['EventCount'],'meal_events')
   check(m['InitialCount']==baseline.get(value,0),'meal_baseline')
   check(bool(fs) and meal_inventory[group]==(m['InventoryBefore'],m['AcquiredCount']) and m['InventoryBefore']-m['InventoryAfter']==len(fs) and m['InventoryAfter']==fs[-1]['inventoryAfter'],'meal_inventory_binding')
   check(number(m['Nutrition']) and m['Nutrition']>0 and number(m['FoodBefore']) and number(m['FoodAfter']) and m['FoodAfter']>m['FoodBefore'] and m['StatsAfter']-m['StatsBefore']==len(fs),'meal_food_and_stats')
   if fs:
    check(all(f['statsBeforeIncrement']==m['StatsBefore']+i for i,f in enumerate(fs)) and fs[-1]['food']==m['FoodAfter'],'raw_food_stats')
   terminals=[r for r in rows if r.get('kind')=='natural_meal_terminal' and r.get('meal')==m];check(len(terminals)==1,'raw_meal_terminal')
   receipts=[r for r in rows if r.get('kind')=='tool_receipt' and r.get('receipt',{}).get('Tool')=='act_Eat' and r['receipt'].get('Sequence')==m['ReceiptSequence']]
   check(len(receipts)==1,'raw_eat_receipt')
   if receipts:
    r=receipts[0];check(r['receipt'].get('State')=='returned' and (r['receipt'].get('Data') or {}).get('runId')==m['RunId'] and r.get('arguments',{}).get('value')==m['Value'] and r['arguments'].get('count')==len(fs),'eat_run_value_count')
   check(any(r.get('kind')=='tool_receipt' and r.get('receipt',{}).get('Tool') in ['act_Eat','action_status'] and (r['receipt'].get('Data') or {}).get('runId')==m['RunId'] and (r['receipt'].get('Data') or {}).get('status')=='succeeded' for r in rows),'raw_eat_success')
   # Initial supplies may be consumed, but can never satisfy the new-food goal.
   if m['InitialCount']==0 and m['AcquiredCount']>=len(fs)>0:valid_meals+=1
  final=one([r for r in rows if r.get('kind')=='final_audit'],'one_final_audit');final_inventory={}
  for slot in final['inventory']['slots']:
   check(type(slot['count']) is int and slot['count']>=0,'final_slot_count')
   if slot['count']:final_inventory[str(slot['value'])]=final_inventory.get(str(slot['value']),0)+slot['count']
  check(final_inventory=={k:v for k,v in inventory.items() if v},'raw_final_inventory')
  check(final['eaten']==len(e['foodEvents']) and final['modelInFlight'] is False and final['requiresReconciliation'] is None,'final_eating_shutdown')
  check(set(event_groups)==meal_groups,'unbound_food_events');check(valid_meals>0,'no_new_food_meal')
 except (KeyError,TypeError,ValueError,OverflowError,IndexError,AttributeError):failures.append('missing_or_invalid_evidence')
 return {'schema':'natural-food-validation-v1','verdict':'fail' if failures else 'pass','failures':sorted(set(failures)),
         'scope':'Independent raw-log consistency for natural block-origin food acquisition and eating; not model autonomy or untampered-log proof.'}

if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('log',type=pathlib.Path);a=p.parse_args()
 result=validate_log([json.loads(l) for l in a.log.read_text().splitlines() if l.strip()]);print(json.dumps(result,indent=2));raise SystemExit(result['verdict']!='pass')
