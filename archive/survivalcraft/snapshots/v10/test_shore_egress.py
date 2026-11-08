import pathlib,json,time,os,math
p=pathlib.Path(__file__).parent;d=p/'ipc-food';f=next(f for f in d.glob('*.state.json') if time.time()-f.stat().st_mtime<3);q=f.with_name(f.name.replace('.state.json','.command.json'));goal=None
def read():return json.loads(f.read_text())
def send(action,**kw):
 c=dict(Id=time.time_ns(),Action=action,Seconds=.3,**kw);t=q.with_suffix('.tmp');t.write_text(json.dumps(c));os.replace(t,q);return c
send('scanfood');time.sleep(.2);b=read();scan=json.loads((d/'food-scan.json').read_text());pos=b['state']['position'];land=min((v for v in scan['surface'] if v['type'] in ('SandBlock','GrassBlock') and v['solid'] and v['headroom']==2),key=lambda v:math.hypot(v['x']+.5-pos['x'],v['z']+.5-pos['z']));goal=(land['x']+.5,land['y']+1,land['z']+.5);assert math.hypot(goal[0]-pos['x'],goal[2]-pos['z'])<=6;origin=b['state']['position'];out={'mode':'bounded shore egress, normal movement only','origin':origin,'goal':goal,'steps':[]};deadline=time.monotonic()+10
try:
 while time.monotonic()<deadline:
  j=read();s=j['state'];a=s['position'];dx=goal[0]-a['x'];dz=goal[2]-a['z'];dist=math.hypot(dx,dz)
  if time.time()-f.stat().st_mtime>2 or s['health']<.9 or s['air']<.99 or math.dist(list(origin.values()),list(a.values()))>6:out['outcome']='safety_stop';break
  target=next((v for v in s['cells'] if (v['x'],v['y'],v['z'])==(land['x'],land['y'],land['z'])),None)
  if target is not None and target['value']!=land['value']:out['outcome']='target_changed';break
  if dist<.22 and abs(a['y']-goal[1])<.3:out['outcome']='reached';break
  rot=s['rotation'];y,w=rot['y'],rot['w'];right=(1-2*y*y,-2*y*w);forward=(-2*y*w,-(1-2*y*y));vx=dx/max(dist,.001)*.5;vz=dz/max(dist,.001)*.5
  c=send('move',X=vx*right[0]+vz*right[1],Y=0,Z=vx*forward[0]+vz*forward[1],Jump=a['y']<goal[1]-.1);time.sleep(.2);k=read();out['steps'].append({'position':k['state']['position'],'ack':k['lastCommand']==c['Id'],'health':k['state']['health'],'air':k['state']['air']})
 else:out['outcome']='deadline'
finally:
 c=send('stop');end=time.time()+5
 while time.time()<end:
  k=read()
  if k['lastCommand']==c['Id']:break
  time.sleep(.1)
 out['stop_ack']=k['lastCommand']==c['Id'];out['final']=k['state']['position'];(p/'shore-egress-results-v2.json').write_text(json.dumps(out,indent=2));print(json.dumps(out))
