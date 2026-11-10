#!/usr/bin/env python3
"""Offline controlled Eat38 contract acceptance, separate from food-goal success."""
import argparse,importlib.util,json
from pathlib import Path

def load(p):
 s=importlib.util.spec_from_file_location('eat38_dependency',p);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m

def validate(rows,audit,food_check=None):
 fail=[];unknown=[]
 def check(ok,label):
  if ok is None:unknown.append(label)
  elif not ok:fail.append(label)
 def one(kind):
  r=[x for x in rows if x.get('kind')==kind]
  if len(r)!=1:unknown.append('unique_'+kind);raise ValueError()
  return r[0]
 try:
  case=audit['case'];a=audit['attempt']
  if a is None:unknown.append('probe_not_reached');raise ValueError()
  check(audit.get('error') is None,'driver_error');scope=(a['sessionId'],a['actorId']);rid=a['requestId'];args=a['submittedEat']
  published=[r for r in rows if r.get('kind')=='controller_request_published' and r.get('requestId')==rid]
  check(len(published)==1 and (published[0].get('sessionId'),published[0].get('actorId'))==scope and published[0].get('observationId')==a['observationId'],'published_probe_binding')
  calls=[r for r in rows if r.get('kind')=='native_call_started'];eat=[r for r in calls if r.get('step',{}).get('Tool')=='act_Eat']
  receipts=[r['receipt'] for r in rows if r.get('kind')=='tool_receipt']
  check(all((r.get('SessionId'),r.get('ActorId'))==scope for r in receipts),'receipt_scope')
  final=one('final_audit');host=one('result');drain=one('stop_drained');chain=one('food_chain_evidence')
  check(host.get('updateFailures')==0 and final.get('modelInFlight') is False and final.get('requiresReconciliation') is None and drain.get('modelCompleted') is True,'clean_shutdown')
  check(chain.get('shutdown',{}).get('lateDispatches')==0,'no_late_dispatch')
  if case=='valid_eat':
   check(a['publicSchemaAccepts'] is True,'valid_tuple_offered')
   check(len(eat)==1 and eat[0].get('decisionId')==rid and eat[0]['step']['Arguments']==args,'one_matching_eat_dispatch')
   check(None if food_check is None else food_check.get('verdict')=='pass','independent_food_chain')
   check(host.get('passed') is True,'native_food_goal_verified')
  else:
   check(not eat and not any(r.get('Tool')=='act_Eat' for r in receipts),'no_native_eat_invocation')
   check(final.get('eaten')==0 and not any(r.get('kind')=='food_event' for r in rows),'no_consumption_side_effect')
   old=a['sourceSlot'];slots=final['inventory']['slots'];owned=sum(s['count'] for s in slots if s['value']==old['value'])
   before=sum(s['count'] for s in a['inventoryAtPlanning']['slots'] if s['value']==old['value'])
   check(owned==before,'new_food_retained_not_eaten')
   if case in ['wrong_slot_value','over_count']:
    check(a['publicSchemaAccepts'] is False,'published_schema_rejects_bad_tuple')
    check(any(r.get('kind')=='controller_response_rejected' and r.get('requestId')==rid and r.get('error')=='Unsupported tool or arguments' for r in rows),'actual_initial_schema_rejection')
    check(not any(r.get('kind')=='controller_response_received' and r.get('requestId')==rid for r in rows),'invalid_plan_never_accepted')
   elif case=='fresh_slot_change':
    check(a['publicSchemaAccepts'] is True,'old_tuple_was_valid_when_planned')
    transferred=[r for r in rows if r.get('kind')=='native_call_returned' and r.get('tool')=='act_TransferItems']
    check(len(transferred)==1 and transferred[0].get('state')=='returned' and transferred[0].get('data',{}).get('status')=='succeeded','one_real_transfer_succeeded')
    check(any(r.get('kind')=='step_revalidation_failed' and r.get('step',{}).get('Tool')=='act_Eat' and r['step'].get('Arguments')==args for r in rows),'fresh_old_slot_rejected_before_invoke')
    source=next(s for s in slots if s['slot']==old['slot']);dest=next(s for s in slots if s['slot']==a['destinationSlot'])
    check(source['count']==0 and dest['value']==old['value'] and dest['count']==old['count'],'actual_inventory_slot_move')
   else:check(False,'unknown_case')
   check(host.get('passed') is False,'negative_food_goal_not_fabricated')
 except (KeyError,ValueError,TypeError,IndexError,StopIteration):unknown.append('missing_or_invalid_evidence')
 return {'schema':'eat-contract38-validation-v1','case':audit.get('case'),'contractVerdict':'fail' if fail else 'unknown' if unknown else 'pass','failures':sorted(set(fail)),'unknown':sorted(set(unknown)),
         'nativeFoodGoal':'separate host/food-chain outcome; negative rejection is not food-goal success','scope':'Deterministic artificial fixture only, not model strategy.'}

if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--output',type=Path);a=p.parse_args()
 try:
  log=a.directory/'tool-agent.jsonl'
  if log.stat().st_size>64*1024*1024:raise ValueError('log_bound')
  rows=[json.loads(l) for l in log.read_text().splitlines() if l.strip()];audit=json.loads((a.directory/'eat-contract-driver-result.json').read_text());chains=[r for r in rows if r.get('kind')=='food_chain_evidence']
  fv=load(a.directory/'sources/experiments/food-acquisition-evidence/validate.py')
  result=validate(rows,audit,fv.validate(chains[0]) if len(chains)==1 else None)
 except (OSError,ValueError) as e:result={'contractVerdict':'unknown','error':str(e)}
 if a.output:
  with a.output.open('x') as f:json.dump(result,f,indent=2)
 print(json.dumps(result,indent=2));raise SystemExit(0 if result['contractVerdict']=='pass' else 1)
