import unittest,tempfile,pathlib,json,hashlib
import stage
class Tests(unittest.TestCase):
 def fixture(self):
  t=tempfile.TemporaryDirectory();self.addCleanup(t.cleanup);r=pathlib.Path(t.name)
  for f in ['ToolAgentGate.dll','Survivalcraft.dll','Westworld.Scapi.Adapter.dll','Westworld.Runtime.Core.dll','Westworld.Decisions.Local.dll','ToolAgentGate.runtimeconfig.json','ToolAgentGate.deps.json','runtimes/linux-x64/native/libglfw.so.3','runtimes/linux-x64/native/libnfd.so']:
   p=r/'build'/f;p.parent.mkdir(parents=True,exist_ok=True);p.write_text('fixture')
  for f in ['game/Survivalcraft/Component/ComponentPickableGathererPlayer.cs','experiments/navigation-continuity/driver.py','game/Survivalcraft/Subsystem/SubsystemTerrain.cs','game/Survivalcraft/Component/ComponentLoot.cs','experiments/food-acquisition-evidence/deterministic_driver.py','experiments/natural-resource-evidence/deterministic_driver.py','experiments/natural-resource-evidence/synthetic-schema.json']:
   p=r/f;p.parent.mkdir(parents=True,exist_ok=True);p.write_text('fixture '+f)
  (r/'content.zip').write_text('not a real game');return r
 def go(self,r,seed='seed_31-1'):return stage.stage(r,'case',r/'build',r/'content.zip',seed)
 def test_mode_and_no_launch(self):
  r=self.fixture();d=self.go(r);m=json.loads((d/'stage.json').read_text());self.assertEqual(m['mode'],'natural file seed_31-1');self.assertEqual(m['maximumDriverRequests'],8);self.assertFalse(m['automaticApi']);self.assertFalse((d/'doc').exists());self.assertIn('ToolAgentGate.dll natural file seed_31-1',(d/'run.sh').read_text())
 def test_frozen_hooks_driver(self):
  d=self.go(self.fixture());s=json.loads((d/'source-hashes.json').read_text());self.assertIn('game/Survivalcraft/Subsystem/SubsystemTerrain.cs',s);self.assertIn('game/Survivalcraft/Component/ComponentLoot.cs',s);self.assertIn('natural-resource-evidence',(d/'deterministic_driver.py').read_text())
 def test_hashes(self):
  d=self.go(self.fixture());h=json.loads((d/'installed-hashes.json').read_text());self.assertTrue(all(hashlib.sha256((d/p).read_bytes()).hexdigest()==v for p,v in h.items()))
 def test_seed_validation(self):
  r=self.fixture()
  for seed in ['', 'a;exit','../a','a b','a\nb','x'*65,None]:
   with self.subTest(seed=seed),self.assertRaises(ValueError):self.go(r,seed)
  self.assertFalse((r/'work/case').exists())
 def test_no_overwrite(self):
  r=self.fixture();self.go(r)
  with self.assertRaises(FileExistsError):self.go(r)
if __name__=='__main__':unittest.main()
