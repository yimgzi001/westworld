#!/usr/bin/env python3
"""37 audit-only native acceptance. New schema2 coverage, never rewrite legacy evidence."""
import argparse, hashlib, importlib.util, json, math
from pathlib import Path


def load(path):
    spec=importlib.util.spec_from_file_location('frozen_'+hashlib.sha256(str(path).encode()).hexdigest()[:12],path)
    module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module); return module

def finite(x): return type(x) in (int,float) and math.isfinite(x)
def vector(x): return isinstance(x,list) and len(x)==3 and all(finite(y) for y in x)
def read_rows(path):
    if path.is_symlink() or path.stat().st_size>64*1024*1024: raise ValueError('log_bound_or_symlink')
    with path.open() as f:
        rows=[]
        for line in f:
            if len(line)>2*1024*1024: raise ValueError('line_bound')
            if line.strip(): rows.append(json.loads(line))
    return rows


def sampling(samples):
    fail=[]; unknown=[]; owners={}; groups={}; emitted_pairs=covered_pairs=0
    eligible_seconds=covered_seconds=chord=0.; moving_gaps=[]; suppressed=0; max_gap=0.
    for sample in samples:
        try:
            if sample['schemaVersion']!=2 or sample['samplePolicy']!='10hz_periodic_plus_boundaries':
                unknown.append('not_schema2'); continue
            t=sample['monotonicSeconds']; owner=sample['ownerId']; request=sample['requestId']
            if not isinstance(owner,str) or not owner or not isinstance(request,str) or not request or not finite(t):
                raise ValueError('identity_time')
            if not vector(sample['position']) or not vector(sample['velocity']): raise ValueError('physical_sample')
            if not isinstance(sample['segmentRequestId'],str) or not sample['segmentRequestId']: raise ValueError('segment_identity')
            if type(sample['suppressedSamples'])!=int or sample['suppressedSamples']<0: raise ValueError('suppression_count')
            if type(sample['coverageUnknown'])!=bool: raise ValueError('coverage_flag')
            suppressed+=sample['suppressedSamples']
            previous=owners.get(owner); trigger=sample['sampleTrigger']; interval=sample['sampleIntervalSeconds']
            if previous is None:
                if trigger!='first' or interval is not None: fail.append('first_sample_contract')
                if sample['coverageUnknown']: fail.append('first_sample_unknown_without_gap')
            else:
                gap=t-previous['monotonicSeconds']; max_gap=max(max_gap,gap)
                if not finite(interval) or abs(interval-gap)>1e-7: fail.append('actual_interval_mismatch')
                if gap>.15 and sample['coverageUnknown'] is not True: fail.append('unreported_coverage_gap')
                if gap<0:
                    unknown.append('clock_regressed')
                    if trigger!='clock_regressed' or sample['coverageUnknown'] is not True: fail.append('unreported_clock_regression')
                if trigger=='periodic' and (sample['busy'] is not True or gap<.1-1e-8): fail.append('periodic_pacing_or_idle_spam')
                if trigger not in ('periodic','boundary','clock_regressed'): fail.append('invalid_emit_trigger')
                if previous['requestId']==request and previous['state']=='Moving' and sample['state']=='Moving' and previous['busy'] is True and sample['busy'] is True:
                    emitted_pairs+=1
                    if gap>0: eligible_seconds+=gap
                    valid=0<gap<=.15 and sample['coverageUnknown'] is False and previous.get('segmentRequestId')==sample['segmentRequestId'] and previous.get('ownsLease') is True and sample['ownsLease'] is True
                    if valid:
                        covered_pairs+=1; covered_seconds+=gap
                        chord+=math.hypot(sample['position'][0]-previous['position'][0],sample['position'][2]-previous['position'][2])
                    else: moving_gaps.append({'requestId':request,'seconds':gap,'flag':sample['coverageUnknown']})
            owners[owner]=sample; groups.setdefault(request,[]).append(sample)
        except (KeyError,ValueError,TypeError) as exc: unknown.append('invalid_sample:'+str(exc))
    if not samples: unknown.append('navigation_samples_missing')
    return {'failures':sorted(set(fail)),'unknown':sorted(set(unknown)), 'requests':groups,
            'metrics':{'sampleCount':len(samples),'suppressedCandidatesRecorded':suppressed,
            'candidateSuppressionFraction':suppressed/(suppressed+len(samples)) if suppressed+len(samples) else None,
            'maxEmittedGapSeconds':max_gap,'coverageGapLimitSeconds':.15,
            'movingPairCount':emitted_pairs,'coveredMovingPairCount':covered_pairs,
            'movingPairCoverageFraction':covered_pairs/emitted_pairs if emitted_pairs else None,
            'movingTimeBetweenPairsSeconds':eligible_seconds,'coveredMovingSeconds':covered_seconds,
            'movingTimeCoverageFraction':covered_seconds/eligible_seconds if eligible_seconds else None,
            'coveredSampleChordMetres':chord,'maxObservedNetDisplacementMetres':max([math.hypot(g[-1]['position'][0]-g[0]['position'][0],g[-1]['position'][2]-g[0]['position'][2]) for g in groups.values() if g] or [0]),'movingCoverageGaps':moving_gaps,
            'coverageUnknownSampleCount':sum(x.get('coverageUnknown') is True for x in samples),'continuousMotionCoverage':'unknown' if moving_gaps or not emitted_pairs or any(x.get('coverageUnknown') is True for x in samples) else 'sampled_intervals_only',
            'warning':'Pairs are observed chords, not interpolated continuous motion or full physical path. Suppressed tail after final emission is not counted.'}}


