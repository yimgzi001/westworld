import copy,unittest
from replay import requests,facts,B
from fact_timeline import describe
class Cases(unittest.TestCase):
 def test_real_late_pickup_keeps_historical_empty_delta(self):
  f=facts(requests(B)[2]);old=copy.deepcopy(f);t=describe(f)
  self.assertEqual(f,old);self.assertEqual(f['outcomeFeedback']['inventoryDelta'],[])
  self.assertEqual(t['outcomeAfterObservationId'],f['outcomeFeedback']['afterObservationId'])
  self.assertEqual(t['currentObservationId'],f['goalExecution']['progress']['observationId'])
  self.assertNotEqual(t['currentObservationId'],t['outcomeAfterObservationId'])
  self.assertNotIn('act_Eat',str(t));self.assertNotIn('226',str(t))
 def test_no_feedback_no_invented_delta(self):self.assertIsNone(describe({'outcomeFeedback':None}))
 def test_missing_time_stays_unknown(self):
  t=describe({'outcomeFeedback':{'kind':'tool_outcome_delta'}});self.assertIsNone(t['currentObservedAt']);self.assertIsNone(t['currentObservationId'])
if __name__=='__main__':unittest.main()
