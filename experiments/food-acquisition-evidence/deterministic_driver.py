#!/usr/bin/env python3
"""Trusted bounded fixture control, NOT a model or production survival policy.
Reads only public request.prompt OBSERVATION plus offered schemas/envelope. No host audit logs.
"""
import argparse,datetime,json,math,os,pathlib,time,uuid
IDS=('sessionId','actorId','observationId','requestId')
def safe(p):
 p=pathlib.Path(p).absolute()
 if any(x.is_symlink() for x in (p,*p.parents)):raise ValueError('symlink')
 return p

def read_request(path):
 path=safe(path)
 with path.open('rb') as f:raw=f.read(262145)
 if len(raw)>262144:raise ValueError('request_bound')
 p=json.loads(raw)
 if p.get('protocol')!='westworld-tool-file-v1':raise ValueError('protocol')
 for key in IDS:
  if not isinstance(p.get(key),str) or uuid.UUID(p[key]).int==0:raise ValueError('identity')
 if not isinstance(p.get('prompt'),str) or not isinstance(p.get('offers'),list):raise ValueError('public_request_required')
 return p

def valid(value,schema):
 if 'oneOf' in schema:return sum(valid(value,s) for s in schema['oneOf'])==1
 if 'enum' in schema and value not in schema['enum']:return False
 t=schema.get('type')
 if t=='object':
  if not isinstance(value,dict):return False
  props=schema.get('properties',{})
  return all(k in value for k in schema.get('required',[])) and all(k in props and valid(v,props[k]) for k,v in value.items())
 if t in ('number','integer'):
  if type(value) not in (int,float) or not math.isfinite(value) or (t=='integer' and type(value)!=int):return False
  return schema.get('minimum',-math.inf)<=value<=schema.get('maximum',math.inf)
 if t=='string':return isinstance(value,str) and schema.get('minLength',0)<=len(value)<=schema.get('maxLength',math.inf)
 if t=='boolean':return type(value)==bool
 return False

def decode_public_facts(facts):
 # Shared by the food and natural-resource consumers, including frozen staging.
 # This is the inverse of ToolResultProjection lossless_columns_v1, not inference.
 if not isinstance(facts,dict):raise ValueError('facts_object_required')
 observation=facts.get('observation')
 if not isinstance(observation,dict):return facts
 decoded=None
 for key in ('visibleBlocks','entities','pickables'):
  table=observation.get(key)
  if not isinstance(table,dict):continue
  if table.get('encoding')!='lossless_columns_v1':raise ValueError('unsupported_public_table')
  if set(table)!=set(('encoding','columns','rows','rowCount')):raise ValueError('public_table_fields')
  columns,rows,n=table['columns'],table['rows'],table['rowCount']
  if not isinstance(columns,list) or not columns or len(columns)>64 or any(not isinstance(c,str) or not c for c in columns) or len(set(columns))!=len(columns):raise ValueError('public_table_columns')
  if not isinstance(rows,list) or type(n)!=int or not 0<=n<=4096 or n!=len(rows):raise ValueError('public_table_count')
  if any(not isinstance(row,list) or len(row)!=len(columns) for row in rows):raise ValueError('public_table_row')
  if decoded is None:decoded=observation.copy()
  decoded[key]=[dict(zip(columns,row)) for row in rows]
 if decoded is None:return facts
 result=facts.copy();result['observation']=decoded;return result

def facts_from_prompt(request):
 # No request.context fallback: this driver must use exactly the model-visible text.
 prompt=request['prompt'];marker='OBSERVATION:\n'
 if prompt.count(marker)!=1:raise ValueError('single_observation_required')
 data,_=json.JSONDecoder().raw_decode(prompt.split(marker,1)[1].lstrip())
 if data.get('projectionStatus')!='projected' or not isinstance(data.get('facts'),dict):raise ValueError('projected_facts_required')
 return decode_public_facts(data['facts'])

def counts(slots):
 out={}
 for s in slots:
  if any(type(s.get(k))!=int or s[k]<0 for k in ('slot','value','count')):raise ValueError('slot_shape')
  out[s['value']]=out.get(s['value'],0)+s['count']
 return out

