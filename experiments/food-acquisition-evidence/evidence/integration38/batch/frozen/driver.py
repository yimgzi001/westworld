#!/usr/bin/env python3
"""38 controlled Eat contract cases. Reuses the frozen food file protocol, never calls API."""
import argparse,importlib.util,json,math
from pathlib import Path

CASES=('wrong_slot_value','over_count','fresh_slot_change','valid_eat')
def load(path):
 if path.is_symlink() or not path.is_file():raise ValueError('frozen_food_dependency_required')
 s=importlib.util.spec_from_file_location('frozen_food_protocol38',path);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m

def matches(value,schema):
 if not isinstance(schema,dict):return False
 if 'oneOf' in schema:return sum(matches(value,s) for s in schema['oneOf'])==1
 if 'const' in schema and value!=schema['const']:return False
 if 'enum' in schema and value not in schema['enum']:return False
 t=schema.get('type')
 if t=='object':
  props=schema.get('properties',{})
  return isinstance(value,dict) and all(k in value for k in schema.get('required',[])) and all(k in props and matches(v,props[k]) for k,v in value.items())
 if t=='integer':return type(value)==int and schema.get('minimum',-math.inf)<=value<=schema.get('maximum',math.inf)
 if t=='number':return type(value) in (int,float) and math.isfinite(value) and schema.get('minimum',-math.inf)<=value<=schema.get('maximum',math.inf)
 if t=='string':return isinstance(value,str) and schema.get('minLength',0)<=len(value)<=schema.get('maxLength',math.inf)
 if t=='boolean':return type(value)==bool
 return False

def schema(p,tool):
 offers=[o for o in p['offers'] if o.get('Name')==tool]
 if len(offers)!=1:raise ValueError('unique_offer_required:'+tool)
 # ParameterBranches narrows base Parameters only inside the effective response schema.
 branches=p.get('responseSchema',{}).get('properties',{}).get('steps',{}).get('items',{}).get('oneOf',[])
 effective=[b.get('properties',{}).get('arguments') for b in branches if b.get('properties',{}).get('tool',{}).get('const')==tool]
 if len(effective)!=1 or not isinstance(effective[0],dict):raise ValueError('effective_response_schema_required:'+tool)
 return effective[0]

def probe(p,case,eat,base):
 facts=base.facts_from_prompt(p);inventory=facts['inventory'];slots=inventory['slots']
 source=next(s for s in slots if s['slot']==eat['slot'] and s['value']==eat['value'] and s['count']>=eat['count'])
 intent={'case':case,'requestId':p['requestId'],'sessionId':p['sessionId'],'actorId':p['actorId'],'observationId':p['observationId'],
         'sourceSlot':dict(source),'originalEat':dict(eat),'inventoryAtPlanning':inventory,'expected':'native_success' if case=='valid_eat' else 'fresh_pre_dispatch_rejection' if case=='fresh_slot_change' else 'initial_plan_rejection'}
 steps=[];arguments=dict(eat)
 if case=='wrong_slot_value':
  other=next((s for s in slots if s['count']>0 and s['value']!=source['value']),None)
  if other is None:raise ValueError('two_distinct_owned_values_required')
  arguments['slot']=other['slot'] # both components observed, the pair is deliberately wrong
 elif case=='over_count':arguments['count']=source['count']+1
 elif case=='fresh_slot_change':
  ref=facts.get('observation',{}).get('inventoryReference');empty=inventory.get('emptySlotIds',[])
  if not isinstance(ref,str) or not ref or not empty:raise ValueError('visible_inventory_reference_and_empty_slot_required')
  destination=min(x for x in empty if type(x)==int and x!=source['slot'])
  transfer={'source':ref,'source_slot':source['slot'],'source_value':source['value'],
            'destination':ref,'destination_slot':destination,'destination_value':0,'count':source['count']}
  if not matches(transfer,schema(p,'act_TransferItems')):raise ValueError('legal_same_inventory_transfer_required')
  steps.append({'tool':'act_TransferItems','arguments':transfer});intent['destinationSlot']=destination
 eat_schema=schema(p,'act_Eat');accepted=matches(arguments,eat_schema)
 if case in ('fresh_slot_change','valid_eat') and not accepted:raise ValueError('actual_current_eat_must_be_offered')
 intent.update(submittedEat=arguments,publicSchemaAccepts=accepted)
 steps.append({'tool':'act_Eat','arguments':arguments})
 return {'goal':'Controlled Eat argument contract probe; not survival policy','steps':steps},intent

class Policy:
 def __init__(self,base,original,case):self.base=base;self.inner=original();self.case=case;self.attempt=None;self.scope=None
 def decide(self,p):
  scope=(p['sessionId'],p['actorId'])
  if self.scope is not None and self.scope!=scope:raise ValueError('scope_changed')
  self.scope=scope
  if self.attempt is not None:return {'goal':'Controlled probe already issued; no retry, Eat or recovery action','steps':[]}
  plan=self.inner.decide(p)
  eats=[s for s in plan['steps'] if s['tool']=='act_Eat']
  if len(eats)!=1:return plan
  plan,self.attempt=probe(p,self.case,eats[0]['arguments'],self.base)
  return plan

def run(directory,case):
 root=Path(directory).absolute();base=load(root/'sources/experiments/food-acquisition-evidence/deterministic_driver.py')
 base.safe(root);original=base.Policy;policy=Policy(base,original,case);base.Policy=lambda:policy
 marker=root/'eat-contract-driver-started.json'
 with marker.open('x') as f:json.dump({'case':case,'nonModel':True,'maximumRequests':4,'maximumSeconds':180,'noRetry':True},f)
 result=None;error=None
 try:result=base.run(root)
 except Exception as exc:error=type(exc).__name__+':'+str(exc)
 audit={'schema':'eat-contract-driver38-v1','case':case,'attempt':policy.attempt,'error':error,'fileProtocolResult':result,
        'claim':'No Eat or rejection success claim; original native logs must independently validate it.'}
 with (root/'eat-contract-driver-result.json').open('x') as f:json.dump(audit,f,indent=2)
 return audit
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory');p.add_argument('--case',choices=CASES,required=True);a=p.parse_args();r=run(a.directory,a.case);print(json.dumps(r));raise SystemExit(1 if r['error'] else 0)
