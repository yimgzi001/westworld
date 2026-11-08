import unittest
from dataclasses import replace
from grid_planner import Cell,Planner,ProgressGuard
class Tests(unittest.TestCase):
 def setUp(self):self.p=Planner();self.m={(x,0):Cell(x,0,64,True,True,'solid',2,0) for x in range(5)}
 def test_flat_route(self):self.assertEqual(self.p.plan(self.m,(0,0),(4,0),1).cells,tuple((x,0) for x in range(5)))
 def test_unknown_not_traversed(self):del self.m[(2,0)];self.assertEqual(self.p.plan(self.m,(0,0),(4,0),1).status,'no_safe_route')
 def test_water_foliage_and_hazard(self):
  for kind in ('water','foliage','lava','unknown','hazard'):
   self.m[(2,0)]=replace(self.m[(2,0)],kind=kind);self.assertEqual(self.p.plan(self.m,(0,0),(4,0),1).status,'no_safe_route')
 def test_height_jump_rejected(self):self.m[(2,0)]=replace(self.m[(2,0)],y=66);self.assertEqual(self.p.plan(self.m,(0,0),(4,0),1).status,'no_safe_route')
 def test_one_step_allowed(self):self.m[(2,0)]=replace(self.m[(2,0)],y=65);self.assertEqual(self.p.plan(self.m,(0,0),(4,0),1).status,'ok')
 def test_no_corner_cut(self):self.m={(0,0):self.m[(0,0)],(1,1):Cell(1,1,64,True,True,'solid',2,0)};self.assertEqual(self.p.plan(self.m,(0,0),(1,1),1).status,'no_safe_route')
 def test_headroom(self):self.m[(2,0)]=replace(self.m[(2,0)],headroom=1);self.assertEqual(self.p.plan(self.m,(0,0),(4,0),1).status,'no_safe_route')
 def test_stale(self):self.assertEqual(self.p.plan(self.m,(0,0),(4,0),20).status,'unsafe_endpoint')
 def test_explore_observed_only(self):r=self.p.exploration(self.m,(0,0),{(0,0)},1);self.assertEqual(r.status,'ok');self.assertTrue(all(x in self.m for x in r.cells))
 def test_nan_rejected(self):self.m[(2,0)]=replace(self.m[(2,0)],y=float('nan'));self.assertEqual(self.p.plan(self.m,(0,0),(4,0),1).status,'no_safe_route')
 def test_stall(self):g=ProgressGuard((0,0),0);self.assertEqual(g.check((0,0),(3,0),0,1,1,0),'running');self.assertEqual(g.check((0,0),(3,0),4,1,1,0),'stall_stop')
 def test_guard_harm_stale_deadline(self):
  for health,age in ((.9,0),(1,3)):
   self.assertEqual(ProgressGuard((0,0),0).check((0,0),(3,0),1,health,1,age),'unsafe')
  self.assertEqual(ProgressGuard((0,0),0).check((0,0),(3,0),31,1,1,0),'deadline_stop')
if __name__=='__main__':unittest.main(verbosity=2)
