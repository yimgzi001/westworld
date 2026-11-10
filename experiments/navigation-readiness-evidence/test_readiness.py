import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import uuid

HERE = Path(__file__).parent
spec = importlib.util.spec_from_file_location('n35_validator', HERE / 'validate.py')
v = importlib.util.module_from_spec(spec); spec.loader.exec_module(v)
d = v.driver_module
S, A, R, J, N = [str(uuid.UUID(int=i)) for i in range(1, 6)]
ZERO = str(uuid.UUID(int=0))

def body(ready=True):
    return dict(Ready=ready, Blockers=[] if ready else ['not_grounded', 'vertical_motion'],
                Grounded=ready, Attached=False, Immersion=0, VerticalVelocity=0 if ready else 3,
                HorizontalSpeed=0, Health=1)

def request():
    facts = {'planningPhase': None, 'observation': {'self': {'isMounted': False, 'hasParentBody': False}},
             'terminal': {'navigation': {'busy': False, 'readiness': body()}}}
    offers = []
    for tool in ['act_Jump', 'host_navigation_status', 'host_navigate_local']:
        props = {} if tool != 'host_navigate_local' else {k: {'type': 'number', 'minimum': 0, 'maximum': 360} for k in d.NAV_ARGS}
        offers.append({'Name': tool, 'Parameters': {'type': 'object', 'properties': props, 'required': list(props)}})
    return {'sessionId': S, 'actorId': A, 'offers': offers,
            'prompt': 'OBSERVATION:\n' + json.dumps({'projectionStatus': 'projected', 'facts': facts})}

def evidence():
    jump = {'kind': 'native_call_returned', 'tool': 'act_Jump', 'sequence': 10, 'state': 'returned',
            'data': {'runId': J, 'action': 'Jump', 'status': 'running'}}
    nd = dict(request_id=N, status='failed', reason='body_not_ready', busy=False, segments=0, path=0,
              readiness=body(False), start_failure_readiness=body(False), elapsed_seconds=.001, budget_seconds=4)
    nav = {'kind': 'native_call_returned', 'tool': 'host_navigate_local', 'sequence': 12, 'state': 'returned', 'data': nd}
    action = {'runId': J, 'action': 'Jump', 'status': 'succeeded'}
    rows = [{'kind': 'native_call_started', 'decisionId': R, 'monotonicSeconds': t,
             'step': {'Tool': name, 'Arguments': dict(d.NAV_ARGS) if name == 'host_navigate_local' else {}}}
            for name, t in [('act_Jump', 1), ('host_navigation_status', 1.12), ('host_navigate_local', 1.14)]]
    rows += [jump, nav, {'kind': 'native_terminal_observed', 'tool': 'act_Jump', 'runId': J,
                         'sequence': 10, 'status': 'succeeded', 'monotonicSeconds': 1.1}]
    for r in [jump, nav]:
        rows.append({'kind': 'tool_receipt', 'receipt': {'SessionId': S, 'ActorId': A, 'Sequence': r['sequence'],
                     'Tool': r['tool'], 'State': r['state'], 'Data': copy.deepcopy(r['data'])}})
    rows.append({'kind': 'tool_receipt', 'receipt': {'SessionId': S, 'ActorId': A, 'Sequence': 11,
                 'Tool': 'action_status', 'State': 'returned', 'Data': action}})
    for t, ready in [(1.11, False), (1.2, False), (2, True), (2.1, True), (2.21, True)]:
        n = copy.deepcopy(nd); n['readiness'] = body(ready)
        rows.append({'kind': 'n35_readiness_fixture_sample', 'sessionId': S, 'actorId': A,
                     'monotonicSeconds': t, 'navigation': n, 'action': action, 'nativeRunning': False,
                     'walk': [0, 0], 'turn': [0, 0]})
    final = copy.deepcopy(nd); final['readiness'] = body()
    rows += [{'kind': 'n35_readiness_fixture_result', 'sessionId': S, 'actorId': A, 'monotonicSeconds': 2.22,
              'Passed': True, 'navigation': final, 'nativeRunning': False, 'hasViewport': False,
              'health': 1, 'localRequestId': ZERO}, {'kind': 'stop_drained', 'modelCompleted': True},
             {'kind': 'result', 'passed': True, 'updateFailures': 0},
             {'kind': 'final_audit', 'modelInFlight': False, 'requiresReconciliation': None}]
    driver = {'case': 'direct_readiness', 'error': None, 'events': [dict(sessionId=S, actorId=A, requestId=R, delivered=True)]}
    return rows, driver

