"""Real private SCNET skill adapter. No model policy and no scripted action sequence.
Only explicitly chosen skills execute; generated paths remain deterministic actuators.
"""
import pathlib,json,time,os,math,sys,subprocess
ROOT=pathlib.Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent/'qwen-lab/navigation'))
from explore_surface import propose as explore
from toward_food import propose as toward
from grid_planner import Cell,Planner
class Gateway:
 def __init__(self,run_name='qwen-autonomous'):
  self.d=ROOT/'ipc-wet';self.f=None;self.resources=[];self.visited=set();self.blocked=set();self.acquired=0;self.eat_uncertain=False;self.seq=0;self.last_result=None
  self.logs=ROOT/run_name;self.logs.mkdir(exist_ok=False)
 def state(self):
  if self.f is None:
   fs=[f for f in self.d.glob('*.state.json') if time.time()-f.stat().st_mtime<2]
   if len(fs)!=1:raise RuntimeError('requires_one_fresh_private_player')
   self.f=fs[0]
  if not 0<=time.time()-self.f.stat().st_mtime<=2:raise RuntimeError('state_stale')
  j=json.loads(self.f.read_text());s=j['state']
  if not all(math.isfinite(v) for v in [*s['position'].values(),s['health'],s['air'],s['vitals']['food']]):raise RuntimeError('nonfinite_state')
  return j
 def command(self,action,**kw):
  self.state();q=self.f.with_name(self.f.name.replace('.state.json','.command.json'));c=dict(Id=time.time_ns(),Action=action,Seconds=.3,**kw);t=q.with_suffix('.tmp');t.write_text(json.dumps(c));os.replace(t,q);return c
 def stop(self):
  c=self.command('stop');end=time.monotonic()+4
  while time.monotonic()<end:
   if self.state()['lastCommand']==c['Id']:return True
   time.sleep(.1)
  return False
 def scan(self):
  c=self.command('scanfood');end=time.monotonic()+4;m=self.d/'food-scan.json'
  while time.monotonic()<end:
   if m.exists():
    raw=json.loads(m.read_text())
    if raw['id']==c['Id']:
     if 'walkSurface' not in raw:raise RuntimeError('requires_verified_ground_map')
     raw['_mtime']=m.stat().st_mtime;return raw
   time.sleep(.1)
  raise RuntimeError('scan_not_confirmed')
 def safe(self,s):
  if s['health']<1 or s['air']<1:raise RuntimeError('health_or_air_guard')
 def normalized(self,raw):return dict(raw,surface=[r for r in raw['walkSurface'] if (r['x'],r['z']) not in self.blocked])
 def route_to(self,raw,s,target):
  pos=list(s['position'].values());data=self.normalized(raw);r=toward(data,pos,target,self.visited)
  if r['status']=='no_safe_progress_within_8_steps':
   cells={(v['x'],v['z']):Cell(v['x'],v['z'],v['y']+1,True,v['solid'],'solid',v['headroom'],0) for v in data['surface']};start=(math.floor(pos[0]),math.floor(pos[2]));paths=[]
   for dx,dz in [(1,0),(-1,0),(0,1),(0,-1)]:
    goal=(target[0]+dx,target[2]+dz);q=Planner(max_radius=32,max_steps=48).plan(cells,start,goal,0)
    if q.status=='ok' and abs(cells[goal].y-target[1])<=1:paths.append(q.cells)
   if paths:
    path=min(paths,key=len);r={'status':'candidate_known_detour','route':[{'center':[x+.5,cells[x,z].y,z+.5]} for x,z in path[:9]]}
  return r
 def observe(self):
  raw=self.scan();s=self.state()['state'];a=s['position'];pos=[a['x'],a['y'],a['z']];self.visited.add((math.floor(a['x']),math.floor(a['z'])))
  found=[r for r in raw['found'] if r['nutrition']>0 and r['sicknessProbability']==0]
  self.resources=sorted(found,key=lambda r:math.dist(pos,[r['x']+.5,r['y'],r['z']+.5]))[:8]
  resource_info=[]
  for i,r in enumerate(self.resources):
   target=[r['x'],r['y'],r['z']];route=self.route_to(raw,s,target);resource_info.append({'resource_index':i,'kind':'mature_pumpkin','distance':round(math.dist(pos,[r['x']+.5,r['y'],r['z']+.5]),2),'safe_food':True,'route_status':route['status'],'adjacent':route['status']=='adjacent_to_food_stop_and_inspect'})
  ex=explore(self.normalized(dict(raw,found=[])),pos,self.visited)
  obs={'health':s['health'],'air':s['air'],'food':round(s['vitals']['food'],4),'naturally_acquired_pumpkins':self.acquired,'starter_food_excluded':True,'resources':resource_info,'safe_exploration_available':ex['status']=='candidate','last_result':None if self.last_result is None else {k:self.last_result[k] for k in ('status','reason','waypoints_reached','final_stop_ack','do_not_retry') if k in self.last_result}}
  obs['available_actions']=['inspect_food','stop']
  if s['health']==1 and s['air']==1:
   if ex['status']=='candidate':obs['available_actions'].append('explore_safe_ground')
   if any(r['route_status'].startswith('candidate') for r in resource_info):obs['available_actions'].append('approach_food')
   if any(r['adjacent'] for r in resource_info):obs['available_actions'].append('gather_food')
   if self.acquired>0 and not self.eat_uncertain and s['vitals']['food']<.98:obs['available_actions'].append('eat')
  return obs
 def move_route(self,raw,route):
  if not route['status'].startswith('candidate'):return {'status':'rejected','reason':route['status']}
  route['source_file_mtime_unix']=raw['_mtime'];path=self.logs/f'route-{self.seq}.json';path.write_text(json.dumps(route,indent=2));name=f'{self.logs.name}/move-{self.seq}.json'
  subprocess.run([sys.executable,str(ROOT/'execute_observed_route.py'),str(path),name,'ipc-wet'],check=True,capture_output=True,text=True)
  result=json.loads((ROOT/name).read_text())
  for w in result['waypoints']:
   for sample in w['samples']:
    a=sample['position'];self.visited.add((math.floor(a['x']),math.floor(a['z'])))
  if result['outcome'] in ('stalled','drop_transition_headroom_blocked'):
   g=result['waypoints'][-1]['goal'];self.blocked.add((math.floor(g[0]),math.floor(g[2])))
  return {'status':result['outcome'],'waypoints_reached':sum(w.get('outcome')=='reached' for w in result['waypoints']),'stop_ack':result['stop_ack']}
 def gather(self,target):
  s=self.state()['state'];origin=s['position'];before=sum(v['count'] for v in s['slots'] if v['value']&1023==131)
  def cell(s):return next((v['value']&1023 for v in s['cells'] if (v['x'],v['y'],v['z'])==tuple(target)),None)
  if cell(s)!=131:return {'status':'rejected','reason':'target_not_in_local_observation'}
  if abs(math.floor(origin['x'])-target[0])+abs(math.floor(origin['z'])-target[2])!=1:return {'status':'rejected','reason':'not_adjacent'}
  end=time.monotonic()+6
  while time.monotonic()<end:
   s=self.state()['state'];self.safe(s);a=s['position']
   if math.dist(list(a.values()),list(origin.values()))>.75:raise RuntimeError('gather_position_guard')
   if cell(s)!=131:break
   self.command('dig',X=target[0]+.5-a['x'],Y=target[1]+.5-(a['y']+1.6),Z=target[2]+.5-a['z']);time.sleep(.25)
  ack=self.stop();end=time.monotonic()+2
  while time.monotonic()<end:
   s=self.state()['state'];count=sum(v['count'] for v in s['slots'] if v['value']&1023==131)
   if count>before:break
   time.sleep(.1)
  gain=count-before
  if cell(s)==0 and gain>0:self.acquired+=gain;return {'status':'gathered','inventory_gain':gain,'stop_ack':ack}
  return {'status':'gather_not_verified','inventory_gain':gain,'stop_ack':ack}
 def eat(self):
  s=self.state()['state'];self.safe(s)
  if self.eat_uncertain:return {'status':'rejected','reason':'prior_eat_uncertain_no_retry'}
  if self.acquired<=0:return {'status':'rejected','reason':'no_session_verified_natural_food'}
  slots=[v for v in s['slots'] if v['value']&1023==131 and v['count']>0 and v['nutrition']>0 and v['sicknessProbability']==0]
  if not slots or s['vitals']['food']>=.98:return {'status':'rejected','reason':'not_safe_to_eat'}
  v=slots[0];c=self.command('eat',Slot=v['slot'],ExpectedValue=v['value'],ExpectedCount=v['count']);end=time.monotonic()+4;result=None
  while time.monotonic()<end:
   path=self.d/'eat-results.jsonl'
   if path.exists():result=next((j for line in path.read_text().splitlines() if (j:=json.loads(line))['id']==c['Id']),None)
   if result:break
   time.sleep(.1)
  if result and not result['error'] and result['afterFood']>result['beforeFood'] and result['afterCount']==result['beforeCount']-1:
   self.acquired-=1;end=time.monotonic()+3
   while time.monotonic()<end:
    s=self.state()['state'];now=s['slots'][v['slot']]
    if now['count']==result['afterCount'] and s['vitals']['food']>result['beforeFood']:return {'status':'ate_natural_food','food_before':result['beforeFood'],'food_after':result['afterFood'],'fresh_state_verified':True}
    time.sleep(.1)
   self.eat_uncertain=True
   return {'status':'ate_transaction_state_pending','do_not_retry':True}
  self.eat_uncertain=True
  return {'status':'eat_uncertain_or_rejected','transaction':result,'do_not_retry':True}
 def execute(self,action,resource_index=0):
  self.seq+=1;result=None
  try:
   if action=='stop':result={'status':'stopped','stop_ack':self.stop()}
   elif action=='inspect_food':result={'status':'inspected','observation':self.observe()}
   elif action=='eat':result=self.eat()
   elif action in ('approach_food','gather_food','explore_safe_ground'):
    target=None
    if action!='explore_safe_ground':
     if type(resource_index)!=int or not 0<=resource_index<len(self.resources):raise RuntimeError('invalid_resource_index')
     r=self.resources[resource_index];target=[r['x'],r['y'],r['z']]
    raw=self.scan();s=self.state()['state'];self.safe(s)
    if target and not any([r['x'],r['y'],r['z']]==target and r['nutrition']>0 and r['sicknessProbability']==0 for r in raw['found']):raise RuntimeError('resource_changed')
    if action=='gather_food':result=self.gather(target)
    else:
     route=explore(self.normalized(dict(raw,found=[])),list(s['position'].values()),self.visited) if action=='explore_safe_ground' else self.route_to(raw,s,target)
     result=self.move_route(raw,route)
   else:result={'status':'rejected','reason':'unknown_action'}
  except Exception as e:result={'status':'failed','reason':str(e)}
  finally:
   if action!='stop':
    try:result['final_stop_ack']=self.stop()
    except Exception as e:result['final_stop_error']=str(e)
  self.last_result=result;(self.logs/f'result-{self.seq}.json').write_text(json.dumps({'chosen_action':action,'resource_index':resource_index,'result':result},indent=2));return result
