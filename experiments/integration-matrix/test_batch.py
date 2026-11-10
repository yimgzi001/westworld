import json,pathlib,tempfile,unittest
from unittest.mock import patch
import batch
P=pathlib.Path
class Tests(unittest.TestCase):
 def test_prepare_bad_seeds_no_process(self):
  with tempfile.TemporaryDirectory() as d,patch.object(batch.subprocess,'Popen') as spawn:
   with self.assertRaises(ValueError):batch.prepare(d,'p5',d,d,['same']*3,'0'*64)
   spawn.assert_not_called()
 def test_bad_hash_path(self):
  with tempfile.TemporaryDirectory() as d:
   with self.assertRaises(ValueError):batch.verify(P(d),{'../secret':'0'*64})
 def test_symlink(self):
  with tempfile.TemporaryDirectory() as d:
   p=P(d)/'link';p.symlink_to(P(d))
   with self.assertRaises(ValueError):batch.safe(p)
 def test_write_no_overwrite(self):
  with tempfile.TemporaryDirectory() as d:
   p=P(d)/'result';batch.write(p,{'original':True})
   with self.assertRaises(FileExistsError):batch.write(p,{})
 def test_model_requires_explicit_permission_and_tty(self):
  with tempfile.TemporaryDirectory() as d,patch.object(batch.subprocess,'Popen') as spawn:
   c=dict(directory=d,kind='official_model')
   with self.assertRaises(ValueError):batch.run_case(c,{},False,None,None)
   with patch.object(batch.sys.stdin,'isatty',return_value=False):
    with self.assertRaises(ValueError):batch.run_case(c,{},True,'gpt-6.1-terra','none')
   spawn.assert_not_called()
 def test_used_fixture_never_replayed(self):
  with tempfile.TemporaryDirectory() as d,patch.object(batch.subprocess,'Popen') as spawn:
   (P(d)/'execution-started.json').write_text('{}')
   with self.assertRaises(FileExistsError):batch.run_case(dict(directory=d,kind='deterministic_fixture'),{})
   spawn.assert_not_called()
 def test_missing_audit_unknown_not_zero_total(self):
  with tempfile.TemporaryDirectory() as d:
   result=batch.summarize_case(dict(id='food-0',kind='official_model',directory=d),P(d))
   self.assertIsNone(result['usage']['totalTokens']);self.assertFalse(result['genuineModelResponseObserved']);self.assertEqual(result['independentValidation']['verdict'],'unknown')
 def test_usage_not_counted_twice_and_unknown_attempt(self):
  with tempfile.TemporaryDirectory() as d:
   p=P(d);(p/'api-audit.jsonl').write_text(json.dumps(dict(requestId='r1',usage={'total_tokens':17},actualModel='gpt-6.1-terra',outcome='received'))+'\n')
   (p/'api-audit.jsonl.events.jsonl').write_text('\n'.join(json.dumps(x) for x in [dict(event='request_attempt'),dict(event='request_result',usage={'total_tokens':17})]))
   result=batch.summarize_case(dict(id='food-0',kind='official_model',directory=d),p)
   self.assertEqual(result['usage']['totalTokens'],17);self.assertEqual(result['usage']['knownTotalTokens'],17);self.assertFalse(result['genuineModelResponseObserved'])
   with (p/'api-audit.jsonl.events.jsonl').open('a') as f:f.write('\n'+json.dumps(dict(event='request_attempt')))
   result=batch.summarize_case(dict(id='food-0',kind='official_model',directory=d),p);self.assertIsNone(result['usage']['totalTokens'])
 def test_fixture_never_genuine_model(self):
  with tempfile.TemporaryDirectory() as d:
   (P(d)/'api-audit.jsonl').write_text(json.dumps(dict(actualModel='gpt-6.1-terra',outcome='received',usage={'total_tokens':17})))
   result=batch.summarize_case(dict(id='direct_late',fixture='direct_late',kind='deterministic_fixture',directory=d),P(d));self.assertFalse(result['genuineModelResponseObserved'])

class PrepareTests(unittest.TestCase):
 def test_complete_synthetic_stage_and_tamper(self):
  import shutil
  original=P(__file__).resolve().parents[2]
  with tempfile.TemporaryDirectory() as tmp,patch.object(batch.subprocess,'Popen') as spawn:
   root=P(tmp);build=root/'built';build.mkdir();content=root/'Content.zip';content.write_bytes(b'SYNTHETIC NOT GAME')
   paths=['experiments/food-acquisition-evidence/stage.py','experiments/food-acquisition-evidence/deterministic_driver.py','experiments/natural-resource-evidence/stage.py','experiments/natural-resource-evidence/deterministic_driver.py','experiments/natural-resource-evidence/synthetic-schema.json','experiments/integration-matrix/batch.py','experiments/integration-matrix/validate_food.py','experiments/integration-matrix/tail_driver.py','experiments/integration-matrix/validate_tail.py','game/Survivalcraft/Component/ComponentPickableGathererPlayer.cs','game/Survivalcraft/Subsystem/SubsystemTerrain.cs','game/Survivalcraft/Component/ComponentLoot.cs','experiments/navigation-continuity/driver.py']
   for name in paths:
    p=root/name;p.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(original/name,p)
   stage=batch.load(root/'experiments/food-acquisition-evidence/stage.py')
   for name in stage.REQUIRED:
    p=build/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b'SYNTHETIC NONEXECUTABLE')
   manifest=batch.prepare(root,'p5',build,content,['a','b','c'],batch.sha(build/'ToolAgentGate.dll'))
   m=batch.manifest(manifest);self.assertEqual(len(m['cases']),9)
   for c in m['cases'][:3]:
    self.assertIn('ToolAgentGate.dll natural_food file '+c['seed'],(P(c['directory'])/'run.sh').read_text())
   spawn.assert_not_called()
   (P(m['cases'][0]['directory'])/'ToolAgentGate.dll').write_bytes(b'tampered')
   with self.assertRaises(ValueError):batch.manifest(manifest)

