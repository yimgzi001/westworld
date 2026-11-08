"""Bounded real observation -> local model choice -> existing safe gateway.
No scripted skill sequence and no direct game IPC writes.
"""
import sys,pathlib,time,json,math
from decision_v2 import LocalModel
ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'survivalcraft-lab'))
from model_skill_gateway import Gateway
run='qwen-autonomous-v2-'+time.strftime('%Y%m%d-%H%M%S',time.gmtime())
gateway=Gateway(run);log=ROOT/'qwen-lab/autonomy'/f'{run}.jsonl';start=time.monotonic();outcomes=[];guard=None;final_stop=False

tracker={'position':None,'changed_at':time.monotonic(),'same_position':0,'resources':None,'same_resources':0,'inspects':0,'last_distance':0,'last_gain':0,'last_ate':False}

def adapt(obs,number):
 state=gateway.state()['state'];foods=[];remaining=gateway.acquired
 pos=tuple(state['position'][k] for k in ('x','y','z'));now=time.monotonic()
 if tracker['position'] is not None and math.dist(pos,tracker['position'])<.1:tracker['same_position']+=1
 else:tracker['same_position']=0;tracker['changed_at']=now
 tracker['position']=pos
 signature=tuple(sorted((r['x'],r['y'],r['z'],r['value']) for r in gateway.resources))
 if signature==tracker['resources']:tracker['same_resources']+=1
 else:tracker['same_resources']=0
 tracker['resources']=signature
 feedback={'observe_confirmed_new_scan':True,'consecutive_inspect_actions':tracker['inspects'],'unchanged_position_observations':tracker['same_position'],'seconds_since_position_change':round(now-tracker['changed_at'],2),'unchanged_resource_observations':tracker['same_resources'],'last_action_position_distance':round(tracker['last_distance'],3),'last_action_inventory_gain':tracker['last_gain'],'last_action_ate_verified':tracker['last_ate']}
 for s in state['slots']:
  if s['count']<=0 or s['nutrition']<=0:continue
  natural=(s['value']&1023)==131 and remaining>0
  count=min(s['count'],remaining) if natural else s['count']
  if natural:remaining-=count
  provenance='natural_collected' if natural else 'starter' if (s['value']&1023)==162 else 'unverified'
  foods.append({'index':len(foods),'count':count,'nutrition':s['nutrition'],'sickness_probability':s['sicknessProbability'],'provenance':provenance})
 resources=[]
 for r in obs['resources']:
  native=gateway.resources[r['resource_index']]
  resources.append({'index':r['resource_index'],'kind':r['kind'],'distance':r['distance'],'nutrition':native['nutrition'],'sickness_probability':native['sicknessProbability'],'reachable':r['route_status'].startswith('candidate') or r['adjacent'],'gatherable':r['adjacent']})
 last=obs.get('last_result') or {}
 return {'feedback':feedback,'observation_id':number,'captured_unix':gateway.f.stat().st_mtime,'health':state['health'],'air':state['air'],'food':state['vitals']['food'],'available_skills':obs['available_actions'],'food_inventory':foods,'resources':resources,'exploration_available':obs['safe_exploration_available'],'last_result':json.dumps({k:last[k] for k in ('status','reason','waypoints_reached','inventory_gain','fresh_state_verified','do_not_retry') if k in last},ensure_ascii=False)}
try:
 with LocalModel(log,max_decisions=10,max_seconds=300,threads=2) as model:
  for number in range(1,11):
   if time.monotonic()-start>240:guard='remaining_wall_budget';break
   obs=gateway.observe();clean=adapt(obs,number)
   if clean['health']<1 or clean['air']<1:guard='health_or_air';break
   decision=model.decide(clean);print(json.dumps({'number':number,'decision':decision},ensure_ascii=False),flush=True)
   if decision['validation_error']:
    result={'status':'model_selection_rejected','reason':decision['validation_error']};model.execution_result(number,result);outcomes.append(result);continue
   if time.monotonic()-start>250:guard='execution_budget_reserve';break
   choice=decision['choice'];before=gateway.state()['state'];beg=time.monotonic()
   result=gateway.execute(choice['action'],choice['target_index'])
   after=gateway.state()['state']
   tracker['inspects']=tracker['inspects']+1 if choice['action']=='inspect_food' else 0
   tracker['last_distance']=math.dist(tuple(before['position'][k] for k in ('x','y','z')),tuple(after['position'][k] for k in ('x','y','z')))
   tracker['last_gain']=result.get('inventory_gain',0);tracker['last_ate']=result.get('status')=='ate_natural_food'
   record={'chosen_action':choice['action'],'target_index':choice['target_index'],'wall_seconds':time.monotonic()-beg,'before':{'position':before['position'],'health':before['health'],'air':before['air'],'food':before['vitals']['food']},'after':{'position':after['position'],'health':after['health'],'air':after['air'],'food':after['vitals']['food']},'gateway_result':result}
   # inspect result embeds another observation but never raw bridge player credentials.
   model.execution_result(number,record);outcomes.append(record);print(json.dumps({'number':number,'action':choice['action'],'result':{k:result[k] for k in ('status','reason','waypoints_reached','inventory_gain','fresh_state_verified','final_stop_ack') if k in result},'seconds':record['wall_seconds']},ensure_ascii=False),flush=True)
   if choice['action']=='stop':guard='model_chose_stop';break
   if result.get('status')=='ate_natural_food':guard='goal_verified';break
   if gateway.eat_uncertain:guard='eat_uncertain';break
   if result.get('final_stop_ack') is False or result.get('final_stop_error'):guard='stop_unconfirmed';break
except Exception as e:
 guard='exception:'+type(e).__name__+':'+str(e)[:180]
finally:
 try:final_stop=gateway.stop()
 except Exception as e:guard=(guard or '')+';final_stop_error:'+str(e)[:100]
 summary={'run':run,'model_log':str(log.relative_to(ROOT)),'gateway_logs':str(gateway.logs.relative_to(ROOT)),'wall_seconds':time.monotonic()-start,'decisions_or_results':len(outcomes),'end_reason':guard or 'decision_count_limit','final_stop_ack':final_stop,'natural_acquired_remaining':gateway.acquired,'eat_uncertain':gateway.eat_uncertain}
 (log.with_suffix('.summary.json')).write_text(json.dumps(summary,ensure_ascii=False,indent=2));print(json.dumps(summary,ensure_ascii=False),flush=True)
