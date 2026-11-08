import unittest
from model_skill_gateway import Gateway
class SafetyTests(unittest.TestCase):
 def setUp(self):
  self.g=object.__new__(Gateway);self.g.state=lambda:{'state':{'health':1,'air':1}}
 def test_uncertain_eat_is_locked(self):
  self.g.eat_uncertain=True
  self.assertEqual(self.g.eat()['reason'],'prior_eat_uncertain_no_retry')
 def test_no_verified_natural_food(self):
  self.g.eat_uncertain=False;self.g.acquired=0
  self.assertEqual(self.g.eat()['reason'],'no_session_verified_natural_food')
 def test_health_drop_stops(self):
  with self.assertRaises(RuntimeError):self.g.safe({'health':.999,'air':1})
 def test_air_drop_stops(self):
  with self.assertRaises(RuntimeError):self.g.safe({'health':1,'air':.999})
if __name__=='__main__':unittest.main()
