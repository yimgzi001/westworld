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
index['bundles'] = {}
for item in index['bundleIndexes']:
    raw = safe_path(item['file']).read_bytes(); assert sha(raw) == item['sha256']; reachable.add(item['file'])
    part_index = json.loads(raw); assert not (index['bundles'].keys() & part_index.keys())
    index['bundles'].update(part_index)
decoded = {}
parts_checked = 0
for key, ref in index['bundles'].items():
    manifest_name = key + '-manifest.json'
    raw = safe_path(manifest_name).read_bytes()
    assert sha(raw) == ref['sha256']
    reachable.add(manifest_name)
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
    assert ref.get('frozenSource') or '/workspace/scratch/' not in value, '云工作区路径未脱敏'
    if ref['pathRedactions'] == 0:
        assert ref['originalSHA256'] == ref['archivedTextSHA256']
    return value
support = decoded[index['supportEvidence']]
index['buildEvidence'] = support['buildEvidence']
source_checks = checkpoint_checks = binary_checks = artifact_checks = 0
all_refs = {}
matrix_texts = {}
for path, ref in index['matrixEvidence'].items():
    matrix_texts[path] = evidence(ref)
    all_refs['work/direct29-v4-matrix/' + path] = ref
matrix = json.loads(matrix_texts['matrix-result.json'])
assert matrix['status'] == index['matrixVerdict'] == 'passed'
assert matrix['recordedCases'] == matrix['plannedCases'] == 5
for path, digest in json.loads(matrix_texts['bundle-hashes.json']).items():
    assert index['matrixEvidence'][path]['originalSHA256'] == digest
build = json.loads(evidence(index['buildEvidence']['work/preceding29/host-manifest04.json']))
reference_inventory = None
for name, ref in index['runs'].items():
    raw = safe_path(ref['manifest']).read_bytes(); assert sha(raw) == ref['sha256']; reachable.add(ref['manifest'])
    run = json.loads(raw); sources = decoded[run['sourceSnapshot']]
    assert len(sources['inventory']) == len(sources['files']) == sources['count'] == 120
    if reference_inventory is None: reference_inventory = sources['inventory']
    assert reference_inventory == sources['inventory']
    for path, digest in sources['inventory'].items():
        p = pathlib.PurePosixPath(path); assert not p.is_absolute() and '..' not in p.parts
        assert sha(decoded[sources['files'][path]].encode()) == digest
        source_checks += 1
    logs = {path: evidence(value) for path, value in run['logs'].items()}
    all_refs.update({'work/' + name + '/' + path: value for path, value in run['logs'].items()})
    assert json.loads(logs['source-hashes.json']) == sources['inventory']
    validation = json.loads(logs['validation.json']); recorded = json.loads(logs['matrix-case-result.json'])
    assert next(r for r in matrix['cases'] if r['checkpointCase'] == name) == recorded
    assert recorded['validationVerdict'] == validation['verdict'] == ref['verdict']
    assert recorded['hostExit'] == int(logs['exit.txt'])
    assert recorded['driverExit'] == int(logs['driver-exit.txt']) == 0
    assert validation['sourceSha256'] == run['logs']['tool-agent.jsonl']['originalSHA256']
    assert validation['driverSha256'] == run['logs']['direct-driver-result.json']['originalSHA256']
    for path, digest in recorded['artifacts'].items():
        assert run['logs'][path]['originalSHA256'] == digest
        artifact_checks += 1
    rows = [json.loads(line) for line in logs['tool-agent.jsonl'].splitlines() if line.strip()]
    counts = dict(collections.Counter(r.get('kind') for r in rows)); summary = run['summary']
    assert counts == summary['eventCounts']
    assert [r for r in rows if r.get('kind') == 'result'] == [summary['hostResult']]
    final = [r for r in rows if r.get('kind') == 'final_audit']; assert len(final) == 1
    assert final[0]['prefetchTaken'] == summary['prefetchTaken']
    assert final[0]['modelInFlight'] is summary['modelInFlightAtEnd'] is False
    assert validation['metrics'] == summary['metrics']
    assert dict(collections.Counter(c['status'] for c in validation['checks'])) == summary['validationCheckCounts']
    assert [c for c in validation['checks'] if c['status'] != 'pass'] == summary['nonPassChecks']
    case = summary['case']
    assert validation['verdict'] == 'pass' and summary['hostExit'] == 0 and not summary['nonPassChecks']
    assert summary['hostResult']['passed'] is True and summary['hostResult']['updateFailures'] == 0
    assert summary['metrics']['sampledInferenceMotionPairs'] >= 2
    assert summary['prefetchTaken'] == (1 if case in ('direct_early', 'direct_late') else 0)
    checkpoint = json.loads(evidence(run['checkpoint'])); assert checkpoint['finishedUtc']
    for path, digest in (checkpoint['sources'] | checkpoint['results']).items():
        assert all_refs[path]['originalSHA256'] == digest
        checkpoint_checks += 1
    installed = json.loads(logs['installed-hashes.json']); correspondence = run['buildCorrespondence']
    for filename, digest in correspondence['verifiedInstalledBinaryHashes'].items():
        assert installed[filename] == digest
        binary_checks += 1
    assert len(correspondence['matchedFrozenHostSources']) == 8
    for path in correspondence['matchedFrozenHostSources']:
        assert sources['inventory'][path] == build['files'][path]
    assert installed['ToolAgentGate.dll'] == build['files']['work/visible-facts-host-isolated/bin/ToolAgentGate/debug/ToolAgentGate.dll']
