import json
import tempfile
import unittest
from pathlib import Path
from summarize import summarize

class SummaryTests(unittest.TestCase):
    def runlog(self, events, suffix=''):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'log.jsonl'
            path.write_text('\n'.join(json.dumps(e) for e in events)+'\n'+suffix, encoding='utf-8')
            return summarize(path)
    def test_realistic_failure_is_not_success(self):
        row=self.runlog([{'kind':'start','mode':'natural','seed':'C','fixture':False},{'kind':'local_route_observed','safeCandidates':0,'reason':'nonintegerFeet'},{'kind':'result','passed':False,'updateFailures':0}])
        self.assertFalse(row['runner_passed']);self.assertEqual(row['projection_rejections'],{'nonintegerFeet':1});self.assertIsNone(row['food_chain_verified'])
    def test_expiry_not_obstacle(self):
        row=self.runlog([{'kind':'route_safety_stop','expired':True},{'kind':'route_safety_stop','expired':False},{'kind':'route_safety_stop'},{'kind':'result','passed':True,'updateFailures':0}])
        self.assertEqual((row['expiry_stops'],row['nonexpiry_safety_stops'],row['unclassified_safety_stops']),(1,1,1))
    def test_history_and_no_survival_claim(self):
        row=self.runlog([{'kind':'local_route_started','provenance':[{'Age':2,'IsCurrent':False}]},{'kind':'resource_observed'},{'kind':'result','passed':True,'updateFailures':0}])
        self.assertEqual(row['maximum_logged_proof_age'],2);self.assertEqual(row['historical_proof_cells'],1);self.assertIsNone(row['food_chain_verified'])
    def test_missing_terminal(self):
        self.assertIn('expected_exactly_one_terminal_result',self.runlog([])['warnings'])
    def test_malformed_and_nonfinite(self):
        row=self.runlog([], '{"kind":"x","value":NaN}\nnotjson\n[]\n')
        self.assertEqual(len(row['parse_errors']),3)
    def test_duplicate_results(self):
        row=self.runlog([{'kind':'local_route_result','choice':'a','outcome':'Arrived'}]*2)
        self.assertEqual(row['duplicate_route_results'],1)
    def test_expired_proof_warning(self):
        row=self.runlog([{'kind':'local_route_started','provenance':[{'Age':3.1}]}])
        self.assertIn('route_evidence_age_outside_3_seconds',row['warnings'])
    def test_old_log_compatibility(self):
        row=self.runlog([{'kind':'local_route_started'},{'kind':'result','passed':False,'updateFailures':0}])
        self.assertIsNone(row['maximum_logged_proof_age']);self.assertIn('route_provenance_missing',row['warnings'])

if __name__=='__main__':unittest.main()
