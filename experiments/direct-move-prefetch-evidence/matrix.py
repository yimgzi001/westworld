#!/usr/bin/env python3
"""Prepare five fresh direct MoveTo stages and checkpoints. Does not start any process."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import shlex

CASES = ('direct_early', 'direct_late', 'direct_failure', 'direct_cancel', 'direct_replace')


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_new(path, value):
    with path.open('x') as f:
        json.dump(value, f, indent=2, sort_keys=True)


def prepare(root, label, build_output, content):
    root = Path(root).absolute()
    stage = load_module('direct_matrix_stage', root / 'experiments/food-acquisition-evidence/stage.py')
    recovery = load_module('direct_matrix_recovery', root / 'experiments/batch-recovery/recovery.py')
    root = stage.safe(root)
    build_output, content = stage.safe(build_output), stage.safe(content)
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]{0,35}', label):
        raise ValueError('matrix_label')
    batch = stage.safe(root / 'work' / (label + '-matrix'))
    labels = [label + '-' + case for case in CASES]
    for path in [batch] + [root / 'work' / name for name in labels] + [
            root / '.batch-checkpoints' / (name + suffix) for name in labels for suffix in ('.json', '.lock')]:
        stage.safe(path)
        if path.exists():
            raise FileExistsError('fresh matrix required: ' + str(path))
    for filename in stage.REQUIRED:
        if not stage.safe(build_output / filename).is_file():
            raise FileNotFoundError(filename)
    if not content.is_file():
        raise FileNotFoundError('Content.zip')
    source = root / 'experiments/direct-move-prefetch-evidence'
    for path in [source / 'driver.py', source / 'validate.py', source / 'matrix_runner.py',
                 root / 'experiments/navigation-continuity/driver.py',
                 root / 'experiments/batch-recovery/recovery.py']:
        if not stage.safe(path).is_file():
            raise FileNotFoundError(path)
    batch.mkdir(parents=True)
    (batch / 'frozen').mkdir()
    shutil.copy2(source / 'matrix_runner.py', batch / 'frozen/matrix_runner.py')
    shutil.copy2(root / 'experiments/batch-recovery/recovery.py', batch / 'frozen/recovery.py')
    cases, first_sources, first_binaries = [], None, None
    try:
        for case, name in zip(CASES, labels):
            dest = stage.stage(root, name, build_output, content, 'deterministic', case)
            sources = json.loads((dest / 'source-hashes.json').read_text())
            installed = json.loads((dest / 'installed-hashes.json').read_text())
            binaries = {k: v for k, v in installed.items() if k == 'Content.zip' or
                        stage.native(Path(k).name) or k.endswith(('.deps.json', '.runtimeconfig.json'))}
            if first_sources is not None and (sources != first_sources or binaries != first_binaries):
                raise RuntimeError('source_or_build_changed_between_cases; keep staged directories, do not run')
            first_sources, first_binaries = sources, binaries
            cases.append({'case': case, 'checkpointCase': name, 'directory': str(dest),
                          'installedHashesSha256': digest(dest / 'installed-hashes.json'),
                          'sourceHashesSha256': digest(dest / 'source-hashes.json')})
        manifest = {'schema': 'direct-move-matrix-v1', 'root': str(root), 'label': label,
                    'status': 'staged_not_run', 'environment': 'dot-cloud', 'cases': cases,
                    'driverSeconds': 65, 'nativeWatchdogSeconds': 80, 'caseBudgetSeconds': 90,
                    'automaticStart': False, 'modelsOrApis': False,
                    'sourceBuildCorrespondence': 'Same staged source and binary hashes across five cases; coordinator must verify build correspondence.'}
        write_new(batch / 'matrix.json', manifest)
        (batch / 'run-matrix.sh').write_text('#!/bin/bash\nset -u\nexec python3 -B ' +
            shlex.quote(str(batch / 'frozen/matrix_runner.py')) + ' --manifest ' +
            shlex.quote(str(batch / 'matrix.json')) + '\n')
        write_new(batch / 'bundle-hashes.json', {p.relative_to(batch).as_posix(): digest(p)
                  for p in batch.rglob('*') if p.is_file()})
        for item in cases:
            dest = Path(item['directory'])
            sources = [batch / 'bundle-hashes.json', batch / 'matrix.json',
                       batch / 'frozen/matrix_runner.py', batch / 'frozen/recovery.py',
                       dest / 'installed-hashes.json', dest / 'source-hashes.json', dest / 'run.sh']
            recovery.begin(root, item['checkpointCase'], 'direct-move-prefetch-native', 'dot-cloud',
                [p.relative_to(root).as_posix() for p in sources],
                [(dest / name).relative_to(root).as_posix() for name in ('matrix-case-result.json', 'validation.json')])
        write_new(batch / 'preparation.json', {'status': 'staged_not_run', 'caseCount': len(cases),
                  'checkpointsCreated': True, 'processesStarted': 0})
    except Exception as exc:
        write_new(batch / 'preparation-failure.json', {'status': 'preparation_failed_not_run',
                  'caseCountStaged': len(cases), 'error': type(exc).__name__ + ': ' + str(exc),
                  'processesStarted': 0, 'automaticCleanupOrReplay': False})
        raise
    return batch


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[2])
    p.add_argument('--label', required=True)
    p.add_argument('--build-output', required=True, type=Path)
    p.add_argument('--content', required=True, type=Path)
    a = p.parse_args()
    print(prepare(a.root, a.label, a.build_output, a.content))
