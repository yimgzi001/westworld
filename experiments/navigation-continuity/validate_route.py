#!/usr/bin/env python3
"""Strict offline acceptance of the controlled 3m + 1m route chain; no execution."""
import argparse,hashlib,json,math,pathlib

def finite(x):return type(x) in (int,float) and math.isfinite(x)
def point(x):
 if isinstance(x,dict):x=[x.get('X'),x.get('Y'),x.get('Z')]
 return x if isinstance(x,list) and len(x)==3 and all(finite(v) for v in x) else None
def distance(a,b):return math.dist(a,b)
def validate(rows):
 checks=[]
 def check(name,condition):checks.append({'check':name,'status':'unknown' if condition is None else 'pass' if condition else 'fail'})
 def finish():return {'schema':'westworld-strict-route-v1','verdict':'fail' if any(c['status']=='fail' for c in checks) else 'unknown' if any(c['status']=='unknown' for c in checks) else 'pass','checks':checks,'binding':'unique serial single-actor log intervals + explicit bounded/local IDs; samples lack routeId; not cryptographic identity proof'}
 starts=[i for i,e in enumerate(rows) if e.get('kind')=='native_call_started' and e.get('step',{}).get('Tool')=='host_navigate_local']
 check('exactly_two_navigation_dispatches',len(starts)==2)
 if len(starts)!=2:return finish()
 def scope(n):
  start=starts[n];end=starts[n+1] if n+1<len(starts) else len(rows);window=rows[start:end]
  receipts=[e for e in window if e.get('kind')=='native_call_returned' and e.get('tool')=='host_navigate_local']
  if len(receipts)!=1:return None,None,None
  data=receipts[0].get('data',{});request=data.get('request_id')
  if not request:return None,None,None
  terminals=[e for e in window if e.get('kind')=='bounded_navigation_terminal' and e.get('snapshot',{}).get('RequestId')==request]
  return window,request,terminals[0] if len(terminals)==1 else None
 args=rows[starts[0]].get('step',{}).get('Arguments',{})
 check('first_command_distance_3m',args.get('distance')==3)
 first,rid,term=scope(0);check('first_unique_receipt_and_terminal',True if first is not None and term is not None else None)
 if first is None or term is None:return finish()
 snap=term.get('snapshot',{})
 check('first_arrived_single_segment',snap.get('Reason')=='arrived' and snap.get('Segments')==1 and snap.get('Busy') is False and term.get('Quarantined') is False)
 check('first_actual_progress',finite(snap.get('Path')) and 2.6<=snap['Path']<=3.5 and finite(snap.get('Remaining')) and 0<=snap['Remaining']<=.4)
 routes=[e for e in first if e.get('kind')=='observed_flat_route_started'];check('one_complete_route',len(routes)==1)
 if len(routes)!=1:return finish()
 route=routes[0];locals=[e for e in first if e.get('kind')=='local_navigation_start' and e.get('RequestId')==route.get('RequestId')]
 segments=[e for e in first if e.get('kind')=='bounded_navigation_segment' and e.get('request')==rid]
 check('unique_local_and_bounded_binding',True if len(locals)==1 and len(segments)==1 and route.get('routeId') else None)
 if len(locals)!=1 or len(segments)!=1:return finish()
 origin=point(locals[0].get('origin'));endpoint=point(locals[0].get('destination'));target=point(segments[0].get('target'));wps=[point(p) for p in route.get('waypoints',[])]
 check('geometry_present',True if origin and endpoint and target and len(wps)>=3 and all(wps) else None)
 if not origin or not endpoint or not target or len(wps)<3 or not all(wps):return finish()
 bearing=args.get('bearing');expected=[origin[0]+3*math.sin(math.radians(bearing)),origin[1],origin[2]+3*math.cos(math.radians(bearing))] if finite(bearing) else None
 check('full_authorized_3m_endpoint',expected is not None and distance(endpoint,expected)<.02 and distance(endpoint,target)<.02 and distance(endpoint,wps[-1])<.02)
 samples=[e for e in first if e.get('kind')=='navigation_sample' and e.get('requestId')==rid]
 check('single_sample_owner',True if samples and len({e.get('ownerId') for e in samples})==1 and samples[0].get('ownerId') else None)
 moving=[i for i,e in enumerate(samples) if e.get('state')=='Moving'];check('moving_samples_present',True if len(moving)>=2 else None)
 if len(moving)<2:return finish()
 block=samples[moving[0]:moving[-1]+1];times=[e.get('monotonicSeconds') for e in block]
 check('no_internal_nonmoving_state',all(e.get('state')=='Moving' for e in block))
 check('continuous_sample_times',all(finite(t) for t in times) and all(0<b-a<=.1 for a,b in zip(times,times[1:])))
 check('actual_lease_while_moving',all(e.get('busy') is True and e.get('ownsLease') is True for e in block))
 direction=[endpoint[k]-origin[k] for k in (0,2)]
 for i,wp in enumerate(wps[:-1]):
  near=[e for e in block if point(e.get('position')) and math.hypot(e['position'][0]-wp[0],e['position'][2]-wp[2])<=.2]
  projections=[(e['position'][0]-wp[0])*direction[0]+(e['position'][2]-wp[2])*direction[1] for e in near]
  check(f'waypoint_{i}_both_sides_sampled',True if len(near)>=2 and min(projections)<0<max(projections) else None)
  check(f'waypoint_{i}_no_sampled_low_speed',all(finite(e.get('horizontalSpeed')) and e['horizontalSpeed']>=.1 for e in near) if near else None)
 second,_,terminal2=scope(1);check('second_command_1m',rows[starts[1]].get('step',{}).get('Arguments',{}).get('distance')==1)
 check('second_actual_arrived',None if terminal2 is None else terminal2.get('snapshot',{}).get('Reason')=='arrived' and terminal2.get('snapshot',{}).get('Busy') is False and terminal2.get('Quarantined') is False and finite(terminal2.get('snapshot',{}).get('Path')) and terminal2['snapshot']['Path']>=.6 and finite(terminal2['snapshot'].get('Remaining')) and 0<=terminal2['snapshot']['Remaining']<=.4)
 results=[e for e in rows if e.get('kind')=='result'];check('overall_native_pass',None if len(results)!=1 else results[0].get('passed') is True and results[0].get('updateFailures')==0)
 return finish()

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('log',type=pathlib.Path);a=p.parse_args();raw=a.log.read_bytes();v=validate([json.loads(x) for x in raw.splitlines() if x.strip()]);v['sourceSha256']=hashlib.sha256(raw).hexdigest();print(json.dumps(v,indent=2))
if __name__=='__main__':main()
