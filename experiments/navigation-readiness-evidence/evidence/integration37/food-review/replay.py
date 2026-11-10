"""Offline only: no controller import, subprocess, API, GUI or response writes."""
import hashlib,json,pathlib,unittest
ROOT=pathlib.Path(__file__).resolve().parents[2]
A=ROOT/'work/integration35-food-0';B=ROOT/'work/assistant36-natural'
def requests(d):
 return [json.loads(p.read_text()) for p in sorted((d/'control').glob('*/request.json'))]
def facts(r):return json.loads(r['prompt'].split('OBSERVATION:\n',1)[1])['facts']
def criteria(r):return {x['name']:x['state'] for x in facts(r)['goalExecution']['progress']['criteria']}
class Replay(unittest.TestCase):
 def test_unknown_not_model_failure_or_free_usage(self):
  i=json.loads((A/'interruption.json').read_text());self.assertEqual(i['requestOutcome'],'unknown');self.assertIsNone(i['usage']);self.assertFalse(i['replayed']);self.assertEqual(i['apiResponsesRecorded'],0)
 def test_no_recorded_response_in_stage35(self):
  self.assertEqual(list((A/'control').glob('*/response-*.received.json')),[])
 def test_initial_food_not_new_food(self):
  r=requests(B)[0];self.assertEqual(criteria(r)['currently_owned_edible'],'verified');self.assertEqual(criteria(r)['new_edible_source_verified'],'notYet')
 def test_future_inventory_not_in_prefetch(self):
  r=requests(B)[1];self.assertTrue(facts(r)['goalExecution']['budget']['speculative']);self.assertNotIn(226,[x['value'] for x in facts(r)['inventory']['slots']]);self.assertEqual(criteria(r)['new_edible_source_verified'],'notYet')
 def test_late_pickup_longterm_progress_does_not_rewrite_delta(self):
  r=requests(B)[2];self.assertEqual(criteria(r)['new_edible_source_currently_held'],'verified');self.assertEqual(criteria(r)['meal_verified'],'notYet');self.assertEqual(facts(r)['outcomeFeedback']['inventoryDelta'],[])
 def test_eat_is_available_for_current_food(self):
  r=requests(B)[2];offer=next(x for x in r['offers'] if x['Name']=='act_Eat');self.assertIn(226,offer['Parameters']['properties']['value']['enum']);self.assertIn(0,offer['Parameters']['properties']['slot']['enum']);self.assertEqual(facts(r)['inventory']['slots'][0]['item']['nutrition'],.05)
 def test_response_eat_matches_real_current_inventory(self):
  p=sorted((B/'control').glob('*/response-0001.received.json'))[-1];r=json.loads(p.read_text());self.assertEqual(r['decision']['steps'],[{'tool':'act_Eat','arguments':{'slot':0,'value':226,'count':1}}])
 def test_native_success_is_separate_from_provider_proof(self):
  rows=[json.loads(l) for l in (B/'tool-agent.jsonl').read_text().splitlines()];r=next(r for r in rows if r['kind']=='result');self.assertTrue(r['passed']);self.assertEqual(r['reason'],'natural_new_food_and_eating_verified');self.assertEqual(r['updateFailures'],0);self.assertFalse((B/'api-audit.jsonl').exists())
 def test_usage_unknown_preserved_in_successful_assistant_chain(self):
  r=requests(B)[2];b=facts(r)['goalExecution']['budget'];self.assertEqual(b['sentRequests'],2);self.assertEqual(b['remainingRequests'],6);self.assertIsNone(b['reportedInputTokens']);self.assertFalse(b['usageComplete']);self.assertEqual(facts(r)['goalExecution']['requestLifecycle'],'prepared_not_sent')
if __name__=='__main__':unittest.main()