class RunTests(unittest.TestCase):
 class Process:
  def __init__(self,polls):self.polls=list(polls);self.returncode=None
  def poll(self):
   self.returncode=self.polls.pop(0) if self.polls else 0
   return self.returncode
 def test_official_ready_then_host_fixed_budget(self):
  from types import SimpleNamespace
  with tempfile.TemporaryDirectory() as d:
   c=dict(id='food-0',directory=d,kind='official_model',watchdogSeconds=195)
   driver=self.Process([None,0]);host=self.Process([0])
   helper=SimpleNamespace(terminate_owned=lambda p:None)
   controller=SimpleNamespace(VERIFIED_MODELS={'gpt-6.1-terra':{'none'}})
   with patch.object(batch,'load',side_effect=[controller,helper]),patch.object(batch.sys.stdin,'isatty',return_value=True),patch.object(batch,'read_rows',return_value=[{'event':'ready'}]) as ready,patch.object(batch.subprocess,'Popen',side_effect=[driver,host]) as spawn:
    result=batch.run_case(c,{},True,'gpt-6.1-terra','none')
   self.assertTrue(result['drainCertain']);self.assertEqual(result['hostExit'],0);self.assertEqual(spawn.call_count,2)
   command=spawn.call_args_list[0].args[0];self.assertEqual(command[command.index('--max-requests')+1],'8');self.assertEqual(command[command.index('--total-seconds')+1],'180')
   self.assertNotIn('key',' '.join(command).lower());ready.assert_called_once()
 def test_controller_not_ready_never_starts_host(self):
  from types import SimpleNamespace
  with tempfile.TemporaryDirectory() as d:
   c=dict(id='food-0',directory=d,kind='official_model',watchdogSeconds=195)
   with patch.object(batch,'load',side_effect=[SimpleNamespace(VERIFIED_MODELS={'gpt-6.1-terra':{'none'}}),SimpleNamespace(terminate_owned=lambda p:None)]),patch.object(batch.sys.stdin,'isatty',return_value=True),patch.object(batch.subprocess,'Popen',return_value=self.Process([1])) as spawn:
    result=batch.run_case(c,{},True,'gpt-6.1-terra','none')
   self.assertEqual(spawn.call_count,1);self.assertIsNone(result['hostExit']);self.assertIsNotNone(result['error'])
 def test_drain_uncertain_exposed(self):
  from types import SimpleNamespace
  with tempfile.TemporaryDirectory() as d:
   c=dict(id='direct_cancel',fixture='direct_cancel',directory=d,kind='deterministic_fixture',watchdogSeconds=80)
   def stop(p):raise RuntimeError('uncertain')
   with patch.object(batch,'load',return_value=SimpleNamespace(terminate_owned=stop)),patch.object(batch.subprocess,'Popen',side_effect=[self.Process([0]),self.Process([0])]):
    result=batch.run_case(c,{})
   self.assertFalse(result['drainCertain'])
class OverlapTests(unittest.TestCase):
 def data(self):
  lease=dict(RequestId='request',SessionId='session',ActorId='actor',PrecedingReceiptSequence=5)
  rows=[dict(kind='prefetch_requested',lease=lease,precedingRunId='run',monotonicSeconds=1)]
  for t,x in [(1.2,0),(1.3,.2)]:rows.append(dict(kind='predecessor_physical_sample',runId='run',sessionId='session',actorId='actor',receiptSequence=5,moving=True,status='running',settled=False,cancelRequested=False,monotonicSeconds=t,position=[x,0,0]))
  events=[dict(event=name,requestId='request',sessionId='session',actorId='actor',monotonicSeconds=t) for name,t in [('request_attempt',1.1),('request_result',1.5)]]
  return rows,events
 def test_same_run_actual_motion(self):
  rows,events=self.data();r=batch.measured_overlap(rows,events)[0];self.assertEqual(r['verdict'],'observed');self.assertAlmostEqual(r['path'],.2)
 def test_wrong_run_or_late_attempt_unknown(self):
  rows,events=self.data();rows[-1]['runId']='replacement';self.assertEqual(batch.measured_overlap(rows,events)[0]['verdict'],'unknown')
  rows,events=self.data();events[0]['monotonicSeconds']=1.4;self.assertEqual(batch.measured_overlap(rows,events)[0]['verdict'],'unknown')
 def test_missing_response_or_sparse_sample_unknown(self):
  rows,events=self.data();self.assertEqual(batch.measured_overlap(rows,events[:1])[0]['verdict'],'unknown')
  rows[-1]['monotonicSeconds']=1.49;self.assertEqual(batch.measured_overlap(rows,events)[0]['verdict'],'unknown')

if __name__=='__main__':unittest.main()
