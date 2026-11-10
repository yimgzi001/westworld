import tempfile,pathlib,unittest,json
from stage import prepare,launcher
class StageTests(unittest.TestCase):
 def setup_root(self,tmp):
  r=pathlib.Path(tmp);out=r/'experiments/scapi-tool-agent/bin/Debug/net10.0';out.mkdir(parents=True);(out/'ToolAgentGate.dll').write_bytes(b'fake-never-executed')
  a=r/'assets';a.mkdir();(a/'Content.zip').write_bytes(b'fake-never-executed')
  scripts=r/'experiments/batch47';scripts.mkdir(parents=True);(scripts/'summarize.py').write_text('# fixture only\n');(scripts/'monitor.py').write_text('# fixture only\n')
  return r,a,out
 def test_freeze_only_fixed_mode(self):
  with tempfile.TemporaryDirectory() as tmp:
   r,a,_=self.setup_root(tmp);d,b=prepare(r,'trial',a);s=(d/'run.sh').read_text()
   self.assertIn('--mode matrix_scale47',s);self.assertNotIn('llama',s);self.assertIn('monitor47.py',s)
   self.assertFalse((d/'doc').exists());self.assertFalse((d/'console.log').exists())
   self.assertIn('summarize47.py',json.loads((d/'installed-hashes.json').read_text()))
   self.assertIn('summary_code',b.read_text())
 def test_existing_run_rejected(self):
  with tempfile.TemporaryDirectory() as tmp:
   r,a,_=self.setup_root(tmp);prepare(r,'trial',a)
   with self.assertRaises(ValueError):prepare(r,'trial',a)
 def test_mutable_build_rejected(self):
  with tempfile.TemporaryDirectory() as tmp:
   r,a,out=self.setup_root(tmp);(out/'doc').mkdir()
   with self.assertRaises(ValueError):prepare(r,'trial',a)
 def test_invalid_label_rejected(self):
  with tempfile.TemporaryDirectory() as tmp:
   r,a,_=self.setup_root(tmp)
   with self.assertRaises(ValueError):prepare(r,'../outside',a)
 def test_missing_artifacts_rejected(self):
  with tempfile.TemporaryDirectory() as tmp:
   r,a,out=self.setup_root(tmp);(out/'ToolAgentGate.dll').unlink()
   with self.assertRaises(ValueError):prepare(r,'trial',a)
 def test_symlink_rejected(self):
  with tempfile.TemporaryDirectory() as tmp:
   r,a,out=self.setup_root(tmp);(out/'linked').symlink_to(a/'Content.zip')
   with self.assertRaises(ValueError):prepare(r,'trial',a)
 def test_four_actor_environment_and_path(self):
  with tempfile.TemporaryDirectory() as tmp:
   r,a,_=self.setup_root(tmp);d,b=prepare(r,'trial',a,'scale50')
   self.assertIn('scale50',d.name);self.assertIn('"WESTWORLD_SCALE47_ACTORS": "50"',(d/'run.sh').read_text())
 def test_unreviewed_count_rejected(self):
  with tempfile.TemporaryDirectory() as tmp:
   r,a,_=self.setup_root(tmp)
   with self.assertRaises(ValueError):prepare(r,'trial',a,'scale51')
if __name__=='__main__':unittest.main()
