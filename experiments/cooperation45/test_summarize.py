import copy,unittest
from summarize import summarize
A='11111111-1111-4111-8111-111111111111';B='22222222-2222-4222-8222-222222222222';C='33333333-3333-4333-8333-333333333333';D='44444444-4444-4444-8444-444444444444';W='55555555-5555-4555-8555-555555555555';R='66666666-6666-4666-8666-666666666666';S='77777777-7777-4777-8777-777777777777'
def fixture():
 rows=[{'kind':'start','mode':'matrix_coop','controller':'file'},{'kind':'coop45_scope','worldId':W,'actors':[{'actorId':A,'sessionId':C},{'actorId':B,'sessionId':D}]}, {'kind':'coop45_receipt','actor':0,'actorId':A,'sessionId':C,'requestId':W,'receipt':{'State':'returned','ActorId':A,'SessionId':C}}]
 for n in ['same_world_distinct_actors','delivery']:rows.append({'kind':'coop45_effect','name':n,'passed':True,'evidence':{'worldId':W}})
 for actor,tool in ((0,'act_PickUp'),(0,'act_OfferItem'),(1,'act_RespondToOffer'),(0,'act_Wait'),(1,'act_Eat')):
  rows.append({'kind':'coop45_adapter_validated','actor':actor,'tool':tool,'session':[C,D][actor],'actorId':[A,B][actor],'capture':W})
 for label,counts in [('baseline',(0,0,0)),('world_drop_created',(0,0,1)),('acquired',(1,0,0)),('delivered',(0,1,0)),('eaten',(0,0,0))]:
  rows.append(dict(kind='coop45_state',label=label,value=89,countA=counts[0],countB=counts[1],worldCount=counts[2]))
 rows += [{'kind':'coop45_effect','name':'acquisition','passed':True,'evidence':{'receipt':{'action':'PickUp','status':'succeeded','affectedCount':1}}},
 {'kind':'coop45_effect','name':'eating','passed':True,'evidence':{'foodBefore':.5,'foodAfter':1,'statisticBefore':0,'statisticAfter':1,'nativeEvents':1}},
 {'kind':'coop45_effect','name':'overlap','passed':True,'evidence':{'waitRunId':R,'eatRunId':S,'runningWaitReceipt':{'runId':R,'status':'running'},'eatReceipt':{'runId':S,'status':'succeeded','action':'Eat','affectedCount':1},'waitStart':1,'eatStart':2,'eatEnd':3,'waitObservedRunningAt':3}},
 {'kind':'coop45_effect','name':'independent_progress','passed':True,'evidence':{'waitRunId':R,'eatRunId':S,'waitReceipt':{'runId':R,'status':'succeeded','action':'Wait'},'waitEnd':9}},
 {'kind':'coop45_summary','passed':True,'idleVerified':True,'modelApi':False,'checks':10},{'kind':'result','passed':True}]
 return rows
class SummaryTests(unittest.TestCase):
 def test_complete_evidence_passes(self):self.assertTrue(summarize(fixture())['passed'])
 def test_missing_scope_fails(self):self.assertFalse(summarize([r for r in fixture() if r['kind']!='coop45_scope'])['passed'])
 def test_same_actor_fails(self):
  r=fixture();r[1]['actors'][1]['actorId']=A;self.assertFalse(summarize(r)['passed'])
 def test_missing_eating_fails(self):self.assertFalse(summarize([r for r in fixture() if r.get('name')!='eating'])['passed'])
 def test_no_food_gain_fails(self):
  r=fixture();next(x for x in r if x.get('name')=='eating')['evidence']['foodAfter']=.4;self.assertFalse(summarize(r)['passed'])
 def test_missing_overlap_fails(self):self.assertFalse(summarize([r for r in fixture() if r.get('name')!='overlap'])['passed'])
 def test_wait_already_finished_is_not_overlap(self):
  r=fixture();next(x for x in r if x.get('name')=='overlap')['evidence']['runningWaitReceipt']['status']='succeeded';self.assertFalse(summarize(r)['passed'])
 def test_wrong_receipt_actor_fails(self):
  r=fixture();r[2]['actorId']=B;self.assertFalse(summarize(r)['passed'])
 def test_cleanup_not_idle_fails(self):
  r=fixture();r[-2]['idleVerified']=False;self.assertFalse(summarize(r)['passed'])
 def test_missing_outer_result_fails(self):self.assertFalse(summarize(fixture()[:-1])['passed'])
 def test_summary_only_fails(self):self.assertFalse(summarize(fixture()[-2:])['passed'])
 def test_error_fails_even_with_success_summary(self):self.assertFalse(summarize(fixture()+[{'kind':'coop45_error'}])['passed'])
 def test_duplicate_delivery_fails(self):
  r=fixture();next(x for x in r if x.get('label')=='delivered')['countA']=1;self.assertFalse(summarize(r)['passed'])
 def test_sequential_execution_is_not_overlap(self):
  r=fixture();next(x for x in r if x.get('name')=='independent_progress')['evidence']['waitEnd']=1.5;self.assertFalse(summarize(r)['passed'])
 def test_native_only_not_menu_coverage(self):self.assertFalse(summarize([r for r in fixture() if r['kind']!='coop45_adapter_validated'])['passed'])
if __name__=='__main__':unittest.main()