host = support['hostRegression']; sources = decoded[host['sourceSnapshot']]
assert sources['inventory'] == reference_inventory and sources['count'] == 120
for path, digest in sources['inventory'].items():
    assert sha(decoded[sources['files'][path]].encode()) == digest
    source_checks += 1
logs = {path: evidence(ref) for path, ref in host['logs'].items()}
assert int(logs['exit.txt']) == int(logs['driver-exit.txt']) == 0
assert json.loads(logs['source-hashes.json']) == sources['inventory']
rows = [json.loads(line) for line in logs['tool-agent.jsonl'].splitlines() if line.strip()]
assert [r for r in rows if r.get('kind') == 'result'] == [host['summary']['hostResult']]
assert host['summary']['hostResult']['passed'] is True and host['summary']['hostResult']['updateFailures'] == 0
final = [r for r in rows if r.get('kind') == 'final_audit'][0]
assert final['prefetchTaken'] == host['summary']['prefetchTaken'] == 1
assert final['path'] == host['summary']['pathMeters']
assert final['modelInFlight'] is host['summary']['modelInFlightAtEnd'] is False
analysis = json.loads(logs['analysis.json'])
assert analysis['sourceSha256'] == host['logs']['tool-agent.jsonl']['originalSHA256']
assert analysis['prefetchTiming'][0]['dispatchAssociation'] == 'temporal_only_unbound'
checkpoint = json.loads(evidence(host['checkpoint'])); assert checkpoint['state'] == 'passed' and checkpoint['finishedUtc']
for path, digest in (checkpoint['sources'] | checkpoint['results']).items():
    assert host['logs'][pathlib.PurePosixPath(path).name]['originalSHA256'] == digest
    checkpoint_checks += 1
installed = json.loads(logs['installed-hashes.json'])
for path, digest in host['verifiedInstalledBinaryHashes'].items():
    assert installed[path] == digest
    binary_checks += 1
assert installed['ToolAgentGate.dll'] == build['files']['work/visible-facts-host-isolated/bin/ToolAgentGate/debug/ToolAgentGate.dll']
for group, summary in support['pureTestSummaries'].items():
    assert summary['suiteCount'] == len(summary['suites']) == 27
    assert summary['assertionCount'] == sum(summary['suites'].values())
    for filename, count in summary['suites'].items():
        value = evidence(index['buildEvidence']['work/' + group + '/' + filename])
        last = value.strip().splitlines()[-1]
        match = re.search(r'(\d+)/(\d+)', last) or re.search(r'TOTAL (\d+)', last)
        assert match and int(match[1]) == count
        if len(match.groups()) == 2: assert match[1] == match[2]
    assert int(evidence(index['buildEvidence']['work/' + group + '-exit.txt'])) == 0
assert support['pureTestSummaries']['direct29-final-contract']['assertionCount'] == 1084
assert support['pureTestSummaries']['direct29-contract-retry']['assertionCount'] == 1070
assert int(evidence(index['buildEvidence']['work/direct29-contract-tests-exit.txt'])) == 1
assert 'project.assets.json' in evidence(index['buildEvidence']['work/direct29-contract-tests/PrecedingExecution.build.log'])
for ref in index['buildEvidence'].values(): evidence(ref)
assert index['archiveComplete'] is True
verdicts = dict(collections.Counter(ref['verdict'] for ref in index['runs'].values()))
assert verdicts == {'pass': 5}
orphans = [p.name for p in HERE.iterdir() if p.is_file() and p.name not in reachable]; assert not orphans
assert all(not p.is_symlink() for p in HERE.iterdir())
print(json.dumps({'directRuns': len(index['runs']), 'hostRegressionRuns': 1, 'bundles': len(decoded), 'parts': parts_checked,
                  'sourceFileVersions': sum(k.startswith('source-file-') for k in decoded),
                  'sourceHashChecks': source_checks, 'checkpointHashChecks': checkpoint_checks,
                  'installedHashLinks': binary_checks, 'matrixArtifactHashLinks': artifact_checks,
                  'buildEvidenceFiles': len(index['buildEvidence']), 'matrixEvidenceFiles': len(index['matrixEvidence']),
                  'archiveComplete': True, 'verdictCounts': verdicts, 'matrixVerdict': index['matrixVerdict'],
                  'reachableBytes': sum((HERE / name).stat().st_size for name in reachable),
                  'largestFileBytes': max((HERE / name).stat().st_size for name in reachable), 'orphanFiles': orphans}, ensure_ascii=False))
