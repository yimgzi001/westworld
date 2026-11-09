"""Offline host-evidence contract; never executes tools or reconstructs missing facts."""
import json, math, sys

def validate(e):
    failures=[]; missing=[]; method=None
    if not isinstance(e,dict):return dict(verdict="fail",failures=["root_not_object"],missing=[])
    def need(obj,key,typ=None):
        if not isinstance(obj,dict): failures.append('object_required_'+key);return None
        if key not in obj or obj[key] is None: missing.append(key); return None
        v=obj[key]
        if typ and (not isinstance(v,typ) or isinstance(v,bool) and typ in (int,float,(int,float))): failures.append('invalid_'+key);return None
        if isinstance(v,float) and not math.isfinite(v): failures.append('nonfinite_'+key);return None
        return v
    def check(ok,reason):
        if not ok: failures.append(reason)
    try:
        if need(e,'schema',int)!=1: failures.append('unsupported_schema')
        for k in ('sessionId','actorId','sourceId'):
            v=need(e,k,str)
            if v is not None:check(bool(v.strip()),'empty_'+k)
        value=need(e,'value',int);fish=need(e,'initialFishValue',int)
        if value is not None and fish is not None:check(value!=fish,'initial_fish_not_new_food')
        for k,want in [('baselineCount',0),('sourceCount',1),('uniqueSourceThroughout',True),('inventoryInjection',False)]:
            v=need(e,k,bool if isinstance(want,bool) else int)
            if v is not None:check(v==want,'invalid_'+k)
        a=need(e,'acquisition',dict);b=need(e,'eat',dict);s=need(e,'shutdown',dict)
        if a is not None:
            for k,want in [('sourceId',e.get('sourceId')),('value',value),('inventoryBefore',0),('inventoryAfter',1),('worldBefore',1),('worldAfter',0)]:
                v=need(a,k,str if k=='sourceId' else int)
                if v is not None:check(v==want,'acquisition_'+k)
            method=need(a,'method',str)
            if method is not None:check(method in ('native_automatic_collection','model_pickup_native_receipt'),'unknown_pickup_method')
            if method=='model_pickup_native_receipt':
                pr=need(a,'pickupReceipt',dict)
                if pr is not None:
                    for k in ('requestId','sequence','nativeStatus'):need(pr,k,str if k!='sequence' else int)
                    check(pr.get('nativeStatus')=='succeeded','pickup_not_native_succeeded')
            at=need(a,'time',(int,float))
        else: at=None;method=None
        if b is not None:
            for k,want in [('value',value),('eventValue',value),('inventoryBefore',1),('inventoryAfter',0),('nativeStatus','succeeded')]:
                v=need(b,k,str if k=='nativeStatus' else int)
                if v is not None:check(v==want,'eat_'+k)
            fb=need(b,'foodBefore',(int,float));fa=need(b,'foodAfter',(int,float));sb=need(b,'statsBefore',int);sa=need(b,'statsAfter',int);bt=need(b,'time',(int,float))
            if fb is not None and fa is not None:check(0<=fb<fa<=1,'food_not_increased')
            if sb is not None and sa is not None:check(sa==sb+1,'native_eat_stat_not_one')
            if at is not None and bt is not None:check(bt>=at,'eat_before_acquisition')
            req=need(b,'requestId',str);seq=need(b,'sequence',int)
        else:bt=req=seq=None
        ds=need(e,'dispatches',list)
        if ds is not None:
            matches=[];last=-math.inf
            for d in ds:
                t=need(d,'time',(int,float));r=need(d,'requestId',str);q=need(d,'sequence',int);tool=need(d,'tool',str)
                if t is not None:check(t>=last,'dispatch_clock_regression');last=t
                if r==req and q==seq and tool=='act_Eat':matches.append(t)
            if method=='model_pickup_native_receipt' and isinstance(a.get('pickupReceipt'),dict):
                pr=a['pickupReceipt'];pm=[d for d in ds if isinstance(d,dict) and d.get('requestId')==pr.get('requestId') and d.get('sequence')==pr.get('sequence') and d.get('tool')=='act_PickUp']
                check(len(pm)==1,'pickup_request_not_uniquely_bound')
                if len(pm)==1 and at is not None and isinstance(pm[0].get('time'),(int,float)):check(pm[0]['time']<=at,'pickup_dispatch_after_acquisition')
            if b is not None:
                check(len(matches)==1,'eat_request_not_uniquely_bound')
                if len(matches)==1 and matches[0] is not None and bt is not None and at is not None:check(at<=matches[0]<=bt,'eat_dispatch_order')
        if s is not None:
            stop=need(s,'stopTime',(int,float));drain=need(s,'drainedTime',(int,float));budget=need(s,'budgetSeconds',(int,float));idle=need(s,'idle',bool);late=need(s,'lateDispatches',int)
            if None not in (stop,drain,budget):check(0<=drain-stop<=budget and budget>0,'drain_outside_budget')
            if idle is not None:check(idle,'not_stopped')
            if late is not None:check(late==0,'late_dispatch')
            if stop is not None and bt is not None:check(bt<=stop,'eat_after_stop')
            if stop is not None and ds is not None:
                for d in ds:
                    if isinstance(d,dict) and isinstance(d.get('time'),(int,float)):check(d['time']<=stop,'dispatch_after_stop')
    except (TypeError,ValueError,KeyError):failures.append('malformed_evidence')
    return dict(verdict='fail' if failures else 'partial' if missing else 'pass',failures=sorted(set(failures)),missing=sorted(set(missing)),pickupClaim='native automatic collection, not a model pickup action' if method=='native_automatic_collection' else 'requires separately retained native pickup receipt',scope='Offline host-record consistency only; not proof of untampered logs or autonomous natural survival')

if __name__=='__main__':
    data=json.load(open(sys.argv[1],encoding='utf-8'));result=validate(data);print(json.dumps(result,ensure_ascii=False,indent=2));sys.exit(0 if result['verdict']=='pass' else 1)
