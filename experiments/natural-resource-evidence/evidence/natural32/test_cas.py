"""仅合成临时目录测试，不读取或执行三场自然世界运行。"""
import importlib.util,json,pathlib,tempfile,unittest
spec=importlib.util.spec_from_file_location('natural32_cas',pathlib.Path(__file__).with_name('cas.py'));cas=importlib.util.module_from_spec(spec);spec.loader.exec_module(cas)
class StoreTests(unittest.TestCase):
 def test_roundtrip_and_dedup(self):
  with tempfile.TemporaryDirectory() as directory:
   root=pathlib.Path(directory);s=cas.Store(root,root/'new');key=s.add('source-file','中文源码')
   self.assertEqual(key,s.add('source-file','中文源码'));self.assertEqual(s.counts()['newBundles'],1)
   s.commit();value,_,files=cas.decode(root,s.refs[key]);self.assertEqual(value,'中文源码');self.assertEqual(len(files),2)
 def test_shared_blob_no_repack(self):
  with tempfile.TemporaryDirectory() as directory:
   root=pathlib.Path(directory);old=cas.Store(root,root/'old');key=old.add('source-file','frozen bytes');old.commit()
   ref=old.refs[key];(root/'old/manifest.json').write_bytes(cas.packed({'bundles':{key:{'manifest':pathlib.Path(ref['manifest']).name,'sha256':ref['sha256']}}}))
   new=cas.Store(root,root/'new',('old',));self.assertEqual(new.add('source-file','frozen bytes'),key)
   self.assertEqual(new.counts()['newPayloadFiles'],0);self.assertEqual(new.counts()['reusedBundles'],1)
   self.assertEqual(cas.decode(root,new.refs[key])[0],'frozen bytes')
 def test_tamper_rejected(self):
  with tempfile.TemporaryDirectory() as directory:
   root=pathlib.Path(directory);store=cas.Store(root,root/'out');key=store.add('test','value');store.commit()
   next((root/'out').glob('*.part001')).write_bytes(b'wrong')
   with self.assertRaises(ValueError):cas.decode(root,store.refs[key])
 def test_path_escape_rejected(self):
  with tempfile.TemporaryDirectory() as directory:
   root=pathlib.Path(directory)
   for path in ('../outside','/absolute'):
    with self.assertRaises(ValueError):cas.safe(root,path)
 def test_credential_pattern_rejected(self):
  with tempfile.TemporaryDirectory() as directory:
   root=pathlib.Path(directory);store=cas.Store(root,root/'out')
   with self.assertRaises(ValueError):store.add('test','Bearer '+'x'*24)
 def test_prepare_does_not_write(self):
  with tempfile.TemporaryDirectory() as directory:
   root=pathlib.Path(directory);store=cas.Store(root,root/'out');store.add('source-file','frozen')
   self.assertFalse((root/'out').exists())
if __name__=='__main__':unittest.main()
