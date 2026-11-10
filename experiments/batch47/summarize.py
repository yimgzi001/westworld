#!/usr/bin/env python3
"""Evidence-only summary: controlled native scenarios, never model autonomy."""
import json,math,pathlib,sys

def summarize(rows):
 errors=[]
 def check(ok,label):
  if not ok:errors.append(label)
 scopes=[r for r in rows if r.get('kind')=='batch47_scope'];check(len(scopes)==1,'unique_scope')
 if len(scopes)!=1:return {'passed':False,'errors':errors}
 scope=scopes[0];actors=[{'actorId':a.get('actorId',a.get('ActorId')),'sessionId':a.get('sessionId',a.get('SessionId'))} for a in scope.get('actors',[])];n=scope.get('actualActors');suite=scope.get('suite');check(n==len(actors) and isinstance(n,int) and 2<=n<=50,'actor_count')
 check(bool(scope.get('worldId')),'world_identity');check(len({a.get('actorId') for a in actors})==n and all(a.get('actorId') and a.get('sessionId') for a in actors),'distinct_actor_bindings')
 ends=[r for r in rows if r.get('kind')=='batch47_summary'];end=ends[-1] if ends else {};check(end.get('passed') is True and end.get('idleVerified') is True and end.get('modelApi') is False,'passed_idle_no_api')
 check(not any(r.get('modelApi') is True or r.get('kind','').endswith(('_error','_failure')) or (r.get('kind','').endswith('_assert') and r.get('passed') is False) for r in rows),'no_failure_events')
 result={'suite':suite,'actualActors':n,'modelApi':False,'controlledFixture':True}
 if suite=='scale':
  windows=[r for r in rows if r.get('kind')=='batch47_window'];by={r.get('phase'):r for r in windows}
  check(len(windows)==3 and set(by)=={'warmup_start','steady_start','steady_end'},'window_boundaries')
  if len(by)==3:
   warm=by['steady_start']['monotonicSeconds']-by['warmup_start']['monotonicSeconds'];steady=by['steady_end']['monotonicSeconds']-by['steady_start']['monotonicSeconds'];check(warm>=9.9 and steady>=29.9,'full_wall_windows');check(by['steady_end']['gameTime']>by['steady_start']['gameTime'],'game_progress')
   result.update(windowBoundaryToleranceSeconds=0.1,windowDurationBasis="host event boundaries; configured 10/30s, up to 0.1s log boundary tolerance",warmupWallSeconds=warm,steadyWallSeconds=steady,steadyGameSeconds=by['steady_end']['gameTime']-by['steady_start']['gameTime'])
  progress=[r for r in rows if r.get('kind')=='scale47_progress'];dispatch={r.get('runId'):r for r in rows if r.get('kind')=='scale47_dispatch'}
  counts=[0]*n;travel=[0.]*n
  for r in progress:
   i=r['actor'];check(0<=i<n,'progress_actor_range')
   if not 0<=i<n:continue
   d=dispatch.get(r.get('runId'),{});receipt=r.get('receipt',{});data=receipt.get('Data',{})
   check(r.get('actorId')==actors[i]['actorId']==receipt.get('ActorId') and r.get('sessionId')==actors[i]['sessionId']==receipt.get('SessionId'),'progress_identity')
   check(d.get('actor')==i and data.get('runId')==r.get('runId') and data.get('status')=='succeeded' and r.get('passed') is True,'dispatch_terminal_binding')
   if r.get('startedInSteady'):
    counts[i]+=1
    if r.get('tool')=='act_MoveTo':
     p,o=r['position'],r['origin'];dist=math.hypot(p[0]-o[0],p[2]-o[2]);check(dist>3 and abs(dist-r['horizontalDisplacement'])<.01,'real_native_displacement');travel[i]+=dist
  check(all(x>=2 for x in counts) and all(x>3 for x in travel),'every_actor_steady_progress');result.update(steadySuccessfulActions=counts,steadyMoveDistances=travel)
 else:
  effects=[r for r in rows if r.get('kind')=='batch47_effect'];check(bool(effects) and all(r.get('passed') is True for r in effects),'recovery_effects');result['effects']=[r.get('name') for r in effects]
  required=['obstacle_failure','failed_owner_released','recovery_move']
  lookup={r.get('name'):r.get('evidence',{}) for r in effects}
  check(all(k in lookup for k in required),'obstacle_recovery_complete')
  failed=lookup.get('obstacle_failure',{});recovered=lookup.get('recovery_move',{})
  check(failed.get('status')=='failed' and failed.get('failure') not in (None,'none'),'native_failure_evidence')
  check(recovered.get('failedRunId')==failed.get('runId') and recovered.get('newRunId')!=failed.get('runId') and recovered.get('status')=='succeeded' and recovered.get('displacement',0)>1 and recovered.get('teleport') is False,'fresh_run_recovered')
  check(end.get('cycles')==2,'two_recreation_cycles')
  previous=actors[0]
  for cycle in (1,2):
   e={r.get('name'):r.get('evidence',{}) for r in effects if r.get('cycle')==cycle}
   check(all(k in e for k in ['remove_owner_idle','removed','new_binding','stale_session_rejected','stale_reference_rejected','new_actor_move','other_owner_unchanged','other_owner_progress']),'cycle_complete_'+str(cycle))
   bind=e.get('new_binding',{});move=e.get('new_actor_move',{});other=e.get('other_owner_progress',{});unchanged=e.get('other_owner_unchanged',{})
   check(bind.get('oldActor')==previous.get('actorId') and bind.get('oldSession')==previous.get('sessionId') and bind.get('newActor') not in (None,bind.get('oldActor')) and bind.get('newSession') not in (None,bind.get('oldSession')) and bind.get('nativeSpawn') is True,'native_new_binding_'+str(cycle))
   check(move.get('actorId')==bind.get('newActor') and move.get('sessionId')==bind.get('newSession') and move.get('status')=='succeeded' and move.get('displacement',0)>1 and move.get('teleport') is False,'new_actor_native_move_'+str(cycle))
   check(e.get('stale_reference_rejected',{}).get('actualState')=='native_error' and e.get('stale_reference_rejected',{}).get('actualError')=='unknown_reference' and e.get('stale_reference_rejected',{}).get('nativeWriteAttempted') is False,'stale_reference_'+str(cycle))
   check(other.get('runId')==bind.get('otherRunId')==unchanged.get('runId') and unchanged.get('status')=='running' and other.get('status')=='succeeded' and other.get('notMovement') is True,'other_original_wait_'+str(cycle))
   previous={'actorId':bind.get('newActor'),'sessionId':bind.get('newSession')}
  result['otherActorAction']='Wait, not movement' 
 result.update(passed=not errors,errors=errors,checks=end.get('checks'));return result

def main():
 p=pathlib.Path(sys.argv[1]);rows=[]
 try:
  rows=[json.loads(line) for line in p.read_text().splitlines() if line.strip()];result=summarize(rows)
 except (OSError,ValueError,KeyError,TypeError,IndexError) as e:result={'passed':False,'errors':['malformed_or_missing_evidence:'+str(e)]}
 for name,key in [('resources.json','resources'),('execution-group.json','executionGroup')]:
  f=p.parent/name
  if f.exists():result[key]=json.loads(f.read_text())
 if result.get('suite')=='scale' and not result.get('resources',{}).get('steady',{}).get('available'):result['passed']=False;result.setdefault('errors',[]).append('reliable_steady_resources_required')
 print(json.dumps(result,ensure_ascii=False,indent=2));raise SystemExit(0 if result['passed'] else 1)
if __name__=='__main__':main()