class Contracts(unittest.TestCase):
    def test_plan_is_jump_query_navigation_and_no_retry(self):
        policy = d.Policy(); p = request()
        self.assertEqual([s['tool'] for s in policy.decide(p)['steps']], ['act_Jump', 'host_navigation_status', 'host_navigate_local'])
        self.assertEqual(policy.decide(p)['steps'], [{'tool': 'host_navigation_status', 'arguments': {}}])
    def test_no_missing_readiness_or_mount_guess(self):
        for k in ['isMounted', 'hasParentBody']:
            for value in [None, 0, True]:
                p = request(); f = d._protocol.facts_from_prompt(p); f['observation']['self'][k] = value
                p['prompt'] = 'OBSERVATION:\n' + json.dumps({'projectionStatus': 'projected', 'facts': f})
                with self.assertRaises(ValueError): d.Policy().decide(p)
        self.assertFalse(d.ready_sample(None)); self.assertFalse(d.ready_sample(body(False)))
    def test_no_private_context_fallback(self):
        p = request(); p['context'] = p['prompt']; p['prompt'] = 'empty'
        with self.assertRaises(ValueError): d.Policy().decide(p)
    def test_missing_query_offer_blocks_whole_plan(self):
        p = request(); p['offers'].pop(1)
        policy = d.Policy()
        with self.assertRaises(ValueError): policy.decide(p)
        self.assertFalse(policy.issued)
    def test_positive_evidence_passes(self):
        self.assertEqual(v.validate(*evidence())['verdict'], 'pass')
    def test_retry_or_late_write_fails(self):
        rows, driver = evidence(); extra = copy.deepcopy(rows[0]); extra['monotonicSeconds'] = 2.3; rows.append(extra)
        self.assertEqual(v.validate(rows, driver)['verdict'], 'fail')
    def test_cross_actor_fails(self):
        rows, driver = evidence(); next(r for r in rows if r['kind'] == 'n35_readiness_fixture_sample')['actorId'] = R
        self.assertEqual(v.validate(rows, driver)['verdict'], 'fail')
    def test_wrong_run_or_fabricated_native_segment_fails(self):
        for kind in ['wrongrun', 'segment']:
            rows, driver = evidence()
            if kind == 'wrongrun': next(r for r in rows if r['kind'] == 'native_terminal_observed')['runId'] = R
            else: rows.append({'kind': 'bounded_navigation_segment'})
            self.assertEqual(v.validate(rows, driver)['verdict'], 'fail')
    def test_missing_evidence_unknown(self):
        self.assertEqual(v.validate([], {})['verdict'], 'unknown')
    def test_sample_coverage_missing_unknown(self):
        rows, driver = evidence(); rows = [r for r in rows if not (r['kind'] == 'n35_readiness_fixture_sample' and r['monotonicSeconds'] in [2, 2.1])]
        self.assertEqual(v.validate(rows, driver)['verdict'], 'unknown')
    def test_missed_airborne_window_not_pass(self):
        rows, driver = evidence()
        for r in rows:
            if r['kind'] == 'native_call_returned' and r['tool'] == 'host_navigate_local': r['data']['start_failure_readiness'] = None
            if r['kind'] == 'tool_receipt' and r['receipt']['Tool'] == 'host_navigate_local': r['receipt']['Data']['start_failure_readiness'] = None
            if r['kind'] == 'n35_readiness_fixture_result': r['navigation']['start_failure_readiness'] = None; r['Passed'] = False
            if r['kind'] == 'result': r['passed'] = False
        self.assertEqual(v.validate(rows, driver)['verdict'], 'uncovered')
    def test_changed_landing_failure_sample_fails(self):
        rows, driver = evidence(); next(r for r in rows if r['kind'] == 'n35_readiness_fixture_result')['navigation']['start_failure_readiness'] = body()
        self.assertEqual(v.validate(rows, driver)['verdict'], 'fail')
    def test_staged_top_level_uses_only_its_frozen_dependency(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); (root / 'driver.py').write_text((HERE / 'driver.py').read_text())
            target = root / 'sources/experiments/food-acquisition-evidence/deterministic_driver.py'; target.parent.mkdir(parents=True)
            target.write_text(d.PROTOCOL.read_text())
            spec = importlib.util.spec_from_file_location('stage_readiness', root / 'driver.py'); staged = importlib.util.module_from_spec(spec); spec.loader.exec_module(staged)
            self.assertEqual(staged.PROTOCOL, target)
            self.assertEqual(len(staged.Policy().decide(request())['steps']), 3)

if __name__ == '__main__': unittest.main()
