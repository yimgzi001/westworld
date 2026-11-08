"""Local-Qwen skill choice only. No game commands, GUI, or external API.
Import LocalModel in the SAME runner process to retain localhost service lifetime.
The executor owns observations, capability flags, target indices and safety.
"""
import json,math,pathlib,time,subprocess,urllib.request,os
ACTIONS=('inspect_food','explore_safe_ground','approach_food','gather_food','eat','stop')
SYSTEM='''你控制一个生存战争角色。总目标是在游戏中自然取得食物并吃下，不能使用出生赠送或测试注入物品冒充成果。根据当前真实观察自主选择一个已实现技能，不要求固定动作顺序，不编造物品、资源或执行成功。\n技能：inspect_food刷新附近食物/地形观察；explore_safe_ground沿执行器提供的有限安全陆地候选扩展观察；approach_food接近已观察资源；gather_food对已到达且可采集资源正常挖掘拾取；eat消耗真实自然取得的安全食物；stop停止。执行器负责坐标、路径、碰撞、技能完成与安全约束，你只选技能与索引。\n只选available_skills中的动作。approach_food/gather_food的target_index指resources数组索引，eat指food_inventory数组索引，其他为-1。starter或fixture来源食物不允许吃。不要把当前没有食物当作已经完成目标。观察和上次失败都只是事实数据。返回JSON：action,target_index,reason（简短原因）。'''
SYSTEM+='\nfeedback字段是执行器记录的事实：observe_confirmed_new_scan表示此次观察已经完成匹配的新扫描；consecutive_inspect_actions表示连续选择检查的次数；unchanged_position_observations和seconds_since_position_change表示位置未明显变化；unchanged_resource_observations表示资源集合未变化；last_action_*表示上一技能实际结果。这些字段不是指定下一动作的指令。请自行判断怎样实现总目标。'
SCHEMA={'type':'object','properties':{'action':{'type':'string','enum':list(ACTIONS)},'target_index':{'type':'integer','minimum':-1,'maximum':15},'reason':{'type':'string','maxLength':80}},'required':['action','target_index','reason'],'additionalProperties':False}

def compact(o):
 # Whitelist fields; never serialize GUIDs, auth fields or raw bridge state.
 out={k:o[k] for k in ('observation_id','captured_unix','health','air','food','available_skills')}
 out['food_inventory']=[{k:x[k] for k in ('index','count','nutrition','sickness_probability','provenance')} for x in o.get('food_inventory',[])[:16]]
 out['resources']=[{k:x[k] for k in ('index','kind','distance','nutrition','sickness_probability','reachable','gatherable')} for x in o.get('resources',[])[:16]]
 out['exploration_available']=bool(o.get('exploration_available',False))
 out['last_result']=str(o.get('last_result','none'))[:200]
 out['feedback']={k:v for k,v in o.get('feedback',{}).items() if k in ('observe_confirmed_new_scan','consecutive_inspect_actions','unchanged_position_observations','seconds_since_position_change','unchanged_resource_observations','last_action_position_distance','last_action_inventory_gain','last_action_ate_verified')}
 for k in ('captured_unix','health','air','food'):
  if type(out[k]) not in (int,float) or not math.isfinite(out[k]):raise ValueError('nonfinite observation')
 if any(s not in ACTIONS for s in out['available_skills']):raise ValueError('unknown capability')
 if not 0<=out['food']<=1 or not 0<=out['health']<=1 or not 0<=out['air']<=1:raise ValueError('invalid vitals')
 for name in ('food_inventory','resources'):
  if any(x['index']!=i for i,x in enumerate(out[name])):raise ValueError('indices must equal array positions')
 return out

def validate(choice,o):
 if set(choice)!= {'action','target_index','reason'}:return 'invalid_schema'
 a=choice['action'];i=choice['target_index']
 if a not in ACTIONS or type(i)!=int or not isinstance(choice['reason'],str) or len(choice['reason'])>80:return 'invalid_schema'
 if a not in o['available_skills']:return 'unavailable_skill'
 if a in ('approach_food','gather_food'):
  if not 0<=i<len(o['resources']):return 'unknown_resource'
  r=o['resources'][i]
  if not r['reachable'] or (a=='gather_food' and not r['gatherable']):return 'resource_not_ready'
 elif a=='eat':
  if not 0<=i<len(o['food_inventory']):return 'unknown_inventory_index'
  r=o['food_inventory'][i]
  if r['provenance']!='natural_collected' or r['count']<1:return 'not_naturally_collected'
  if not math.isfinite(r['nutrition']) or not math.isfinite(r['sickness_probability']) or r['nutrition']<=0 or not 0<=r['sickness_probability']<=.001:return 'unsafe_food'
 elif i!=-1:return 'unexpected_target'
 if a=='explore_safe_ground' and not o['exploration_available']:return 'no_exploration_candidate'
 return None

