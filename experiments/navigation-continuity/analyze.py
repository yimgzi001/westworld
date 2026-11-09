#!/usr/bin/env python3
"""Offline timing analysis. Never starts game/model or infers missing telemetry."""
import argparse,collections,hashlib,json,math,pathlib

def number(v):return type(v) in (int,float) and math.isfinite(v)
def stamp(e):
    if number(e.get('monotonicSeconds')):return e['monotonicSeconds']
    t,f=e.get('monotonicTicks'),e.get('monotonicFrequency')
    return t/f if number(t) and number(f) and f>0 else None

def selected_routes(rows,stop_speed):
    result=[]
    for index,event in enumerate(rows):
        if event.get('kind')!='observed_flat_route_started':continue
        frames=[]
        for e in rows[index+1:]:
            if e.get('kind')=='observed_flat_route_started':break
            if e.get('kind')!='navigation_sample':continue
            if e.get('state')!='Moving':break
            if frames and (e.get('requestId'),e.get('ownerId'))!=(frames[0].get('requestId'),frames[0].get('ownerId')):break
            frames.append(e)
        waypoint=event.get('waypoints',[None])[0]
        near=[]
        if isinstance(waypoint,dict) and number(waypoint.get('X')) and number(waypoint.get('Z')):
            for e in frames:
                pos=e.get('position')
                if isinstance(pos,list) and len(pos)==3 and all(number(x) for x in pos) and math.hypot(pos[0]-waypoint['X'],pos[2]-waypoint['Z'])<=.2 and number(e.get('horizontalSpeed')):near.append(e)
        times=[stamp(e) for e in frames];valid=all(t is not None for t in times)
        maxgap=max((b-a for a,b in zip(times,times[1:])),default=None) if valid else None
        internal=[]
        for wi,wp in enumerate(event.get('waypoints',[])[:-1]):
            if not isinstance(wp,dict) or not number(wp.get('X')) or not number(wp.get('Z')):continue
            window=[e for e in frames if isinstance(e.get('position'),list) and len(e['position'])==3 and all(number(x) for x in e['position']) and math.hypot(e['position'][0]-wp['X'],e['position'][2]-wp['Z'])<=.2 and number(e.get('horizontalSpeed'))]
            internal.append({'index':wi,'sampleCount':len(window),'minimumSampledSpeed':min((e['horizontalSpeed'] for e in window),default=None),'lowSpeedSamples':sum(e['horizontalSpeed']<stop_speed for e in window) if window else None})
        origin=next((e.get('origin') for e in reversed(rows[:index]) if e.get('kind')=='local_navigation_start' and e.get('RequestId')==event.get('RequestId')),None)
        endpoint=event.get('waypoints',[None])[-1]
        length=math.hypot(endpoint['X']-origin['X'],endpoint['Z']-origin['Z']) if isinstance(origin,dict) and isinstance(endpoint,dict) and all(number(o.get(k)) for o in (origin,endpoint) for k in ('X','Z')) else None
        result.append({'routeId':event.get('routeId'),'plannedEndpointDistance':length,'internalWaypoints':internal,'skillRequestId':event.get('RequestId'),
            'association':'serial_log_order_not_explicit_route_id_in_samples','waypoints':event.get('waypoints'),
            'movingSampleCount':len(frames),'movingSampleSpanSeconds':times[-1]-times[0] if len(times)>1 and valid else None,
            'maximumMovingSampleGapSeconds':maxgap,'firstWaypointRadius':.2,'firstWaypointNearSampleCount':len(near),
            'firstWaypointMinimumSampledSpeed':min((e['horizontalSpeed'] for e in near),default=None),
            'firstWaypointLowSpeedSamples':sum(e['horizontalSpeed']<stop_speed for e in near) if near else None,
            'startSamplePosition':frames[0].get('position') if frames else None,'endMovingSamplePosition':frames[-1].get('position') if frames else None})
    return result

