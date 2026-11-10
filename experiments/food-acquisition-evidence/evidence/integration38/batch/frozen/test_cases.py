import copy,importlib.util,json,unittest
from pathlib import Path
s=importlib.util.spec_from_file_location('eat38driver',Path(__file__).with_name('driver.py'));d=importlib.util.module_from_spec(s);s.loader.exec_module(d)
class Public:
 @staticmethod
 def facts_from_prompt(p):return p['facts']
def branch(slot,value,count):
 return {'type':'object','properties':{'slot':{'type':'integer','const':slot},'value':{'type':'integer','const':value},'count':{'type':'integer','minimum':1,'maximum':count}},'required':['slot','value','count'],'additionalProperties':False}
def request():
 schema={'oneOf':[branch(0,89,1),branch(1,162,1)]}
 props={k:{'type':'integer','minimum':0,'maximum':4096} for k in ['source_slot','source_value','destination_slot','destination_value','count']}
 props.update(source={'type':'string','enum':['own']},destination={'type':'string','enum':['own']})
 p={'requestId':'r','sessionId':'s','actorId':'a','observationId':'o','offers':[{'Name':'act_Eat','Parameters':schema},{'Name':'act_TransferItems','Parameters':{'type':'object','properties':props,'required':list(props),'additionalProperties':False}}],
 'facts':{'observation':{'inventoryReference':'own'},'inventory':{'slots':[{'slot':0,'value':89,'count':1},{'slot':1,'value':162,'count':1}],'emptySlotIds':[2,3]}}}
 p['responseSchema']={'properties':{'steps':{'items':{'oneOf':[{'properties':{'tool':{'const':o['Name']},'arguments':copy.deepcopy(o['Parameters'])}} for o in p['offers']]}}}}
 return p
class Cases(unittest.TestCase):
 def test_cross_pair_is_rejected_even_each_component_visible(self):
  p=request();plan,a=d.probe(p,'wrong_slot_value',{'slot':0,'value':89,'count':1},Public)
  self.assertEqual(plan['steps'][0]['arguments'],{'slot':1,'value':89,'count':1});self.assertFalse(a['publicSchemaAccepts'])
 def test_over_count_rejected(self):
  _,a=d.probe(request(),'over_count',{'slot':0,'value':89,'count':1},Public);self.assertFalse(a['publicSchemaAccepts']);self.assertEqual(a['submittedEat']['count'],2)
 def test_valid_eat_admitted_not_called(self):
  plan,a=d.probe(request(),'valid_eat',{'slot':0,'value':89,'count':1},Public);self.assertEqual(len(plan['steps']),1);self.assertTrue(a['publicSchemaAccepts'])
 def test_fresh_transfer_then_old_eat_initially_valid(self):
  plan,a=d.probe(request(),'fresh_slot_change',{'slot':0,'value':89,'count':1},Public)
  self.assertEqual([x['tool'] for x in plan['steps']],['act_TransferItems','act_Eat']);self.assertTrue(a['publicSchemaAccepts']);self.assertEqual(a['destinationSlot'],2)
  fresh={'oneOf':[branch(2,89,1),branch(1,162,1)]};self.assertFalse(d.matches(a['submittedEat'],fresh))
 def test_same_value_stacks_not_summed(self):
  schema={'oneOf':[branch(0,89,1),branch(2,89,2)]}
  self.assertFalse(d.matches({'slot':0,'value':89,'count':2},schema));self.assertTrue(d.matches({'slot':2,'value':89,'count':2},schema))
 def test_no_private_reference_or_empty_slot_guess(self):
  for mutate in ['ref','empty']:
   p=request()
   if mutate=='ref':p['facts']['observation'].pop('inventoryReference')
   else:p['facts']['inventory']['emptySlotIds']=[]
   with self.assertRaises(ValueError):d.probe(p,'fresh_slot_change',{'slot':0,'value':89,'count':1},Public)
 def test_extra_key_and_bool_integer_rejected(self):
  self.assertFalse(d.matches({'slot':0,'value':89,'count':True},branch(0,89,1)))
  self.assertFalse(d.matches({'slot':0,'value':89,'count':1,'extra':1},branch(0,89,1)))
 def test_legacy_cross_product_detected_not_hidden(self):
  p=request();p['offers'][0]['Parameters']={'type':'object','properties':{'slot':{'type':'integer','enum':[0,1]},'value':{'type':'integer','enum':[89,162]},'count':{'type':'integer','minimum':1,'maximum':64}},'required':['slot','value','count']}
  p['responseSchema']['properties']['steps']['items']['oneOf'][0]['properties']['arguments']=p['offers'][0]['Parameters']
  _,a=d.probe(p,'wrong_slot_value',{'slot':0,'value':89,'count':1},Public);self.assertTrue(a['publicSchemaAccepts']) # future validator MUST fail this, not claim new correlation
 def test_base_parameters_do_not_override_effective_schema(self):
  p=request();p['offers'][0]['Parameters']={'type':'object','properties':{}}
  _,a=d.probe(p,'wrong_slot_value',{'slot':0,'value':89,'count':1},Public);self.assertFalse(a['publicSchemaAccepts'])
 def test_missing_effective_schema_never_falls_back(self):
  p=request();p.pop('responseSchema')
  with self.assertRaises(ValueError):d.probe(p,'valid_eat',{'slot':0,'value':89,'count':1},Public)
if __name__=='__main__':unittest.main()
