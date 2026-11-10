import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import uuid

spec = importlib.util.spec_from_file_location('direct_driver', Path(__file__).with_name('driver.py'))
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)


def offer(name, **properties):
    return {'Name': name, 'Parameters': {'type': 'object', 'properties': properties, 'required': []}}


class DriverTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.t = 100.
        self.session, self.actor = str(uuid.uuid4()), str(uuid.uuid4())

    def clock(self):
        return self.t

    def sleep(self, amount):
        self.t += amount

    def request(self, ordinal):
        directory = self.root / 'control' / str(ordinal)
        directory.mkdir(parents=True)
        payload = {k: str(uuid.uuid4()) for k in driver.IDS}
        payload.update(protocol='westworld-tool-file-v1', sessionId=self.session, actorId=self.actor)
        num = {'type': 'number', 'minimum': -180, 'maximum': 180}
        payload['offers'] = [offer('act_MoveTo', bearing=num, distance={'type': 'number', 'minimum': 1, 'maximum': 4}, range={'type': 'number', 'minimum': .1, 'maximum': 1}, timeout={'type': 'number', 'minimum': .1, 'maximum': 4}),
                             offer('act_Wait', duration=num),
                             offer('act_PickUp', target={'type': 'string', 'enum': ['observed-drop-1']})]
        (directory / 'request.json').write_text(json.dumps(payload))
        return directory, payload

    def test_early_two_fixed_actions(self):
        a, _ = self.request(1)
        b, _ = self.request(2)
        r = driver.run(self.root, 'direct_early', self.clock, self.sleep)
        self.assertEqual(r['responses'], 2)
        self.assertAlmostEqual(r['elapsedSeconds'], .2)
        first = json.loads((a / 'response.json').read_text())['decision']['steps'][0]
        second = json.loads((b / 'response.json').read_text())['decision']['steps'][0]
        self.assertEqual(first, {'tool': 'act_MoveTo', 'arguments':
            {'bearing': 0, 'distance': 4, 'range': .25, 'timeout': 4}})
        self.assertEqual(second, {'tool': 'act_Wait', 'arguments': {'duration': .2}})

    def test_late_delay(self):
        self.request(1)
        self.request(2)
        self.assertAlmostEqual(driver.run(self.root, 'direct_late', self.clock, self.sleep)['elapsedSeconds'], 8)

    def test_negative_deliberate_retired_response(self):
        self.request(1)
        b, _ = self.request(2)
        (b / 'canceled.json').write_text('{}')
        r = driver.run(self.root, 'direct_cancel', self.clock, self.sleep)
        self.assertEqual(r['responses'], 2)
        self.assertTrue(r['events'][1]['canceledBeforeWrite'])
        self.assertTrue(r['events'][1]['deliberatelyRetiredDelivery'])
        self.assertAlmostEqual(r['elapsedSeconds'], 2)

    def test_positive_honors_cancel(self):
        self.request(1)
        b, _ = self.request(2)
        (b / 'canceled.json').write_text('{}')
        r = driver.run(self.root, 'direct_early', self.clock, self.sleep)
        self.assertEqual(r['responses'], 1)
        self.assertFalse((b / 'response.json').exists())

    def test_stale_reference_only_from_public_offer(self):
        self.request(1)
        b, _ = self.request(2)
        driver.run(self.root, 'direct_stale_reference', self.clock, self.sleep)
        self.assertEqual(json.loads((b / 'response.json').read_text())['decision']['steps'][0],
                         {'tool': 'act_PickUp', 'arguments': {'target': 'observed-drop-1'}})

    def test_no_reference_fabrication(self):
        _, p = self.request(1)
        p['offers'] = [offer('act_PickUp', target={'type': 'string', 'enum': []})]
        with self.assertRaisesRegex(ValueError, 'public_pickup'):
            driver.decision(p, 2, 'direct_stale_reference')

    def test_no_third_response(self):
        self.request(1)
        self.request(2)
        c, _ = self.request(3)
        driver.run(self.root, 'direct_early', self.clock, self.sleep)
        self.assertFalse((c / 'response.json').exists())

    def test_host_exit_during_delay_no_response(self):
        self.request(1)
        b, _ = self.request(2)
        def sleep(amount):
            self.sleep(amount)
            (self.root / 'exit.txt').write_text('0')
        r = driver.run(self.root, 'direct_late', self.clock, sleep)
        self.assertEqual(r['responses'], 1)
        self.assertFalse((b / 'response.json').exists())

    def test_no_replay(self):
        (self.root / 'exit.txt').write_text('0')
        driver.run(self.root, 'direct_early', self.clock, self.sleep)
        with self.assertRaises(FileExistsError):
            driver.run(self.root, 'direct_early', self.clock, self.sleep)

    def test_existing_response_not_overwritten(self):
        a, _ = self.request(1)
        (a / 'response.json').write_text('keep')
        with self.assertRaises(FileExistsError):
            driver.run(self.root, 'direct_early', self.clock, self.sleep)
        self.assertEqual((a / 'response.json').read_text(), 'keep')

    def test_session_actor_identity_mismatch_rejected(self):
        self.request(1)
        b, p = self.request(2)
        p['actorId'] = str(uuid.uuid4())
        (b / 'request.json').write_text(json.dumps(p))
        with self.assertRaisesRegex(ValueError, 'session_or_actor_replaced'):
            driver.run(self.root, 'direct_early', self.clock, self.sleep)

    def test_duplicate_identity_rejected(self):
        _, p = self.request(1)
        b, q = self.request(2)
        q['requestId'] = p['requestId']
        (b / 'request.json').write_text(json.dumps(q))
        with self.assertRaisesRegex(ValueError, 'duplicate_request'):
            driver.run(self.root, 'direct_early', self.clock, self.sleep)

    def test_unoffered_tool_rejected(self):
        _, p = self.request(1)
        p['offers'] = []
        with self.assertRaisesRegex(ValueError, 'unique_offered'):
            driver.decision(p, 1, 'direct_early')

    def test_bounds_rejected(self):
        _, p = self.request(1)
        p['offers'][0]['Parameters']['properties']['distance'] = {'type': 'number', 'maximum': 3}
        with self.assertRaisesRegex(ValueError, 'fixture_number_not_offered'):
            driver.decision(p, 1, 'direct_early')

    def test_symlink_rejected(self):
        a, _ = self.request(1)
        (a / 'request.json').rename(a / 'real.json')
        (a / 'request.json').symlink_to('real.json')
        with self.assertRaisesRegex(ValueError, 'symlink'):
            driver.run(self.root, 'direct_early', self.clock, self.sleep)

    def test_no_requests_deadline(self):
        r = driver.run(self.root, 'direct_early', self.clock, self.sleep)
        self.assertEqual(r['reason'], 'deadline')
        self.assertAlmostEqual(r['elapsedSeconds'], 65)

    def test_real_native_menu_rejects_old_timeout8(self):
        _, p = self.request(1)
        schema = driver.schema_for(p, 'act_MoveTo')
        with self.assertRaisesRegex(ValueError, 'fixture_number_not_offered'):
            driver.validate_arguments(schema, {'bearing': 0, 'distance': 4, 'range': .25, 'timeout': 8})
