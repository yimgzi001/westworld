import copy
import datetime
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest
import uuid

spec = importlib.util.spec_from_file_location('natural_driver', Path(__file__).with_name('deterministic_driver.py'))
d = importlib.util.module_from_spec(spec)
spec.loader.exec_module(d)


def offer(name, props, required, branches=None):
    return {'Name': name, 'Parameters': {'type': 'object', 'properties': props, 'required': required,
                                        'additionalProperties': False}, 'ParameterBranches': branches}


def block(ref='soil', x=1, y=64, z=0, value=2, distance=1.5):
    return {'reference': ref, 'cell': {'x': x, 'y': y, 'z': z},
            'position': {'x': x + .5, 'y': y + .999, 'z': z + .5},
            'value': value, 'distance': distance}


def facts(request):
    return d.shared.facts_from_prompt(request)


def setfacts(request, value, fresh=True):
    if fresh:
        request['observationId'] = str(uuid.uuid4())
    request['prompt'] = 'PUBLIC TOOLS\nOBSERVATION:\n' + json.dumps({'projectionStatus': 'projected', 'facts': value})


def request():
    number = lambda low, high: {'type': 'number', 'minimum': low, 'maximum': high}
    refs = {'type': 'string', 'enum': ['soil', 'grass', 'wood', 'other', 'drop', 'existing', 'refreshed']}
    p = {k: str(uuid.uuid4()) for k in d.IDS}
    p.update(protocol='westworld-tool-file-v1', deadlineUtc='2099-01-01T00:00:00+00:00', offers=[
        offer('act_Dig', {'target': refs, 'timeout': number(.1, 30)}, ['target', 'timeout']),
        offer('act_MoveTo', {'destination': refs, 'bearing': number(-180, 180), 'distance': number(1, 4),
                            'range': number(.1, 1), 'timeout': number(.1, 4)}, ['timeout'],
              [{'Required': ['destination'], 'Forbidden': ['bearing', 'distance']},
               {'Required': ['bearing', 'distance'], 'Forbidden': ['destination']}]),
        offer('act_PickUp', {'target': refs, 'count': {'type': 'integer', 'minimum': 1, 'maximum': 64}},
              ['count'], [{'Required': ['target'], 'Forbidden': ['food_value']}]),
    ])
    setfacts(p, {'observation': {'self': {'position': {'x': 0, 'y': 65, 'z': 0}, 'health': 1., 'isSleeping': False},
                                'visibleBlocks': [block()], 'pickables': []},
                 'inventory': {'slots': [{'slot': 0, 'value': 162, 'count': 1}]}, 'planningPhase': None,
                 'terminal': {'action': None, 'task': {'status': 'idle'}, 'navigation': {'status': 'idle'}},
                 'outcomeFeedback': {'status': 'no_valid_delta'}})
    return p


def after_dig(p, status='succeeded', distance=.8, visible=True, drop_ref='drop', value=2):
    f = facts(p)
    f['terminal']['action'] = {'action': 'Dig', 'status': status}
    f['outcomeFeedback'] = {'preceding': {'tool': 'act_Dig', 'status': status}}
    f['observation']['pickables'] = [{'reference': drop_ref, 'value': value, 'count': 1,
        'sense': 'vision' if visible else 'proximity', 'position': {'x': distance, 'y': 65, 'z': 0}}]
    setfacts(p, f)


