import copy,json,pathlib,unittest,uuid
import validate_food as v

def fixture():
 e=json.loads((pathlib.Path(__file__).resolve().parents[1]/'natural-resource-evidence/synthetic-schema.json').read_text())['example']
 # This fixture remains synthetic test input, never a native/model result.
 e.update(mode='natural_food',conversionAttributionSupported=False,foodGoalVerified=False)
 e['actualInventory']={};e['ledger']['ExpectedInventory']={};e['ledger']['Consumed']={'2':1}
 req=str(uuid.uuid4());run=str(uuid.uuid4())
 m=dict(RequestId=req,ReceiptSequence=30,RunId=run,Value=2,InitialCount=0,AcquiredCount=1,InventoryBefore=1,InventoryAfter=0,EventCount=1,MatchingEvents=True,Nutrition=.3,FoodBefore=.4,FoodAfter=.7,StatsBefore=0,StatsAfter=1,NativeStatus='succeeded')
 f=dict(requestId=req,value=2,time=103.5,inventoryAfter=0,food=.7,statsBeforeIncrement=0)
 e['meals']=[dict(Result=m)];e['foodEvents']=[f]
 a=e['attempts'][0];o=e['origins'][0];t=e['transfers'][0]
 return [dict(kind='natural_origin_bound',**o),dict(kind='natural_transfer',**t),dict(kind='native_call_returned',sequence=a['ReceiptSequence'],tool='act_Dig',state='returned',data=dict(runId=a['RunId'])),dict(kind='tool_receipt',receipt=dict(Tool='action_status',Data=dict(runId=a['RunId'],status='succeeded'))),dict(kind='natural_food_event',fact=f),dict(kind='tool_receipt',receipt=dict(Tool='act_Eat',Sequence=30,State='returned',Data=dict(runId=run,status='succeeded')),arguments=dict(value=2,count=1)),dict(kind='natural_meal_terminal',meal=m,verified=False),dict(kind='natural_scope',**{k:e[k] for k in ['worldId','nativeActorId','gatewaySessionId','gatewayActorId','baselineInventory']},**e['fixture']),dict(kind='final_audit',inventory={'slots':[]},eaten=1,modelInFlight=False,requiresReconciliation=None),e,dict(kind='result',passed=True,updateFailures=0)]
class Tests(unittest.TestCase):
 def test_independent_of_host_claim(self):self.assertEqual(v.validate_log(fixture())['verdict'],'pass')
 def test_unrelated_null_idle_receipt(self):
  rows=fixture();rows.insert(0,dict(kind='tool_receipt',receipt={'Tool':'action_status','Data':None}));self.assertEqual(v.validate_log(rows)['verdict'],'pass')
 def test_actual_idle_record_with_synthetic_food_contract(self):
  # Only the idle receipt is real; this composite unit input is NOT a native food run.
  raw=json.loads((pathlib.Path(__file__).parent/'fixtures/tail_actual34_early.json').read_text())
  idle=next(r for r in raw['rows'] if r.get('kind')=='tool_receipt' and r['receipt'].get('Data') is None)
  rows=fixture();rows.insert(0,idle);self.assertEqual(v.validate_log(rows)['verdict'],'pass')
  self.assertEqual(v.validate_log(raw['rows'])['verdict'],'fail')
 def test_missing_raw_events(self):
  rows=fixture()
  for i in range(len(rows)):
   with self.subTest(i=i):self.assertEqual(v.validate_log(rows[:i]+rows[i+1:])['verdict'],'fail')
 def test_corruptions(self):
  changes=[lambda e:e['fixture'].update(foodInjected=True),lambda e:e['ledger'].update(Unknown=True),lambda e:e.update(unknownReason='lost'),lambda e:e['transfers'][0]['fact'].update(InventoryAfter=2),lambda e:e['foodEvents'][0].update(inventoryAfter=1),lambda e:e['meals'][0]['Result'].update(RunId=str(uuid.uuid4())),lambda e:e['meals'][0]['Result'].update(FoodAfter=.4),lambda e:e['meals'][0]['Result'].update(StatsAfter=0),lambda e:e['meals'][0]['Result'].update(EventCount=2),lambda e:e['meals'][0]['Result'].update(Nutrition=0),lambda e:e['meals'][0]['Result'].update(NativeStatus='failed'),lambda e:e['meals'][0]['Result'].update(InitialCount=1),lambda e:e.update(mode='natural'),lambda e:e['actualInventory'].update({'2':1}),lambda e:e['auditFailures'].update(originRecordAfter=1),lambda e:e['shutdown'].update(lateDispatches=1)]
  for i,change in enumerate(changes):
   with self.subTest(i=i):
    rows=fixture();change(rows[-2]);self.assertEqual(v.validate_log(rows)['verdict'],'fail')
 def test_initial_fish_only_never_goal(self):
  rows=fixture();e=rows[-2];e['baselineInventory']={'162':1};e['actualInventory']={'2':1};e['ledger']['ExpectedInventory']={'2':1};e['ledger']['Consumed']={}
  m=e['meals'][0]['Result'];m.update(Value=162,InitialCount=1,AcquiredCount=0)
  e['foodEvents'][0]['value']=162;rows[5]['arguments']['value']=162
  self.assertIn('no_new_food_meal',v.validate_log(rows)['failures'])
 def test_raw_wrong_run(self):
  rows=fixture();rows[5]['receipt']['Data']['runId']=str(uuid.uuid4());self.assertEqual(v.validate_log(rows)['verdict'],'fail')
 def test_duplicate_event(self):
  rows=fixture();rows.append(copy.deepcopy(rows[4]));self.assertEqual(v.validate_log(rows)['verdict'],'fail')
 def test_ambiguous_same_request_meal(self):
  rows=fixture();rows[-2]['meals']*=2;self.assertEqual(v.validate_log(rows)['verdict'],'fail')
 def test_nan_and_missing(self):
  rows=fixture();rows[-2]['meals'][0]['Result']['FoodBefore']=float('nan');self.assertEqual(v.validate_log(rows)['verdict'],'fail');self.assertEqual(v.validate_log([])['verdict'],'fail')
if __name__=='__main__':unittest.main()
