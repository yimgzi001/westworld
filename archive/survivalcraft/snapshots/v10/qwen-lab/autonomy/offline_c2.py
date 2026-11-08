"""Two offline calls, same C prompt/state, explicit discriminated schema vC2."""
import json,pathlib,time,urllib.request
from decision_v2 import LocalModel,validate
root=pathlib.Path(__file__).resolve().parent
ab=json.loads((root/'offline-ab-results.json').read_text());v=ab['variants']['C_concise_prompt_minimal_state'];obs=ab['variants']['A_existing_prompt_existing_state']['state']
def branch(actions,index):return {'type':'object','properties':{'action':{'type':'string','enum':actions},'target_index':index,'reason':{'type':'string','maxLength':80}},'required':['action','target_index','reason'],'additionalProperties':False}
schema={'anyOf':[branch(['inspect_food','explore_safe_ground','stop'],{'type':'integer','enum':[-1]}),branch(['approach_food','gather_food','eat'],{'type':'integer','minimum':0,'maximum':15})]}
out={'version':'C2_schema_discriminated_no_posthoc_correction','offline_stale_snapshot':True,'system':v['system'],'state':v['state'],'schema':schema,'temperature':0,'calls':[]};path=root/'offline-c2-results.json';path.write_text(json.dumps(out,ensure_ascii=False,indent=2))
with LocalModel(root/'offline-c2-runtime.jsonl',max_decisions=2,threads=2) as model:
 for i in range(2):
  model.n+=1
  p={'messages':[{'role':'system','content':v['system']},{'role':'user','content':json.dumps(v['state'],ensure_ascii=False,separators=(',',':'))}],'temperature':0,'max_tokens':96,'chat_template_kwargs':{'enable_thinking':False},'response_format':{'type':'json_schema','json_schema':{'name':'skill_choice_c2','strict':True,'schema':schema}}}
  before=model.metrics();t=time.monotonic();error=None;r={};choice=None
  try:
   req=urllib.request.Request('http://127.0.0.1:18082/v1/chat/completions',data=json.dumps(p).encode(),headers={'Content-Type':'application/json'});r=json.load(urllib.request.urlopen(req,timeout=30));choice=json.loads(r['choices'][0]['message']['content']);error=validate(choice,obs)
  except Exception as e:error=type(e).__name__+':'+str(e)[:180]
  after=model.metrics();item={'call':i+1,'choice':choice,'validation_error':error,'seconds':time.monotonic()-t,'cpu_seconds':after['cpu_seconds']-before['cpu_seconds'],'metrics':after,'response':r};out['calls'].append(item);path.write_text(json.dumps(out,ensure_ascii=False,indent=2));print(json.dumps({k:item[k] for k in ('call','choice','validation_error','seconds','cpu_seconds')},ensure_ascii=False),flush=True)
print('C2 offline done, model exited, no game actions')
