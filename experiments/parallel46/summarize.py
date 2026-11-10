#!/usr/bin/env python3
"""Read-only verification of same-world parallel effects; no launch or API."""
import argparse,json,pathlib,uuid,math
REQUIRED={'same_world_distinct_actors','parallel_success','cancel_isolation_running','cancel_isolation_completed','cancelled_actor_recovered'}
def guid(v):
 try:return isinstance(v,str) and uuid.UUID(v).int!=0
 except (ValueError,TypeError,AttributeError):return False
def number(v):return type(v) in (float,int) and math.isfinite(v)
def data(r):return r.get('Data',r.get('data',{})) if isinstance(r,dict) else {}
def summarize(rows):
 errors=[]
 def check(ok,label):
  if not ok:errors.append(label)
 scopes=[r for r in rows if r.get('kind')=='parallel46_scope'];scope=scopes[0] if scopes else {};actors=scope.get('actors',[])
 count=len(actors);valid=count in (2,4) and all(isinstance(a,dict) and guid(a.get('actorId')) and guid(a.get('sessionId')) for a in actors)
 check(len(scopes)==1 and guid(scope.get('worldId')),'single_world_required')
 check(scope.get('actualActors')==count,'actual_actor_count_mismatch')
 check(valid and len({a['actorId'] for a in actors})==count and len({a['sessionId'] for a in actors})==count,'distinct_actor_sessions_required')
 check(any(r.get('kind')=='start' and r.get('mode')=='matrix_parallel' and r.get('controller')=='file' for r in rows),'fixed_file_mode_required')
 effects={r.get('name'):r for r in rows if r.get('kind')=='parallel46_effect'}
 for name in REQUIRED:check(effects.get(name,{}).get('passed') is True,'missing_or_failed_effect:'+name)
 evidence={k:r.get('evidence',{}) for k,r in effects.items()}
 check(evidence.get('same_world_distinct_actors',{}).get('worldId')==scope.get('worldId'),'effect_world_mismatch')
 rounds={r.get('round'):r for r in rows if r.get('kind')=='parallel46_round_start'}
 for n in (1,2):
  ids=rounds.get(n,{}).get('runIds',[]);check(len(ids)==count and all(guid(i) for i in ids) and len(set(ids))==count,'distinct_run_ids_round_'+str(n))
 if all(n in rounds for n in (1,2)):check(not set(rounds[1].get('runIds',[]))&set(rounds[2].get('runIds',[])),'rounds_reused_run_ids')
 samples=[r for r in rows if r.get('kind')=='parallel46_sample'];both=[]
 for sample in samples:
  n=sample.get('round');rs=sample.get('rows',[]);ids=list(rounds.get(n,{}).get('runIds',[]))
  if sample.get('label')=='A_recovered' and ids:ids[0]=evidence.get('cancelled_actor_recovered',{}).get('runId')
  check(len(rs)==count and len(ids)==count,'sample_two_rows_required')
  if len(rs)!=count or len(ids)!=count or not valid:continue
  for i,r in enumerate(rs):
   receipt=r.get('receipt',{});d=data(receipt)
   check(r.get('actor')==i and r.get('actorId')==actors[i]['actorId'] and r.get('sessionId')==actors[i]['sessionId'],'sample_owner_mismatch')
   check(receipt.get('ActorId',receipt.get('actorId'))==actors[i]['actorId'] and receipt.get('SessionId',receipt.get('sessionId'))==actors[i]['sessionId'],'native_receipt_owner_mismatch')
   check(r.get('runId')==ids[i] and d.get('runId')==ids[i] and d.get('status')==r.get('status'),'sample_native_run_status_mismatch')
   pos,origin=r.get('position',[]),r.get('origin',[])
   if len(pos)==3 and len(origin)==3 and all(number(x) for x in pos+origin) and number(r.get('horizontalDisplacement')):
    check(abs(math.hypot(pos[0]-origin[0],pos[2]-origin[2])-r['horizontalDisplacement'])<.001,'position_displacement_mismatch')
   else:check(False,'numeric_positions_required')
  if n==1 and all(r.get('status')=='running' for r in rs):both.append(sample)
 times=[r.get('gameTime') for r in both];ordered=all(number(t) for t in times) and all(a<=b for a,b in zip(times,times[1:]))
 span=times[-1]-times[0] if len(times)>1 and ordered else 0;gap=max((b-a for a,b in zip(times,times[1:])),default=0) if ordered else None
 check(len(times)>=3 and ordered and span>=1,'overlap_sample_span_under_one_game_second')
 check(gap is not None and gap<=1,'overlap_sampling_gap_over_one_second')
 final=evidence.get('parallel_success',{});displacements=final.get('allDisplacements',[]);check(len(displacements)==count and all(number(x) and x>3 for x in displacements),'all_real_displacements_required')
 for name,statuses,n in [('parallel_success',['succeeded']*count,1),('cancel_isolation_running',['cancelled']+['running']*(count-1),2),('cancel_isolation_completed',['cancelled']+['succeeded']*(count-1),2)]:
  e=evidence.get(name,{});rs=e.get('receipts',[]);ids=rounds.get(n,{}).get('runIds',[])
  check(len(rs)==count and len(ids)==count and e.get('runIds')==ids and all(data(r).get('runId')==ids[i] and (data(r).get('status')==statuses[i] or name=='cancel_isolation_running' and i>1 and data(r).get('status')=='succeeded') for i,r in enumerate(rs)),'effect_terminal_identity:'+name)
 recovered=evidence.get('cancelled_actor_recovered',{});rr=data(recovered.get('receipt'))
 check(guid(recovered.get('runId')) and recovered.get('runId') not in rounds.get(2,{}).get('runIds',[]) and rr.get('runId')==recovered.get('runId') and rr.get('status')=='succeeded' and number(recovered.get('displacementA')) and recovered['displacementA']>3 and recovered.get('otherOwnersIdle') is True,'cancelled_actor_new_run_recovery_missing')
 done=evidence.get('cancel_isolation_completed',{})
 check(number(done.get('settledDrift')) and 0<=done['settledDrift']<=.03 and number(done.get('bDisplacementAfterCancel')) and done['bDisplacementAfterCancel']>.4,'cancel_independent_motion_missing')
 menus=[r for r in rows if r.get('kind')=='parallel46_adapter_validated']
 for i in range(count):check(valid and sum(r.get('actor')==i and r.get('tool')=='act_MoveTo' and r.get('actorId')==actors[i]['actorId'] and r.get('session')==actors[i]['sessionId'] and guid(r.get('capture')) for r in menus)>=(3 if i==0 else 2),'current_movement_menu_missing_actor_'+str(i))
 check(valid and any(r.get('actor')==0 and r.get('tool')=='cancel_action' and r.get('actorId')==actors[0]['actorId'] and r.get('session')==actors[0]['sessionId'] for r in menus),'current_cancel_menu_missing')
 summaries=[r for r in rows if r.get('kind')=='parallel46_summary'];last=summaries[-1] if summaries else {}
 check(last.get('passed') is True and last.get('idleVerified') is True and last.get('modelApi') is False,'successful_idle_summary_required')
 check(any(r.get('kind')=='result' and r.get('passed') is True for r in rows),'outer_success_required')
 for r in rows:
  if r.get('kind') in ('parallel46_error','failure','fatal','cleanup_failure','parse_error') or r.get('modelApi') is True:errors.append(r.get('kind'))
  if r.get('kind') in ('parallel46_assert','parallel46_effect') and r.get('passed') is False:errors.append('failed_assertion_or_effect')
 return dict(mode='matrix_parallel',passed=not errors,errors=sorted(set(errors)),actorCount=count if valid else 0,assertions=last.get('checks',0),overlapSamples=len(both),sampledOverlapGameSeconds=span,maxSampleGapSeconds=gap,displacementA=final.get('displacementA'),displacementB=final.get('displacementB'),aDriftAfterCancel=done.get('aDriftAfterCancel'),bDisplacementAfterCancel=done.get('bDisplacementAfterCancel'),gameTimeSpan=max((r.get('gameTime',0) for r in samples),default=0)-min((r.get('gameTime',0) for r in samples),default=0),modelApi=False,autonomousSurvival=False,note='Sampled simultaneous running and physical effects, not continuous every-instant coverage; resource costs are entire process tree.')
def main():
 ap=argparse.ArgumentParser();ap.add_argument('log');a=ap.parse_args();rows=[]
 for line in pathlib.Path(a.log).read_text().splitlines():
  if not line.strip():continue
  try:r=json.loads(line)
  except json.JSONDecodeError:r={'kind':'parse_error'}
  if isinstance(r,dict):rows.append(r)
 result=summarize(rows);print(json.dumps(result,ensure_ascii=False,indent=2));raise SystemExit(0 if result['passed'] else 1)
if __name__=='__main__':main()
