#!/usr/bin/env python3
"""独立离线验证：仅解码、校验哈希与证据边界，不执行归档源码或游戏。"""
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
support = decoded[index['supportEvidence']]; build_refs = support['buildEvidence']
build_result = json.loads(evidence(build_refs['work/direct30-model-build-logs/result.json']))
changed = json.loads(evidence(build_refs['work/direct30-model-tests/changed-sources.json']))
source_checks = checkpoint_checks = binary_checks = 0
run_values = {}
for name, ref in index['runs'].items():
    raw = safe_path(ref['manifest']).read_bytes(); assert sha(raw) == ref['sha256']; reachable.add(ref['manifest'])
    run = json.loads(raw); run_values[name] = run; sources = decoded[run['sourceSnapshot']]
    assert len(sources['inventory']) == len(sources['files']) == sources['count'] == (120 if name == 'food30-terra' else 121)
    for path, digest in sources['inventory'].items():
        relative = pathlib.PurePosixPath(path); assert not relative.is_absolute() and '..' not in relative.parts
        assert sha(decoded[sources['files'][path]].encode()) == digest
        source_checks += 1
    logs = {path: evidence(value) for path, value in run['logs'].items()}
    assert json.loads(logs['source-hashes.json']) == sources['inventory']
    rows = [json.loads(line) for line in logs['tool-agent.jsonl'].splitlines() if line.strip()]
    summary = run['summary']; assert dict(collections.Counter(row.get('kind') for row in rows)) == summary['eventCounts']
    assert [row for row in rows if row.get('kind') == 'result'] == [summary['hostResult']]
    assert [row for row in rows if row.get('kind') == 'food_chain_evidence'] == [summary['foodEvidence']]
    assert summary['hostExit'] == int(logs['exit.txt'])
    final = [row for row in rows if row.get('kind') == 'final_audit'][0]
    assert final['modelInFlight'] is summary['modelInFlightAtEnd'] is False
    assert final['prefetchTaken'] == summary['prefetchTaken'] and final['path'] == summary['pathMeters']
    checkpoint = json.loads(evidence(run['checkpoint'])); assert checkpoint['finishedUtc']
    for path, digest in (checkpoint['sources'] | checkpoint['results']).items():
        assert run['logs'][pathlib.PurePosixPath(path).name]['originalSHA256'] == digest
        checkpoint_checks += 1
    installed = json.loads(logs['installed-hashes.json']); correspondence = run['buildCorrespondence']
    for path, digest in correspondence['verifiedInstalledBinaryHashes'].items():
        assert installed[path] == digest
        binary_checks += 1
    analysis = json.loads(logs['analysis.json']); food = summary['foodEvidence']
    assert summary['hostResult']['updateFailures'] == 0
    assert food['shutdown']['idle'] and food['shutdown']['lateDispatches'] == 0
    assert food['shutdown']['drainedTime'] >= food['shutdown']['stopTime']
    assert food['shutdown']['drainedTime'] - food['shutdown']['stopTime'] <= food['shutdown']['budgetSeconds']
    if name == 'food30-terra':
        assert summary['classification'] == ref['classification'] == analysis['classification'] == 'infrastructure_interrupted_model_outcome_unknown'
        assert summary['apiOutcome'] == summary['usageAndBilling'] == 'unknown'
        assert summary['modelQualityVerdict'] == 'not_evaluated' and analysis['model_quality_verdict'] == 'not evaluated'
        assert logs['openai-audit.jsonl'] == '' and summary['auditBytes'] == analysis['audit_bytes'] == 0
        assert 'food-driver-result.json' not in logs and 'api-driver-exit.txt' not in logs and 'driver-exit.txt' not in logs
        assert summary['hostExit'] == 1 and summary['hostResult']['passed'] is False
        assert summary['hostResult']['reason'] == 'wall_budget_exhausted' and summary['hostResult']['seconds'] >= 180
        assert summary['eventCounts'].get('native_call_started', 0) == summary['eventCounts'].get('controller_response_received', 0) == 0
        assert summary['eventCounts']['controller_request_published'] == 4
        assert not food['dispatches'] and food['acquisition'] is None and food['eat'] is None
        assert correspondence['buildLabel'] == 'build04'
    else:
        assert summary['classification'] == ref['classification'] == 'deterministic_native_food_pass'
        assert summary['modelQualityVerdict'] == 'not_applicable' and summary['apiOutcome'] == 'not_a_model_run'
        assert summary['hostExit'] == 0 and summary['hostResult']['passed'] is True
        assert analysis['evidence'] == food and analysis['host'] == summary['hostResult']
        assert analysis['validation']['verdict'] == 'pass' and not analysis['validation']['failures'] and not analysis['validation']['missing']
        assert json.loads(logs['food-driver-result.json'])['kind'] == 'deterministic_fixture_not_model'
        assert 'driver-exit.txt' not in logs
        assert food['inventoryInjection'] is False and food['uniqueSourceThroughout'] is True
        assert food['value'] != food['initialFishValue'] and food['baselineCount'] == 0 and food['sourceCount'] == 1
        acquisition = food['acquisition']; eat = food['eat']; ledger = food['sourceLedger']
        assert acquisition['sourceId'] == food['sourceId'] and acquisition['value'] == eat['value'] == eat['eventValue'] == food['value']
        assert acquisition['method'] == 'native_automatic_collection' and acquisition['nativeEventBound'] is True and acquisition['pickupReceipt'] is None
        assert (acquisition['worldBefore'], acquisition['worldAfter'], acquisition['inventoryBefore'], acquisition['inventoryAfter']) == (1, 0, 0, 1)
        assert (eat['inventoryBefore'], eat['inventoryAfter'], eat['statsBefore'], eat['statsAfter']) == (1, 0, 0, 1)
        assert eat['foodAfter'] > eat['foodBefore'] and eat['nativeStatus'] == 'succeeded'
        dispatch = [item for item in food['dispatches'] if item['tool'] == 'act_Eat']
        assert len(dispatch) == 1 and dispatch[0]['requestId'] == eat['requestId'] and dispatch[0]['sequence'] == eat['sequence']
        assert acquisition['time'] < dispatch[0]['time'] <= eat['time'] <= food['shutdown']['stopTime']
        assert not ledger['Unknown'] and ledger['AcquiredCount'] == ledger['ConsumedCount'] == 1 and ledger['ExpectedInventory'] == ledger['SourceRemaining'] == 0
        assert correspondence['buildLabel'] == 'build05' and installed['ToolAgentGate.dll'] == build_result['sha256']
        assert build_result['buildExit'] == 0 and len(correspondence['matchedFrozenChangedSourcePaths']) == 5
        for path in correspondence['matchedFrozenChangedSourcePaths']: assert sources['inventory'][path] == changed[path]
