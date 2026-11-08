import pathlib,json,time,os,math,sys
p=pathlib.Path(__file__).parent;d=p/'ipc-food';f=next(f for f in d.glob('*.state.json') if time.time()-f.stat().st_mtime<3);q=f.with_name(f.name.replace('.state.json','.command.json'))
def read():return json.loads(f.read_text())
def cmd(action,**kw):
 c=dict(Id=time.time_ns(),Action=action,Seconds=.4,**kw);t=q.with_suffix('.tmp');t.write_text(json.dumps(c));os.replace(t,q);return c
b=read();o=b['state']['position'];out={'mode':'bounded scripted exploration, no model','origin':o,'steps':[]};last=b
try:
 for i in range(24):
  last=read();s=last['state'];pos=s['position']
  if time.time()-f.stat().st_mtime>2 or s['health']<.8 or s['air']<.9 or o['y']-pos['y']>1.2 or math.dist(list(pos.values()),list(o.values()))>12:
   out['stop_reason']='safety_bound';break
  c=cmd('move',X=0,Y=0,Z=-.7,Jump=True);time.sleep(.6);last=read();out['steps'].append({'position':last['state']['position'],'health':last['state']['health'],'air':last['state']['air'],'ack':last['lastCommand']==c['Id']})
  if i%6==5:cmd('scanfood');time.sleep(.3);scan=json.loads((d/'food-scan.json').read_text());
  if (d/'food-scan.json').exists() and json.loads((d/'food-scan.json').read_text())['found']:
   out['stop_reason']='food_found';break
finally:
 c=cmd('stop');deadline=time.time()+5
 while time.time()<deadline:
  last=read()
  if last['lastCommand']==c['Id']:break
  time.sleep(.1)
 out['stop_ack']=last['lastCommand']==c['Id'];out['final']=last['state']['position'];(p/(sys.argv[1] if len(sys.argv)>1 else 'food-exploration-1.json')).write_text(json.dumps(out,indent=2));print(json.dumps(out))
