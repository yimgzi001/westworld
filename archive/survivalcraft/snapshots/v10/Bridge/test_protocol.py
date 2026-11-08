import tempfile,unittest,json,pathlib
from controller import issue
class ProtocolTest(unittest.TestCase):
 def test_atomic_and_isolated(self):
  with tempfile.TemporaryDirectory() as d:
   a=issue(d,'aaaa','move',z=1);b=issue(d,'bbbb','stop')
   self.assertEqual(json.loads((pathlib.Path(d)/'aaaa.command.json').read_text()),a)
   self.assertEqual(json.loads((pathlib.Path(d)/'bbbb.command.json').read_text()),b)
   self.assertEqual(len(list(pathlib.Path(d).glob('*.tmp'))),0)
 def test_reject_bad_input(self):
  with tempfile.TemporaryDirectory() as d:
   for player,action,duration in [('../x','move',.2),('aaaa','teleport',.2),('aaaa','move',9)]:
    with self.assertRaises(ValueError):issue(d,player,action,seconds=duration)
if __name__=='__main__':unittest.main()
