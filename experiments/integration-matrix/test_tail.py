import copy,unittest,uuid,json,pathlib
import tail_driver,validate_tail

def rows(case='tail_early'):
 plan,rid,move,face,wait=[str(uuid.uuid4()) for _ in range(5)]
 steps=[dict(Tool='act_MoveTo',Arguments={'bearing':0,'distance':4,'range':.25,'timeout':4}),dict(Tool='act_FaceTowards',Arguments={'bearing':90,'timeout':4})]
 result=[dict(kind='continuation_bound',planId=plan,stepCount=2,firstReceipt=1,steps=steps,monotonicSeconds=1),dict(kind='prefetch_requested',lease={'RequestId':rid,'PrecedingReceiptSequence':1}),dict(kind='continuation_tail_settled',planId=plan,lastRunId=face,lastReceipt=2,monotonicSeconds=4),dict(kind='prefetch_handoff_taken',requestId=rid,monotonicSeconds=6),dict(kind='prefetch_response',requestId=rid,accepted=True,monotonicSeconds=1.5 if case=='tail_early' else 5),dict(kind='result',passed=True,updateFailures=0),dict(kind='final_audit',modelInFlight=False)]
 for i,(run,tool) in enumerate([(move,'act_MoveTo'),(face,'act_FaceTowards')]):
  result.extend([dict(kind='continuation_step_bound',planId=plan,index=i,sequence=i+1,runId=run,tool=tool,monotonicSeconds=1 if i==0 else 2.1),dict(kind='continuation_step_terminal',planId=plan,index=i,sequence=i+1,runId=run,status='succeeded',accepted=True,monotonicSeconds=2 if i==0 else 3),dict(kind='native_call_returned',tool=tool,sequence=i+1,state='returned',data={'runId':run}),dict(kind='tool_receipt',receipt={'Tool':'action_status','Data':{'runId':run,'status':'succeeded'}})])
 result.extend([dict(kind='native_call_started',step=steps[0],monotonicSeconds=.5,candidateRequestId=None),dict(kind='native_call_started',step=steps[1],monotonicSeconds=2.1,candidateRequestId=None),dict(kind='native_call_started',step={'Tool':'act_Wait','Arguments':{'duration':.2}},monotonicSeconds=6.1,candidateRequestId=rid),dict(kind='native_call_returned',tool='act_Wait',sequence=3,state='returned',data={'runId':wait}),dict(kind='tool_receipt',receipt={'Tool':'act_Wait','Data':{'runId':wait,'status':'succeeded'}})])
 return result,dict(case='direct_early' if case=='tail_early' else 'direct_late',events=[dict(delivered=True),dict(delivered=True,requestId=rid)])
