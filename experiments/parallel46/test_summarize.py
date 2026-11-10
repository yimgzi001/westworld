import unittest,uuid
from summarize import summarize
G=[str(uuid.UUID(int=i+1)) for i in range(12)]
A,B,S,T,W,R0,R1,R2,R3=G[:9]
def receipt(i,run,status):return {'ActorId':[A,B][i],'SessionId':[S,T][i],'State':'returned','Data':{'action':'MoveTo','runId':run,'status':status}}
def fixture():
 rows=[{'kind':'start','mode':'matrix_parallel','controller':'file'},{'kind':'parallel46_scope','worldId':W,'actualActors':2,'actors':[{'actorId':A,'sessionId':S},{'actorId':B,'sessionId':T}]}]
 for n,ids in [(1,[R0,R1]),(2,[R2,R3])]:
  rows.append({'kind':'parallel46_round_start','round':n,'runIds':ids})
  for i in (0,1):rows.append({'kind':'parallel46_adapter_validated','actor':i,'actorId':[A,B][i],'session':[S,T][i],'capture':W,'tool':'act_MoveTo'})
 rows.append({'kind':'parallel46_adapter_validated','actor':0,'actorId':A,'session':S,'capture':W,'tool':'cancel_action'})
 rows.append({'kind':'parallel46_adapter_validated','actor':0,'actorId':A,'session':S,'capture':W,'tool':'act_MoveTo'})
 for time in (0,.5,1):
  rows.append({'kind':'parallel46_sample','round':1,'gameTime':time,'rows':[{'actor':i,'actorId':[A,B][i],'sessionId':[S,T][i],'runId':[R0,R1][i],'status':'running','receipt':receipt(i,[R0,R1][i],'running'),'origin':[0,65,i*3],'position':[time,65,i*3],'horizontalDisplacement':time} for i in (0,1)]})
 evidence={'same_world_distinct_actors':{'worldId':W},'parallel_success':{'runIds':[R0,R1],'receipts':[receipt(i,[R0,R1][i],'succeeded') for i in (0,1)],'displacementA':3.8,'displacementB':3.8,'allDisplacements':[3.8,3.8]},'cancel_isolation_running':{'runIds':[R2,R3],'receipts':[receipt(0,R2,'cancelled'),receipt(1,R3,'running')]},'cancel_isolation_completed':{'runIds':[R2,R3],'receipts':[receipt(0,R2,'cancelled'),receipt(1,R3,'succeeded')],'aDriftAfterCancel':.1,'settledDrift':.001,'bDisplacementAfterCancel':2}}
 evidence['cancelled_actor_recovered']={'runId':G[9],'receipt':receipt(0,G[9],'succeeded'),'displacementA':3.8,'otherOwnersIdle':True}
 rows.extend({'kind':'parallel46_effect','name':n,'passed':True,'evidence':e} for n,e in evidence.items())
 rows.extend([{'kind':'parallel46_summary','passed':True,'idleVerified':True,'modelApi':False,'checks':30},{'kind':'result','passed':True}]);return rows
class SummaryTests(unittest.TestCase):
 def test_complete_passes(self):self.assertTrue(summarize(fixture())['passed'])
 def test_no_samples_fails(self):self.assertFalse(summarize([r for r in fixture() if r['kind']!='parallel46_sample'])['passed'])
 def test_summary_only_fails(self):self.assertFalse(summarize(fixture()[-2:])['passed'])
 def test_wrong_actor_fails(self):
  r=fixture();next(x for x in r if x['kind']=='parallel46_sample')['rows'][1]['actorId']=A;self.assertFalse(summarize(r)['passed'])
 def test_no_overlap_fails(self):
  r=fixture()
  for x in r:
   if x['kind']=='parallel46_sample':x['rows'][1]['status']='succeeded'
  self.assertFalse(summarize(r)['passed'])
 def test_large_gap_fails(self):
  r=fixture();ss=[x for x in r if x['kind']=='parallel46_sample'];ss[-1]['gameTime']=3;self.assertFalse(summarize(r)['passed'])
 def test_fake_displacement_fails(self):
  r=fixture();next(x for x in r if x['kind']=='parallel46_sample')['rows'][0]['horizontalDisplacement']=99;self.assertFalse(summarize(r)['passed'])
 def test_b_cancelled_too_fails(self):
  r=fixture();next(x for x in r if x.get('name')=='cancel_isolation_running')['evidence']['receipts'][1]['Data']['status']='cancelled';self.assertFalse(summarize(r)['passed'])
 def test_b_did_not_continue_fails(self):
  r=fixture();next(x for x in r if x.get('name')=='cancel_isolation_completed')['evidence']['bDisplacementAfterCancel']=0;self.assertFalse(summarize(r)['passed'])
 def test_no_cancel_menu_fails(self):self.assertFalse(summarize([r for r in fixture() if r.get('tool')!='cancel_action'])['passed'])
 def test_missing_idle_fails(self):
  r=fixture();r[-2]['idleVerified']=False;self.assertFalse(summarize(r)['passed'])
 def test_missing_outer_fails(self):self.assertFalse(summarize(fixture()[:-1])['passed'])
 def test_four_actors_pass(self):
  r=fixture();scope=r[1];scope['actualActors']=4
  extraActors=[str(uuid.UUID(int=101)),str(uuid.UUID(int=102))];extraSessions=[str(uuid.UUID(int=103)),str(uuid.UUID(int=104))]
  scope['actors'] += [{'actorId':a,'sessionId':s} for a,s in zip(extraActors,extraSessions)]
  ids={1:[str(uuid.UUID(int=105)),str(uuid.UUID(int=106))],2:[str(uuid.UUID(int=107)),str(uuid.UUID(int=108))]}
  for row in list(r):
   if row['kind']=='parallel46_round_start':row['runIds']+=ids[row['round']]
   if row['kind']=='parallel46_sample':
    for j in range(2):
     t=row['gameTime'];run=ids[1][j];receipt4={'ActorId':extraActors[j],'SessionId':extraSessions[j],'State':'returned','Data':{'runId':run,'status':'running'}}
     row['rows'].append({'actor':j+2,'actorId':extraActors[j],'sessionId':extraSessions[j],'runId':run,'status':'running','receipt':receipt4,'origin':[0,65,6+j*3],'position':[t,65,6+j*3],'horizontalDisplacement':t})
   if row.get('name') in ('parallel_success','cancel_isolation_running','cancel_isolation_completed'):
    e=row['evidence'];n=1 if row['name']=='parallel_success' else 2;e['runIds']+=ids[n]
    for j in range(2):e['receipts'].append({'Data':{'runId':ids[n][j],'status':'running' if row['name']=='cancel_isolation_running' else 'succeeded'}})
    if n==1:e['allDisplacements'] += [3.8,3.8]
  for j in range(2):
   for _ in range(2):r.append({'kind':'parallel46_adapter_validated','actor':j+2,'actorId':extraActors[j],'session':extraSessions[j],'capture':W,'tool':'act_MoveTo'})
  self.assertTrue(summarize(r)['passed'],summarize(r))
 def test_recovery_old_id_fails(self):
  r=fixture();next(x for x in r if x.get('name')=='cancelled_actor_recovered')['evidence']['runId']=R2;self.assertFalse(summarize(r)['passed'])
if __name__=='__main__':unittest.main()
