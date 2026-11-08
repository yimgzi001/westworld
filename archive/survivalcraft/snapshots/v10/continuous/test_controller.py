import unittest
from concurrent.futures import Future
from controller import Controller,State,Plan,PlannerPool
from threading import Event
import time
class FakePlanner:
 def __init__(self):self.calls=[]
 def submit(self,s):
  f=Future(); self.calls.append(f); return f
class Tests(unittest.TestCase):
 def setUp(self):
  self.p=FakePlanner();self.sent=[];self.c=Controller('LabBot1',self.p,self.sent.append,replan_after=.2)
 def tick(self,t,health=1,observed=None):self.c.tick(State('LabBot1',t if observed is None else observed,health,{}),t)
 def test_waiting_does_not_stop_last_plan(self):
  self.tick(0);self.p.calls[0].set_result(Plan('move',z=1,horizon=3));self.tick(.1)
  self.tick(.4);self.tick(.6);self.tick(1)
  self.assertEqual(len(self.p.calls),2);self.assertTrue(all(x['action']=='move' for x in self.sent));self.assertEqual(self.c.mode,'executing+planning')
 def test_delayed_plan_expires_and_stops(self):
  self.tick(0);self.p.calls[0].set_result(Plan('move',z=1,horizon=.5));self.tick(.1);self.tick(.7)
  self.assertEqual(self.sent[-1]['action'],'stop')
 def test_stale_state_latches_stop(self):
  self.tick(0);self.p.calls[0].set_result(Plan('move',z=1));self.tick(.1);self.tick(3,observed=.1);self.tick(3.2)
  self.assertEqual(self.sent[-1]['action'],'stop');self.assertTrue(self.c.emergency_latched)
 def test_damage_and_death_stop(self):
  self.tick(0);self.p.calls[0].set_result(Plan('move',z=1));self.tick(.1);self.tick(.2,health=.8)
  self.assertEqual(self.sent[-1]['action'],'stop')
 def test_late_response_not_applied_after_emergency(self):
  self.tick(0);self.p.calls[0].set_running_or_notify_cancel();self.tick(.1,health=0)
  self.p.calls[0].set_result(Plan('move',z=1));self.c.resume();self.tick(.2)
  self.assertFalse(any(c['action']=='move' for c in self.sent))
 def test_invalid_plan_never_sent(self):
  self.tick(0);self.p.calls[0].set_result(Plan('move',x=999));self.tick(.1)
  self.assertFalse(any(c['action']=='move' for c in self.sent))
 def test_too_old_inference_rejected(self):
  self.tick(0);self.p.calls[0].set_result(Plan('move',z=1));self.tick(6)
  self.assertFalse(any(c['action']=='move' for c in self.sent))
 def test_20_players_one_pending_each(self):
  cs=[Controller(str(i),self.p,self.sent.append) for i in range(20)]
  for t in (0,.1,.2):
   for c in cs:c.tick(State(c.alias,t,1,{}),t)
  self.assertEqual(len(self.p.calls),20)
 def test_lease_is_bounded(self):
  self.tick(0);self.p.calls[0].set_result(Plan('move',z=1));self.tick(.1);self.tick(.15)
  self.assertEqual(len(self.sent),1);self.assertLessEqual(self.sent[0]['seconds'],.5)
class PoolTests(unittest.TestCase):
 def test_bounded_admission_without_blocking_tick(self):
  event=Event(); pool=PlannerPool(lambda _:event.wait(2),workers=1,capacity=2)
  try:
   s=State('test',0,1,{})
   self.assertIsNotNone(pool.submit(s));self.assertIsNotNone(pool.submit(s))
   t=time.monotonic();self.assertIsNone(pool.submit(s));self.assertLess(time.monotonic()-t,.1)
  finally:event.set();pool.close()
if __name__=='__main__':unittest.main(verbosity=2)