class Policy:
 def __init__(self):self.scope=None;self.baseline=None
 def decide(self,p):
  scope=(p['sessionId'],p['actorId'])
  if self.scope is not None and scope!=self.scope:raise ValueError('scope_changed')
  self.scope=scope;facts=facts_from_prompt(p);obs=facts.get('observation',{});slots=facts.get('inventory',{}).get('slots')
  if not isinstance(slots,list):raise ValueError('actual_inventory_required')
  owned=counts(slots)
  if self.baseline is None:self.baseline=owned.copy()
  def plan(reason,tool=None,args=None):
   steps=[]
   if tool:
    offers=[o for o in p['offers'] if o.get('Name')==tool]
    if len(offers)!=1 or not valid(args,offers[0].get('Parameters',{})):return {'goal':'No legal offered action for current visible facts','steps':[]}
    steps=[{'tool':tool,'arguments':args}]
   return {'goal':reason,'steps':steps}
  phase=facts.get('planningPhase')
  if phase is not None and not isinstance(phase,dict):raise ValueError('planning_phase_shape')
  if phase is not None and phase.get('phase')=='AfterPrecedingSuccess':return plan('Await actual arrival and newly observed inventory; no invented future food')
  # Public static item labels identify food; never read fixture value/position/audit.
  meat={i['id'] for i in facts.get('manual',{}).get('items',[]) if type(i.get('id'))==int and i.get('name','').casefold() in ('cooked meat','熟肉')}
  for slot in slots:
   item=slot.get('item',{})
   if item.get('craftingId')=='cookedmeat' or item.get('name')=='熟肉':meat.add(slot['value']&1023)
  for slot in slots:
   value=slot['value']
   if value&1023 in meat and slot['count']>0 and owned[value]>self.baseline.get(value,0):
    return plan('Consume newly observed owned cooked meat','act_Eat',{'slot':slot['slot'],'value':value,'count':1})
  pos=obs.get('self',{}).get('position',{})
  if any(type(pos.get(k)) not in (int,float) or not math.isfinite(pos[k]) for k in ('x','y','z')):raise ValueError('actual_position_required')
  drops=[]
  for drop in obs.get('pickables',[]):
   if drop.get('sense')!='vision' or type(drop.get('value'))!=int or drop['value']&1023 not in meat or type(drop.get('count'))!=int or drop['count']<=0:continue
   q=drop.get('position',{})
   if any(type(q.get(k)) not in (int,float) or not math.isfinite(q[k]) for k in ('x','y','z')):continue
   dx=q['x']-pos['x'];dz=q['z']-pos['z'];distance=math.hypot(dx,dz)
   if distance>0:drops.append((distance,dx,dz,drop))
  if not drops:return plan('No currently visible new cooked-meat source; require new observation')
  distance,dx,dz,drop=min(drops,key=lambda d:d[0])
  if distance<1:return plan('Allow native collection then reobserve actual inventory','act_Wait',{'duration':.3})
  return plan('Approach actually visible cooked-meat drop; collection not yet claimed','host_navigate_local',{'bearing':math.degrees(math.atan2(dx,dz)),'distance':min(4,distance),'max_seconds':12})

def run(directory,clock=time.monotonic,sleep=time.sleep):
 root=safe(directory)
 if not root.is_dir():raise ValueError('root')
 with (root/'food-driver-started.json').open('x') as f:json.dump({'kind':'deterministic_fixture_not_model','maximumRequests':4,'maximumSeconds':180},f)
 end=clock()+180;seen=set();ids=set();sent=0;policy=Policy();reasons=[]
 def expired():return clock()>=end or (root/'exit.txt').exists()
 while not expired() and len(seen)<4:
  control=safe(root/'control')
  for request in sorted(control.glob('*/request.json')):
   if expired() or len(seen)>=4:break
   if request in seen:continue
   seen.add(request);p=read_request(request)
   if p['requestId'] in ids:raise ValueError('duplicate_identity')
   ids.add(p['requestId']);safe(request.parent)
   if (request.parent/'canceled.json').exists():continue
   deadline=datetime.datetime.fromisoformat(p['deadlineUtc'].replace('Z','+00:00'))
   if deadline.tzinfo is None:raise ValueError('deadline_timezone')
   if datetime.datetime.now(datetime.timezone.utc)>=deadline:continue
   decision=policy.decide(p);response={k:p[k] for k in IDS};response['decision']=decision
   output=request.parent/'response.json';temporary=request.parent/'food-response.tmp'
   if output.exists():raise ValueError('response_exists')
   with temporary.open('x') as f:json.dump(response,f);f.flush();os.fsync(f.fileno())
   try:
    if expired() or (request.parent/'canceled.json').exists() or datetime.datetime.now(datetime.timezone.utc)>=deadline:continue
    os.link(temporary,output);sent+=1;reasons.append(decision['goal'])
   finally:temporary.unlink()
  if not expired() and len(seen)<4:sleep(min(.05,max(0,end-clock())))
 result={'kind':'deterministic_fixture_not_model','receivedRequests':len(seen),'responses':sent,'elapsedSeconds':180-(end-clock()),'goals':reasons,'reason':'host_exit' if (root/'exit.txt').exists() else 'request_limit' if len(seen)>=4 else 'deadline','acquisitionClaim':'none: native host evidence must independently prove collection and eating'}
 with (root/'food-driver-result.json').open('x') as f:json.dump(result,f)
 return result
if __name__=='__main__':
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('directory');args=ap.parse_args();print(json.dumps(run(args.directory)))
