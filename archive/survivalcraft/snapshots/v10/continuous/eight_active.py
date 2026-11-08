"""8 scripted actors in 50-actor world; no model. Read-only 42 controls."""
import pathlib,json,time,math,importlib.util
from skill import Skill
root=pathlib.Path(__file__).resolve().parents[2];ipc=root/'survivalcraft-lab/ipc-bot'
spec=importlib.util.spec_from_file_location('transport',root/'survivalcraft-lab/Bridge/controller.py');bridge=importlib.util.module_from_spec(spec);spec.loader.exec_module(bridge)
def dist(a,b):return math.sqrt(sum((a[k]-b[k])**2 for k in ('x','y','z')))
records={};start=time.monotonic()
for f in ipc.glob('*.state.json'):
 d=json.load(open(f));alias=d.get('playerAlias');s=d['state']
 if alias and time.time()-f.stat().st_mtime<2:records[alias]={'file':f,'player':f.name.removesuffix('.state.json'),'before':s,'last_tick':d['tickSeconds'],'observed':start}
assert len(records)==50
active={f'LabBot{i}':records[f'LabBot{i}'] for i in range(1,9)}
for alias,a in active.items():
 assert a['before']['health']==a['before']['air']==1
 a.update(skill=None,segments=[],sent=[],ids=set(),acked=set(),waiting=None,last_send=-999,unsafe=False,samples=[])
 def sink(action,a=a):
  payload=bridge.issue(ipc,a['player'],**action);a['ids'].add(payload['Id']);a['sent'].append({'at':time.monotonic()-start,'action':action});return payload['Id']
 a['sink']=sink
try:
 while time.monotonic()-start<17:
  for alias,a in active.items():
   now=time.monotonic();d=json.load(open(a['file']));s=d['state'];pos=s['position']
   if d['tickSeconds']!=a['last_tick']:a['last_tick']=d['tickSeconds'];a['observed']=now
   if d['lastCommand'] in a['ids']:a['acked'].add(d['lastCommand'])
   age=now-a['observed'];bad=s['health']<1 or s['air']<1 or age>2 or dist(pos,a['before']['position'])>1 or abs(pos['y']-a['before']['position']['y'])>.6
   if bad and not a['unsafe']:
    a['unsafe']=True;a['waiting']=a['sink']({'action':'stop','x':0,'y':0,'z':0,'seconds':.4})
   if a['waiting'] is not None and d['lastCommand']==a['waiting']:a['waiting']=None
   if not a['unsafe'] and len(a['segments'])<2 and a['waiting'] is None:
    if a['skill'] is None:
     first=int(alias.removeprefix('LabBot'))<=4;forward=first==(len(a['segments'])==0)
     a['skill']=Skill('forward' if forward else 'backward',dict(pos),d['tickSeconds'],now,target_distance=.15,max_game_seconds=.7,max_wall_seconds=6)
    skill=a['skill'];outcome=skill.evaluate(pos,d['tickSeconds'],now,s['health'],age)
    if outcome!='running':
     a['segments'].append({'direction':skill.direction,'result':outcome,'seconds':now-skill.start_wall,'game_seconds':d['tickSeconds']-skill.start_tick,'distance':dist(pos,skill.origin),'before':skill.origin,'after':pos})
     a['skill']=None;a['waiting']=a['sink']({'action':'stop','x':0,'y':0,'z':0,'seconds':.4})
    elif now-a['last_send']>=.2:
     a['sink']({'action':'move','x':0,'y':0,'z':.2 if skill.direction=='forward' else -.2,'seconds':.4});a['last_send']=now
   a['samples'].append({'at':now-start,'position':pos,'tick':d['tickSeconds'],'health':s['health'],'air':s['air'],'state_age':age})
  if all((len(a['segments'])==2 or a['unsafe']) and a['waiting'] is None for a in active.values()):break
  time.sleep(.05)
finally:
 for a in active.values():a['final']=a['sink']({'action':'stop','x':0,'y':0,'z':0,'seconds':.4})
 deadline=min(start+20,time.monotonic()+3)
 while time.monotonic()<deadline:
  for a in active.values():a['stop_ack']=json.load(open(a['file']))['lastCommand']==a['final']
  if all(a['stop_ack'] for a in active.values()):break
  time.sleep(.05)
 out={'kind':'scripted 8 actors, no model; 42 read-only controls','wall_seconds':time.monotonic()-start,'active':{},'controls':{}}
 for alias,a in records.items():
  after=json.load(open(a['file']))['state']
  if alias in active:
   out['active'][alias]={'segments':a['segments'],'sent_count':len(a['sent']),'unique_observed_command_acks':len(a['acked']),'final_stop_ack':a.get('stop_ack',False),'unsafe':a['unsafe'],'samples':a['samples'],'commands':a['sent'],'max_displacement':max(dist(v['position'],a['before']['position']) for v in a['samples'])}
  else:
   out['controls'][alias]={'distance':dist(after['position'],a['before']['position']),'health_before':a['before']['health'],'health_after':after['health'],'air_before':a['before']['air'],'air_after':after['air'],'commands_sent':0}
 pathlib.Path(__file__).with_name('eight-active-results.json').write_text(json.dumps(out,indent=2))
 print(json.dumps({'wall_seconds':out['wall_seconds'],'active':{k:{q:v[q] for q in ('sent_count','unique_observed_command_acks','final_stop_ack','unsafe','max_displacement')} for k,v in out['active'].items()},'control_count':len(out['controls']),'control_max_drift':max(v['distance'] for v in out['controls'].values())}),flush=True)
