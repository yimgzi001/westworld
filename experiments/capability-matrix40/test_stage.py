import unittest
from stage import runtime_environment
class EnvironmentTests(unittest.TestCase):
 def test_social_flag_name_preserved(self):
  self.assertEqual(runtime_environment('baseline',True),{'WESTWORLD_CAPABILITY_MENU':'1','WESTWORLD_MATRIX41':'baseline','WESTWORLD_SOCIAL42':'1'})
 def test_single_family(self):
  self.assertEqual(runtime_environment('sleep',False)['WESTWORLD_SOCIAL42'],'0')
  self.assertEqual(runtime_environment('sleep',False)['WESTWORLD_MATRIX41'],'sleep')
if __name__=='__main__':unittest.main()
