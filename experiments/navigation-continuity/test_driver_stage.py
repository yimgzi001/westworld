import importlib.util,json,pathlib,tempfile,unittest,uuid

def mod(name):
 s=importlib.util.spec_from_file_location(name,pathlib.Path(__file__).with_name(name+'.py'));m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
d=mod('driver');st=mod('stage')
class DriverTests(unittest.TestCase):
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);self.root=pathlib.Path(self.tmp.name);self.t=0
 def clock(self):return self.t
 def sleep(self,n):self.t+=n
 def request(self,n):
  p=self.root/'control'/str(n);p.mkdir(parents=True);v={k:str(uuid.uuid4()) for k in d.IDS};v['protocol']='westworld-tool-file-v1';(p/'request.json').write_text(json.dumps(v));return p
 def test_two_responses_delay(self):
  a=self.request(1);b=self.request(2);r=d.run(self.root,3,self.clock,self.sleep);self.assertEqual(r['responses'],2);self.assertAlmostEqual(self.t,3);self.assertEqual(json.loads((a/'response.json').read_text())['decision']['steps'][0]['arguments']['distance'],3);self.assertEqual(json.loads((b/'response.json').read_text())['decision']['steps'][0]['arguments']['distance'],1)
 def test_third_not_processed(self):
  self.request(1);self.request(2);c=self.request(3);d.run(self.root,1,self.clock,self.sleep);self.assertFalse((c/'response.json').exists())
 def test_host_exit_during_delay(self):
  self.request(1);b=self.request(2)
  def sleep(n):self.sleep(n);(self.root/'exit.txt').write_text('0')
  r=d.run(self.root,8,self.clock,sleep);self.assertEqual(r['responses'],1);self.assertFalse((b/'response.json').exists())
 def test_cancel_no_response(self):
  self.request(1);b=self.request(2);(b/'canceled.json').write_text('{}');r=d.run(self.root,1,self.clock,self.sleep);self.assertEqual(r['responses'],1)
 def test_deadline_no_requests(self):
  r=d.run(self.root,1,self.clock,self.sleep);self.assertEqual(r['reason'],'deadline');self.assertAlmostEqual(self.t,65)
 def test_repeat_no_replay(self):
  (self.root/'exit.txt').write_text('0');d.run(self.root,1,self.clock,self.sleep)
  with self.assertRaises(FileExistsError):d.run(self.root,1,self.clock,self.sleep)
 def test_bound_delay(self):
  with self.assertRaises(ValueError):d.run(self.root,2,self.clock,self.sleep)
 def test_stage_selective(self):
  out=self.root/'experiments/scapi-tool-agent/bin/Release/net10.0';out.mkdir(parents=True)
  for n in ['ToolAgentGate.dll','Survivalcraft.dll','Westworld.Scapi.Adapter.dll','Westworld.Runtime.Core.dll','Westworld.Decisions.Local.dll','ToolAgentGate.runtimeconfig.json','ToolAgentGate.deps.json','secret.key','model.gguf']:(out/n).write_text('fixture')
  native=out/'runtimes/linux-x64/native';native.mkdir(parents=True)
  for name in ['libglfw.so.3','libglfw.so.3.4','libnfd.so']:(native/name).write_text('fake native library')
  src=self.root/'experiments/navigation-continuity';src.mkdir(parents=True);(src/'driver.py').write_text('# fixed fixture');(src/'stage.py').write_text('# fixed fixture');content=self.root/'Content.zip';content.write_text('not real content')
  result=st.stage(self.root,'test',1,1,content);self.assertFalse((result/'secret.key').exists());self.assertFalse((result/'model.gguf').exists());self.assertTrue((result/'installed-hashes.json').exists());self.assertIn('handoff file early',(result/'run.sh').read_text())
  for name in ['libglfw.so.3','libglfw.so.3.4','libnfd.so']:self.assertTrue((result/'runtimes/linux-x64/native'/name).is_file())
  with self.assertRaises(FileExistsError):st.stage(self.root,'test',1,1,content)
  for case in st.CASES[1:]:
   result=st.stage(self.root,'test',1,1,content,case);metadata=json.loads((result/'stage.json').read_text());self.assertEqual(metadata['case'],case);self.assertEqual(metadata['status'],'staged_not_run');self.assertIn('handoff file '+case,(result/'run.sh').read_text())
  with self.assertRaises(ValueError):st.stage(self.root,'test',1,1,content,'arbitrary')
  (native/'libglfw.so.3').unlink()
  with self.assertRaises(FileNotFoundError):st.stage(self.root,'missing-glfw',1,1,content)
  self.assertFalse((self.root/'work/missing-glfw-delay1-repeat1').exists())

 def test_library_versions_whitelist(self):
  for name in ['libglfw.so','libglfw.so.3','libglfw.so.3.4','libx.so.1.2.3','glfw3.dll','libglfw.3.dylib']:
   self.assertTrue(st.native_library_name(name),name)
  for name in ['secret.key','model.gguf','private.json','libglfw.so.3.key','libglfw.so.secret','readme.so.txt']:
   self.assertFalse(st.native_library_name(name),name)
