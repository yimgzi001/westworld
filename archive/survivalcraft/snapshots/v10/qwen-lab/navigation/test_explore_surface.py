import unittest
from explore_surface import propose
class Tests(unittest.TestCase):
 def setUp(self):self.m={'surface':[{'x':x,'z':0,'y':64,'type':'GrassBlock','solid':True,'headroom':2} for x in range(10)],'found':[]}
 def test_bounded_novel_route(self):
  r=propose(self.m,[.5,65,.5],{(0,0)});self.assertEqual(r['status'],'candidate');self.assertLessEqual(len(r['route'])-1,8);self.assertNotEqual(r['goal'],[0,0])
 def test_no_repeated_visited_goal(self):self.assertEqual(propose(self.m,[.5,65,.5],{(x,0) for x in range(10)})['status'],'no_unvisited_safe_grass_goal')
 def test_stop_when_food_found(self):self.m['found']=[{'x':2}];self.assertEqual(propose(self.m,[.5,65,.5],set())['status'],'food_observed_stop_exploration')
 def test_water_not_crossed(self):self.m['surface'][1]['type']='WaterBlock';self.assertEqual(propose(self.m,[.5,65,.5],set())['status'],'no_unvisited_safe_grass_goal')
 def test_mismatch_stops(self):self.assertEqual(propose(self.m,[.5,67,.5],set())['status'],'start_height_mismatch')
if __name__=='__main__':unittest.main()
