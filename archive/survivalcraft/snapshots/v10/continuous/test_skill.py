import unittest
from skill import Skill,SkillSlot
class Tests(unittest.TestCase):
 def test_new_plan_cannot_reverse_running_skill(self):
  s=SkillSlot();s.prepare('forward');s.start_if_idle({'x':0,'y':0,'z':0},0,0);s.prepare('backward');self.assertEqual(s.start_if_idle({},0,1).direction,'forward')
 def test_distance(self):self.assertEqual(Skill('forward',{'x':0,'y':0,'z':0},0,0).evaluate({'x':0,'y':0,'z':.21},.2,2),'distance_complete')
 def test_game_budget(self):self.assertEqual(Skill('forward',{'x':0,'y':0,'z':0},0,0).evaluate({'x':0,'y':0,'z':0},1,8),'game_budget_complete')
 def test_wall_deadline(self):self.assertEqual(Skill('forward',{'x':0,'y':0,'z':0},0,0).evaluate({'x':0,'y':0,'z':0},.1,18),'timeout')
 def test_danger_interrupts(self):self.assertEqual(Skill('forward',{'x':0,'y':0,'z':0},0,0).evaluate({'x':0,'y':0,'z':.3},.2,1,health=.9),'unsafe')
 def test_stale_interrupts(self):self.assertEqual(Skill('forward',{'x':0,'y':0,'z':0},0,0).evaluate({'x':0,'y':0,'z':0},.2,1,state_age=2.1),'unsafe')
 def test_nan_rejected(self):
  p={'x':0,'y':0,'z':0}
  for kwargs in ({'tick':float('nan'),'now':0},{'tick':0,'now':0,'health':float('nan')},{'tick':0,'now':0,'state_age':-1}):
   self.assertEqual(Skill('forward',p,0,0).evaluate(p,**kwargs),'unsafe')
 def test_clock_rollback_rejected(self):
  p={'x':0,'y':0,'z':0};s=Skill('forward',p,0,0);s.evaluate(p,.5,1)
  self.assertEqual(s.evaluate(p,.4,2),'unsafe')
 def test_nan_position_rejected(self):
  self.assertEqual(Skill('forward',{'x':0,'y':0,'z':0},0,0).evaluate({'x':float('nan'),'y':0,'z':0},0,0),'unsafe')
if __name__=='__main__':unittest.main()
