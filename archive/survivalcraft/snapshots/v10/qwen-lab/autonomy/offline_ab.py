"""6-call offline representation/prompt A/B. Frozen stale observation. NO gateway."""
import pathlib,json,time,urllib.request
from decision_v2 import LocalModel,SYSTEM,SCHEMA,validate
root=pathlib.Path(__file__).resolve().parent
source=root/'qwen-autonomous-v2-20261107-140307.jsonl'
records=[json.loads(l) for l in source.read_text().splitlines()]
obs=[r['observation'] for r in records if r['event']=='decision'][-1]
# Preserve the factual task and available choices. Remove no available skill.
short={'offline_stale_snapshot':True,'goal':'Naturally collect food and eat it. Do not eat starter or fixture items.','health':obs['health'],'air':obs['air'],'hunger_food_level':obs['food'],'natural_food_items':0,'excluded_starter_items':sum(x['count'] for x in obs['food_inventory'] if x['provenance']=='starter'),'resources':obs['resources'],'available_skills':obs['available_skills'],'safe_exploration_available':obs['exploration_available'],'fresh_scan_already_done_in_snapshot':True,'consecutive_checks_without_new_resources':obs['feedback']['consecutive_inspect_actions'],'seconds_stationary':obs['feedback']['seconds_since_position_change'],'previous_action':'inspect_food','previous_result':'No resources discovered; no movement; no food collected or eaten.'}
english='''Choose one skill to achieve natural food collection and eating in Survivalcraft. This is an OFFLINE replay of a stale real observation: do not claim any action happened. Choose what the next skill would be at that snapshot. Available skills are listed in the observation; do not invent items or coordinates. Never eat starter/fixture items. Skills: inspect_food rescans the current location; explore_safe_ground follows a bounded safe route to another observed land location; approach_food approaches a known food resource; gather_food collects a reachable resource; eat consumes a verified naturally collected safe item; stop does nothing. Select action, target_index (-1 for inspect/explore/stop; otherwise index in the relevant array), and a short reason. All listed available skills remain permissible. The decision is yours.'''
variants={'A_existing_prompt_existing_state':{'system':SYSTEM,'state':dict(obs,offline_stale_snapshot=True)},'B_concise_prompt_same_state':{'system':english,'state':dict(obs,offline_stale_snapshot=True)},'C_concise_prompt_minimal_state':{'system':english,'state':short}}
meta={'source':source.name,'offline_only':True,'stale_snapshot':True,'snapshot_not_refreshed':True,'variants':variants,'temperature':0,'calls':[]}
out=root/'offline-ab-results.json';out.write_text(json.dumps(meta,ensure_ascii=False,indent=2))
with LocalModel(root/'offline-ab-runtime.jsonl',max_decisions=6,threads=2) as model:
 # Alternate order on repeat to expose caching/order confounding. This is not an iid quality estimate.
 for name in ['A_existing_prompt_existing_state','B_concise_prompt_same_state','C_concise_prompt_minimal_state','C_concise_prompt_minimal_state','B_concise_prompt_same_state','A_existing_prompt_existing_state']:
  v=variants[name];messages=[{'role':'system','content':v['system']},{'role':'user','content':json.dumps(v['state'],ensure_ascii=False,separators=(',',':'))}]
  payload={'messages':messages,'temperature':0,'max_tokens':96,'chat_template_kwargs':{'enable_thinking':False},'response_format':{'type':'json_schema','json_schema':{'name':'skill_choice','strict':True,'schema':SCHEMA}}}
  before=model.metrics();t=time.monotonic();error=None;response={};choice=None
  try:
   req=urllib.request.Request('http://127.0.0.1:18082/v1/chat/completions',data=json.dumps(payload).encode(),headers={'Content-Type':'application/json'})
   response=json.load(urllib.request.urlopen(req,timeout=30));choice=json.loads(response['choices'][0]['message']['content']);error=validate(choice,obs)
  except Exception as e:error=type(e).__name__+':'+str(e)[:200]
  after=model.metrics();item={'call':len(meta['calls'])+1,'variant':name,'choice':choice,'validation_error':error,'seconds':time.monotonic()-t,'cpu_seconds':after['cpu_seconds']-before['cpu_seconds'],'metrics':after,'response':response};meta['calls'].append(item);out.write_text(json.dumps(meta,ensure_ascii=False,indent=2));print(json.dumps({k:item[k] for k in ('call','variant','choice','validation_error','seconds','cpu_seconds')},ensure_ascii=False),flush=True)
print('OFFLINE COMPLETE; no gateway imported, no game actions; model exited',flush=True)
