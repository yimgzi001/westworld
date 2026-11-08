import pathlib,json,time,sys,math
p=pathlib.Path(__file__).resolve().parent;d=p/'ipc-bot';sys.path.insert(0,str(p/'Bridge'));from controller import issue
f=next(f for f in d.glob('*.state.json') if json.loads(f.read_text()).get('playerAlias')=='LabBot35');pid=f.name.removesuffix('.state.json')
def read():return json.loads(f.read_text())
def inv(s):return sum(v['count'] for v in s['slots'] if v['value']&1023==7)
def cell(s,t):return next((b['value']&1023 for b in s['cells'] if (b['x'],b['y'],b['z'])==t),None)
before=read();origin=before['state']['position'];out={'alias':'LabBot35','mode':'bounded scripted gather, no model','initial_sand':inv(before['state']),'targets':[]};laststop=None
try:
 for target in [(567,65,-142),(568,65,-141),(567,65,-141)]:
  start=read();assert cell(start['state'],target)==7
  events=[];deadline=time.monotonic()+6;ack=False
  while time.monotonic()<deadline:
   j=read();s=j['state'];pos=s['position']
   if time.time()-f.stat().st_mtime>2 or s['health']<1 or s['air']<.9 or math.dist(list(origin.values()),list(pos.values()))>.75:raise RuntimeError('safety guard')
   if cell(s,target)!=7:break
   cmd=issue(d,pid,'dig',x=target[0]+.5-pos['x'],y=target[1]+.5-(pos['y']+1.6),z=target[2]+.5-pos['z'],seconds=.3)
   time.sleep(.25);n=read();ack|=n['lastCommand']==cmd['Id'];events.append({'tick':n['tickSeconds'],'ack':n['lastCommand']==cmd['Id'],'sand':inv(n['state'])})
  laststop=issue(d,pid,'stop');time.sleep(.7);end=read();out['targets'].append({'cell':target,'before':7,'after':cell(end['state'],target),'sand_before':inv(start['state']),'sand_after':inv(end['state']),'observed_dig_ack':ack,'events':events})
  if cell(end['state'],target)==7:break
except Exception as e:out['error']=str(e)
finally:
 laststop=issue(d,pid,'stop');deadline=time.monotonic()+5
 while time.monotonic()<deadline:
  last=read()
  if last['lastCommand']==laststop['Id']:break
  time.sleep(.1)
 out['stop_ack']=last['lastCommand']==laststop['Id'];out['final_sand']=inv(last['state']);out['final_health']=last['state']['health'];out['final_air']=last['state']['air'];out['final_position']=last['state']['position']
 (p/'continuous-gather-results.json').write_text(json.dumps(out,indent=2));print(json.dumps(out))
