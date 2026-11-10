#!/usr/bin/env python3
"""Explicit GUI-terminal entry point for a frozen, bounded serial fixture matrix."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time

CASES = ('direct_early', 'direct_late', 'direct_failure', 'direct_cancel', 'direct_replace')


def safe(path):
    path = Path(path).absolute()
    if any(p.is_symlink() for p in (path, *path.parents)):
        raise ValueError('symlink')
    return path


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_new(path, value):
    with safe(path).open('x') as f:
        json.dump(value, f, indent=2, sort_keys=True)


def verify_hashes(directory, hashes):
    for relative, expected in hashes.items():
        p = Path(relative)
        if p.is_absolute() or '..' in p.parts or not relative:
            raise ValueError('unsafe_hash_path')
        if digest(safe(directory / p)) != expected:
            raise ValueError('hash_changed: ' + relative)


def load_manifest(path):
    path = safe(path)
    batch = path.parent
    verify_hashes(batch, json.loads((batch / 'bundle-hashes.json').read_text()))
    m = json.loads(path.read_text())
    root = safe(m['root'])
    if m.get('schema') != 'direct-move-matrix-v1' or m.get('environment') != 'dot-cloud':
        raise ValueError('manifest_schema')
    if batch != root / 'work' / (m['label'] + '-matrix') or tuple(c['case'] for c in m['cases']) != CASES:
        raise ValueError('matrix_scope')
    if (m.get('nativeWatchdogSeconds'), m.get('caseBudgetSeconds'), m.get('driverSeconds')) != (80, 90, 65):
        raise ValueError('fixed_budget_required')
    for item in m['cases']:
        expected = m['label'] + '-' + item['case']
        if item['checkpointCase'] != expected or safe(item['directory']) != root / 'work' / expected:
            raise ValueError('case_scope')
    return m


def terminate_owned(process):
    """Only process groups started by this runner, never global game/API kill patterns."""
    if process is None:
        return
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        return
    try:
        process.wait(timeout=1)
    except subprocess.TimeoutExpired:
        pass
    # Kill lingering descendants even if the shell exited on SIGTERM.
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    try:
        process.wait(timeout=1)
    except subprocess.TimeoutExpired:
        raise RuntimeError('owned_process_drain_uncertain')


def validation_unknown(case, reason):
    return {'schema': 'direct-move-validation-v1', 'case': case, 'verdict': 'unknown',
            'checks': [{'check': reason, 'status': 'unknown'}], 'metrics': {}}


def run_case(item, manifest, recovery):
    root, dest = safe(manifest['root']), safe(item['directory'])
    started = time.monotonic()
    result = {'schema': 'direct-move-matrix-case-v1', 'case': item['case'],
              'checkpointCase': item['checkpointCase'], 'status': 'running',
              'hostExit': None, 'driverExit': None, 'watchdogExpired': False,
              'executionError': None, 'checkpointError': None, 'artifacts': {}, 'missingArtifacts': []}
    driver = host = None
    handles = []
    validation = validation_unknown(item['case'], 'not_run')
    replay = any((dest / name).exists() for name in
                 ('tool-agent.jsonl', 'doc', 'Mods', 'exit.txt', 'direct-driver-started.json',
                  'matrix-case-started.json', 'matrix-case-result.json', 'validation.json'))
    if replay:
        # Never modify or replay a used case. A matrix invocation cannot resume implicitly.
        return dict(result, status='refused_existing_evidence', executionError='fresh_case_required')
    write_new(dest / 'matrix-case-started.json', {'case': item['case'], 'monotonicSeconds': started})
    try:
        if digest(dest / 'installed-hashes.json') != item['installedHashesSha256'] or \
                digest(dest / 'source-hashes.json') != item['sourceHashesSha256']:
            raise ValueError('stage_manifest_changed')
        verify_hashes(dest, json.loads((dest / 'installed-hashes.json').read_text()))
        checkpoint = recovery.load(root / '.batch-checkpoints' / (item['checkpointCase'] + '.json'))
        if checkpoint['state'] != 'running' or checkpoint['environment'] != 'dot-cloud':
            raise ValueError('checkpoint_not_ready')
        driver_path = dest / 'sources/experiments/direct-move-prefetch-evidence/driver.py'
        driver_log = (dest / 'driver.log').open('x')
        host_log = (dest / 'matrix-launch.log').open('x')
        handles += [driver_log, host_log]
        driver = subprocess.Popen([sys.executable, '-B', str(driver_path), str(dest), '--case', item['case']],
                                  cwd=dest, stdout=driver_log, stderr=subprocess.STDOUT, start_new_session=True)
        host = subprocess.Popen(['bash', str(dest / 'run.sh')], cwd=dest, stdout=host_log,
                                stderr=subprocess.STDOUT, start_new_session=True)
        while host.poll() is None or driver.poll() is None:
            if time.monotonic() - started >= manifest['nativeWatchdogSeconds']:
                result['watchdogExpired'] = True
                break
            if host.poll() is not None and not (dest / 'exit.txt').exists():
                # Separate wrapper outcome, not a fabricated native exit.txt.
                if not (dest / 'matrix-host-exit.json').exists():
                    write_new(dest / 'matrix-host-exit.json', {'wrapperExit': host.returncode,
                              'nativeExitFileMissing': True})
                terminate_owned(driver)
                break
            time.sleep(.05)
    except Exception as exc:
        result['executionError'] = type(exc).__name__ + ': ' + str(exc)
    finally:
        for process in (host, driver):
            if process is not None and process.poll() is None:
                try:
                    terminate_owned(process)
                except Exception as exc:
                    result['executionError'] = str(exc)
        result['hostExit'] = host.returncode if host else None
        result['driverExit'] = driver.returncode if driver else None
        for h in handles:
            h.close()
    if not (dest / 'driver-exit.txt').exists():
        with (dest / 'driver-exit.txt').open('x') as f:
            f.write(str(result['driverExit']) + '\n')
    log, driver_result = dest / 'tool-agent.jsonl', dest / 'direct-driver-result.json'
    if log.is_file() and driver_result.is_file():
        validator = dest / 'sources/experiments/direct-move-prefetch-evidence/validate.py'
        remaining = max(.05, min(5, manifest['caseBudgetSeconds'] - (time.monotonic() - started)))
        try:
            completed = subprocess.run([sys.executable, '-B', str(validator), str(log), '--driver-result',
                str(driver_result), '--case', item['case']], cwd=dest, capture_output=True, text=True,
                timeout=remaining, check=False)
            with (dest / 'validation-process.log').open('x') as f:
                f.write(completed.stdout + '\n' + completed.stderr)
            validation = json.loads(completed.stdout)
            if validation.get('verdict') not in ('pass', 'fail', 'unknown'):
                raise ValueError('validator_verdict')
            if completed.returncode != {'pass': 0, 'fail': 1, 'unknown': 2}[validation['verdict']]:
                raise ValueError('validator_exit_mismatch')
        except Exception as exc:
            validation = validation_unknown(item['case'], 'validator_error:' + type(exc).__name__)
    else:
        validation = validation_unknown(item['case'], 'native_log_or_driver_result_missing')
    write_new(dest / 'validation.json', validation)
    result['validationVerdict'] = validation['verdict']
    result['status'] = 'passed' if validation['verdict'] == 'pass' and result['hostExit'] == 0 and \
        result['driverExit'] == 0 and not result['watchdogExpired'] and not result['executionError'] else 'failed'
    names = ('tool-agent.jsonl', 'console.log', 'driver.log', 'direct-driver-result.json',
             'exit.txt', 'driver-exit.txt', 'validation.json', 'validation-process.log')
    for name in names:
        p = dest / name
        if p.is_file():
            result['artifacts'][name] = digest(p)
        else:
            result['missingArtifacts'].append(name)
    result['elapsedSeconds'] = time.monotonic() - started
    result['caseBudgetExceeded'] = result['elapsedSeconds'] > manifest['caseBudgetSeconds']
    if result['caseBudgetExceeded']:
        result['status'] = 'failed'
    write_new(dest / 'matrix-case-result.json', result)
    try:
        recovery.finish(root, item['checkpointCase'], result['status'])
    except Exception as exc:
        # Keep original case result immutable. A separate record reports checkpoint failure.
        result['checkpointError'] = type(exc).__name__ + ': ' + str(exc)
        write_new(dest / 'matrix-checkpoint-error.json', {'error': result['checkpointError']})
        result['status'] = 'failed'
    return result


def run_matrix(path, case_runner=run_case):
    manifest = load_manifest(path)
    batch = safe(path).parent
    write_new(batch / 'matrix-started.json', {'monotonicSeconds': time.monotonic(), 'replay': False})
    spec = importlib.util.spec_from_file_location('frozen_matrix_recovery', batch / 'frozen/recovery.py')
    recovery = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(recovery)
    results = []
    for item in manifest['cases']:
        try:
            result = case_runner(item, manifest, recovery)
        except Exception as exc:
            result = {'case': item['case'], 'status': 'failed', 'executionError': type(exc).__name__ + ': ' + str(exc)}
        results.append(result)
        print(json.dumps({'case': item['case'], 'status': result['status'],
                          'validationVerdict': result.get('validationVerdict')}), flush=True)
        # An ordinary failed check/exit never aborts the next fresh case.
        # Uncertain owned-process drain is different: launching another GUI would violate serial execution.
        if result.get('executionError') == 'owned_process_drain_uncertain':
            break
    summary = {'schema': 'direct-move-matrix-result-v1', 'cases': results,
               'plannedCases': len(manifest['cases']), 'recordedCases': len(results),
               'status': 'passed' if len(results) == len(manifest['cases']) and
                         all(r['status'] == 'passed' for r in results) else 'failed'}
    write_new(batch / 'matrix-result.json', summary)
    return summary


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--manifest', required=True, type=Path)
    a = p.parse_args()
    result = run_matrix(a.manifest)
    raise SystemExit(0 if result['status'] == 'passed' else 1)
