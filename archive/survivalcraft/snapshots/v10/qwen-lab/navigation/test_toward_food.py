import unittest
from toward_food import propose
class Tests(unittest.TestCase):
 def setUp(self):self.m={'found':[{'x':20,'y':65,'z':0}], 'surface':[{'x':x,'y':64,'z':0,'type':'GrassBlock','solid':True,'headroom':2} for x in range(12)]}
 def test_found_does_not_prevent_approach(self):r=propose(self.m,[.5,65,.5],[20,65,0]);self.assertEqual(r['status'],'candidate_progress');self.assertEqual(len(r['route']),9)
 def test_never_walk_into_food(self):r=propose(self.m,[.5,65,.5],[4,65,0]);self.assertEqual(r['status'],'candidate_adjacent');self.assertEqual(r['route'][-1]['cell'],[3,0])
 def test_adjacent_stop(self):self.assertEqual(propose(self.m,[3.5,65,.5],[4,65,0])['status'],'adjacent_to_food_stop_and_inspect')
 def test_no_unknown_bridge(self):self.m['surface']=self.m['surface'][:1];self.assertEqual(propose(self.m,[.5,65,.5],[20,65,0])['status'],'no_safe_progress_within_8_steps')
 def test_visited_no_oscillation(self):self.assertEqual(propose(self.m,[.5,65,.5],[20,65,0],{(x,0) for x in range(12)})['status'],'no_safe_progress_within_8_steps')
if __name__=='__main__':unittest.main()
