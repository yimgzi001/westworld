import hashlib,json,pathlib,tempfile,unittest
import stage
class StageTests(unittest.TestCase):
 def setup(self,tmp):
  root=pathlib.Path(tmp);out=root/'built';out.mkdir()
  for name in stage.REQUIRED:
   p=out/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b'fixture-not-executable')
  content=root/'Content.zip';content.write_bytes(b'fixture-not-game')
  driver=root/'experiments/food-acquisition-evidence/deterministic_driver.py';driver.parent.mkdir(parents=True);driver.write_text('# synthetic driver')
  (root/'runtime/x').mkdir(parents=True);(root/'runtime/x/x.cs').write_text('// fixture');(out/'.env').write_text('not copied')
  for relative in stage.SOURCE_FILES:
   p=root/relative;p.parent.mkdir(parents=True,exist_ok=True);p.write_text('// native source fixture')
  return root,out,content
 def test_stage_exact_mode_no_autostart(self):
  with tempfile.TemporaryDirectory() as tmp:
   root,out,content=self.setup(tmp);dest=stage.stage(root,'food24',out,content);m=json.loads((dest/'stage.json').read_text());self.assertEqual(m['mode'],'food_drop file');self.assertFalse(m['automaticApi']);self.assertFalse(m['automaticDriver']);self.assertNotIn('handoff',(dest/'run.sh').read_text());self.assertNotIn('driver.py',(dest/'run.sh').read_text());self.assertFalse((dest/'.env').exists())
 def test_all_installed_hashes(self):
  with tempfile.TemporaryDirectory() as tmp:
   root,out,content=self.setup(tmp);dest=stage.stage(root,'food24',out,content)
   for name,h in json.loads((dest/'installed-hashes.json').read_text()).items():self.assertEqual(hashlib.sha256((dest/name).read_bytes()).hexdigest(),h)
   self.assertIn('run.sh',json.loads((dest/'installed-hashes.json').read_text()))
 def test_model_controller_source_frozen_and_hashed(self):
  with tempfile.TemporaryDirectory() as tmp:
   root,out,content=self.setup(tmp);folder=root/'experiments/openai-tool-controller';folder.mkdir(parents=True)
   source=folder/'controller.py';source.write_text('# synthetic frozen controller')
   evidence=folder/'evidence';evidence.mkdir();(evidence/'private.py').write_text('# must not stage')
   dest=stage.stage(root,'food24',out,content,'model');relative='experiments/openai-tool-controller/controller.py'
   frozen=dest/'sources'/relative;expected=hashlib.sha256(source.read_bytes()).hexdigest()
   self.assertEqual(json.loads((dest/'source-hashes.json').read_text())[relative],expected)
   self.assertEqual(json.loads((dest/'installed-hashes.json').read_text())['sources/'+relative],expected)
   source.write_text('# changed after stage');self.assertEqual(frozen.read_text(),'# synthetic frozen controller')
   self.assertFalse((dest/'sources/experiments/openai-tool-controller/evidence').exists())
 def test_native_component_source_frozen(self):
  with tempfile.TemporaryDirectory() as tmp:
   root,out,content=self.setup(tmp);dest=stage.stage(root,'food28',out,content)
   for relative in stage.SOURCE_FILES:
    self.assertEqual((dest/'sources'/relative).read_bytes(),(root/relative).read_bytes())
    self.assertIn(relative,json.loads((dest/'source-hashes.json').read_text()))
 def test_missing_component_no_partial_stage(self):
  with tempfile.TemporaryDirectory() as tmp:
   root,out,content=self.setup(tmp);(root/stage.SOURCE_FILES[0]).unlink()
   with self.assertRaises(FileNotFoundError):stage.stage(root,'food28',out,content)
   self.assertFalse((root/'work/food28').exists())
 def test_direct_handoff_mode_is_exact(self):
  with tempfile.TemporaryDirectory() as tmp:
   root,out,content=self.setup(tmp);dest=stage.stage(root,'direct29',out,content,handoff_case='direct_early')
   self.assertEqual(json.loads((dest/'stage.json').read_text())['mode'],'handoff file direct_early')
   self.assertIn('ToolAgentGate.dll handoff file direct_early >console.log',(dest/'run.sh').read_text())
   self.assertNotIn('food_drop file',(dest/'run.sh').read_text())
 def test_direct_model_exact_mode_and_explicit_controller(self):
  with tempfile.TemporaryDirectory() as tmp:
   root,out,content=self.setup(tmp)
   with self.assertRaises(ValueError):stage.stage(root,'model30',out,content,handoff_case='direct_model')
   self.assertFalse((root/'work/model30').exists())
   dest=stage.stage(root,'model30',out,content,'model','direct_model')
   m=json.loads((dest/'stage.json').read_text())
   self.assertEqual(m['mode'],'handoff file direct_model')
   self.assertEqual(m['maximumDriverRequests'],4);self.assertEqual(m['maximumDriverSeconds'],180)
   self.assertIn('ToolAgentGate.dll handoff file direct_model >console.log',(dest/'run.sh').read_text())
   self.assertNotIn('controller.py',(dest/'run.sh').read_text())
 def test_unknown_case_no_stage(self):
  with tempfile.TemporaryDirectory() as tmp:
   root,out,content=self.setup(tmp)
   with self.assertRaises(ValueError):stage.stage(root,'direct29',out,content,handoff_case='bad; command')
   self.assertFalse((root/'work/direct29').exists())
 def test_fresh_required(self):
  with tempfile.TemporaryDirectory() as tmp:
   root,out,content=self.setup(tmp);stage.stage(root,'food24',out,content)
   with self.assertRaises(FileExistsError):stage.stage(root,'food24',out,content)
 def test_missing_native_no_partial_stage(self):
  with tempfile.TemporaryDirectory() as tmp:
   root,out,content=self.setup(tmp);(out/stage.REQUIRED[-1]).unlink()
   with self.assertRaises(FileNotFoundError):stage.stage(root,'food24',out,content)
   self.assertFalse((root/'work/food24').exists())
 def test_model_is_metadata_only(self):
  with tempfile.TemporaryDirectory() as tmp:
   root,out,content=self.setup(tmp);dest=stage.stage(root,'food24',out,content,'model');self.assertEqual(json.loads((dest/'stage.json').read_text())['controllerKind'],'model');self.assertNotIn('http',(dest/'run.sh').read_text())
 def test_path_escape(self):
  with tempfile.TemporaryDirectory() as tmp:
   root,out,content=self.setup(tmp)
   with self.assertRaises(ValueError):stage.stage(root,'../bad',out,content)
 def test_symlink(self):
  with tempfile.TemporaryDirectory() as tmp:
   root,out,content=self.setup(tmp);link=root/'link';link.symlink_to(out,target_is_directory=True)
   with self.assertRaises(ValueError):stage.stage(root,'food24',link,content)
if __name__=='__main__':unittest.main()
