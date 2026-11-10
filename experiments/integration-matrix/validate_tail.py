#!/usr/bin/env python3
"""Offline two-step original-tail barrier verifier; artificial lifecycle evidence only."""
import math,uuid

def validate(rows,driver,case):
 fail=[];unknown=[]
 def check(ok,name):
  if not ok:fail.append(name)
 def unique(kind):
  found=[r for r in rows if r.get('kind')==kind]
  if len(found)!=1:unknown.append('unique_'+kind);raise ValueError()
  return found[0]
 def uid(v):return isinstance(v,str) and uuid.UUID(v).int!=0
 try:
  negative=case=='tail_failure'
  if case not in ['tail_early','tail_late','tail_failure']:raise ValueError()
  bound=unique('continuation_bound');plan=bound['planId'];check(uid(plan) and bound['stepCount']==2,'bound_two_steps')
  check([s['Tool'] for s in bound['steps']]==['act_MoveTo','act_FaceTowards'],'original_tail_tools')
  request=unique('prefetch_requested');rid=request['lease']['RequestId'];check(uid(rid) and request['lease']['PrecedingReceiptSequence']==bound['firstReceipt'],'request_binding')
  bind=[r for r in rows if r.get('kind')=='continuation_step_bound' and r.get('planId')==plan]
  terms=[r for r in rows if r.get('kind')=='continuation_step_terminal' and r.get('planId')==plan]
  check(len(bind)==len(terms)==2,'all_step_events')
  if len(bind)!=2 or len(terms)!=2:raise ValueError()
  check([b['index'] for b in bind]==[0,1] and [t['index'] for t in terms]==[0,1],'step_index_order')
  times=[bound['monotonicSeconds']]
  for i,(b,t) in enumerate(zip(bind,terms)):
   check(uid(b['runId']) and b['runId']==t['runId'] and b['sequence']==t['sequence'] and b['tool']==bound['steps'][i]['Tool'],'step_receipt_run')
   expected_status=t['status'] in ['failed','cancelled'] and t['accepted'] is False if negative and i==1 else t['status']=='succeeded' and t['accepted'] is True
   check(expected_status and b['monotonicSeconds']<=t['monotonicSeconds'],'actual_step_success')
   check(any(r.get('kind')=='native_call_returned' and r.get('sequence')==b['sequence'] and r.get('tool')==b['tool'] and r.get('state')=='returned' and (r.get('data') or {}).get('runId')==b['runId'] for r in rows),'raw_step_receipt')
   check(any(r.get('kind')=='tool_receipt' and r.get('receipt',{}).get('Tool') in [b['tool'],'action_status'] and (r['receipt'].get('Data') or {}).get('runId')==b['runId'] and (r['receipt'].get('Data') or {}).get('status')==t['status'] for r in rows),'raw_step_terminal')
   times.append(t['monotonicSeconds'])
  check(bind[0]['runId']!=bind[1]['runId'] and terms[0]['monotonicSeconds']<=bind[1]['monotonicSeconds'],'real_sequential_runs')
  if negative:
   check(bound['steps'][1]['Arguments']=={'bearing':180,'timeout':.1},'legal_short_turn_tail')
   check(not any(r.get('kind') in ['continuation_tail_settled','prefetch_handoff_taken'] for r in rows),'failed_tail_never_admitted')
   calls=[r for r in rows if r.get('kind')=='native_call_started']
   check(len(calls)==2 and all(calls[i].get('step')==bound['steps'][i] for i in range(min(2,len(calls)))),'only_original_two_steps')
   check(not any(r.get('candidateRequestId')==rid or r.get('decisionId')==rid for r in calls),'no_candidate_dispatch')
   check(any(r.get('kind')=='prefetch_invalidated' and r.get('requestId')==rid and r['monotonicSeconds']>=terms[-1]['monotonicSeconds'] for r in rows),'tail_failure_invalidates_candidate')
   check(all(type(t) in [int,float] and math.isfinite(t) for t in times) and times==sorted(times),'negative_terminal_order')
   check(driver.get('case')=='direct_tail_failure' and len(driver.get('events',[]))==2 and all(x.get('delivered') is True for x in driver['events']) and driver['events'][1].get('requestId')==rid,'negative_driver_deliveries')
   drained=unique('stop_drained');check(drained.get('modelCompleted') is True and drained.get('direct',{}).get('runId')==bind[-1]['runId'] and drained['direct'].get('status') in ['failed','cancelled'] and drained.get('task',{}).get('status') in ['idle','succeeded','cancelled','failed','skipped'],'negative_native_drain')
   result=unique('result');final=unique('final_audit');check(result.get('passed') is True and result.get('updateFailures')==0 and final.get('modelInFlight') is False,'negative_host_result')
   return {'schema':'original-tail-validation-v1','verdict':'fail' if fail else 'unknown' if unknown else 'pass','failures':sorted(set(fail)),'unknown':sorted(set(unknown)),'scope':'Artificial real original-tail failure; native receipt/run/invalidation/no successor derived independently, not from tailFailure summary. Physical-idle guard remains host evidence; no model/food claim.'}
  settled=unique('continuation_tail_settled');take=unique('prefetch_handoff_taken');response=unique('prefetch_response')
  check(settled['planId']==plan and settled['lastRunId']==bind[-1]['runId'] and settled['lastReceipt']==bind[-1]['sequence'],'tail_settled_identity')
  check(take['requestId']==response['requestId']==rid and response['accepted'] is True,'candidate_binding')
  times.extend([settled['monotonicSeconds'],take['monotonicSeconds']]);check(all(type(t) in [int,float] and math.isfinite(t) for t in times) and times==sorted(times),'barrier_order')
  calls=[r for r in rows if r.get('kind')=='native_call_started'];check(len(calls)==3 and [r.get('step',{}).get('Tool') for r in calls]==['act_MoveTo','act_FaceTowards','act_Wait'],'only_original_tail_then_candidate')
  check(len(calls)>=2 and all(calls[i].get('step')==bound['steps'][i] for i in range(2)),'original_step_fingerprints')
  waits=[r for r in rows if r.get('kind')=='native_call_returned' and r.get('tool')=='act_Wait']
  check(len(waits)==1,'one_candidate_receipt')
  if len(waits)==1:
   w=waits[0];wr=(w.get('data') or {}).get('runId');check(uid(wr) and wr not in [b['runId'] for b in bind] and w.get('state')=='returned','candidate_new_native_run')
   check(any(r.get('kind')=='tool_receipt' and r.get('receipt',{}).get('Tool') in ['act_Wait','action_status'] and (r['receipt'].get('Data') or {}).get('runId')==wr and (r['receipt'].get('Data') or {}).get('status')=='succeeded' for r in rows),'candidate_native_success')
  successors=[r for r in calls if r.get('candidateRequestId')==rid or r.get('decisionId')==rid];check(len(successors)==1 and successors[0]['step']['Tool']=='act_Wait' and successors[0]['monotonicSeconds']>=take['monotonicSeconds'],'no_candidate_before_barrier')
  check(response['monotonicSeconds']<settled['monotonicSeconds'] if case=='tail_early' else response['monotonicSeconds']>settled['monotonicSeconds'],'actual_early_or_late_coverage')
  check(not any(r.get('kind')=='prefetch_invalidated' and r.get('requestId')==rid and r['monotonicSeconds']<take['monotonicSeconds'] for r in rows),'candidate_not_invalidated')
  deliveries=driver['events'];check(len(deliveries)==2 and all(r.get('delivered') is True for r in deliveries) and deliveries[1]['requestId']==rid,'driver_two_deliveries')
  check(driver['case']==('direct_early' if case=='tail_early' else 'direct_late'),'driver_host_case')
  result=unique('result');final=unique('final_audit');check(result['passed'] is True and result['updateFailures']==0 and final['modelInFlight'] is False,'native_success_drain')
 except (KeyError,TypeError,ValueError,IndexError,AttributeError) as error:
  unknown.append('missing_or_invalid_tail_evidence:'+type(error).__name__)
 return {'schema':'original-tail-validation-v1','verdict':'fail' if fail else 'unknown' if unknown else 'pass','failures':sorted(set(fail)),'unknown':sorted(set(unknown)),'scope':'Artificial original MoveTo+FaceTowards tail and candidate barrier; no actual model/food/autonomy claim.'}
