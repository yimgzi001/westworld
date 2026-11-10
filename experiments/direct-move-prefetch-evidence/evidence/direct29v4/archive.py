#!/usr/bin/env python3
"""阶段29 v4矩阵归档。只读五场已结束的云端文件；--write仅写本证据目录。
不启动游戏、不运行归档源码、不调用API、不访问网络，不读取凭据或世界存档。
复用food28逐文件SHA256寻址、确定性gzip与Base64分片方法，保存完整冻结源码。
"""
import argparse, base64, collections, gzip, hashlib, json, pathlib, re
HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[3]
CASES = ('early', 'late', 'failure', 'cancel', 'replace')
EXPECTED = {case: 'pass' for case in CASES}
RUNS = tuple('direct29-v4-direct_' + case for case in CASES)
ALLOW = ('tool-agent.jsonl', 'console.log', 'exit.txt', 'driver-exit.txt', 'driver.log',
         'run.sh', 'init.js', 'stage.json', 'source-hashes.json', 'installed-hashes.json',
         'direct-driver-started.json', 'direct-driver-result.json', 'validation.json',
         'validation-process.log', 'matrix-case-started.json', 'matrix-case-result.json', 'matrix-launch.log')
BUILD = tuple('work/preceding29/' + f for f in (
    'host-build04.log', 'host-exit04.txt', 'host-manifest04.json',
    'terminal-evidence.log', 'terminal-evidence-exit.txt')) + tuple(
    'work/' + name + suffix for name in ('direct29-contract-tests', 'direct29-contract-retry', 'direct29-final-contract')
    for suffix in ('-summary.log', '-exit.txt')) + tuple(
    path.relative_to(ROOT).as_posix() for folder in ('direct29-contract-tests', 'direct29-contract-retry', 'direct29-final-contract')
    for path in sorted((ROOT / 'work' / folder).iterdir()) if path.is_file() and path.suffix in ('.log', '.json', '.txt'))
MATRIX_FILES = ('preparation.json', 'matrix.json', 'matrix-started.json', 'matrix-result.json',
                'bundle-hashes.json', 'run-matrix.sh', 'frozen/matrix_runner.py', 'frozen/recovery.py')
TEXT = {'.cs', '.csproj', '.props', '.targets', '.json', '.xml', '.md', '.py',
        '.sh', '.yml', '.yaml', '.txt', '.sln', '.slnx', '.config', '.resx', '.js', '.ts'}
SECRET = re.compile(rb'(?:sk-[A-Za-z0-9_.-]{16,}|Bearer\s+[A-Za-z0-9_.-]{16,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|(?:api[_-]?key|access[_-]?token|password)\s*[:=]\s*[\x22\x27][A-Za-z0-9_./+\-=]{20,}[\x22\x27])', re.I)
def sha(raw):
    return hashlib.sha256(raw).hexdigest()
