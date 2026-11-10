import json,pathlib,tempfile,unittest,uuid
from types import SimpleNamespace
from unittest.mock import patch
import batch
P=pathlib.Path
class Process:
 def __init__(self):self.returncode=None
 def poll(self):return self.returncode
class Tests(unittest.TestCase):
 def test_first_network_record_stops_owned_processes(self):
  with tempfile.TemporaryDirectory() as d:
   driver,host=Process(),Process();stopped=[]
   def kill(p):p.returncode=-15;stopped.append(p)
   c=dict(id='food-0',directory=d,kind='official_model',watchdogSeconds=195)
   # Fake fatal result appears after readiness; there is no transport or key.
   def fatal(path):return {'code':'network_error','requestId':str(uuid.uuid4())}
   with patch.object(batch,'load',side_effect=[SimpleNamespace(VERIFIED_MODELS={'gpt-6.1-terra':{'none'}}),SimpleNamespace(terminate_owned=kill)]),patch.object(batch.sys.stdin,'isatty',return_value=True),patch.object(batch,'read_rows',return_value=[{'event':'ready'}]),patch.object(batch,'fatal_model_audit',side_effect=fatal),patch.object(batch.subprocess,'Popen',side_effect=[driver,host]) as spawn,patch.object(batch.time,'sleep') as sleep:
    r=batch.run_case(c,{},True,'gpt-6.1-terra','none')
   self.assertEqual(r['error'],'fatal_model_failure');self.assertTrue(r['stopRemainingModels']);self.assertEqual(stopped,[host,driver]);self.assertEqual(spawn.call_count,2);sleep.assert_not_called()
   saved=json.loads((P(d)/'execution.json').read_text());self.assertEqual(saved['fatalModelFailure']['code'],'network_error');self.assertEqual(saved['hostExit'],-15)
 def test_keyboard_interrupt_persists_case_and_drains(self):
  with tempfile.TemporaryDirectory() as d:
   driver,host=Process(),Process();stopped=[]
   def kill(p):stopped.append(p);p.returncode=-15
   c=dict(id='direct_late',fixture='direct_late',kind='deterministic_fixture',directory=d,watchdogSeconds=80)
   with patch.object(batch,'load',return_value=SimpleNamespace(terminate_owned=kill)),patch.object(batch.subprocess,'Popen',side_effect=[driver,host]),patch.object(batch.time,'sleep',side_effect=KeyboardInterrupt()):r=batch.run_case(c,{})
   self.assertEqual(r['error'],'KeyboardInterrupt');self.assertTrue(r['interrupted']);self.assertEqual(stopped,[host,driver]);self.assertEqual(json.loads((P(d)/'execution.json').read_text())['error'],'KeyboardInterrupt')
 def test_fatal_stops_group_remaining_models_and_preserves_report(self):
  with tempfile.TemporaryDirectory() as d:
   m={'cases':[dict(id='food-'+str(i),kind='official_model') for i in range(3)]};calls=[]
   def runner(c,*args):calls.append(c['id']);return dict(case=c['id'],hostExit=-15,driverExit=-15,error='fatal_model_failure',drainCertain=True,stopRemainingModels=True,interrupted=False)
   with patch.object(batch,'summarize_case',return_value={'independentValidation':{'verdict':'unknown'}}):r=batch.run_group(m,P(d)/'matrix.json','models',True,'gpt-6.1-terra','none',runner)
   self.assertEqual(calls,['food-0']);self.assertEqual(r['unstartedCases'],['food-1','food-2']);self.assertEqual(r['stopReason'],'fatal_model_failure');self.assertTrue((P(d)/'run-models-result.json').exists())
 def test_keyboard_between_case_and_summary_keeps_result(self):
  with tempfile.TemporaryDirectory() as d:
   m={'cases':[dict(id='a',kind='deterministic_fixture'),dict(id='b',kind='deterministic_fixture')]}
   def runner(c,*args):return dict(case=c['id'],hostExit=0,driverExit=0,error=None,drainCertain=True,interrupted=False)
   with patch.object(batch,'summarize_case',side_effect=KeyboardInterrupt()):r=batch.run_group(m,P(d)/'matrix.json','deterministic',case_runner=runner)
   self.assertTrue(r['interrupted']);self.assertEqual(r['cases'][0]['case'],'a');self.assertEqual(r['unstartedCases'],['b']);self.assertEqual(json.loads((P(d)/'run-deterministic-result.json').read_text())['stopReason'],'interrupted')
 def test_partial_audit_line_not_failure_but_complete_protocol_error_is(self):
  with tempfile.TemporaryDirectory() as d:
   p=P(d)/'api-audit.jsonl';p.write_text('{"unfinished":')
   self.assertIsNone(batch.fatal_model_audit(P(d)));p.write_text('{broken}\n')
   self.assertEqual(batch.fatal_model_audit(P(d))['code'],'audit_protocol_invalid')
 def test_fatal_allowlist_and_unknown_usage_not_zero(self):
  with tempfile.TemporaryDirectory() as d:
   p=P(d)/'api-audit.jsonl'
   for error in ['network_error','worker_protocol','decision_shape','http_status_401']:
    p.write_text(json.dumps(dict(outcome='failed',error=error,usage=None,requestId=str(uuid.uuid4())))+'\n')
    self.assertEqual(batch.fatal_model_audit(P(d))['code'],error)
   p.write_text(json.dumps(dict(outcome='failed',error='timeout',usage=None))+'\n');self.assertIsNone(batch.fatal_model_audit(P(d)))
 def test_unrecognized_error_text_never_leaks(self):
  with tempfile.TemporaryDirectory() as d:
   (P(d)/'api-audit.jsonl').write_text(json.dumps(dict(outcome='failed',error='PRIVATE-SECRET'))+'\n');result=batch.fatal_model_audit(P(d));self.assertEqual(result['code'],'controller_unclassified_failure');self.assertNotIn('PRIVATE',json.dumps(result))
 def test_existing_group_never_replays(self):
  with tempfile.TemporaryDirectory() as d:
   (P(d)/'run-models-events.jsonl').write_text('{}\n')
   with patch.object(batch,'run_case') as run:
    with self.assertRaises(FileExistsError):batch.run_group({'cases':[]},P(d)/'matrix.json','models')
    run.assert_not_called()
if __name__=='__main__':unittest.main()
