import unittest,copy
from summarize import summarize

def fixture(n=2):
 actors=[{'actorId':f'a{i}','sessionId':f's{i}'} for i in range(n)]
 rows=[{'kind':'batch47_scope','suite':'scale','worldId':'w','actualActors':n,'actors':actors}]
 for phase,t in [('warmup_start',0),('steady_start',10),('steady_end',40)]:rows.append({'kind':'batch47_window','phase':phase,'monotonicSeconds':t,'gameTime':t/2})
 for i in range(n):
  for j,tool in enumerate(['act_MoveTo','act_FaceTowards']):
   run=f'r{i}_{j}';rows.append({'kind':'scale47_dispatch','actor':i,'runId':run})
   rows.append({'kind':'scale47_progress','actor':i,**actors[i],'runId':run,'passed':True,'startedInSteady':True,'tool':tool,'origin':[0,0,0],'position':[4,0,0],'horizontalDisplacement':4,'receipt':{'ActorId':f'a{i}','SessionId':f's{i}','Data':{'runId':run,'status':'succeeded'}}})
 rows.append({'kind':'batch47_summary','passed':True,'idleVerified':True,'modelApi':False});return rows
class SummaryTests(unittest.TestCase):
 def test_two(self):self.assertTrue(summarize(fixture())['passed'])
 def test_fifty(self):self.assertTrue(summarize(fixture(50))['passed'])
 def test_no_scope(self):self.assertFalse(summarize([])['passed'])
 def test_missing_progress(self):self.assertFalse(summarize([r for r in fixture() if r.get('kind')!='scale47_progress'])['passed'])
 def test_wrong_owner(self):
  r=fixture();next(x for x in r if x.get('kind')=='scale47_progress')['receipt']['ActorId']='wrong';self.assertFalse(summarize(r)['passed'])
 def test_short_window(self):
  r=fixture();next(x for x in r if x.get('phase')=='steady_end')['monotonicSeconds']=20;self.assertFalse(summarize(r)['passed'])
 def test_no_displacement(self):
  r=fixture();next(x for x in r if x.get('kind')=='scale47_progress')['position']=[0,0,0];self.assertFalse(summarize(r)['passed'])
if __name__=='__main__':unittest.main()

def recovery_fixture():
 r=[{'kind':'batch47_scope','suite':'all','worldId':'w','actualActors':2,'actors':[{'ActorId':'a0','SessionId':'s0'},{'ActorId':'a1','SessionId':'s1'}]}]
 def effect(name,evidence,cycle=0):r.append({'kind':'batch47_effect','name':name,'passed':True,'cycle':cycle,'evidence':evidence})
 effect('obstacle_failure',{'runId':'failed','status':'failed','failure':'blocked'});effect('failed_owner_released',{})
 effect('recovery_move',{'failedRunId':'failed','newRunId':'recovered','status':'succeeded','displacement':2,'teleport':False})
 oldA,oldS='a0','s0'
 for c in (1,2):
  a,s=f'new{c}',f'ns{c}';b=f'wait{c}'
  for name in ['remove_owner_idle','removed','stale_session_rejected']:effect(name,{},c)
  effect('new_binding',{'oldActor':oldA,'oldSession':oldS,'newActor':a,'newSession':s,'nativeSpawn':True,'otherRunId':b},c)
  effect('stale_reference_rejected',{'actualState':'native_error','actualError':'unknown_reference','nativeWriteAttempted':False},c)
  effect('new_actor_move',{'actorId':a,'sessionId':s,'status':'succeeded','runId':f'move{c}','displacement':2,'teleport':False},c)
  effect('other_owner_unchanged',{'runId':b,'status':'running'},c);effect('other_owner_progress',{'runId':b,'status':'succeeded','notMovement':True},c);oldA,oldS=a,s
 r.append({'kind':'batch47_summary','passed':True,'idleVerified':True,'modelApi':False,'cycles':2});return r
class RecoveryTests(unittest.TestCase):
 def test_recovery(self):self.assertTrue(summarize(recovery_fixture())['passed'])
 def test_missing_cycle(self):self.assertFalse(summarize([r for r in recovery_fixture() if r.get('cycle')!=2])['passed'])
 def test_identity_reuse(self):
  r=recovery_fixture();next(e for e in r if e.get('name')=='new_binding')['evidence']['newActor']='a0';self.assertFalse(summarize(r)['passed'])
 def test_other_restarted(self):
  r=recovery_fixture();next(e for e in r if e.get('name')=='other_owner_progress')['evidence']['runId']='changed';self.assertFalse(summarize(r)['passed'])

class StaleContractTests(unittest.TestCase):
 def test_wrong_native_error(self):
  r=recovery_fixture();next(e for e in r if e.get('name')=='stale_reference_rejected')['evidence']['actualError']='timeout';self.assertFalse(summarize(r)['passed'])
 def test_old_null_contract_not_accepted(self):
  r=recovery_fixture();next(e for e in r if e.get('name')=='stale_reference_rejected')['evidence']={'returnedNull':True};self.assertFalse(summarize(r)['passed'])
 def test_write_attempt_not_accepted(self):
  r=recovery_fixture();next(e for e in r if e.get('name')=='stale_reference_rejected')['evidence']['nativeWriteAttempted']=True;self.assertFalse(summarize(r)['passed'])

class BoundaryToleranceTests(unittest.TestCase):
 def test_recorded_warmup_9991_passes_retains_actual(self):
  r=fixture();next(x for x in r if x.get('phase')=='warmup_start')['monotonicSeconds']=.009
  out=summarize(r);self.assertTrue(out['passed']);self.assertAlmostEqual(out['warmupWallSeconds'],9.991)
 def test_recorded_warmup_98_fails(self):
  r=fixture();next(x for x in r if x.get('phase')=='warmup_start')['monotonicSeconds']=.2
  self.assertFalse(summarize(r)['passed'])
 def test_recorded_steady_2995_passes_retains_actual(self):
  r=fixture();next(x for x in r if x.get('phase')=='steady_end')['monotonicSeconds']=39.95
  out=summarize(r);self.assertTrue(out['passed']);self.assertAlmostEqual(out['steadyWallSeconds'],29.95)
 def test_recorded_steady_298_fails(self):
  r=fixture();next(x for x in r if x.get('phase')=='steady_end')['monotonicSeconds']=39.8
  self.assertFalse(summarize(r)['passed'])