for ref in build_refs.values(): evidence(ref)
pure = support['pureTests']; assert pure['suiteCount'] == len(pure['suiteCounts']) == 28
assert pure['assertionCount'] == sum(pure['suiteCounts'].values()) == 1094
for filename, count in pure['suiteCounts'].items():
    last = evidence(build_refs['work/direct30-model-tests/contracts/' + filename]).strip().splitlines()[-1]
    match = re.search(r'(\d+)/(\d+)', last) or re.search(r'TOTAL (\d+)', last) or re.search(r'PASS (\d+) endpoint', last)
    assert match and int(match[1]) == count
    if len(match.groups()) == 2: assert match[1] == match[2]
assert pure['endpointAlreadyIncludedIn1094'] is True and pure['endpointChecks'] == 10 and pure['stageTests'] == 13
assert 'Ran 13 tests' in evidence(build_refs['work/direct30-model-tests/stage.log'])
assert int(evidence(build_refs['work/direct30-model-build-logs/exit.txt'])) == 0
assert index['archiveComplete'] is True
# 额外验证包的传递引用，不能仅在包注册表里列出便算被使用。
used = set()
def visit(value):
    if isinstance(value, dict):
        for item in value.values(): visit(item)
    elif isinstance(value, list):
        for item in value: visit(item)
    elif isinstance(value, str) and value in decoded and value not in used:
        used.add(value); visit(decoded[value])
visit({key: value for key, value in index.items() if key not in ('bundles', 'bundleIndexes')})
for run in run_values.values(): visit(run)
assert used == decoded.keys(), '存在未被实际证据引用的包'
orphans = [p.name for p in HERE.iterdir() if p.is_file() and p.name not in reachable]; assert not orphans
assert all(not p.is_symlink() for p in HERE.iterdir())
print(json.dumps({'runs': 2, 'bundles': len(decoded), 'parts': parts_checked,
    'sourceFileVersions': sum(key.startswith('source-file-') for key in decoded),
    'sourceHashChecks': source_checks, 'checkpointHashChecks': checkpoint_checks, 'installedHashLinks': binary_checks,
    'buildEvidenceFiles': len(build_refs), 'pureSuites': 28, 'pureAssertions': 1094, 'stageTests': 13,
    'modelOutcome': 'unknown', 'modelUsageAndBilling': 'unknown', 'deterministicNativeFood': 'pass',
    'archiveComplete': True, 'transitivelyReferencedBundles': len(used), 'orphanFiles': orphans,
    'files': len(reachable), 'reachableBytes': sum((HERE / name).stat().st_size for name in reachable),
    'largestFileBytes': max((HERE / name).stat().st_size for name in reachable)}, ensure_ascii=False))