def safety(rows,case,ids):
    checks=[]
    def check(name,ok): checks.append({'check':name,'status':'unknown' if ok is None else 'pass' if ok else 'fail'})
    try:
        ending=ids[-1]
        receipts=[r['receipt'] for r in rows if r.get('kind')=='tool_receipt']
        nav=[r for r in receipts if r.get('Tool')=='host_navigation_status' and (r.get('Data') or {}).get('request_id')==ending]
        check('final_public_owner_status',None if not nav else nav[-1].get('State')=='returned' and nav[-1]['Data'].get('owner_quarantined') is False and nav[-1]['Data'].get('busy') is False)
        scope=(nav[-1]['SessionId'],nav[-1]['ActorId']) if nav else None
        physical=[r for r in rows if (r.get('kind')=='predecessor_physical_sample' and r.get('runId')==ending) or (case=='readiness' and r.get('kind')=='n35_readiness_fixture_sample' and r.get('navigation',{}).get('request_id')==ending)]
        check('final_physical_evidence',None if not physical else True)
        if physical:
            last=physical[-1];velocity=last.get('velocity');walk=last.get('walk');turn=last.get('turn')
            shape=vector(velocity) and isinstance(walk,list) and len(walk)==2 and all(finite(x) for x in walk) and isinstance(turn,list) and len(turn)==2 and all(finite(x) for x in turn)
            check('final_speed_input_quiet',None if not shape else sum(x*x for x in velocity)<=.01 and sum(x*x for x in walk)<=1e-6 and sum(x*x for x in turn)<=1e-6)
            check('physical_same_scope',scope is not None and (last.get('sessionId'),last.get('actorId'))==scope)
            check('physical_actual_idle',last.get('nativeRunning') is False if case=='readiness' else last.get('moving') is False and last.get('settled') is True)
        stop_indices=[i for i,r in enumerate(rows) if r.get('kind') in ['stop_requested','stop_drained','food_stop_requested','route_fixture_cancel','n35_readiness_fixture_result','handoff_fixture_result','route_fixture_result'] or (r.get('kind')=='tool_receipt' and r.get('receipt',{}).get('Tool')=='host_cancel_navigation')]
        check('explicit_stop_boundary_present',True if stop_indices else None)
        if stop_indices:
            boundary=min(stop_indices)
            def write_tool(t):return isinstance(t,str) and not (t.startswith(('read_','find_')) or t in ['observe','action_status','task_status','host_navigation_status','cancel_action','cancel_task','host_cancel_navigation'])
            late=[r for r in rows[boundary+1:] if (r.get('kind')=='native_call_started' and write_tool(r.get('step',{}).get('Tool'))) or (r.get('kind')=='tool_receipt' and write_tool(r.get('receipt',{}).get('Tool')))]
            check('no_ordinary_write_from_stop_through_final',not late)
            check('exact_host_stop_target',all((r['receipt'].get('Data') or {}).get('request_id')==ending for r in rows[boundary:] if r.get('kind')=='tool_receipt' and r.get('receipt',{}).get('Tool')=='host_cancel_navigation'))
        # Cleanup action/task observations come from the same ledger after cancellation.
        for tool in ['action_status','task_status']:
            final=[r for r in receipts if r.get('Tool')==tool]
            data=final[-1].get('Data') if final else None
            terminal=data is None if tool=='action_status' else False
            if isinstance(data,dict):terminal=data.get('status') in (['succeeded','failed','cancelled'] if tool=='action_status' else ['idle','succeeded','failed','cancelled','skipped'])
            check('cleanup_terminal_'+tool,None if not final else final[-1].get('State')=='returned' and (final[-1]['SessionId'],final[-1]['ActorId'])==scope and terminal)
        if case=='readiness':
            drained=[r for r in rows if r.get('kind')=='stop_drained'];jumps=[r for r in rows if r.get('kind')=='native_call_returned' and r.get('tool')=='act_Jump']
            check('readiness_drain_same_owner',None if len(drained)!=1 or len(jumps)!=1 else drained[0].get('modelCompleted') is True and drained[0].get('navigation',{}).get('RequestId')==ending and drained[0]['navigation'].get('Busy') is False and drained[0].get('direct',{}).get('runId')==jumps[0].get('data',{}).get('runId') and drained[0]['direct'].get('status')=='succeeded' and drained[0].get('task',{}).get('status') in ['idle','succeeded','failed','cancelled','skipped'])
    except (KeyError,TypeError,IndexError,ValueError):check('missing_safety_evidence',None)
    return checks


