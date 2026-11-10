"""Offline consistency verifier; never runs a game or reconstructs missing evidence."""
import json,math,sys,uuid

def validate(e):
 failures=[]
 def check(ok,label):
  if not ok:failures.append(label)
 def number(x):return type(x) in (int,float) and math.isfinite(x)
 def guid(x):
  try:return isinstance(x,str) and uuid.UUID(x).int!=0
  except (ValueError,AttributeError):return False
 try:
  check(e['kind']=='natural_resource_evidence' and e['schema']==1,'schema')
  for k in ['worldId','nativeActorId','gatewaySessionId','gatewayActorId']:check(guid(e[k]),'id_'+k)
  check(set(e['fixture'])=={'terrainEdited','positionEdited','inventoryInjected','foodInjected','healthEdited'} and all(x is False for x in e['fixture'].values()),'fixture_injection')
  check(e['unknownReason'] is None and e['ledger']['Unknown'] is False,'unknown_provenance')
  a=e['auditFailures']
  for k in ['originRecord','originListener','transferListener']:check(type(a[k+'Before']) is int and a[k+'Before']>=0 and a[k+'After']==a[k+'Before'],'audit_failures_'+k)
  stop=e['shutdown'];check(stop['idle'] is True and stop['lateDispatches']==0,'unsafe_shutdown');check(number(stop['stopTime']) and number(stop['drainedTime']) and 0<=stop['drainedTime']-stop['stopTime']<=7,'drain_window')
  check(e['animalAttributionSupported'] is False,'unsupported_scope')
  check(type(e['aiViewportCount']) is int and e['aiViewportCount']==0 and e['actorGameWidgetNull'] is True,'ai_viewport_present')
  obs={x['evidenceId']:x for x in e['observations']};attempts={x['BindingId']:x for x in e['attempts']}
  check(len(obs)==len(e['observations'])<=128 and len(attempts)==len(e['attempts'])<=128,'duplicate_or_capacity')
  inventory=dict(e['baselineInventory']);check(all(type(v)is int and v>=0 for v in inventory.values()),'baseline')
  sources={};events=set()
  for origin in e['origins']:
   f=origin['fact'];a=attempts[origin['bindingId']];o=obs[a['EvidenceId']];t=o['target'];sid=f['SourceId']
   check(sid not in sources and guid(sid) and guid(f['EventId']) and f['EventId'] not in events,'origin_identity');events.add(f['EventId'])
   check(a['Tool']=='act_Dig' and a['Target']==t and a['TerminalStatus']=='succeeded' and guid(a['RunId']) and guid(a['RequestId']),'actual_dig_binding')
   check(type(a['ReceiptSequence']) is int and a['ReceiptSequence']>=0,'receipt_sequence')
   check(f['WorldId']==e['worldId']==t['WorldId'] and f['Kind']==t['Kind']=='block_drop' and f['OriginEntityId'] is None,'origin_scope')
   for fk,tk in [('CellX','X'),('CellY','Y'),('CellZ','Z')]:check(f[fk]==t[tk],'original_block_'+tk)
   check(type(f['OriginalBlockValue']) is int and (f['OriginalBlockValue'] & ~15360)==t['OriginalBlockValue'],'original_block_data_without_light')
   times=[o['observedAt'],a['StartedAt'],f['MonotonicSeconds'],a['TerminalAt'],stop['stopTime']]
   check(all(number(v) for v in times) and times==sorted(times) and f['MonotonicSeconds']-o['observedAt']<=30,'origin_timeline')
   check(number(f['CreationGameTime']) and number(f['GameTime']) and 0<=f['CreationGameTime']<=f['GameTime'],'origin_game_time')
   check(type(f['Value'])is int and f['Value']>=0 and type(f['Count'])is int and f['Count']>0,'origin_quantity')
   sources[sid]={'Value':f['Value'],'Produced':f['Count'],'Remaining':f['Count'],'Acquired':0,'Time':f['MonotonicSeconds']}
  acquired=0;last=-1
  for transfer in e['transfers']:
   f=transfer['fact'];sid=f['SourceId'];s=sources[sid];event=transfer['eventId'];value=str(f['Value'])
   check(guid(event) and event not in events,'transfer_identity');events.add(event)
   check(f['ActorId']==e['nativeActorId'] and f['Value']==s['Value'] and f['Method'] in ['automatic','explicit'],'transfer_scope')
   check(number(f['MonotonicSeconds']) and max(last,s['Time'])<=f['MonotonicSeconds']<=stop['stopTime'],'transfer_timeline');last=f['MonotonicSeconds']
   gain=f['SourceBefore']-f['SourceAfter'];check(type(gain)is int and gain>0 and f['SourceBefore']==s['Remaining'] and f['SourceAfter']>=0,'source_conservation')
   check(f['InventoryBefore']==inventory.get(value,0) and f['InventoryAfter']-f['InventoryBefore']==gain,'inventory_conservation')
   inventory[value]=f['InventoryAfter'];s['Remaining']=f['SourceAfter'];s['Acquired']+=gain;acquired+=gain
  check(acquired>0,'no_acquisition');check(not e['ledger']['Consumed'],'consumption_not_this_gate')
  actual={k:v for k,v in e['actualInventory'].items() if v!=0};expected={k:v for k,v in inventory.items() if v!=0}
  check(actual==expected=={k:v for k,v in e['ledger']['ExpectedInventory'].items() if v!=0},'final_inventory')
  ledger={s['SourceId']:s for s in e['ledger']['Sources']};check(set(ledger)==set(sources),'ledger_sources')
  for sid,s in sources.items():check(all(ledger[sid][k]==s[k] for k in ['Value','Produced','Remaining','Acquired']),'ledger_source_counts')
 except (KeyError,TypeError,ValueError,OverflowError):failures.append('missing_or_invalid_evidence')
 return {'verdict':'fail' if failures else 'pass','failures':failures,'scope':'Host-record consistency only; not proof of untampered logs, model autonomy, or an edible-food chain.'}

