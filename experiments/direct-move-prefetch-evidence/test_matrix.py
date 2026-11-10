"""Pure matrix preparation/dispatch tests. No subprocess/game/GUI/API execution."""
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch

SOURCE = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


matrix = load('prepare_direct_matrix', Path(__file__).with_name('matrix.py'))
runner = load('run_direct_matrix', Path(__file__).with_name('matrix_runner.py'))


class MatrixTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.build = self.root / 'build'
        self.build.mkdir()
        for path in ('experiments/food-acquisition-evidence/stage.py',
                     'experiments/food-acquisition-evidence/deterministic_driver.py',
                     'experiments/batch-recovery/recovery.py',
                     'experiments/navigation-continuity/driver.py',
                     'experiments/direct-move-prefetch-evidence/driver.py',
                     'experiments/direct-move-prefetch-evidence/validate.py',
                     'experiments/direct-move-prefetch-evidence/matrix_runner.py'):
            dest = self.root / path
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(SOURCE / path, dest)
        fake_game = self.root / 'game/Survivalcraft/Component/ComponentPickableGathererPlayer.cs'
        fake_game.parent.mkdir(parents=True)
        fake_game.write_text('// synthetic source; not compiled')
        stage = load('fake_stage', self.root / 'experiments/food-acquisition-evidence/stage.py')
        for name in stage.REQUIRED:
            dest = self.build / name
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_text('synthetic artifact; never executed')
        self.content = self.root / 'Content.zip'
        self.content.write_text('synthetic content; never opened by game')

    def prepare(self):
        with patch.object(runner.subprocess, 'Popen', side_effect=AssertionError('must_not_launch')):
            return matrix.prepare(self.root, 'fixture', self.build, self.content)

    def test_prepare_five_fresh_stages_and_checkpoints(self):
        batch = self.prepare()
        manifest = runner.load_manifest(batch / 'matrix.json')
        self.assertEqual(len(manifest['cases']), 5)
        for item in manifest['cases']:
            directory = Path(item['directory'])
            checkpoint = json.loads((self.root / '.batch-checkpoints' / (item['checkpointCase'] + '.json')).read_text())
            self.assertEqual(checkpoint['state'], 'running')
            self.assertEqual(set(Path(p).name for p in checkpoint['results']), {'matrix-case-result.json', 'validation.json'})
            self.assertFalse((directory / 'tool-agent.jsonl').exists())
            self.assertFalse((directory / 'direct-driver-started.json').exists())
            self.assertIn('handoff file ' + item['case'], (directory / 'run.sh').read_text())
        self.assertTrue((batch / 'run-matrix.sh').is_file())

    def test_same_source_hashes_all_five_cases(self):
        batch = self.prepare()
        manifest = runner.load_manifest(batch / 'matrix.json')
        self.assertEqual(len({c['sourceHashesSha256'] for c in manifest['cases']}), 1)

    def test_repeated_prepare_refuses(self):
        self.prepare()
        with self.assertRaises(FileExistsError):
            self.prepare()

    def test_preexisting_case_refuses_before_creation(self):
        (self.root / 'work/fixture-direct_cancel').mkdir(parents=True)
        with self.assertRaises(FileExistsError):
            self.prepare()
        self.assertFalse((self.root / 'work/fixture-matrix').exists())

    def test_missing_build_rejects_before_creation(self):
        (self.build / 'ToolAgentGate.dll').unlink()
        with self.assertRaises(FileNotFoundError):
            self.prepare()
        self.assertFalse((self.root / 'work/fixture-matrix').exists())

    def test_manifest_modification_rejected(self):
        batch = self.prepare()
        p = batch / 'matrix.json'
        data = json.loads(p.read_text())
        data['caseBudgetSeconds'] = 999
        p.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, 'hash_changed'):
            runner.load_manifest(p)

    def test_failures_do_not_stop_five_case_iteration(self):
        batch = self.prepare()
        seen = []
        def fake(item, manifest, recovery):
            seen.append(item['case'])
            return {'case': item['case'], 'status': 'failed', 'validationVerdict': 'unknown'}
        with patch.object(runner.subprocess, 'Popen', side_effect=AssertionError('must_not_launch')):
            result = runner.run_matrix(batch / 'matrix.json', case_runner=fake)
        self.assertEqual(seen, list(matrix.CASES))
        self.assertEqual(result['recordedCases'], 5)
        self.assertEqual(result['status'], 'failed')

    def test_unhandled_case_error_still_runs_remaining(self):
        batch = self.prepare()
        seen = []
        def fake(item, manifest, recovery):
            seen.append(item['case'])
            raise ValueError('controlled fixture error')
        result = runner.run_matrix(batch / 'matrix.json', case_runner=fake)
        self.assertEqual(len(seen), 5)
        self.assertEqual(result['recordedCases'], 5)

    def test_uncertain_process_drain_stops_serial_safety(self):
        batch = self.prepare()
        def fake(item, manifest, recovery):
            return {'case': item['case'], 'status': 'failed', 'executionError': 'owned_process_drain_uncertain'}
        result = runner.run_matrix(batch / 'matrix.json', case_runner=fake)
        self.assertEqual(result['recordedCases'], 1)

    def test_case_integrity_failure_finishes_checkpoint_without_fake_raw_logs(self):
        batch = self.prepare()
        manifest = runner.load_manifest(batch / 'matrix.json')
        item = manifest['cases'][0]
        dest = Path(item['directory'])
        (dest / 'ToolAgentGate.dll').write_text('changed')
        recovery = load('test_matrix_recovery', batch / 'frozen/recovery.py')
        with patch.object(runner.subprocess, 'Popen', side_effect=AssertionError('must_not_launch')):
            result = runner.run_case(item, manifest, recovery)
        self.assertEqual(result['status'], 'failed')
        self.assertIn('tool-agent.jsonl', result['missingArtifacts'])
        self.assertFalse((dest / 'tool-agent.jsonl').exists())
        self.assertFalse((dest / 'direct-driver-result.json').exists())
        record = json.loads((self.root / '.batch-checkpoints' / (item['checkpointCase'] + '.json')).read_text())
        self.assertEqual(record['state'], 'failed')

    def test_used_case_not_overwritten(self):
        batch = self.prepare()
        manifest = runner.load_manifest(batch / 'matrix.json')
        item = manifest['cases'][0]
        dest = Path(item['directory'])
        (dest / 'tool-agent.jsonl').write_text('keep original')
        result = runner.run_case(item, manifest, None)
        self.assertEqual(result['status'], 'refused_existing_evidence')
        self.assertEqual((dest / 'tool-agent.jsonl').read_text(), 'keep original')
        self.assertFalse((dest / 'matrix-case-result.json').exists())

    def test_matrix_replay_refused(self):
        batch = self.prepare()
        fake = lambda item, manifest, recovery: {'case': item['case'], 'status': 'passed'}
        runner.run_matrix(batch / 'matrix.json', case_runner=fake)
        with self.assertRaises(FileExistsError):
            runner.run_matrix(batch / 'matrix.json', case_runner=fake)