def analyze(rows,max_gap=.1,stop_speed=.1):
    if not 0<max_gap<=1 or not 0<=stop_speed<=1:raise ValueError('threshold_bound')
    dispatch=[e for e in rows if e.get('kind')=='native_call_started' and stamp(e) is not None]
    pairs=[]
    for term in rows:
        if term.get('kind')!='prefetch_preceding_terminal':continue
        t=stamp(term);request=term.get('requestId')
        response=next((e for e in rows if e.get('kind')=='prefetch_response' and e.get('requestId')==request),None)
        asked=next((e for e in rows if e.get('kind')=='prefetch_requested' and e.get('lease',{}).get('RequestId')==request),None)
        taken=next((e for e in rows if e.get('kind')=='prefetch_handoff_taken' and e.get('requestId')==request),None)
        # Historical dispatch lacks candidate request ID: nearest dispatch is only
        # a temporal association, NOT proof that it executed this candidate.
        following=next((e for e in dispatch if t is not None and stamp(e)>=t),None)
        native=next((e for e in rows if term.get('precedingNavigation') and e.get('kind')=='bounded_navigation_terminal' and e.get('snapshot',{}).get('RequestId')==term['precedingNavigation']),None)
        nt=stamp(native) if native else None
        rt=stamp(response) if response else None;qt=stamp(asked) if asked else None
        pairs.append({'requestId':request,'precedingSuccess':term.get('success'),
            'requestToResponseSeconds':rt-qt if rt is not None and qt is not None and rt>=qt else None,
            'terminalToNextDispatchSeconds':stamp(following)-t if following else None,
            'nativeTerminalToNextDispatchSeconds':stamp(following)-nt if following and nt is not None and stamp(following)>=nt else None,
            'terminalObservationDelaySeconds':t-nt if t is not None and nt is not None and t>=nt else None,
            'dispatchAssociation':'temporal_only_unbound' if following else 'unknown',
            'responseAfterTerminalSeconds':max(0,rt-t) if rt is not None and t is not None else None,
            'candidateTaken':taken is not None,'reportedMovingOverlapSeconds':response.get('overlapSeconds') if response else None,
            'reportedMovingOverlapPath':response.get('overlapPath') if response else None})
    samples=[e for e in rows if e.get('kind')=='navigation_sample'];transitions=0;times=collections.defaultdict(float);stops=[];active=None;covered=0.;gaps=0
    for a,b in zip(samples,samples[1:]):
        ta,tb=stamp(a),stamp(b);dt=tb-ta if ta is not None and tb is not None else None
        same=a.get('requestId') is not None and a.get('requestId')==b.get('requestId') and a.get('ownerId') is not None and a.get('ownerId')==b.get('ownerId')
        if dt is None or not 0<dt<=max_gap or not same:
            gaps+=1;active=None;continue
        sa,sb=a.get('horizontalSpeed'),b.get('horizontalSpeed');state=a.get('state')
        if not isinstance(state,str) or not number(sa) or not number(sb) or min(sa,sb)<0:
            gaps+=1;active=None;continue
        covered+=dt;times[state]+=dt
        if isinstance(b.get('state'),str) and b['state']!=state:transitions+=1
        if sa<stop_speed and sb<stop_speed:
            if active is None:
                active={'requestId':a['requestId'],'ownerId':a['ownerId'],'start':ta,'end':tb,'seconds':dt};stops.append(active)
            else:active['end']=tb;active['seconds']+=dt
        else:active=None
    return {'schema':'westworld-continuity-analysis-v1','prefetchTiming':pairs,
        'sampledStateSeconds':dict(times) if covered else None,'sampledCoverageSeconds':covered if samples else None,
        'lowSpeedIntervals':stops if covered else None,'sampleGapOrInvalidCount':gaps if samples else None,
        'sampledStateTransitionCount':transitions if covered else None,
        'reportedSegmentCounts':sorted({e['segmentCount'] for e in samples if type(e.get('segmentCount')) is int and e['segmentCount']>=0}),
        'postTerminalPhysicalState':'sampled_after_busy_not_continuous_proof' if any(e.get('busy') is False and e.get('state') in ('Arrived','Failed','Cancelled') for e in samples) else 'unknown_busy_only_telemetry',
        'postTerminalSampleCount':sum(e.get('busy') is False and e.get('state') in ('Arrived','Failed','Cancelled') for e in samples),
        'selectedFlatRoutes':selected_routes(rows,stop_speed),
        'flatRouteFallbackCount':sum(e.get('kind')=='observed_flat_route_not_selected' for e in rows),
        'internalWaypointStopCount':None,
        'stopSpeedThreshold':stop_speed,'maximumSampleGapSeconds':max_gap,
        'queryProjectionCpuSeconds':None,'nativeActualStopTimes':None,
        'unknown':['No per-frame navigation_sample telemetry; state dwell/physical stops unknown'] if not samples else ['Intervals are sampled bounds, not proof of continuous zero speed; gaps are not interpolated'],
        'navigationTerminals':[{'snapshot':e.get('snapshot'),'elapsedSeconds':e.get('elapsedSeconds')} for e in rows if e.get('kind')=='bounded_navigation_terminal'],
        'result':next((e for e in reversed(rows) if e.get('kind')=='result'),None)}

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('log',type=pathlib.Path);a=p.parse_args()
    raw=a.log.read_bytes();rows=[json.loads(line) for line in raw.splitlines() if line.strip()]
    result=analyze(rows);result['sourceSha256']=hashlib.sha256(raw).hexdigest();print(json.dumps(result,ensure_ascii=False,indent=2))
if __name__=='__main__':main()