class PolicyTests(unittest.TestCase):
    def test_single_safe_dig_then_actual_visible_pickup(self):
        p = request(); policy = d.Policy()
        self.assertEqual(policy.decide(p)['steps'], [{'tool': 'act_Dig', 'arguments': {'target': 'soil', 'timeout': 4}}])
        after_dig(p)
        self.assertEqual(policy.decide(p)['steps'], [{'tool': 'act_PickUp', 'arguments': {'target': 'drop', 'count': 1}}])

    def test_no_move_before_dig(self):
        p = request(); after_dig(p, distance=2)
        self.assertEqual(d.Policy().decide(p)['steps'][0]['tool'], 'act_Dig')

    def test_visible_far_drop_allows_bounded_native_move(self):
        p = request(); policy = d.Policy(); policy.decide(p); after_dig(p, distance=2)
        self.assertEqual(policy.decide(p)['steps'], [{'tool': 'act_MoveTo', 'arguments': {'destination': 'drop', 'range': .75, 'timeout': 4}}])

    def test_inventory_increase_is_observation_not_attribution(self):
        p = request(); policy = d.Policy(); policy.decide(p); after_dig(p)
        f = facts(p); f['inventory']['slots'].append({'slot': 1, 'value': 2, 'count': 1}); setfacts(p, f)
        result = policy.decide(p)
        self.assertEqual(result['steps'], [])
        self.assertIn('independently prove', result['goal'])

    def test_initial_inventory_not_new(self):
        p = request(); f = facts(p); f['inventory']['slots'].append({'slot': 1, 'value': 2, 'count': 1}); setfacts(p, f)
        self.assertEqual(d.Policy().decide(p)['steps'][0]['tool'], 'act_Dig')

    def test_repeated_observation_does_not_replay(self):
        p = request(); policy = d.Policy(); policy.decide(p)
        self.assertEqual(policy.decide(p)['steps'], [])

    def test_new_ref_same_failed_cell_never_redigs(self):
        p = request(); policy = d.Policy(); policy.decide(p); after_dig(p, status='failed')
        f = facts(p); f['observation']['visibleBlocks'][0]['reference'] = 'refreshed'; setfacts(p, f)
        self.assertEqual(policy.decide(p)['steps'], [])

    def test_failed_ref_can_switch_to_other_safe_cell(self):
        p = request(); policy = d.Policy(); policy.decide(p); after_dig(p, status='failed')
        f = facts(p); f['observation']['visibleBlocks'].append(block('other', x=-2)); setfacts(p, f)
        self.assertEqual(policy.decide(p)['steps'][0]['arguments']['target'], 'other')

    def test_missing_terminal_does_not_assume_dig_success(self):
        p = request(); policy = d.Policy(); policy.decide(p); f = facts(p); setfacts(p, f)
        self.assertEqual(policy.decide(p)['steps'], [])

    def test_busy_native_state_no_action(self):
        p = request(); f = facts(p); f['terminal']['action'] = {'action': 'Dig', 'status': 'running'}; setfacts(p, f)
        self.assertEqual(d.Policy().decide(p)['steps'], [])

    def test_unknown_outcome_stops_all_future_replay(self):
        p = request(); policy = d.Policy(); policy.decide(p); after_dig(p)
        f = facts(p); f['outcomeFeedback'] = {'status': 'outcome_unknown'}; setfacts(p, f)
        self.assertEqual(policy.decide(p)['steps'], [])
        after_dig(p); self.assertEqual(policy.decide(p)['steps'], [])

    def test_prefetch_no_invented_future_drop(self):
        p = request(); f = facts(p); f['planningPhase'] = {'phase': 'AfterPrecedingSuccess'}; setfacts(p, f)
        self.assertEqual(d.Policy().decide(p)['steps'], [])

    def test_proximity_drop_not_visible(self):
        p = request(); policy = d.Policy(); policy.decide(p); after_dig(p, visible=False)
        self.assertEqual(policy.decide(p)['steps'], [])

    def test_preexisting_drop_not_used_as_harvest(self):
        p = request(); f = facts(p); f['observation']['pickables'] = [{'reference': 'existing'}]; setfacts(p, f)
        policy = d.Policy(); policy.decide(p); after_dig(p, drop_ref='existing')
        self.assertEqual(policy.decide(p)['steps'], [])

    def test_unoffered_drop_reference_not_acted_on(self):
        p = request(); policy = d.Policy(); policy.decide(p); after_dig(p, drop_ref='invented')
        self.assertEqual(policy.decide(p)['steps'], [])

    def test_wrong_resource_drop_not_followed(self):
        p = request(); policy = d.Policy(); policy.decide(p); after_dig(p, value=89)
        self.assertEqual(policy.decide(p)['steps'], [])

    def test_drop_too_far_or_vertical_not_followed(self):
        for distance, y in [(4, 65), (2, 63)]:
            p = request(); policy = d.Policy(); policy.decide(p); after_dig(p, distance=distance)
            f = facts(p); f['observation']['pickables'][0]['position']['y'] = y; setfacts(p, f)
            self.assertEqual(policy.decide(p)['steps'], [])

    def test_same_pickup_ref_not_blindly_retried(self):
        p = request(); policy = d.Policy(); policy.decide(p); after_dig(p); policy.decide(p)
        f = facts(p); f['terminal']['action'] = {'action': 'PickUp', 'status': 'failed'}
        f['outcomeFeedback'] = {'preceding': {'tool': 'act_PickUp', 'status': 'failed'}}; setfacts(p, f)
        self.assertEqual(policy.decide(p)['steps'], [])

    def test_support_under_or_straddling_body_rejected(self):
        for position, b in [(0, block(x=0)), (.9, block(x=1)), (.01, block(x=-1))]:
            p = request(); f = facts(p); f['observation']['self']['position']['x'] = position
            f['observation']['visibleBlocks'] = [b]; setfacts(p, f)
            self.assertEqual(d.Policy().decide(p)['steps'], [])

    def test_liquid_unknown_rock_and_sand_rejected(self):
        for value in [0, 3, 7, 18, 92, 1000, -1]:
            p = request(); f = facts(p); f['observation']['visibleBlocks'] = [block(value=value)]; setfacts(p, f)
            self.assertEqual(d.Policy().decide(p)['steps'], [])

    def test_known_adjacent_liquid_rejected(self):
        p = request(); f = facts(p); f['observation']['visibleBlocks'].append(block('other', x=2, value=18)); setfacts(p, f)
        self.assertEqual(d.Policy().decide(p)['steps'], [])

    def test_dirt_before_wood_but_wood_allowed(self):
        p = request(); f = facts(p); f['observation']['visibleBlocks'] = [block('wood', x=-2, value=9)]; setfacts(p, f)
        self.assertEqual(d.Policy().decide(p)['steps'][0]['arguments']['target'], 'wood')
        f['observation']['visibleBlocks'].append(block()); setfacts(p, f)
        self.assertEqual(d.Policy().decide(p)['steps'][0]['arguments']['target'], 'soil')

    def test_remote_below_surface_and_mismatched_cell_rejected(self):
        for b in [block(x=5), block(y=62), block(y=68), block(distance=4)]:
            p = request(); f = facts(p); f['observation']['visibleBlocks'] = [b]; setfacts(p, f)
            self.assertEqual(d.Policy().decide(p)['steps'], [])
        p = request(); f = facts(p); f['observation']['visibleBlocks'][0]['position']['x'] = -3; setfacts(p, f)
        self.assertEqual(d.Policy().decide(p)['steps'], [])

    def test_no_private_context_or_audit_fallback(self):
        p = request(); p['prompt'] = 'missing public observation'; p['context'] = {'visibleBlocks': [block()]}
        with self.assertRaises(ValueError): d.Policy().decide(p)

    def test_schema_timeout_boundary_and_missing_tool(self):
        p = request(); p['offers'][0]['Parameters']['properties']['timeout']['maximum'] = 3
        self.assertEqual(d.Policy().decide(p)['steps'], [])
        p['offers'] = []; self.assertEqual(d.Policy().decide(p)['steps'], [])

    def test_invalid_position_and_scope_rejected(self):
        p = request(); f = facts(p); f['observation']['self']['position']['x'] = float('nan'); setfacts(p, f)
        with self.assertRaises(ValueError): d.Policy().decide(p)
        p = request(); policy = d.Policy(); policy.decide(p); p['actorId'] = str(uuid.uuid4())
        with self.assertRaises(ValueError): policy.decide(p)

    def test_low_health_stops(self):
        p = request(); f = facts(p); f['observation']['self']['health'] = .4; setfacts(p, f)
        self.assertEqual(d.Policy().decide(p)['steps'], [])


class DriverTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name); self.t = 0.; self.base = request()
    def clock(self): return self.t
    def sleep(self, dt): self.t += dt
    def put(self, n, p=None):
        p = copy.deepcopy(p or self.base); p['requestId'] = str(uuid.uuid4()); p['observationId'] = str(uuid.uuid4())
        folder = self.root / 'control' / str(n); folder.mkdir(parents=True); (folder / 'request.json').write_text(json.dumps(p)); return folder
    def run_driver(self): return d.run(self.root, self.clock, self.sleep)

    def test_eight_requests_only_and_replay_rejected(self):
        for i in range(9): self.put(i)
        r = self.run_driver(); self.assertEqual(r['receivedRequests'], 8); self.assertEqual(r['responses'], 8)
        self.assertFalse((self.root / 'control/8/response.json').exists())
        with self.assertRaises(FileExistsError): self.run_driver()

    def test_request_deadline_and_cancellation_no_response(self):
        for i in range(8):
            p = request(); p['sessionId'], p['actorId'] = self.base['sessionId'], self.base['actorId']
            if i % 2 == 0: p['deadlineUtc'] = '2000-01-01T00:00:00Z'
            folder = self.put(i, p)
            if i % 2: (folder / 'canceled.json').write_text('{}')
        self.assertEqual(self.run_driver()['responses'], 0)

    def test_wall_deadline(self):
        r = self.run_driver(); self.assertEqual(r['reason'], 'deadline'); self.assertAlmostEqual(r['elapsedSeconds'], 180)

    def test_host_exit_no_work(self):
        self.put(0); (self.root / 'exit.txt').write_text('1')
        r = self.run_driver(); self.assertEqual(r['responses'], 0); self.assertEqual(r['reason'], 'host_exit')

    def test_existing_response_preserved_and_error_recorded(self):
        folder = self.put(0); (folder / 'response.json').write_text('keep')
        r = self.run_driver(); self.assertEqual(r['reason'], 'driver_error')
        self.assertEqual((folder / 'response.json').read_text(), 'keep')
        self.assertTrue((self.root / 'natural-driver-result.json').is_file())

    def test_symlink_request_rejected_with_error_result(self):
        folder = self.put(0); (folder / 'request.json').rename(folder / 'actual.json')
        (folder / 'request.json').symlink_to('actual.json'); r = self.run_driver()
        self.assertEqual(r['reason'], 'driver_error'); self.assertEqual(r['responses'], 0)

    def test_same_observation_cannot_replay_under_new_request(self):
        a, b = self.put(0), self.put(1); p = json.loads((a / 'request.json').read_text()); q = json.loads((b / 'request.json').read_text()); q['observationId'] = p['observationId']; (b / 'request.json').write_text(json.dumps(q))
        r = self.run_driver(); self.assertEqual(r['responses'], 1); self.assertIn('duplicate_request_or_observation', r['error'])

    def test_identity_mismatch_is_bounded_error(self):
        self.put(0); p = request(); self.put(1, p); r = self.run_driver()
        self.assertEqual(r['responses'], 1); self.assertIn('scope_changed', r['error'])

    def test_top_level_staged_dependency_uses_frozen_copy(self):
        entry = self.root / 'deterministic_driver.py'; shutil.copy2(Path(__file__).with_name('deterministic_driver.py'), entry)
        dependency = self.root / 'sources/experiments/food-acquisition-evidence/deterministic_driver.py'
        dependency.parent.mkdir(parents=True); shutil.copy2(d._shared_path, dependency)
        spec = importlib.util.spec_from_file_location('frozen_natural_driver', entry)
        loaded = importlib.util.module_from_spec(spec); spec.loader.exec_module(loaded)
        self.assertEqual(loaded._shared_path, dependency)
        self.assertEqual(loaded.Policy().decide(request())['steps'][0]['tool'], 'act_Dig')
