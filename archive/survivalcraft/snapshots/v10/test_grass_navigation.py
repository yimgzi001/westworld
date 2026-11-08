import pathlib,json,time,os,math
p=pathlib.Path(__file__).parent;d=p/'ipc-food';f=next(f for f in d.glob('*.state.json') if time.time()-f.stat().st_mtime<3);q=f.with_name(f.name.replace('.state.json','.command.json'))
route=[(554.5,65,-150.5),(554.5,65,-149.5),(554.5,66,-148.5),(554.5,66,-147.5),(554.5,66,-146.5)]
def read():return json.loads(f.read_text())
def send(action,**kw):
 c=dict(Id=time.time_ns(),Action=action,Seconds=.3,**kw);t=q.with_suffix('.tmp');t.write_text(json.dumps(c));os.replace(t,q);return c
b=read();origin=b['state']['position'];out={'mode':'observed-map scripted navigation, no model or teleport','origin':origin,'waypoints':[]};deadline=time.monotonic()+30
try:
 for goal in route:
  step={'goal':goal,'samples':[]};out['waypoints'].append(step);best=999;improved=time.monotonic()
  while time.monotonic()<deadline:
   j=read();s=j['state'];a=s['position'];dist=math.hypot(goal[0]-a['x'],goal[2]-a['z'])
   if time.time()-f.stat().st_mtime>2 or s['health']<1 or s['air']<1 or math.dist(list(origin.values()),list(a.values()))>8:raise RuntimeError('safety_stop')
   block=next((v for v in s['cells'] if (v['x'],v['y'],v['z'])==(math.floor(goal[0]),goal[1]-1,math.floor(goal[2]))),None)
   if block is None or block['value']&1023 not in (7,8):raise RuntimeError('local_ground_changed_or_unobserved')
   if dist<.17 and abs(a['y']-goal[1])<.3:step['outcome']='reached';break
   if best-dist>.05:best=dist;improved=time.monotonic()
   if time.monotonic()-improved>4:raise RuntimeError('stalled')
   if goal[1]-a['y']>1.1:raise RuntimeError('step_too_high')
   y,w=s['rotation']['y'],s['rotation']['w'];right=(1-2*y*y,-2*y*w);forward=(-2*y*w,-(1-2*y*y));v=min(.5,dist*1.5);vx=(goal[0]-a['x'])/max(dist,.001)*v;vz=(goal[2]-a['z'])/max(dist,.001)*v
   c=send('move',X=vx*right[0]+vz*right[1],Y=0,Z=vx*forward[0]+vz*forward[1],Jump=a['y']<goal[1]-.1);time.sleep(.2);k=read();step['samples'].append({'position':k['state']['position'],'health':k['state']['health'],'air':k['state']['air'],'ack':k['lastCommand']==c['Id']})
  else:raise RuntimeError('deadline')
 out['outcome']='route_completed'
except Exception as e:out['outcome']=str(e)
finally:
 c=send('stop');end=time.time()+5
 while time.time()<end:
  k=read()
  if k['lastCommand']==c['Id']:break
  time.sleep(.1)
 out['stop_ack']=k['lastCommand']==c['Id'];out['final']=k['state']['position'];out['food']=k['state']['vitals']['food'];(p/'grass-navigation-results.json').write_text(json.dumps(out,indent=2));print(json.dumps({'outcome':out['outcome'],'waypoints_reached':sum(w.get('outcome')=='reached' for w in out['waypoints']),'final':out['final'],'stop_ack':out['stop_ack']}))