def validate(rows,case,readiness=None):
    checks=[]
    def check(name,ok): checks.append({'check':name,'status':'unknown' if ok is None else 'pass' if ok else 'fail'})
    samples=[r for r in rows if r.get('kind')=='navigation_sample']; s=sampling(samples)
    for issue in s['failures']: check(issue,False)
    for issue in s['unknown']: check(issue,None)
    try:
        calls=[r for r in rows if r.get('kind')=='native_call_started']
        nav=[r for r in rows if r.get('kind')=='native_call_returned' and r.get('tool')=='host_navigate_local']
        expected=2 if case=='arrive' else 1
        check('exact_navigation_attempt_count',len(nav)==expected)
        ids=[r['data']['request_id'] for r in nav]
        check('unique_host_request_ids',len(ids)==len(set(ids)))
        receipts=[r['receipt'] for r in rows if r.get('kind')=='tool_receipt']
        scopes={(r.get('SessionId'),r.get('ActorId')) for r in receipts}
        check('one_bound_session_actor',len(scopes)==1 and all(all(isinstance(x,str) and x for x in q) for q in scopes))
        for n in nav:
            check('raw_native_call_receipt_'+str(n['sequence']),any(r.get('Tool')==n['tool'] and r.get('Sequence')==n['sequence'] and r.get('Data')==n['data'] for r in receipts))
        check('sample_requests_exactly_observed_runs',set(s['requests'])==set(ids))
        check('one_sample_owner',len({x.get('ownerId') for x in samples})==1)
        terminals=[r for r in rows if r.get('kind')=='bounded_navigation_terminal']
        for rid in ids:
            group=s['requests'].get(rid,[])
            check('start_sample_'+rid,bool(group))
            if not group: continue
            check('start_boundary_'+rid,group[0]['sampleTrigger'] in ['first','boundary'])
            ending='Arrived' if case=='arrive' else 'Cancelled' if case=='cancel' else 'Failed'
            terminal_samples=[x for x in group if x.get('boundedState')==ending and x.get('busy') is False]
            check('terminal_sample_'+rid,bool(terminal_samples))
            if terminal_samples:
                ts=terminal_samples[0]
                check('terminal_not_periodic_'+rid,ts['sampleTrigger'] in ['first','boundary'])
                check('terminal_state_matches_'+rid,ts.get('state')==ending and ts.get('requestId')==rid)
                if case!='readiness': check('terminal_still_motionless_'+rid,vector(ts.get('velocity')) and sum(x*x for x in ts['velocity'])<=.01 and ts.get('ownsLease') is False)
            if case!='readiness':
                matches=[r for r in terminals if r.get('snapshot',{}).get('RequestId')==rid]
                check('unique_raw_terminal_'+rid,len(matches)==1)
                if len(matches)==1:
                    snap=matches[0]['snapshot']; check('true_terminal_state_'+rid,snap.get('Busy') is False and snap.get('Status')==(3 if case=='arrive' else 5) and matches[0].get('Quarantined') is False)
                    check('real_path_'+rid,finite(snap.get('Path')) and snap['Path']>(.5 if case=='arrive' else .1))
                    if case=='arrive':check('arrived_remaining_'+rid,finite(snap.get('Remaining')) and 0<=snap['Remaining']<=.4 and snap.get('Reason')=='arrived')
                    terminal_time=matches[0]['monotonicTicks']/matches[0]['monotonicFrequency']
                    check('terminal_sample_time_binding_'+rid,None if not terminal_samples else finite(terminal_time) and 0<=terminal_samples[0]['monotonicSeconds']-terminal_time<=.15)
            if case=='arrive':
                check('running_and_stopping_preserved_'+rid,any(x.get('boundedState')=='Running' for x in group) and any(x.get('state')=='Settling' for x in group))
            elif case=='cancel':
                check('explicit_stopping_preserved',any(x.get('boundedState')=='Stopping' for x in group))
        if case in ['arrive','cancel']:
            check('actual_observed_displacement',s['metrics']['coveredSampleChordMetres']>.3 if case=='arrive' else s['metrics']['maxObservedNetDisplacementMetres']>.1)
            check('only_navigation_decisions',all(c.get('step',{}).get('Tool')=='host_navigate_local' for c in calls))
            check('same_existing_native_routes',any(r.get('kind')=='observed_flat_route_started' for r in rows))
        if case=='cancel':
            bound=[r for r in rows if r.get('kind')=='route_fixture_bound']
            cancel=[r for r in rows if r.get('kind')=='route_fixture_cancel']
            check('one_explicit_cancel_after_displacement',len(bound)==len(cancel)==1 and cancel[0].get('phase')=='after_actual_displacement' and bound[0].get('fixtureOverallId')==ids[0]==cancel[0].get('fixtureOverallId'))
            check('cancel_receipt_preserved',any(r.get('Tool')=='host_cancel_navigation' and r.get('State')=='returned' and r.get('Data',{}).get('request_id')==ids[0] for r in receipts))
            check('no_candidate_after_cancel',not any(r.get('kind')=='prefetch_handoff_taken' for r in rows))
        if case=='readiness':
            check('readiness_independent_semantics',None if readiness is None or readiness.get('verdict') in ['unknown','uncovered'] else readiness['verdict']=='pass')
            check('no_native_navigation_segment',not any(r.get('kind')=='bounded_navigation_segment' for r in rows))
        final=[r for r in rows if r.get('kind')=='final_audit']; result=[r for r in rows if r.get('kind')=='result']
        check('final_state_available',len(final)==len(result)==1)
        if len(final)==len(result)==1:
            check('actual_exit_success',result[0].get('passed') is True and result[0].get('updateFailures')==0)
            check('native_and_controller_idle',final[0].get('modelInFlight') is False and final[0].get('requiresReconciliation') is None and final[0].get('navigation',{}).get('Busy') is False)
            end=rows.index(final[0]); check('no_late_dispatch',not any(r.get('kind')=='native_call_started' for r in rows[end+1:]))
        checks.extend(safety(rows,case,ids))
        check('no_raw_failure_removed',not any(r.get('kind') in ['fatal','failure','cleanup_failure','native_call_outcome_unknown'] for r in rows))
    except (KeyError,TypeError,IndexError,ValueError) as exc: check('missing_or_invalid_lifecycle:'+type(exc).__name__,None)
    return {'schema':'navigation-audit37-native-v1','case':case,'verdict':'fail' if any(c['status']=='fail' for c in checks) else 'unknown' if any(c['status']=='unknown' for c in checks) else 'pass','checks':checks,'sampling':s['metrics'],'readiness':readiness,
            'scope':'Audit-only sampling lifecycle and observed displacement. No API, FPS improvement, zero-wait or all-interval continuity claim.'}


def evaluate(directory,case):
    rows=read_rows(directory/'tool-agent.jsonl'); old=None
    if case=='readiness':
        module=load(directory/'sources/experiments/navigation-readiness-evidence/validate.py')
        old=module.validate(rows,json.loads((directory/'readiness-driver-result.json').read_text()))
    result=validate(rows,case,old)
    result['rawLogSha256']=hashlib.sha256((directory/'tool-agent.jsonl').read_bytes()).hexdigest()
    result['rawLogBytes']=(directory/'tool-agent.jsonl').stat().st_size
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--case',choices=['arrive','cancel','readiness'],required=True);p.add_argument('--output',type=Path);a=p.parse_args()
    try:r=evaluate(a.directory,a.case)
    except (OSError,ValueError,KeyError) as e:r={'verdict':'unknown','error':str(e),'case':a.case}
    if a.output:
        with a.output.open('x') as f:json.dump(r,f,indent=2)
    print(json.dumps(r,indent=2));raise SystemExit(0 if r['verdict']=='pass' else 1)
