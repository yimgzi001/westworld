"""Synthetic contract tests, never native execution evidence."""
import copy
import importlib.util
from pathlib import Path
import unittest
import uuid

spec = importlib.util.spec_from_file_location('direct_validate', Path(__file__).with_name('validate.py'))
v = importlib.util.module_from_spec(spec)
spec.loader.exec_module(v)
S, A, R, O, V, RUN, NEXT = [str(uuid.UUID(int=i)) for i in range(1, 8)]
ARGS = {'bearing': 0, 'distance': 4, 'range': .25, 'timeout': 4}


def fixture(case='direct_early'):
    positive = case in ('direct_early', 'direct_late')
    status = 'cancelled' if case == 'direct_cancel' else 'failed' if case == 'direct_failure' else 'succeeded'
    response_at = 110. if case == 'direct_late' else 100.6
    rows = [
        {'kind': 'native_call_started', 'step': {'Tool': 'act_MoveTo', 'Arguments': ARGS},
         'candidateRequestId': None, 'monotonicSeconds': 100.},
        {'kind': 'native_call_returned', 'tool': 'act_MoveTo', 'state': 'returned', 'sequence': 12,
         'data': {'runId': RUN, 'status': 'running'}},
        {'kind': 'predecessor_bound', 'sessionId': S, 'actorId': A, 'receiptSequence': 12,
         'runId': RUN, 'precedingKind': 'NativeMoveTo', 'tool': 'act_MoveTo', 'position': [0., 65., 0.]},
        {'kind': 'prefetch_requested', 'precedingRunId': RUN, 'precedingKind': 'NativeMoveTo',
         'lease': {'RequestId': R, 'SessionId': S, 'ActorId': A, 'PrecedingReceiptSequence': 12,
                   'PlanningObservationId': O}, 'monotonicSeconds': 100.2},
    ]
    for i in range(41):
        rows.append({'kind': 'predecessor_physical_sample', 'sessionId': S, 'actorId': A,
                     'receiptSequence': 12, 'runId': RUN, 'status': status if i == 40 else 'running',
                     'moving': i != 40, 'settled': i == 40, 'cancelRequested': status == 'cancelled' and i == 40,
                     'position': [i / 10, 65., 0.], 'monotonicSeconds': 100. + i / 20})
    rows.append({'kind': 'predecessor_settled', 'sessionId': S, 'actorId': A, 'receiptSequence': 12,
                 'runId': RUN, 'monotonicSeconds': 102.})
    if positive:
        rows += [
            {'kind': 'prefetch_response', 'requestId': R, 'accepted': True, 'monotonicSeconds': response_at},
            {'kind': 'prefetch_handoff_taken', 'requestId': R, 'validationObservationId': V,
             'monotonicSeconds': max(response_at, 102.) + .01},
            {'kind': 'native_call_started', 'candidateRequestId': R,
             'step': {'Tool': 'act_Wait', 'Arguments': {'duration': .2}},
             'monotonicSeconds': max(response_at, 102.) + .02},
            {'kind': 'native_call_returned', 'tool': 'act_Wait', 'sequence': 20,
             'data': {'runId': NEXT, 'status': 'running'}},
            {'kind': 'native_terminal_observed', 'tool': 'act_Wait', 'sequence': 20,
             'runId': NEXT, 'status': 'succeeded'},
        ]
    else:
        rows.append({'kind': 'prefetch_invalidated', 'requestId': R, 'monotonicSeconds': 100.8,
                     'reason': 'identity_replaced' if case == 'direct_replace' else 'preceding_' + status})
        if case == 'direct_stale_reference':
            rows += [{'kind': 'direct_fixture_reference_invalidated', 'reference': 'public-ref', 'runId': RUN},
                     {'kind': 'prefetch_validation_failed', 'requestId': R}]
        else:
            rows.append({'kind': 'direct_fixture_injection', 'scenario': case, 'before': {'runId': RUN},
                         'replacement': {'runId': NEXT} if case == 'direct_replace' else None})
        if case == 'direct_replace':
            rows.append({'kind': 'predecessor_physical_sample', 'runId': NEXT})
    rows += [{'kind': 'final_audit', 'modelInFlight': False},
             {'kind': 'result', 'passed': True, 'updateFailures': 0}]
    driver = {'case': case, 'events': [
        {'delivered': True},
        {'delivered': True, 'requestId': R, 'sessionId': S, 'actorId': A, 'observationId': O,
         'canceledBeforeWrite': not positive, 'arguments': {'target': 'public-ref'}},
    ]}
    return rows, driver


