#!/usr/bin/env python3
"""Strict read-only cooperative evidence summary; never launches game/API."""
import argparse,json,pathlib,uuid
REQUIRED={'same_world_distinct_actors','acquisition','delivery','eating','overlap','independent_progress'}
def guid(v):
 try:return isinstance(v,str) and uuid.UUID(v).int!=0
 except (ValueError,TypeError,AttributeError):return False
def number(v):return isinstance(v,(int,float)) and not isinstance(v,bool)
def data(r):
 if not isinstance(r,dict):return {}
 return r.get('Data',r.get('data',r))
def summarize(rows):
 errors=[]
 def require(ok,name):
  if not ok:errors.append(name)
 starts=[r for r in rows if r.get('kind')=='start']
 require(bool(starts) and starts[-1].get('mode')=='matrix_coop' and starts[-1].get('controller')=='file','fixed_file_coop_start_missing')
 scopes=[r for r in rows if r.get('kind')=='coop45_scope'];scope=scopes[0] if scopes else {};actors=scope.get('actors',[])
 require(len(scopes)==1 and guid(scope.get('worldId')),'single_world_scope_missing')
 valid_actors=isinstance(actors,list) and len(actors)==2 and all(isinstance(a,dict) and guid(a.get('actorId')) and guid(a.get('sessionId')) for a in actors)
 require(valid_actors and len({a['actorId'] for a in actors})==2 and len({a['sessionId'] for a in actors})==2,'two_distinct_actor_sessions_required')
 effects=[r for r in rows if r.get('kind')=='coop45_effect'];by_name={r.get('name'):r for r in effects}
 for name in sorted(REQUIRED):require(name in by_name and by_name[name].get('passed') is True,'effect_missing_or_failed:'+name)
 require(all(r.get('passed') is True for r in effects),'failed_effect')
 receipts=[r for r in rows if r.get('kind')=='coop45_receipt']
 require(bool(receipts),'receipts_missing')
 for r in receipts:
  i=r.get('actor');ok=valid_actors and type(i) is int and 0<=i<2
  if ok:ok=r.get('actorId')==actors[i]['actorId'] and r.get('sessionId')==actors[i]['sessionId']
  require(ok,'receipt_actor_scope_mismatch')
  receipt=r.get('receipt',{})
  if valid_actors and type(i) is int and 0<=i<2:
   require(receipt.get('ActorId',receipt.get('actorId'))==actors[i]['actorId'] and receipt.get('SessionId',receipt.get('sessionId'))==actors[i]['sessionId'],'native_receipt_identity_mismatch')
  require(guid(r.get('requestId')),'dispatch_request_identity_missing')
  require(isinstance(receipt,dict) and receipt.get('State',receipt.get('state'))=='returned','receipt_not_returned')
 same=by_name.get('same_world_distinct_actors',{}).get('evidence',{})
 require(same.get('worldId')==scope.get('worldId') and guid(same.get('worldId')),'effect_world_identity_mismatch')
 eating=by_name.get('eating',{}).get('evidence',{})
 require(all(number(eating.get(k)) for k in ('foodBefore','foodAfter','statisticBefore','statisticAfter')) and eating.get('foodAfter',0)>eating.get('foodBefore',1) and eating.get('statisticAfter',0)==eating.get('statisticBefore',0)+1,'eating_numeric_effect_missing')
 overlap=by_name.get('overlap',{}).get('evidence',{});wait=data(overlap.get('runningWaitReceipt'));eat=data(overlap.get('eatReceipt'))
 require(guid(overlap.get('waitRunId')) and guid(overlap.get('eatRunId')) and overlap.get('waitRunId')!=overlap.get('eatRunId'),'overlap_distinct_runs_missing')
 require(wait.get('runId')==overlap.get('waitRunId') and wait.get('status')=='running' and eat.get('runId')==overlap.get('eatRunId') and eat.get('status')=='succeeded','overlap_native_running_success_evidence_missing')
 states={r.get('label'):r for r in rows if r.get('kind')=='coop45_state'}
 expected={'baseline':(0,0,0),'world_drop_created':(0,0,1),'acquired':(1,0,0),'delivered':(0,1,0),'eaten':(0,0,0)}
 values=[]
 for label,counts in expected.items():
  state=states.get(label,{})
  require(tuple(state.get(k) for k in ('countA','countB','worldCount'))==counts,'inventory_chain_missing_or_wrong:'+label)
  values.append(state.get('value'))
 require(all(type(v) is int and v>0 for v in values) and len(set(values))==1,'inventory_chain_value_mismatch')
 acquired=data(by_name.get('acquisition',{}).get('evidence',{}).get('receipt'))
 require(acquired.get('action')=='PickUp' and acquired.get('status')=='succeeded' and acquired.get('affectedCount')==1,'explicit_pickup_receipt_missing')
 require(eat.get('action')=='Eat' and eat.get('affectedCount')==1 and eating.get('nativeEvents')==1,'explicit_eat_and_event_missing')
 progress=by_name.get('independent_progress',{}).get('evidence',{});waited=data(progress.get('waitReceipt'))
 require(progress.get('waitRunId')==overlap.get('waitRunId') and progress.get('eatRunId')==overlap.get('eatRunId'),'progress_run_identity_mismatch')
 require(waited.get('runId')==overlap.get('waitRunId') and waited.get('status')=='succeeded' and waited.get('action')=='Wait','independent_wait_completion_missing')
 times=[overlap.get(k) for k in ('waitStart','eatStart','eatEnd','waitObservedRunningAt')]+[progress.get('waitEnd')]
 require(all(number(t) for t in times) and all(a<=b for a,b in zip(times,times[1:])) and times[0]<times[-1],'actual_overlap_time_interval_missing')
 menus=[r for r in rows if r.get('kind')=='coop45_adapter_validated']
 for actor,tool in ((0,'act_PickUp'),(0,'act_OfferItem'),(1,'act_RespondToOffer'),(0,'act_Wait'),(1,'act_Eat')):
  require(valid_actors and any(m.get('actor')==actor and m.get('tool')==tool and m.get('session')==actors[actor]['sessionId'] and m.get('actorId')==actors[actor]['actorId'] and guid(m.get('capture')) for m in menus),'current_menu_missing:'+str(actor)+':'+tool)
 terminal=[r for r in rows if r.get('kind')=='coop45_summary'];last=terminal[-1] if terminal else {}
 outer=[r for r in rows if r.get('kind')=='result']
 require(last.get('passed') is True and last.get('idleVerified') is True and last.get('modelApi') is False,'verified_idle_summary_missing')
 require(bool(outer) and outer[-1].get('passed') is True,'successful_outer_result_missing')
 for r in rows:
  if r.get('kind') in ('coop45_error','failure','fatal','cleanup_failure','parse_error'):errors.append(r.get('kind'))
  if r.get('modelApi') is True:errors.append('unexpected_model_api')
 return dict(mode='matrix_coop',passed=not errors,errors=sorted(set(errors)),worldId=scope.get('worldId'),actorCount=len(actors) if valid_actors else 0,effects={n:by_name.get(n,{}).get('passed') is True for n in sorted(REQUIRED)},assertions=last.get('checks',0),receiptCount=len(receipts),idleVerified=last.get('idleVerified') is True,modelApi=False,autonomousSurvival=False,note='Controlled same-world native effects only. Repeated assertions are not capability counts; no model autonomy claim.')
def main():
 ap=argparse.ArgumentParser();ap.add_argument('log');a=ap.parse_args();rows=[]
 for line in pathlib.Path(a.log).read_text().splitlines():
  if not line.strip():continue
  try:r=json.loads(line)
  except json.JSONDecodeError:r={'kind':'parse_error'}
  if isinstance(r,dict):rows.append(r)
 result=summarize(rows);print(json.dumps(result,ensure_ascii=False,indent=2));raise SystemExit(0 if result['passed'] else 1)
if __name__=='__main__':main()
