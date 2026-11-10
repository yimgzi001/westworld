"""Offline fixture tests; never launch a process, game, or API."""
import importlib.util,json,pathlib,tempfile,unittest
from unittest.mock import patch
P=pathlib.Path
spec=importlib.util.spec_from_file_location('model43_run',P(__file__).with_name('run.py'));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class Tests(unittest.TestCase):
 def test_import_has_fixed_food_budget(self):
  self.assertEqual((m.REQUESTS,m.SECONDS,m.WATCHDOG),(4,180,195))
  self.assertEqual(m.MODEL,'gpt-6.1-terra')
 def test_run_needs_explicit_permission(self):
  with patch.object(m.subprocess,'Popen',side_effect=AssertionError('must_not_launch')):
   with self.assertRaises(ValueError):m.run('/not-used',False)
 def test_run_needs_tty(self):
  with patch.object(m.sys.stdin,'isatty',return_value=False),patch.object(m.subprocess,'Popen',side_effect=AssertionError('must_not_launch')):
   with self.assertRaises(ValueError):m.run('/not-used',True)
 def test_summary_does_not_accept_pass_without_menu(self):
  with tempfile.TemporaryDirectory() as t:
   d=P(t);(d/'tool-agent.jsonl').write_text(json.dumps(dict(kind='result',passed=True))+'\n');(d/'api-audit.jsonl').write_text(json.dumps(dict(actualModel=m.MODEL))+'\n')
   s=m.summarize(d);self.assertFalse(s['passed']);self.assertEqual(s['usage'],[None])
 def test_summary_requires_actual_model(self):
  with tempfile.TemporaryDirectory() as t:
   d=P(t);a=[dict(kind='model_prompt',prompt='capabilities read_capability_schema'),dict(kind='result',passed=True),dict(kind='controller_request_published',requestId='r'),dict(kind='controller_response_received',requestId='r')]
   (d/'tool-agent.jsonl').write_text('\n'.join(map(json.dumps,a)))
   self.assertFalse(m.summarize(d)['passed'])
   (d/'api-audit.jsonl').write_text(json.dumps(dict(actualModel=m.MODEL,usage={'input_tokens':1},requestId='r',outcome='received')))
   self.assertTrue(m.summarize(d)['passed'])
 def test_verify_forbids_existing_world(self):
  with tempfile.TemporaryDirectory() as t:
   d=P(t);(d/'stage.json').write_text(json.dumps(dict(schema='model-menu43-stage-v1',model=m.MODEL,reasoningEffort='none',maximumDriverRequests=4,hostMaxRequests=4,maximumDriverSeconds=180,capabilityMenu=True)))
   (d/'run.sh').write_text('env WESTWORLD_CAPABILITY_MENU=1 dummy')
   (d/'installed-hashes.json').write_text(json.dumps({'run.sh':m.sha(d/'run.sh')}))
   (d/'doc').mkdir()
   with self.assertRaises(FileExistsError):m.verify(d)
if __name__=='__main__':unittest.main()
