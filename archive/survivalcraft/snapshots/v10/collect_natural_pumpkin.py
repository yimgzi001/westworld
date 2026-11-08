import pathlib,json,time,sys,math,os
p=pathlib.Path(__file__).resolve().parent;d=p/'ipc-wet';sys.path.insert(0,str(p/'Bridge'));from controller import issue
f=next(f for f in d.glob('*.state.json') if time.time()-f.stat().st_mtime<3);pid=f.name.removesuffix('.state.json');target=(-139,66,70)
def read():return json.loads(f.read_text())
def inv(s):return sum(v['count'] for v in s['slots'] if v['value']&1023==131)
def cell(s):return next((b['value']&1023 for b in s['cells'] if (b['x'],b['y'],b['z'])==target),None)
b=read();origin=b['state']['position'];out={'mode':'real native pumpkin normal dig and pickup, no model or injection','target':target,'initial_pumpkins':inv(b['state']),'initial_food':b['state']['vitals']['food'],'events':[]}
try:
 assert cell(b['state'])==131,'natural target not currently observed'
 assert inv(b['state'])==0,'unexpected prior pumpkin inventory'
 assert math.dist([origin['x'],origin['z']],[-138.5,70.5])<2.1,'not safely adjacent'
 deadline=time.monotonic()+6
 while time.monotonic()<deadline:
  j=read();s=j['state'];a=s['position']
  if time.time()-f.stat().st_mtime>2 or s['health']<1 or s['air']<1 or math.dist(list(a.values()),list(origin.values()))>.75:raise RuntimeError('safety_stop')
  if cell(s)!=131:break
  c=issue(d,pid,'dig',x=target[0]+.5-a['x'],y=target[1]+.5-(a['y']+1.6),z=target[2]+.5-a['z'],seconds=.3);time.sleep(.25);n=read();out['events'].append({'ack':n['lastCommand']==c['Id'],'pumpkins':inv(n['state']),'target_contents':cell(n['state'])})
 issue(d,pid,'stop');time.sleep(1);s=read()['state'];out['after_dig']={'target_contents':cell(s),'pumpkins':inv(s),'slots':[v for v in s['slots'] if v['count']]}
 if cell(s)!=0 or inv(s)!=1:raise RuntimeError('gather_not_verified_do_not_eat')
 slot=next(v for v in s['slots'] if v['value']&1023==131 and v['count']==1)
 assert slot['nutrition']>0 and slot['sicknessProbability']==0
 c={'Id':time.time_ns(),'Action':'eat','Seconds':.4,'Slot':slot['slot'],'ExpectedValue':slot['value'],'ExpectedCount':slot['count']};q=f.with_name(f.name.replace('.state.json','.command.json'));t=q.with_suffix('.tmp');t.write_text(json.dumps(c));os.replace(t,q)
 end=time.monotonic()+4;result=None
 while time.monotonic()<end:
  log=d/'eat-results.jsonl'
  if log.exists():result=next((j for line in log.read_text().splitlines() if (j:=json.loads(line))['id']==c['Id']),None)
  if result:break
  time.sleep(.1)
 out['eat_transaction']=result
 end=time.monotonic()+3
 while time.monotonic()<end:
  s=read()['state']
  if inv(s)==0 and result and s['vitals']['food']>result['beforeFood']:break
  time.sleep(.1)
 out['after_eat']={'pumpkins':inv(s),'food':s['vitals']['food'],'starter_bread_count':sum(v['count'] for v in s['slots'] if v['value']&1023==162)}
 out['natural_chain_verified']=bool(result and not result['error'] and result['afterFood']>result['beforeFood'] and inv(s)==0 and result['beforeCount']-result['afterCount']==1)
except Exception as e:out['error']=str(e)
finally:
 c=issue(d,pid,'stop');end=time.monotonic()+5
 while time.monotonic()<end:
  last=read()
  if last['lastCommand']==c['Id']:break
  time.sleep(.1)
 out['stop_ack']=last['lastCommand']==c['Id'];out['final_health']=last['state']['health'];out['final_air']=last['state']['air'];(p/'natural-food-chain-results.json').write_text(json.dumps(out,indent=2));print(json.dumps(out))