def packed(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode()
def read(path):
    if path.is_symlink() or any(p.is_symlink() for p in path.parents):
        raise ValueError('拒绝符号链接：' + str(path))
    raw = path.read_bytes()
    if SECRET.search(raw):
        raise ValueError('发现凭据特征，停止归档：' + str(path))
    raw.decode('utf-8')
    return raw

def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--write', action='store_true')
    args = ap.parse_args()
    index = {'schema': 'westworld-direct29-v4-evidence-v1', 'runs': {}, 'bundles': {},
             'sourceBasis': '只读取五场各自sources冻结源码及source-hashes.json，不使用当前工作树。',
             'scope': '人工原生MoveTo夹具、确定性文件控制器；非真实模型、非自然生存。5场独立pass；另保留旧host导航回归及首次失败/中间/最终纯测记录。',
             'sanitization': '日志和配置中的云仓库绝对路径替换为<CLOUD_REPO>；冻结源码保持原样。分别保存原始与归档文本SHA256。凭据特征命中即拒绝。',
             'buildEvidence': {}, 'matrixEvidence': {}, 'matrixVerdict': 'passed',
             'archiveCompleteMeaning': '封存完成不等于全批验收通过；旧失败与v3仍保留在独立归档，后续新运行不纳入此包。',
             'excluded': ['凭据和环境变量', '游戏二进制和资产', '世界存档', '第三方依赖', '后续未归档批次']}
    def bundle(kind, value):
        raw = packed(value)
        assert not SECRET.search(raw), '凭据特征：' + kind
        h = sha(raw); key = kind + '-' + h[:20]
        if key not in index['bundles']:
            zipped = gzip.compress(raw, mtime=0); encoded = base64.b64encode(zipped); parts = []
            for offset in range(0, len(encoded), 29000):
                filename = f'{key}.gz.b64.part{offset // 29000 + 1:03}'
                part = encoded[offset:offset+29000]
                parts.append({'file': filename, 'bytes': len(part), 'sha256': sha(part)})
                if args.write: (HERE / filename).write_bytes(part)
            assert gzip.decompress(base64.b64decode(encoded)) == raw
            metadata = packed({'rawSHA256': h, 'rawBytes': len(raw), 'gzipSHA256': sha(zipped), 'parts': parts})
            if args.write: (HERE / (key + '-manifest.json')).write_bytes(metadata)
            # manifest名称由内容寻址键推导，避免重复路径撑大索引。
            index['bundles'][key] = {'sha256': sha(metadata)}
        return key
    def evidence(path, kind='evidence-file', frozen=False):
        raw = read(path); original = raw.decode()
        safe = original if frozen else original.replace(str(ROOT), '<CLOUD_REPO>')
        return {'bundle': bundle(kind, safe), 'originalSHA256': sha(raw),
                'archivedTextSHA256': sha(safe.encode()), 'pathRedactions': 0 if frozen else original.count(str(ROOT)),
                'frozenSource': frozen}
    matrix_dir = ROOT / 'work/direct29-v4-matrix'
    matrix = json.loads(read(matrix_dir / 'matrix-result.json'))
    assert matrix['status'] == 'passed' and matrix['recordedCases'] == matrix['plannedCases'] == 5
    bundle_hashes = json.loads(read(matrix_dir / 'bundle-hashes.json'))
    for path, digest in bundle_hashes.items():
        assert sha(read(matrix_dir / path)) == digest
    for path in MATRIX_FILES:
        frozen = path.startswith('frozen/')
        index['matrixEvidence'][path] = evidence(matrix_dir / path, 'source-file' if frozen else 'matrix-file', frozen)
    build = json.loads(read(ROOT / 'work/preceding29/host-manifest04.json'))
    reference_inventory = None
    for name in RUNS:
        directory = ROOT / 'work' / name
        case = name.removeprefix('direct29-v4-direct_')
        inventory = json.loads(read(directory / 'source-hashes.json'))
        actual = {p.relative_to(directory / 'sources').as_posix(): sha(read(p))
                  for p in sorted((directory / 'sources').rglob('*')) if p.is_file()}
        assert actual == inventory and len(inventory) == 120
        if reference_inventory is None: reference_inventory = inventory
        assert inventory == reference_inventory, '五场源码快照不一致'
        source_refs = {}
        for path, digest in sorted(inventory.items()):
            relative = pathlib.PurePosixPath(path)
            assert not relative.is_absolute() and '..' not in relative.parts and relative.suffix in TEXT
            raw = read(directory / 'sources' / path); assert sha(raw) == digest
            source_refs[path] = bundle('source-file', raw.decode())
        logs = {path: evidence(directory / path) for path in ALLOW}
        rows = [json.loads(line) for line in read(directory / 'tool-agent.jsonl').decode().splitlines() if line.strip()]
        counts = dict(collections.Counter(row.get('kind') for row in rows))
        result = [r for r in rows if r.get('kind') == 'result']; final = [r for r in rows if r.get('kind') == 'final_audit']
        assert len(result) == len(final) == 1
        validation = json.loads(read(directory / 'validation.json'))
        recorded = json.loads(read(directory / 'matrix-case-result.json'))
        assert validation['verdict'] == recorded['validationVerdict'] == EXPECTED[case]
        assert recorded == next(r for r in matrix['cases'] if r['case'] == 'direct_' + case)
        for filename, digest in recorded['artifacts'].items():
            assert sha(read(directory / filename)) == digest
        assert validation['sourceSha256'] == logs['tool-agent.jsonl']['originalSHA256']
        assert validation['driverSha256'] == logs['direct-driver-result.json']['originalSHA256']
        assert recorded['hostExit'] == int(read(directory / 'exit.txt'))
        assert recorded['driverExit'] == int(read(directory / 'driver-exit.txt')) == 0
        checkpoint_path = ROOT / '.batch-checkpoints' / (name + '.json')
        checkpoint = json.loads(read(checkpoint_path)); assert checkpoint['finishedUtc'] is not None
        checked = []
        for relative, digest in (checkpoint['sources'] | checkpoint['results']).items():
            path = pathlib.PurePosixPath(relative)
            assert not path.is_absolute() and '..' not in path.parts and digest is not None
            assert sha(read(ROOT / relative)) == digest
            checked.append(relative)
        installed = json.loads(read(directory / 'installed-hashes.json'))
        binary_hashes = {}
        for filename in ('ToolAgentGate.dll', 'Survivalcraft.dll', 'Westworld.Runtime.Core.dll', 'Westworld.Scapi.Adapter.dll', 'Westworld.Decisions.Local.dll'):
            observed = sha((directory / filename).read_bytes())
            assert observed == installed[filename]
            binary_hashes[filename] = observed
        assert build['files']['work/visible-facts-host-isolated/bin/ToolAgentGate/debug/ToolAgentGate.dll'] == installed['ToolAgentGate.dll']
        matched = [path for path, digest in build['files'].items() if path in inventory and inventory[path] == digest]
        assert len(matched) == 8
        for path, digest in build['files'].items():
            if path in inventory: assert inventory[path] == digest
        summary = {'case': 'direct_' + case, 'validationVerdict': validation['verdict'],
                   'validationCheckCounts': dict(collections.Counter(c['status'] for c in validation['checks'])),
                   'nonPassChecks': [c for c in validation['checks'] if c['status'] != 'pass'],
                   'metrics': validation['metrics'], 'hostResult': result[0],
                   'hostExit': recorded['hostExit'], 'driverExit': recorded['driverExit'], 'eventCounts': counts,
                   'prefetchTaken': final[0]['prefetchTaken'], 'modelInFlightAtEnd': final[0]['modelInFlight'],
                   'hostReportedOverlapSeconds': final[0]['overlapSeconds'], 'hostReportedOverlapPath': final[0]['overlapPath']}
        entry = {'summary': summary, 'logs': logs,
                 'sourceSnapshot': bundle('source-inventory', {'inventory': inventory, 'files': source_refs, 'count': len(inventory)}),
                 'checkpoint': evidence(checkpoint_path), 'verifiedCheckpointHashPaths': checked,
                 'buildCorrespondence': {'hostManifest': 'work/preceding29/host-manifest04.json',
                     'matchedFrozenHostSources': matched, 'verifiedInstalledBinaryHashes': binary_hashes,
                     'limit': '8份宿主源码与宿主产物匹配；不声称整个游戏按全部冻结源码重新构建。'}}
        raw = json.dumps(entry, ensure_ascii=False, indent=2).encode() + b'\n'
        filename = name + '-manifest.json'
        if args.write: (HERE / filename).write_bytes(raw)
        index['runs'][name] = {'manifest': filename, 'sha256': sha(raw), 'verdict': validation['verdict']}
    for path in BUILD: index['buildEvidence'][path] = evidence(ROOT / path, 'test-build-file')
    index['buildEvidenceScope'] = 'build04与27套回归的首次缺assets失败、中间1070项和最终1084项原始日志全部保留；本次归档不重跑。'
    # 旧host导航回归独立记录，不冒充直接MoveTo五场之一。
    host_dir = ROOT / 'work/direct29-host-regression'
    host_allow = ('tool-agent.jsonl', 'console.log', 'exit.txt', 'driver-exit.txt', 'driver.log',
                  'run.sh', 'init.js', 'stage.json', 'source-hashes.json', 'installed-hashes.json',
                  'continuity-driver-started.json', 'continuity-driver-result.json', 'analysis.json')
    host_logs = {path: evidence(host_dir / path) for path in host_allow}
    host_inventory = json.loads(read(host_dir / 'source-hashes.json'))
    host_actual = {path.relative_to(host_dir / 'sources').as_posix(): sha(read(path))
                   for path in sorted((host_dir / 'sources').rglob('*')) if path.is_file()}
    assert host_inventory == host_actual == reference_inventory
    host_source_refs = {path: bundle('source-file', read(host_dir / 'sources' / path).decode()) for path in host_inventory}
    host_rows = [json.loads(line) for line in read(host_dir / 'tool-agent.jsonl').decode().splitlines() if line.strip()]
    host_result = [row for row in host_rows if row.get('kind') == 'result']
    host_final = [row for row in host_rows if row.get('kind') == 'final_audit']
    assert len(host_result) == len(host_final) == 1 and host_result[0]['passed'] is True
    assert host_result[0]['updateFailures'] == 0 and int(read(host_dir / 'exit.txt')) == int(read(host_dir / 'driver-exit.txt')) == 0
    host_analysis = json.loads(read(host_dir / 'analysis.json'))
    assert host_analysis['sourceSha256'] == host_logs['tool-agent.jsonl']['originalSHA256']
    host_checkpoint_path = ROOT / '.batch-checkpoints/direct29-host-regression.json'
    host_checkpoint = json.loads(read(host_checkpoint_path))
    assert host_checkpoint['state'] == 'passed' and host_checkpoint['finishedUtc']
    for relative, digest in (host_checkpoint['sources'] | host_checkpoint['results']).items():
        assert digest and sha(read(ROOT / relative)) == digest
    host_installed = json.loads(read(host_dir / 'installed-hashes.json')); host_binary_hashes = {}
    for filename in ('ToolAgentGate.dll', 'Survivalcraft.dll', 'Westworld.Runtime.Core.dll', 'Westworld.Scapi.Adapter.dll', 'Westworld.Decisions.Local.dll'):
        digest = sha((host_dir / filename).read_bytes()); assert digest == host_installed[filename]
        host_binary_hashes[filename] = digest
    assert host_binary_hashes['ToolAgentGate.dll'] == build['files']['work/visible-facts-host-isolated/bin/ToolAgentGate/debug/ToolAgentGate.dll']
    host = {'logs': host_logs, 'checkpoint': evidence(host_checkpoint_path),
            'sourceSnapshot': bundle('source-inventory', {'inventory': host_inventory, 'files': host_source_refs, 'count': len(host_inventory)}),
            'verifiedInstalledBinaryHashes': host_binary_hashes,
            'summary': {'hostResult': host_result[0], 'prefetchTaken': host_final[0]['prefetchTaken'],
                        'pathMeters': host_final[0]['path'], 'modelInFlightAtEnd': host_final[0]['modelInFlight'],
                        'scope': '旧host导航回归；analysis接续关联为temporal_only_unbound，不等同直接MoveTo独立身份验收或无停顿证明。'}}
    pure = {}
    for group, expected_total in (('direct29-contract-retry', 1070), ('direct29-final-contract', 1084)):
        suite_counts = {}
        for path in sorted((ROOT / 'work' / group).glob('*.test.log')):
            last = read(path).decode().strip().splitlines()[-1]
            match = re.search(r'(\d+)/(\d+)', last) or re.search(r'TOTAL (\d+)', last)
            assert match, '未识别纯测计数：' + str(path)
            if len(match.groups()) == 2: assert match[1] == match[2]
            suite_counts[path.name] = int(match[1])
        assert len(suite_counts) == 27 and sum(suite_counts.values()) == expected_total
        assert int(read(ROOT / 'work' / (group + '-exit.txt'))) == 0
        pure[group] = {'suites': suite_counts, 'suiteCount': 27, 'assertionCount': expected_total}
    assert int(read(ROOT / 'work/direct29-contract-tests-exit.txt')) == 1
    assert 'project.assets.json' in read(ROOT / 'work/direct29-contract-tests/PrecedingExecution.build.log').decode()
    index['supportEvidence'] = bundle('support-index', {'buildEvidence': index.pop('buildEvidence'),
        'hostRegression': host, 'pureTestSummaries': pure,
        'firstAggregateAttempt': '缺少project.assets.json失败，exit1；日志保留。'})
    index['archiveComplete'] = True
    # 分开存储包索引，所有普通文件保持有界大小。
    bundle_refs = index.pop('bundles'); items = list(bundle_refs.items()); index['bundleIndexes'] = []
    for offset in range(0, len(items), 120):
        filename = f'bundle-index-{offset // 120 + 1:03}.json'
        raw = packed(dict(items[offset:offset+120]))
        index['bundleIndexes'].append({'file': filename, 'sha256': sha(raw)})
        if args.write: (HERE / filename).write_bytes(raw)
    if args.write: (HERE / 'manifest.json').write_bytes(packed(index) + b'\n')
    print(json.dumps({'runs': len(index['runs']), 'bundles': len(bundle_refs),
                      'sourceVersions': sum(k.startswith('source-file-') for k in bundle_refs),
                      'buildFiles': len(BUILD), 'matrixFiles': len(index['matrixEvidence']),
                      'verdictCounts': dict(collections.Counter(r['verdict'] for r in index['runs'].values())),
                      'hostRegression': 'passed', 'pureTests': [1070, 1084], 'write': args.write}, ensure_ascii=False))
if __name__ == '__main__':
    main()