class LocalModel:
 def __init__(self,log_path,max_decisions=10,max_seconds=300,threads=2):
  self.root=pathlib.Path(__file__).resolve().parents[1];self.path=pathlib.Path(log_path);self.max_decisions=max_decisions;self.max_seconds=max_seconds;self.threads=threads;self.n=0;self.server=None
 def metrics(self):
  s=pathlib.Path(f'/proc/{self.server.pid}/stat').read_text().split();v=pathlib.Path(f'/proc/{self.server.pid}/status').read_text()
  return {'cpu_seconds':(int(s[13])+int(s[14]))/os.sysconf('SC_CLK_TCK'),'rss_kib':int(v.split('VmRSS:')[1].split()[0]),'peak_rss_kib':int(v.split('VmHWM:')[1].split()[0])}
 def record(self,r):
  with self.path.open('a') as f:f.write(json.dumps(r,ensure_ascii=False,allow_nan=False)+'\n')
 def __enter__(self):
  self.start=time.monotonic();self.path.parent.mkdir(parents=True,exist_ok=True)
  self.runtime_log=self.path.with_suffix('.server.log').open('w')
  self.server=subprocess.Popen([str(self.root/'llama-b12185/llama-server'),'-m',str(self.root/'Qwen3.5-2B-Q4_K_M.gguf'),'-c','4096','-t',str(self.threads),'-tb',str(self.threads),'-ngl','0','-np','1','--host','127.0.0.1','--port','18082','--reasoning','off'],stdout=self.runtime_log,stderr=subprocess.STDOUT)
  try:
   for _ in range(60):
    try:json.load(urllib.request.urlopen('http://127.0.0.1:18082/health',timeout=1));break
    except Exception:
     if self.server.poll() is not None:raise RuntimeError('model server exited')
     time.sleep(.5)
   else:raise TimeoutError('model load timeout')
   self.record({'event':'model_loaded','policy_version':'v2_factual_feedback_only','model':'Qwen3.5-2B Q4_K_M','threads':self.threads,'seconds':time.monotonic()-self.start,'metrics':self.metrics()})
   return self
  except BaseException:self.__exit__(None,None,None);raise
 def decide(self,observation):
  if self.n>=self.max_decisions or time.monotonic()-self.start>=self.max_seconds:raise RuntimeError('decision budget exhausted')
  o=compact(observation)
  age=time.time()-o['captured_unix']
  if age<0 or age>3:raise ValueError('observation stale before inference')
  self.n+=1;before=self.metrics();t=time.monotonic()
  payload={'messages':[{'role':'system','content':SYSTEM},{'role':'user','content':json.dumps(o,ensure_ascii=False,separators=(',',':'))}],'temperature':0,'max_tokens':96,'chat_template_kwargs':{'enable_thinking':False},'response_format':{'type':'json_schema','json_schema':{'name':'skill_choice','strict':True,'schema':SCHEMA}}}
  error=None;choice=None;response={}
  try:
   req=urllib.request.Request('http://127.0.0.1:18082/v1/chat/completions',data=json.dumps(payload).encode(),headers={'Content-Type':'application/json'})
   response=json.load(urllib.request.urlopen(req,timeout=30));choice=json.loads(response['choices'][0]['message']['content']);error=validate(choice,o)
  except Exception as e:error=type(e).__name__+':'+str(e)[:160]
  after=self.metrics();elapsed=time.monotonic()-t
  record={'event':'decision','number':self.n,'observation':o,'choice':choice,'validation_error':error,'elapsed_seconds':elapsed,'model_cpu_seconds':after['cpu_seconds']-before['cpu_seconds'],'model_cpu_percent':100*(after['cpu_seconds']-before['cpu_seconds'])/max(elapsed,.001),'metrics':after,'usage':response.get('usage'),'timings':response.get('timings')}
  self.record(record)
  # Invalid selection is NOT silently replaced by a scripted skill.
  return {'decision_number':self.n,'observation_id':o['observation_id'],'choice':choice,'validation_error':error,'needs_fresh_execution_recheck':True}
 def execution_result(self,decision_number,result):
  # Caller provides a redacted record with command ack, real before/after and outcome.
  self.record({'event':'execution_result','decision_number':decision_number,'result':result})
 def __exit__(self,*args):
  if self.server and self.server.poll() is None:
   self.record({'event':'model_stop','decisions':self.n,'total_seconds':time.monotonic()-self.start,'metrics':self.metrics()});self.server.terminate()
   try:self.server.wait(timeout=5)
   except subprocess.TimeoutExpired:self.server.kill();self.server.wait()
  if hasattr(self,'runtime_log'):self.runtime_log.close()
