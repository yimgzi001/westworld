import copy,datetime,json,pathlib,tempfile,unittest,uuid
import deterministic_driver as d

def request():
 f={'planningPhase':{'phase':'Immediate'},'manual':{'items':[{'id':89,'name':'cooked meat'},{'id':162,'name':'cooked fish'}]},'inventory':{'slots':[{'slot':1,'value':162,'count':1}]},'observation':{'self':{'position':{'x':0,'y':65,'z':0}},'pickables':[{'reference':'real','value':89,'count':1,'position':{'x':2.75,'y':65.2,'z':0},'sense':'vision'}]}}
 p={k:str(uuid.uuid4()) for k in d.IDS};p.update(protocol='westworld-tool-file-v1',offers=[{'Name':'host_navigate_local','Parameters':{'type':'object','properties':{'bearing':{'type':'number','minimum':0,'maximum':359.999999},'distance':{'type':'number','minimum':1,'maximum':4},'max_seconds':{'type':'number','minimum':1,'maximum':12}},'required':['bearing','distance']}},{'Name':'act_Eat','Parameters':{'type':'object','properties':{'slot':{'type':'integer','enum':list(range(26))},'value':{'type':'integer','enum':[0,89,162]},'count':{'type':'integer','minimum':1,'maximum':64}},'required':['slot','value','count']}},{'Name':'act_Wait','Parameters':{'type':'object','properties':{'duration':{'type':'number','minimum':.1,'maximum':10}},'required':['duration']}}],deadlineUtc=(datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(seconds=45)).isoformat())
 setfacts(p,f);return p

def setfacts(p,f):p['prompt']='TOOLS schema here\nOBSERVATION:\n'+json.dumps({'projectionStatus':'projected','facts':f})
class DriverTests(unittest.TestCase):
 def test_visible_nav(self):
  r=d.Policy().decide(request());self.assertEqual(r['steps'][0]['arguments'],{'bearing':90.,'distance':2.75,'max_seconds':12})
 def test_native_null_phase(self):
  p=request();f=d.facts_from_prompt(p);f['planningPhase']=None;setfacts(p,f);self.assertEqual(d.Policy().decide(p)['steps'][0]['tool'],'host_navigate_local')
 def test_invalid_phase_rejected(self):
  p=request();f=d.facts_from_prompt(p);f['planningPhase']='not a phase';setfacts(p,f)
  with self.assertRaises(ValueError):d.Policy().decide(p)
 def test_negative_bearing_schema(self):
  p=request();p['offers'][0]['Parameters']['properties']['bearing'].update(minimum=-180,maximum=180);f=d.facts_from_prompt(p);f['observation']['pickables'][0]['position']['x']=-2.75;setfacts(p,f)
  self.assertEqual(d.Policy().decide(p)['steps'][0]['arguments']['bearing'],-90)
 def test_real_new_inventory(self):
  p=request();policy=d.Policy();policy.decide(p);f=d.facts_from_prompt(p);f['inventory']['slots'].append({'slot':4,'value':89,'count':1});setfacts(p,f)
  self.assertEqual(policy.decide(p)['steps'][0],{'tool':'act_Eat','arguments':{'slot':4,'value':89,'count':1}})
 def test_initial_meat_not_new(self):
  p=request();f=d.facts_from_prompt(p);f['inventory']['slots'].append({'slot':4,'value':89,'count':1});setfacts(p,f)
  self.assertNotEqual(d.Policy().decide(p)['steps'][0]['tool'],'act_Eat')
 def test_no_manual_no_target(self):
  p=request();f=d.facts_from_prompt(p);f['manual']={};setfacts(p,f);self.assertEqual(d.Policy().decide(p)['steps'],[])
 def test_prefetch_no_future_slot(self):
  p=request();f=d.facts_from_prompt(p);f['planningPhase']['phase']='AfterPrecedingSuccess';setfacts(p,f);self.assertEqual(d.Policy().decide(p)['steps'],[])
 def test_no_private_context_fallback(self):
  p=request();p['context']={'fixtureValue':89};p['prompt']='no observation'
  with self.assertRaises(ValueError):d.Policy().decide(p)
 def test_missing_tool(self):
  p=request();p['offers']=[];self.assertEqual(d.Policy().decide(p)['steps'],[])
 def test_schema_bounds_respected(self):
  p=request();p['offers'][0]['Parameters']['properties']['distance']['maximum']=2;self.assertEqual(d.Policy().decide(p)['steps'],[])
 def test_scope_changed(self):
  p=request();policy=d.Policy();policy.decide(p);p['actorId']=str(uuid.uuid4())
  with self.assertRaises(ValueError):policy.decide(p)
 def test_nonvision_not_navigated(self):
  p=request();f=d.facts_from_prompt(p);f['observation']['pickables'][0]['sense']='proximity';setfacts(p,f);self.assertEqual(d.Policy().decide(p)['steps'],[])
 def test_close_wait_not_claim_pickup(self):
  p=request();f=d.facts_from_prompt(p);f['observation']['pickables'][0]['position']['x']=.6;setfacts(p,f);self.assertEqual(d.Policy().decide(p)['steps'][0]['tool'],'act_Wait')
 def test_nan_rejected(self):
  p=request();f=d.facts_from_prompt(p);f['observation']['self']['position']['x']=float('nan');setfacts(p,f)
  with self.assertRaises(ValueError):d.Policy().decide(p)
 def test_bounded_driver_and_no_replay(self):
  with tempfile.TemporaryDirectory() as tmp:
   root=pathlib.Path(tmp);first=request()
   for n in range(5):
    p=copy.deepcopy(first);p['requestId']=str(uuid.uuid4());folder=root/'control'/str(n);folder.mkdir(parents=True);(folder/'request.json').write_text(json.dumps(p))
   result=d.run(root);self.assertEqual(result['receivedRequests'],4);self.assertEqual(result['responses'],4);self.assertFalse((root/'control/4/response.json').exists())
   with self.assertRaises(FileExistsError):d.run(root)
 def test_canceled_counts_budget_no_response(self):
  with tempfile.TemporaryDirectory() as tmp:
   root=pathlib.Path(tmp)
   for n in range(4):
    folder=root/'control'/str(n);folder.mkdir(parents=True);(folder/'request.json').write_text(json.dumps(request()));(folder/'canceled.json').write_text('{}')
   result=d.run(root);self.assertEqual(result['receivedRequests'],4);self.assertEqual(result['responses'],0)
 def test_elapsed_deadline_without_requests(self):
  with tempfile.TemporaryDirectory() as tmp:
   now=[0.];r=d.run(tmp,clock=lambda:now[0],sleep=lambda _:now.__setitem__(0,181.));self.assertEqual(r['reason'],'deadline');self.assertEqual(r['responses'],0)
 def test_no_overwrite(self):
  with tempfile.TemporaryDirectory() as tmp:
   folder=pathlib.Path(tmp)/'control/a';folder.mkdir(parents=True);(folder/'request.json').write_text(json.dumps(request()));(folder/'response.json').write_text('original')
   with self.assertRaises(ValueError):d.run(tmp)
   self.assertEqual((folder/'response.json').read_text(),'original')
 def test_symlink_rejected(self):
  with tempfile.TemporaryDirectory() as tmp:
   root=pathlib.Path(tmp);(root/'actual').mkdir();(root/'link').symlink_to(root/'actual',target_is_directory=True)
   with self.assertRaises(ValueError):d.run(root/'link')
if __name__=='__main__':unittest.main()
