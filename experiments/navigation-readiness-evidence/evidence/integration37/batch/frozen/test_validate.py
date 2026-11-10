import copy,importlib.util,unittest
from pathlib import Path
p=Path(__file__).with_name('validate.py');spec=importlib.util.spec_from_file_location('v37',p);v=importlib.util.module_from_spec(spec);spec.loader.exec_module(v)
def sample(t,trigger='first',interval=None,unknown=False,busy=True,state='Moving'):
 return dict(schemaVersion=2,samplePolicy='10hz_periodic_plus_boundaries',sampleTrigger=trigger,sampleIntervalSeconds=interval,coverageUnknown=unknown,suppressedSamples=0,monotonicSeconds=t,ownerId='o',requestId='r',segmentRequestId='s',state=state,boundedState='Running' if busy else 'Failed',busy=busy,ownsLease=busy,position=[t,0,0],velocity=[1,0,0],grounded=True,kinematicallyStopped=False,reason='started')
class Cases(unittest.TestCase):
 def test_actual_point_one_plus_epsilon_is_covered(self):
  r=v.sampling([sample(1),sample(1.10001,'periodic',.10001)])
  self.assertEqual(r['failures'],[]);self.assertEqual(r['unknown'],[]);self.assertEqual(r['metrics']['movingPairCoverageFraction'],1)
 def test_point_fifteen_exact_boundary(self):
  r=v.sampling([sample(0),sample(.15,'periodic',.15)])
  self.assertEqual(r['metrics']['coveredMovingPairCount'],1)
 def test_gap_unknown_not_interpolated(self):
  r=v.sampling([sample(0),sample(.16,'periodic',.16,True)])
  self.assertEqual(r['failures'],[]);self.assertEqual(r['metrics']['continuousMotionCoverage'],'unknown');self.assertEqual(r['metrics']['coveredSampleChordMetres'],0)
 def test_unreported_gap_fails(self):
  self.assertIn('unreported_coverage_gap',v.sampling([sample(0),sample(.16,'periodic',.16)])['failures'])
 def test_fast_periodic_spam_fails(self):
  self.assertIn('periodic_pacing_or_idle_spam',v.sampling([sample(0),sample(.01,'periodic',.01)])['failures'])
 def test_fast_boundary_is_not_spam(self):
  self.assertEqual(v.sampling([sample(0),sample(.01,'boundary',.01)])['failures'],[])
 def test_idle_periodic_spam_fails(self):
  self.assertIn('periodic_pacing_or_idle_spam',v.sampling([sample(0),sample(.11,'periodic',.11,busy=False,state='Failed')])['failures'])
 def test_request_replacement_never_motion_pair(self):
  b=sample(.1,'boundary',.1);b['requestId']='other'
  self.assertEqual(v.sampling([sample(0),b])['metrics']['coveredMovingPairCount'],0)
 def test_segment_replacement_never_motion_pair(self):
  b=sample(.1,'boundary',.1);b['segmentRequestId']='other'
  self.assertEqual(v.sampling([sample(0),b])['metrics']['coveredMovingPairCount'],0)
 def test_invalid_interval_and_physical_shape(self):
  b=sample(.1,'periodic',.2)
  self.assertIn('actual_interval_mismatch',v.sampling([sample(0),b])['failures'])
  b['position']=[float('nan'),0,0];self.assertTrue(v.sampling([b])['unknown'])
 def test_no_schema_upgrade_of_old_evidence(self):
  b=sample(0);b.pop('schemaVersion')
  self.assertTrue(v.sampling([b])['unknown']);self.assertNotEqual(v.validate([], 'arrive')['verdict'],'pass')
 def test_suppression_count_not_assumed_frame_rate(self):
  b=sample(.1,'periodic',.1);b['suppressedSamples']=9;r=v.sampling([sample(0),b])
  self.assertEqual(r['metrics']['suppressedCandidatesRecorded'],9);self.assertAlmostEqual(r['metrics']['candidateSuppressionFraction'],9/11)
if __name__=='__main__':unittest.main()

class SafetyRegression(unittest.TestCase):
 def rows(self):
  scope=dict(SessionId='s',ActorId='a',State='returned')
  def receipt(tool,data):return {'kind':'tool_receipt','receipt':dict(scope,Tool=tool,Data=data)}
  return [receipt('host_navigation_status',{'request_id':'r','busy':False,'owner_quarantined':False}),
   {'kind':'predecessor_physical_sample','runId':'r','sessionId':'s','actorId':'a','velocity':[0,0,0],'walk':[0,0],'turn':[0,0],'moving':False,'settled':True},
   {'kind':'route_fixture_cancel'},receipt('action_status',{'status':'cancelled'}),receipt('task_status',{'status':'idle'}),{'kind':'final_audit'}]
 def passed(self,rows):return all(x['status']=='pass' for x in v.safety(rows,'cancel',['r']))
 def test_same_owner_quiet_cleanup_passes(self):self.assertTrue(self.passed(self.rows()))
 def test_stop_to_final_dispatch_rejected(self):
  rows=self.rows();rows.insert(-1,{'kind':'native_call_started','step':{'Tool':'act_Jump'}});self.assertFalse(self.passed(rows))
 def test_terminal_still_moving_rejected(self):
  rows=self.rows();rows[1]['velocity']=[1,0,0];self.assertFalse(self.passed(rows))
 def test_owner_quarantined_rejected(self):
  rows=self.rows();rows[0]['receipt']['Data']['owner_quarantined']=True;self.assertFalse(self.passed(rows))
 def test_input_missing_unknown_not_pass(self):
  rows=self.rows();rows[1].pop('walk');self.assertFalse(self.passed(rows))
 def test_remaining_ten_arrival_rejected(self):
  rows=[{'kind':'native_call_returned','tool':'host_navigate_local','sequence':1,'data':{'request_id':'r'}},
        {'kind':'bounded_navigation_terminal','snapshot':{'RequestId':'r','Status':3,'Reason':'arrived','Busy':False,'Segments':1,'Path':3,'Remaining':10},'Quarantined':False,'monotonicTicks':1,'monotonicFrequency':1}]
  first=sample(0);last=sample(1.01,'boundary',1.01,True,False,'Arrived');last.update(boundedState='Arrived',ownsLease=False,velocity=[0,0,0])
  rows += [dict(first,kind='navigation_sample'),dict(last,kind='navigation_sample')]
  result=v.validate(rows,'arrive');self.assertTrue(any(x['check']=='arrived_remaining_r' and x['status']=='fail' for x in result['checks']))
