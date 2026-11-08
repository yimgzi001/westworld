import unittest,time
from decision import compact,validate,ACTIONS
class Tests(unittest.TestCase):
 def setUp(self):
  self.o={'observation_id':1,'captured_unix':time.time(),'health':1,'air':1,'food':.2,'available_skills':list(ACTIONS),'resources':[{'index':0,'kind':'mature_pumpkin','distance':2,'nutrition':.15,'sickness_probability':0,'reachable':True,'gatherable':False}], 'food_inventory':[{'index':0,'count':1,'nutrition':.5,'sickness_probability':0,'provenance':'starter'}],'exploration_available':True}
 def choice(self,a,i=-1):return {'action':a,'target_index':i,'reason':'test'}
 def test_no_guid_leak(self):self.o['player']='secret';self.assertNotIn('player',compact(self.o))
 def test_approach_valid(self):self.assertIsNone(validate(self.choice('approach_food',0),self.o))
 def test_gather_not_ready(self):self.assertEqual(validate(self.choice('gather_food',0),self.o),'resource_not_ready')
 def test_starter_rejected(self):self.assertEqual(validate(self.choice('eat',0),self.o),'not_naturally_collected')
 def test_fixture_rejected(self):self.o['food_inventory'][0]['provenance']='fixture';self.assertEqual(validate(self.choice('eat',0),self.o),'not_naturally_collected')
 def test_real_natural_food_allowed(self):self.o['food_inventory'][0]['provenance']='natural_collected';self.assertIsNone(validate(self.choice('eat',0),self.o))
 def test_bad_resource_index(self):self.assertEqual(validate(self.choice('approach_food',7),self.o),'unknown_resource')
 def test_unavailable_skill(self):self.o['available_skills']=['stop'];self.assertEqual(validate(self.choice('inspect_food'),self.o),'unavailable_skill')
 def test_no_fake_explore(self):self.o['exploration_available']=False;self.assertEqual(validate(self.choice('explore_safe_ground'),self.o),'no_exploration_candidate')
 def test_nan_vitals(self):self.o['health']=float('nan');self.assertRaises(ValueError,compact,self.o)
 def test_unexpected_coordinate(self):c=self.choice('stop');c['x']=1;self.assertEqual(validate(c,self.o),'invalid_schema')
 def test_nonfinite_food(self):self.o['food_inventory'][0].update(provenance='natural_collected',nutrition=float('nan'));self.assertEqual(validate(self.choice('eat',0),self.o),'unsafe_food')
if __name__=='__main__':unittest.main(verbosity=2)
