"""Two real game actors, one shared LOCAL Qwen service; no cloud API."""
import pathlib,sys,json,time,math,subprocess,urllib.request,threading,importlib.util
from controller import Controller,State,Plan,PlannerPool
lab=pathlib.Path(__file__).resolve().parents[1]; root=lab.parent; ipc=root/'survivalcraft-lab/ipc-bot'
spec=importlib.util.spec_from_file_location('bridge_transport',root/'survivalcraft-lab/Bridge/controller.py');bridge=importlib.util.module_from_spec(spec);spec.loader.exec_module(bridge)
actors={}
for f in ipc.glob('*.state.json'):
 d=json.load(open(f));alias=d.get('playerAlias')
 if alias in ('LabBot1','LabBot2') and time.time()-f.stat().st_mtime<2:
  assert d['state']['health']==1
  actors[alias]={'file':f,'player':f.name.removesuffix('.state.json'),'origin':d['state']['position'],'last_tick':None,'observed':time.monotonic(),'commands':[],'samples':[],'requests':0}
assert len(actors)==2,'Require two fresh healthy actors'
logs=[];started=time.monotonic();server=None;pool=None
schema={'type':'object','properties':{'action':{'type':'string','enum':['forward','backward','stop']}},'required':['action'],'additionalProperties':False}
def planner(state):
 a=actors[state.alias]; n=a['requests'];a['requests']+=1;goal='向前短步' if n%2==0 else '向后短步'
 prompt='根据目标选动作。forward=向前，backward=向后，stop=停止。只返回action字段。目标：'+goal+'。状态：'+json.dumps(state.observation,separators=(',',':'))
 data={'messages':[{'role':'user','content':prompt}],'temperature':0,'max_tokens':20,'chat_template_kwargs':{'enable_thinking':False},'response_format':{'type':'json_schema','json_schema':{'name':'action','strict':True,'schema':schema}}}
 begin=time.monotonic();req=urllib.request.Request('http://127.0.0.1:18082/v1/chat/completions',data=json.dumps(data).encode(),headers={'Content-Type':'application/json'})
 r=json.load(urllib.request.urlopen(req,timeout=20));result=json.loads(r['choices'][0]['message']['content'])['action'];assert result in ('forward','backward','stop')
 logs.append({'alias':state.alias,'goal':goal,'output':result,'started':begin-started,'model_seconds':time.monotonic()-begin,'observation_age_at_return':time.monotonic()-state.observed_at,'usage':r['usage'],'timings':r.get('timings')})
 return Plan('stop' if result=='stop' else 'move',z={'forward':.1,'backward':-.1,'stop':0}[result],horizon=3)
def dist(a,b):return math.sqrt(sum((a[k]-b[k])**2 for k in ('x','y','z')))
try:
 server=subprocess.Popen([str(lab/'llama-b12185/llama-server'),'-m',str(lab/'Qwen3.5-2B-Q4_K_M.gguf'),'-c','2048','-t','2','-tb','2','-ngl','0','-np','1','--host','127.0.0.1','--port','18082','--reasoning','off'],stdout=open(lab/'continuous/local-two-server.log','w'),stderr=subprocess.STDOUT)
 for _ in range(60):
  try:json.load(urllib.request.urlopen('http://127.0.0.1:18082/health',timeout=1));break
  except Exception:
   if server.poll() is not None:raise RuntimeError('server died')
   time.sleep(.5)
 else:raise RuntimeError('model load timeout')
 pool=PlannerPool(planner,workers=1,capacity=2)
 for alias,a in actors.items():
  def sink(action,a=a):
   payload=bridge.issue(ipc,a['player'],**action);a['commands'].append({'at':time.monotonic()-started,'id':payload['Id'],'action':action})
  a['controller']=Controller(alias,pool,sink,refresh=.2,replan_after=.1)
 run=time.monotonic()
 while time.monotonic()-run<30:
  for alias,a in actors.items():
   now=time.monotonic();d=json.load(open(a['file']));s=d['state']
   if d['tickSeconds']!=a['last_tick']:a['last_tick']=d['tickSeconds'];a['observed']=now
   c=a['controller']
   if dist(s['position'],a['origin'])>2 or abs(s['position']['y']-a['origin']['y'])>.75:c.emergency(now,'range guard')
   c.tick(State(alias,a['observed'],s['health'],{'position':s['position'],'health':s['health']}),now)
   ids={v['id'] for v in a['commands']}
   a['samples'].append({'at':now-started,'tick':d['tickSeconds'],'position':s['position'],'health':s['health'],'ack_ours':d['lastCommand'] in ids,'mode':c.mode,'state_age':now-a['observed']})
  if any(a['controller'].emergency_latched for a in actors.values()):break
  time.sleep(.05)
finally:
 # Stop before waiting on in-flight inference; never allow late plans to actuate.
 for a in actors.values():a['final_stop']=bridge.issue(ipc,a['player'],'stop')['Id']
 deadline=time.monotonic()+20
 while time.monotonic()<deadline:
  for a in actors.values():a['stop_ack']=json.load(open(a['file']))['lastCommand']==a['final_stop']
  if all(a['stop_ack'] for a in actors.values()):break
  time.sleep(.1)
 if pool:pool.close()
 if server:
  status=pathlib.Path(f'/proc/{server.pid}/status').read_text();peak=int(status.split('VmHWM:')[1].split()[0]);server.terminate();server.wait(timeout=10)
 else:peak=None
 out={'mode':'local Qwen3.5-2B Q4_K_M, CPU 2 threads, 1 shared slot, 1 planner worker','wall_seconds':time.monotonic()-started,'model_peak_rss_kib':peak,'inferences':logs,'actors':{}}
 for alias,a in actors.items():
  out['actors'][alias]={'origin':a['origin'],'stop_ack':a.get('stop_ack'),'commands':[{k:v for k,v in x.items() if k!='id'} for x in a['commands']],'samples':a['samples'],'max_displacement':max((dist(x['position'],a['origin']) for x in a['samples']),default=0),'events':[(t-started,e) for t,e in a['controller'].events] if 'controller' in a else []}
 (lab/'continuous/local-two-results.json').write_text(json.dumps(out,ensure_ascii=False,indent=2))
 print(json.dumps({'inferences':len(logs),'wall_seconds':out['wall_seconds'],'peak_rss_kib':peak,'actors':{k:{'stop_ack':v['stop_ack'],'commands':len(v['commands']),'max_displacement':v['max_displacement'],'events':v['events']} for k,v in out['actors'].items()}},ensure_ascii=False),flush=True)