class TailTests(unittest.TestCase):
 def test_early_late(self):
  for case in ['tail_early','tail_late']:
   r,d=rows(case);self.assertEqual(validate_tail.validate(r,d,case)['verdict'],'pass')
 def test_missing_each_event_never_passes(self):
  r,d=rows()
  for i in range(len(r)):
   with self.subTest(i=i):self.assertNotEqual(validate_tail.validate(r[:i]+r[i+1:],d,'tail_early')['verdict'],'pass')
 def test_candidate_cannot_beat_tail(self):
  r,d=rows();next(x for x in r if x['kind']=='prefetch_handoff_taken')['monotonicSeconds']=2.5
  self.assertEqual(validate_tail.validate(r,d,'tail_early')['verdict'],'fail')
 def test_replaced_tail_run(self):
  r,d=rows();[x for x in r if x['kind']=='continuation_step_terminal'][1]['runId']=str(uuid.uuid4())
  self.assertEqual(validate_tail.validate(r,d,'tail_early')['verdict'],'fail')
 def test_tail_failure_not_positive(self):
  r,d=rows();[x for x in r if x['kind']=='continuation_step_terminal'][1]['status']='failed'
  self.assertEqual(validate_tail.validate(r,d,'tail_early')['verdict'],'fail')
 def test_declared_early_is_actually_late(self):
  r,d=rows();next(x for x in r if x['kind']=='prefetch_response')['monotonicSeconds']=5
  self.assertEqual(validate_tail.validate(r,d,'tail_early')['verdict'],'fail')
 def negative(self):
  r,d=rows();bad=[x for x in r if x['kind']=='continuation_step_terminal'][1];bad.update(status='failed',accepted=False)
  bound=next(x for x in r if x['kind']=='continuation_bound');bound['steps'][1]['Arguments']={'bearing':180,'timeout':.1}
  for x in r:
   if x['kind']=='tool_receipt' and x['receipt']['Data']['runId']==bad['runId']:x['receipt']['Data']['status']='failed'
  r=[x for x in r if x['kind'] not in ['continuation_tail_settled','prefetch_handoff_taken','prefetch_response'] and not (x['kind']=='native_call_started' and x['step']['Tool']=='act_Wait') and not (x['kind']=='native_call_returned' and x['tool']=='act_Wait') and not (x['kind']=='tool_receipt' and x['receipt']['Tool']=='act_Wait')]
  rid=d['events'][1]['requestId'];d['case']='direct_tail_failure'
  r.extend([dict(kind='prefetch_invalidated',requestId=rid,monotonicSeconds=3.1),dict(kind='stop_drained',modelCompleted=True,direct={'runId':bad['runId'],'status':'failed'},task={'status':'idle'})])
  return r,d
 def test_negative_uses_real_events_not_claim(self):
  r,d=self.negative();self.assertEqual(validate_tail.validate(r,d,'tail_failure')['verdict'],'pass')
  # An untrusted success summary cannot rescue missing raw failure.
  r.append(dict(kind='direct_fixture_result',tailFailure={'everything':'passed'}))
  r=[x for x in r if x['kind']!='continuation_step_terminal']
  self.assertNotEqual(validate_tail.validate(r,d,'tail_failure')['verdict'],'pass')
 def test_negative_cannot_execute_candidate(self):
  r,d=self.negative();r.append(dict(kind='native_call_started',candidateRequestId=d['events'][1]['requestId'],step={'Tool':'act_Wait','Arguments':{'duration':.2}},monotonicSeconds=4))
  self.assertEqual(validate_tail.validate(r,d,'tail_failure')['verdict'],'fail')
 def test_negative_wrong_run_or_no_invalidation(self):
  r,d=self.negative();next(x for x in r if x['kind']=='stop_drained')['direct']['runId']=str(uuid.uuid4())
  self.assertEqual(validate_tail.validate(r,d,'tail_failure')['verdict'],'fail')
  r,d=self.negative();r=[x for x in r if x['kind']!='prefetch_invalidated'];self.assertEqual(validate_tail.validate(r,d,'tail_failure')['verdict'],'fail')
 def test_actual_native34_sanitized_logs(self):
  for path in (pathlib.Path(__file__).parent/'fixtures').glob('tail_actual34_*.json'):
   with self.subTest(path=path.name):
    f=json.loads(path.read_text());self.assertEqual(validate_tail.validate(f['rows'],f['driver'],f['case'])['verdict'],'pass')
    # Null idle is valid, but removing the real terminal is not.
    damaged=[r for r in f['rows'] if r.get('kind')!='continuation_step_terminal']
    self.assertNotEqual(validate_tail.validate(damaged,f['driver'],f['case'])['verdict'],'pass')
 def test_idle_null_ignored_not_used_as_success(self):
  r,d=rows();r.insert(0,dict(kind='tool_receipt',receipt={'Tool':'action_status','Data':None}))
  self.assertEqual(validate_tail.validate(r,d,'tail_early')['verdict'],'pass')
  r=[x for x in r if not (x['kind']=='tool_receipt' and x['receipt']['Data'] is not None)]
  self.assertNotEqual(validate_tail.validate(r,d,'tail_early')['verdict'],'pass')
 def test_driver_public_offers_only(self):
  driver=tail_driver.load_driver()
  names={'act_MoveTo':['bearing','distance','range','timeout'],'act_FaceTowards':['bearing','timeout'],'act_Wait':['duration']}
  payload={'offers':[{'Name':name,'Parameters':{'properties':{p:{'type':'number','minimum':-180,'maximum':180} for p in props},'required':props}} for name,props in names.items()]}
  failure=tail_driver.decision(driver,payload,1,'direct_tail_failure')
  self.assertEqual(failure['steps'][1]['arguments'],{'bearing':180,'timeout':.1})
  self.assertEqual(len(tail_driver.decision(driver,payload,1,'tail_early')['steps']),2)
  self.assertEqual(tail_driver.decision(driver,payload,2,'tail_late')['steps'][0]['tool'],'act_Wait')
  payload['offers']=[o for o in payload['offers'] if o['Name']!='act_FaceTowards']
  with self.assertRaises(ValueError):tail_driver.decision(driver,payload,1,'tail_early')
if __name__=='__main__':unittest.main()
