import importlib.util,pathlib,unittest
spec=importlib.util.spec_from_file_location('a',pathlib.Path(__file__).with_name('analyze.py'));a=importlib.util.module_from_spec(spec);spec.loader.exec_module(a)
class Tests(unittest.TestCase):
 def s(self,t,speed=0,owner='owner',req='nav',state='Moving'):return dict(kind='navigation_sample',monotonicSeconds=t,horizontalSpeed=speed,ownerId=owner,requestId=req,state=state)
 def test_absent_unknown(self):self.assertIsNone(a.analyze([])['lowSpeedIntervals'])
 def test_stop(self):
  r=a.analyze([self.s(0),self.s(.05),self.s(.09)]);self.assertAlmostEqual(r['lowSpeedIntervals'][0]['seconds'],.09)
 def test_gap_not_stop(self):self.assertIsNone(a.analyze([self.s(0),self.s(1)])['lowSpeedIntervals'])
 def test_owner_changed(self):self.assertIsNone(a.analyze([self.s(0),self.s(.05,owner='other')])['sampledStateSeconds'])
 def test_request_changed(self):self.assertIsNone(a.analyze([self.s(0),self.s(.05,req='other')])['lowSpeedIntervals'])
 def test_clock_regression(self):self.assertIsNone(a.analyze([self.s(1),self.s(.5)])['lowSpeedIntervals'])
 def test_missing_speed(self):
  r=self.s(.05);del r['horizontalSpeed'];self.assertIsNone(a.analyze([self.s(0),r])['lowSpeedIntervals'])
 def test_moving_not_stopped(self):self.assertEqual(a.analyze([self.s(0,1),self.s(.05,1)])['lowSpeedIntervals'],[])
 def test_timing(self):
  rows=[dict(kind='prefetch_requested',lease={'RequestId':'r'},monotonicSeconds=1),dict(kind='prefetch_response',requestId='r',monotonicSeconds=9),dict(kind='prefetch_preceding_terminal',requestId='r',success=True,monotonicSeconds=6),dict(kind='native_call_started',monotonicSeconds=9.1)]
  r=a.analyze(rows)['prefetchTiming'][0];self.assertEqual(r['requestToResponseSeconds'],8);self.assertAlmostEqual(r['terminalToNextDispatchSeconds'],3.1);self.assertEqual(r['dispatchAssociation'],'temporal_only_unbound')
 def test_utc_not_substitute(self):self.assertIsNone(a.stamp({'wallUtc':'2026-11-09T01:00:00Z'}))

 def test_actual_terminal_distinct_from_observed(self):
  rows=[dict(kind='bounded_navigation_terminal',snapshot={'RequestId':'nav'},monotonicSeconds=5),dict(kind='prefetch_preceding_terminal',requestId='r',precedingNavigation='nav',monotonicSeconds=5.2),dict(kind='native_call_started',monotonicSeconds=6)]
  r=a.analyze(rows)['prefetchTiming'][0];self.assertAlmostEqual(r['terminalObservationDelaySeconds'],.2);self.assertEqual(r['nativeTerminalToNextDispatchSeconds'],1)

 def test_native_busy_schema(self):
  x=self.s(0,0,state='Settling');y=self.s(.05,.01,state='Observing')
  for v in [x,y]:v.update(position=[1,65,2],velocity=[0,0,0],grounded=True,segmentCount=2,reason='actual')
  r=a.analyze([x,y]);self.assertEqual(r['reportedSegmentCounts'],[2]);self.assertEqual(r['sampledStateTransitionCount'],1);self.assertIsNone(r['internalWaypointStopCount'])
 def test_terminal_missing_frames_not_invented_static(self):
  r=a.analyze([self.s(0,1),self.s(.05,1),dict(kind='bounded_navigation_terminal',monotonicSeconds=.05,snapshot={'RequestId':'nav'}),dict(kind='native_call_started',monotonicSeconds=8)])
  self.assertEqual(r['sampledCoverageSeconds'],.05);self.assertEqual(r['lowSpeedIntervals'],[]);self.assertEqual(r['postTerminalPhysicalState'],'unknown_busy_only_telemetry')

 def test_postterminal_real_samples_retained(self):
  x=self.s(0,state='Arrived');y=self.s(.05,state='Arrived');x['busy']=y['busy']=False
  v=a.analyze([x,y]);self.assertEqual(v['postTerminalSampleCount'],2);self.assertEqual(v['postTerminalPhysicalState'],'sampled_after_busy_not_continuous_proof');self.assertAlmostEqual(v['sampledStateSeconds']['Arrived'],.05)
 def test_selected_vs_fallback(self):
  x=self.s(0,.8);x['position']=[1,65,0];y=self.s(.05,.9);y['position']=[1.1,65,0]
  v=a.analyze([dict(kind='observed_flat_route_not_selected'),dict(kind='observed_flat_route_started',routeId='r',waypoints=[{'X':1,'Y':65,'Z':0},{'X':2,'Y':65,'Z':0}]),x,y]);self.assertEqual(v['flatRouteFallbackCount'],1);self.assertEqual(v['selectedFlatRoutes'][0]['firstWaypointLowSpeedSamples'],0)
