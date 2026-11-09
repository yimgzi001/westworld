import importlib.util,json,pathlib,tempfile,unittest
from unittest.mock import patch
spec=importlib.util.spec_from_file_location('recovery',pathlib.Path(__file__).with_name('recovery.py'));r=importlib.util.module_from_spec(spec);spec.loader.exec_module(r)
class Tests(unittest.TestCase):
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);self.root=pathlib.Path(self.tmp.name);(self.root/'code.py').write_text('source')
 def begin(self):return r.begin(self.root,'case01','native','dot-cloud',['code.py'],['result.json'])
 def result(self): (self.root/'result.json').write_text('{"passed":true}')
 def test_complete(self):
  self.begin();self.result();r.finish(self.root,'case01','passed');v=r.inspect(self.root,'case01','dot-cloud');self.assertEqual(v['recoveryStatus'],'verified_record');self.assertFalse(v['automaticReplay'])
 def test_running_unknown_even_result_present(self):
  self.begin();self.result();v=r.inspect(self.root,'case01');self.assertEqual(v['recoveryStatus'],'interrupted_unknown');self.assertEqual(v['issues'][0]['kind'],'uncommitted_result')
 def test_failed_record_not_success(self):
  self.begin();self.result();r.finish(self.root,'case01','failed');self.assertEqual(r.inspect(self.root,'case01')['declaredOutcome'],'failed')
 def test_missing_result_prevents_finish(self):
  self.begin()
  with self.assertRaises(FileNotFoundError):r.finish(self.root,'case01','passed')
  self.assertEqual(r.inspect(self.root,'case01')['declaredOutcome'],'running')
 def test_changed_source(self):
  self.begin();self.result();(self.root/'code.py').write_text('different')
  with self.assertRaisesRegex(r.RecoveryError,'source_changed'):r.finish(self.root,'case01','passed')
 def test_result_tamper(self):
  self.begin();self.result();r.finish(self.root,'case01','passed');(self.root/'result.json').write_text('tamper');self.assertEqual(r.inspect(self.root,'case01')['issues'][0]['kind'],'hash_changed')
 def test_missing_after_finish(self):
  self.begin();self.result();r.finish(self.root,'case01','passed');(self.root/'result.json').unlink();self.assertEqual(r.inspect(self.root,'case01')['issues'][0]['kind'],'missing')
 def test_atomic_failure_preserves_checkpoint(self):
  self.begin();self.result();p=self.root/'.batch-checkpoints/case01.json';before=p.read_bytes()
  with patch.object(r.os,'replace',side_effect=OSError('private content')):
   with self.assertRaises(OSError):r.finish(self.root,'case01','passed')
  self.assertEqual(p.read_bytes(),before);self.assertEqual(list(p.parent.glob('.checkpoint-*')),[])
 def test_duplicate_never_replays(self):
  self.begin()
  with self.assertRaisesRegex(r.RecoveryError,'case_exists'):self.begin()
 def test_path_escape(self):
  for p in ['../x','/tmp/x','a/../x','a//x','a\\x','./x']:
   with self.subTest(p=p),self.assertRaises(r.RecoveryError):r.begin(self.root,'case01','native','dot',['code.py'],[p])
 def test_symlink(self):
  (self.root/'link').symlink_to(self.root,target_is_directory=True)
  with self.assertRaisesRegex(r.RecoveryError,'symlink'):r.begin(self.root,'case01','native','dot',['link/code.py'],['result.json'])
 def test_checkpoint_symlink(self):
  (self.root/'.batch-checkpoints').symlink_to(self.root,target_is_directory=True)
  with self.assertRaisesRegex(r.RecoveryError,'symlink'):self.begin()
 def test_result_symlink_after_begin(self):
  self.begin();(self.root/'result.json').symlink_to(self.root/'code.py')
  with self.assertRaisesRegex(r.RecoveryError,'symlink'):r.finish(self.root,'case01','passed')
 def test_environment_mismatch(self):
  self.begin();self.result();r.finish(self.root,'case01','passed');self.assertEqual(r.inspect(self.root,'case01','different')['recoveryStatus'],'integrity_mismatch')
 def test_lock_no_pid_assumptions(self):
  (self.root/'.batch-checkpoints').mkdir();(self.root/'.batch-checkpoints/case01.lock').write_text('obsolete pid')
  with self.assertRaisesRegex(r.RecoveryError,'manual_review'):self.begin()
 def test_schema_unknown_fields(self):
  self.begin();p=self.root/'.batch-checkpoints/case01.json';v=json.loads(p.read_text());v['pid']=123;p.write_text(json.dumps(v))
  with self.assertRaisesRegex(r.RecoveryError,'schema'):r.inspect(self.root,'case01')
 def test_no_file_contents_in_checkpoint(self):
  (self.root/'code.py').write_text('private-not-a-real-key');self.begin();self.assertNotIn('private-not-a-real-key',(self.root/'.batch-checkpoints/case01.json').read_text())
 def test_finish_once(self):
  self.begin();self.result();r.finish(self.root,'case01','passed')
  with self.assertRaisesRegex(r.RecoveryError,'not_running'):r.finish(self.root,'case01','passed')

 def test_existing_result_rejected(self):
  self.result()
  with self.assertRaisesRegex(r.RecoveryError,'result_exists_requires_retrospective'):self.begin()
  self.assertFalse((self.root/'.batch-checkpoints/case01.json').exists())
 def test_retrospective_explicit(self):
  self.result();r.begin(self.root,'case01','old-native','dot-cloud',['code.py'],['result.json'],retrospective=True)
  r.finish(self.root,'case01','passed');self.assertTrue(r.inspect(self.root,'case01')['retrospective'])
 def test_retrospective_stage_label_not_permission(self):
  self.result()
  with self.assertRaisesRegex(r.RecoveryError,'result_exists_requires_retrospective'):
   r.begin(self.root,'case01','retrospective','dot-cloud',['code.py'],['result.json'])
 def test_old_schema_stays_readable_unknown_retrospective(self):
  self.begin();p=self.root/'.batch-checkpoints/case01.json';v=json.loads(p.read_text());del v['retrospective'];p.write_text(json.dumps(v))
  self.assertIsNone(r.inspect(self.root,'case01')['retrospective']);self.result();r.finish(self.root,'case01','passed');self.assertIsNone(r.inspect(self.root,'case01')['retrospective'])
 def test_retrospective_not_truthy_string(self):
  with self.assertRaisesRegex(r.RecoveryError,'retrospective_flag'):r.begin(self.root,'case01','native','dot',['code.py'],['result.json'],retrospective='false')
