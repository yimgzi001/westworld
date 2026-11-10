#!/usr/bin/env python3
"""独立离线验证：仅解码、校验哈希与失败语义，不执行归档源码或游戏。"""
import base64, collections, gzip, hashlib, json, pathlib, re
HERE = pathlib.Path(__file__).resolve().parent
sha = lambda value: hashlib.sha256(value).hexdigest()
SECRET = re.compile(rb'(?:sk-[A-Za-z0-9_.-]{16,}|Bearer\s+[A-Za-z0-9_.-]{16,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|(?:api[_-]?key|access[_-]?token|password)\s*[:=]\s*[\x22\x27][A-Za-z0-9_./+\-=]{20,}[\x22\x27])', re.I)
def safe_path(value):
    p = pathlib.PurePosixPath(value)
    assert not p.is_absolute() and '..' not in p.parts
    return HERE / value
index = json.loads((HERE / 'manifest.json').read_text())
reachable = {'manifest.json', 'README.md', 'archive.py', 'verify.py'}
decoded = {}
parts_checked = 0
for key, ref in index['bundles'].items():
    raw = safe_path(ref['manifest']).read_bytes()
    assert sha(raw) == ref['sha256']
    reachable.add(ref['manifest'])
    metadata = json.loads(raw)
    parts = []
    for item in metadata['parts']:
        raw_part = safe_path(item['file']).read_bytes()
        assert len(raw_part) == item['bytes'] <= 29000 and sha(raw_part) == item['sha256']
        parts.append(raw_part)
        reachable.add(item['file'])
        parts_checked += 1
    zipped = base64.b64decode(b''.join(parts), validate=True)
    assert sha(zipped) == metadata['gzipSHA256']
    raw = gzip.decompress(zipped)
    assert sha(raw) == metadata['rawSHA256'] and len(raw) == metadata['rawBytes']
    assert key.endswith(sha(raw)[:20])
    assert not SECRET.search(raw), key
    decoded[key] = json.loads(raw)
def evidence(ref):
    value = decoded[ref['bundle']]
    assert isinstance(value, str) and sha(value.encode()) == ref['archivedTextSHA256']
    assert '/workspace/scratch/' not in value, '云工作区路径未脱敏'
    if ref['pathRedactions'] == 0:
        assert ref['originalSHA256'] == ref['archivedTextSHA256']
    return value
source_checks = 0
checkpoint_checks = 0
binary_checks = 0
for name, ref in index['runs'].items():
    raw = safe_path(ref['manifest']).read_bytes()
    assert sha(raw) == ref['sha256']
    reachable.add(ref['manifest'])
    run = json.loads(raw)
    sources = decoded[run['sourceSnapshot']]
    assert len(sources['inventory']) == len(sources['files']) == sources['count'] == 117
    for path, digest in sources['inventory'].items():
        p = pathlib.PurePosixPath(path)
        assert not p.is_absolute() and '..' not in p.parts
        assert sha(decoded[sources['files'][path]].encode()) == digest
        source_checks += 1
    logs = {path: evidence(value) for path, value in run['logs'].items()}
    assert json.loads(logs['source-hashes.json']) == sources['inventory']
    assert int(logs['exit.txt']) == 1 and ref['verdict'] == 'fail'
    rows = [json.loads(line) for line in logs['tool-agent.jsonl'].splitlines() if line.strip()]
    counts = dict(collections.Counter(row.get('kind') for row in rows))
    summary = run['summary']
    assert counts == summary['eventCounts']
    assert [row for row in rows if row.get('kind') == 'result'] == [summary['hostResult']]
    assert summary['hostResult']['passed'] is False and summary['hostResult']['reason'] == 'handoff_test_budget'
    final = [row for row in rows if row.get('kind') == 'final_audit']
    assert len(final) == 1 and final[0]['prefetchTaken'] == summary['prefetchTaken'] == 0
    assert final[0]['overlapSeconds'] == summary['overlapSeconds'] == 0
    assert final[0]['modelInFlight'] is False
    assert final[0]['path'] == summary['pathMeters']
    assert summary['nativeTerminals'] == [row for row in rows if row.get('kind') == 'native_terminal_observed']
    checkpoint = json.loads(evidence(run['checkpoint']))
    assert checkpoint['state'] == summary['checkpointStateAsRecorded']
    assert checkpoint['finishedUtc'] == summary['checkpointFinishedUtcAsRecorded']
    installed = json.loads(logs['installed-hashes.json'])
    verified = run['buildCorrespondence']['verifiedInstalledBinaryHashes']
    for filename, digest in verified.items():
        assert installed[filename] == digest
        binary_checks += 1
    for relative, digest in (checkpoint['sources'] | checkpoint['results']).items():
        if digest is None:
            continue
        short = pathlib.PurePosixPath(relative).name
        if short in logs:
            assert run['logs'][short]['originalSHA256'] == digest
        else:
            assert verified[short] == digest
        checkpoint_checks += 1
    if name == 'direct29-early':
        assert 'direct-driver-result.json' not in logs and summary['driverExit'] == 1
        assert counts.get('native_call_started', 0) == 0
        assert checkpoint['finishedUtc'] is None and checkpoint['state'] == 'running'
        assert 'fixture_number_not_offered' in logs['driver.log']
    else:
        assert summary['driverExit'] == 0 and counts['prefetch_setup_failure'] == 220
        assert json.loads(logs['independent-validation.json'])['verdict'] == 'fail'
        driver = json.loads(logs['direct-driver-result.json'])
        assert driver['responses'] == 2 and driver['reason'] == 'request_limit'
        move = [row for row in summary['nativeTerminals'] if row['tool'] == 'act_MoveTo']
        assert len(move) == 1 and move[0]['status'] == 'succeeded'
        assert abs(move[0]['nativeCallToTerminalObservedSeconds'] - 2.1140880470047705) < 1e-9
for ref in index['buildEvidence'].values():
    evidence(ref)
build = json.loads(evidence(index['buildEvidence']['work/preceding29/host-manifest.json']))
for name, ref in index['runs'].items():
    run = json.loads(safe_path(ref['manifest']).read_text())
    correspondence = run['buildCorrespondence']
    for path in correspondence['matchedFrozenHostSources']:
        assert build['files'][path] == decoded[run['sourceSnapshot']]['inventory'][path]
    assert len(correspondence['matchedFrozenHostSources']) == 7
    assert build['files']['work/visible-facts-host-isolated/bin/ToolAgentGate/debug/ToolAgentGate.dll'] == correspondence['verifiedInstalledBinaryHashes']['ToolAgentGate.dll']
assert index['archiveComplete'] is True
orphans = [p.name for p in HERE.iterdir() if p.is_file() and p.name not in reachable]
assert not orphans, orphans
assert all(not p.is_symlink() for p in HERE.iterdir())
print(json.dumps({'runs': len(index['runs']), 'bundles': len(decoded), 'parts': parts_checked,
                  'sourceFileVersions': sum(k.startswith('source-file-') for k in decoded),
                  'sourceHashChecks': source_checks, 'checkpointHashChecks': checkpoint_checks,
                  'installedHashLinks': binary_checks, 'buildEvidenceFiles': len(index['buildEvidence']),
                  'archiveComplete': True, 'scenarioPasses': 0,
                  'reachableBytes': sum((HERE / name).stat().st_size for name in reachable),
                  'largestFileBytes': max((HERE / name).stat().st_size for name in reachable), 'orphanFiles': orphans}, ensure_ascii=False))