def validate_log(rows):
 """Require matching raw hook receipts as well as the final ledger, not only a summary."""
 try:
  evidence=[x for x in rows if x.get('kind')=='natural_resource_evidence']
  if len(evidence)!=1:return {'verdict':'fail','failures':['exactly_one_final_evidence']}
  e=evidence[0];result=validate(e);fail=result['failures']
  if any(x.get('kind') in ['artificial_fixture','food_fixture','direct_fixture_prepared','hunt_fixture'] for x in rows):fail.append('artificial_setup_event')
  ending=[x for x in rows if x.get('kind')=='result']
  if len(ending)!=1 or ending[0].get('passed') is not True or ending[0].get('updateFailures')!=0:fail.append('native_host_failed_or_missing')
  for origin in e['origins']:
   if not any(x.get('kind')=='natural_origin_bound' and x.get('bindingId')==origin['bindingId'] and x.get('fact')==origin['fact'] for x in rows):fail.append('raw_origin_missing')
  for transfer in e['transfers']:
   if not any(x.get('kind')=='natural_transfer' and x.get('eventId')==transfer['eventId'] and x.get('fact')==transfer['fact'] for x in rows):fail.append('raw_transfer_missing')
  for a in e['attempts']:
   if a['TerminalStatus']!='succeeded':continue
   if not any(x.get('kind')=='native_call_returned' and x.get('sequence')==a['ReceiptSequence'] and x.get('tool')=='act_Dig' and isinstance(x.get('data'),dict) and x['data'].get('runId')==a['RunId'] for x in rows):fail.append('raw_dig_receipt_missing')
   if not any(x.get('kind')=='tool_receipt' and x.get('receipt',{}).get('Tool') in ['act_Dig','action_status'] and isinstance(x['receipt'].get('Data'),dict) and x['receipt']['Data'].get('runId')==a['RunId'] and x['receipt']['Data'].get('status')=='succeeded' for x in rows):fail.append('raw_success_terminal_missing')
  result['verdict']='fail' if fail else 'pass';return result
 except (KeyError,TypeError,ValueError):return {'verdict':'fail','failures':['invalid_raw_log']}

if __name__=="__main__":
 p=sys.argv[1]
 if p.endswith(".jsonl"):
  r=validate_log([json.loads(l) for l in open(p) if l.strip()])
 else:r=validate(json.load(open(p)))
 print(json.dumps(r));sys.exit(r["verdict"]!="pass")