class ValidationTests(unittest.TestCase):
    def report(self, rows, driver, case='direct_early'):
        return v.validate(rows, driver, case)

    def assert_check(self, report, name, status):
        self.assertEqual(next(c['status'] for c in report['checks'] if c['check'] == name), status)

    def test_synthetic_early_pass(self):
        rows, d = fixture()
        report = self.report(rows, d)
        self.assertEqual(report['verdict'], 'pass', report)
        self.assertGreater(report['metrics']['sampledInferencePathMetres'], 0)

    def test_synthetic_late_pass(self):
        rows, d = fixture('direct_late')
        self.assertEqual(self.report(rows, d, 'direct_late')['verdict'], 'pass')

    def test_synthetic_negatives_pass(self):
        for case in v.CASES[2:]:
            with self.subTest(case=case):
                rows, d = fixture(case)
                report = self.report(rows, d, case)
                self.assertEqual(report['verdict'], 'pass', report)

    def test_no_native_result_unknown(self):
        rows, d = fixture()
        rows = [e for e in rows if e['kind'] != 'result']
        self.assertEqual(self.report(rows, d)['verdict'], 'unknown')

    def test_legacy_bounded_log_cannot_pass(self):
        self.assertNotEqual(self.report([{'kind': 'result', 'passed': True, 'updateFailures': 0}],
                                       {'case': 'direct_early'})['verdict'], 'pass')

    def test_no_physical_samples_unknown(self):
        rows, d = fixture()
        rows = [e for e in rows if e['kind'] != 'predecessor_physical_sample']
        self.assertEqual(self.report(rows, d)['verdict'], 'unknown')

    def test_fake_reported_overlap_does_not_substitute_motion(self):
        rows, d = fixture()
        for e in rows:
            if e['kind'] == 'predecessor_physical_sample':
                e['position'] = [0., 65., 0.]
            e['overlapSeconds'] = e['overlapPath'] = 1000
        report = self.report(rows, d)
        self.assert_check(report, 'real_displacement_while_inference_pending', 'fail')

    def test_duplicate_successor_fails(self):
        rows, d = fixture()
        rows.append(copy.deepcopy(next(e for e in rows if e['kind'] == 'native_call_started' and e['candidateRequestId'])))
        self.assertEqual(self.report(rows, d)['verdict'], 'fail')

    def test_unbound_dispatch_unknown(self):
        rows, d = fixture()
        for e in rows:
            if e['kind'] == 'native_call_started':
                del e['candidateRequestId']
        self.assertEqual(self.report(rows, d)['verdict'], 'unknown')

    def test_wrong_run_receipt_fails(self):
        rows, d = fixture()
        next(e for e in rows if e['kind'] == 'native_call_returned')['data']['runId'] = NEXT
        self.assert_check(self.report(rows, d), 'move_receipt_matches_real_run', 'fail')

    def test_wrong_actor_sample_fails(self):
        rows, d = fixture()
        next(e for e in rows if e['kind'] == 'predecessor_physical_sample')['actorId'] = NEXT
        self.assert_check(self.report(rows, d), 'samples_bound_to_actor_receipt', 'fail')

    def test_time_reversal_fails(self):
        rows, d = fixture()
        samples = [e for e in rows if e['kind'] == 'predecessor_physical_sample']
        samples[10]['monotonicSeconds'] = samples[9]['monotonicSeconds'] - .01
        self.assert_check(self.report(rows, d), 'strictly_increasing_sample_time', 'fail')

    def test_sampling_gap_unknown_not_action_failure(self):
        rows, d = fixture()
        rows = [e for e in rows if not (e['kind'] == 'predecessor_physical_sample' and 100.8 < e['monotonicSeconds'] < 101.3)]
        self.assertEqual(self.report(rows, d)['verdict'], 'unknown')

    def test_old_observation_fails(self):
        rows, d = fixture()
        next(e for e in rows if e['kind'] == 'prefetch_handoff_taken')['validationObservationId'] = O
        self.assert_check(self.report(rows, d), 'fresh_validation_observation', 'fail')

    def test_early_dispatch_fails(self):
        rows, d = fixture()
        next(e for e in rows if e['kind'] == 'native_call_started' and e['candidateRequestId'])['monotonicSeconds'] = 100.7
        self.assert_check(self.report(rows, d), 'successor_only_after_settled_and_response', 'fail')

    def test_early_reply_cannot_count_as_late(self):
        rows, d = fixture()
        d['case'] = 'direct_late'
        self.assert_check(self.report(rows, d, 'direct_late'), 'response_timing_exercises_case', 'fail')

    def test_negative_missing_injection_unknown(self):
        rows, d = fixture('direct_cancel')
        rows = [e for e in rows if e['kind'] != 'direct_fixture_injection']
        self.assertEqual(self.report(rows, d, 'direct_cancel')['verdict'], 'unknown')

    def test_negative_cancel_not_late_injection_fails(self):
        rows, d = fixture('direct_cancel')
        d['events'][1]['canceledBeforeWrite'] = False
        self.assert_check(self.report(rows, d, 'direct_cancel'), 'actual_retired_response_injection', 'fail')

    def test_late_accepted_after_invalidation_fails(self):
        rows, d = fixture('direct_cancel')
        rows.append({'kind': 'prefetch_response', 'requestId': R, 'accepted': True, 'monotonicSeconds': 101.})
        self.assert_check(self.report(rows, d, 'direct_cancel'), 'retired_response_never_accepted', 'fail')

    def test_replace_same_identity_fails(self):
        rows, d = fixture('direct_replace')
        next(e for e in rows if e['kind'] == 'direct_fixture_injection')['replacement']['runId'] = RUN
        self.assert_check(self.report(rows, d, 'direct_replace'), 'actual_identity_replacement', 'fail')

    def test_stale_reference_mismatch_fails(self):
        rows, d = fixture('direct_stale_reference')
        d['events'][1]['arguments']['target'] = 'not-the-invalidated-reference'
        self.assert_check(self.report(rows, d, 'direct_stale_reference'), 'old_public_reference_binds_invalidation', 'fail')

    def test_nonfinite_geometry_fails(self):
        rows, d = fixture()
        next(e for e in rows if e['kind'] == 'predecessor_physical_sample')['position'][0] = float('nan')
        self.assert_check(self.report(rows, d), 'finite_body_geometry_and_monotonic_time', 'fail')

    def test_failed_result_is_failure(self):
        rows, d = fixture()
        next(e for e in rows if e['kind'] == 'result')['passed'] = False
        self.assertEqual(self.report(rows, d)['verdict'], 'fail')

    def test_no_running_pair_in_short_interval_is_unknown_not_zero_motion(self):
        rows, d = fixture('direct_cancel')
        next(e for e in rows if e['kind'] == 'prefetch_invalidated')['monotonicSeconds'] = 100.21
        self.assert_check(self.report(rows, d, 'direct_cancel'), 'real_displacement_while_inference_pending', 'unknown')
