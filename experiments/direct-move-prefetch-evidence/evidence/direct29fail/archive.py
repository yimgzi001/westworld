#!/usr/bin/env python3
"""阶段29失败归档。只读两场已结束的云端文件；--write仅写本证据目录。
不启动游戏、不运行归档源码、不调用API、不访问网络，不读取凭据或世界存档。
复用food28逐文件SHA256寻址、确定性gzip与Base64分片方法，保存完整冻结源码。
"""
import argparse, base64, collections, gzip, hashlib, json, pathlib, re
HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[3]
RUNS = ('direct29-early', 'direct29-early2')
ALLOW = ('tool-agent.jsonl', 'console.log', 'exit.txt', 'driver-exit.txt', 'driver.log',
         'run.sh', 'init.js', 'stage.json', 'source-hashes.json', 'installed-hashes.json',
         'direct-driver-started.json', 'direct-driver-result.json', 'independent-validation.json')
BUILD = tuple('work/preceding29/' + f for f in (
    'host-build.log', 'host-exit.txt', 'host-build02.log', 'host-exit02.txt',
    'host-manifest.json', 'manifest.json', 'tests.log', 'exit.txt',
    'phase.log', 'phase-final.log', 'phase-exit.txt')) + (
    'work/direct29-stage-tests/tests.log', 'work/direct29-stage-tests/exit.txt')
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
    index = {'schema': 'westworld-direct29-failure-evidence-v1', 'runs': {}, 'bundles': {},
             'sourceBasis': '仅使用每场sources冻结文件及source-hashes.json，不采集当前工作树源码。',
             'scope': '两场人工夹具、确定性文件控制器失败记录；非模型、非自然生存；修复在途不算通过。',
             'sanitization': '日志、运行配置和构建文本中的本云仓库绝对路径替换为<CLOUD_REPO>；源码保持逐字节不变。原始与脱敏SHA256分别保存。检测到凭据特征立即拒绝。',
             'archiveCompleteMeaning': '所列失败记录已封存，不表示场景通过、驱动结果完整或checkpoint完成。',
             'buildEvidence': {}, 'excluded': ['凭据和环境变量', '游戏二进制与内容资产', '世界和存档', '第三方依赖', '后续修复构建及活动测试目录']}
    def bundle(kind, value):
        raw = packed(value)
        if SECRET.search(raw):
            raise ValueError('发现凭据特征：' + kind)
        h = sha(raw)
        key = kind + '-' + h[:20]
        if key not in index['bundles']:
            zipped = gzip.compress(raw, mtime=0)
            encoded = base64.b64encode(zipped)
            parts = []
            for offset in range(0, len(encoded), 29000):
                name = f'{key}.gz.b64.part{offset // 29000 + 1:03}'
                part = encoded[offset:offset + 29000]
                parts.append({'file': name, 'bytes': len(part), 'sha256': sha(part)})
                if args.write:
                    (HERE / name).write_bytes(part)
            assert gzip.decompress(base64.b64decode(encoded)) == raw
            metadata = packed({'rawSHA256': h, 'rawBytes': len(raw), 'gzipSHA256': sha(zipped), 'parts': parts})
            manifest = key + '-manifest.json'
            if args.write:
                (HERE / manifest).write_bytes(metadata)
            index['bundles'][key] = {'manifest': manifest, 'sha256': sha(metadata)}
        return key
    def evidence(path, kind='evidence-file'):
        raw = read(path)
        original = raw.decode()
        safe = original.replace(str(ROOT), '<CLOUD_REPO>')
        return {'bundle': bundle(kind, safe), 'originalSHA256': sha(raw),
                'archivedTextSHA256': sha(safe.encode()), 'pathRedactions': original.count(str(ROOT))}
    build_manifest = json.loads(read(ROOT / 'work/preceding29/host-manifest.json'))
    for name in RUNS:
        directory = ROOT / 'work' / name
        inventory = json.loads(read(directory / 'source-hashes.json'))
        actual = {p.relative_to(directory / 'sources').as_posix(): sha(read(p))
                  for p in sorted((directory / 'sources').rglob('*')) if p.is_file()}
        assert actual == inventory and len(inventory) == 117, '冻结源码清单不匹配'
        source_refs = {}
        for path, expected in sorted(inventory.items()):
            relative = pathlib.PurePosixPath(path)
            assert not relative.is_absolute() and '..' not in relative.parts
            assert relative.suffix in TEXT, '非源码文本：' + path
            raw = read(directory / 'sources' / path)
            assert sha(raw) == expected
            source_refs[path] = bundle('source-file', raw.decode())
        logs = {f: evidence(directory / f) for f in ALLOW if (directory / f).is_file()}
        rows = [json.loads(line) for line in read(directory / 'tool-agent.jsonl').decode().splitlines() if line.strip()]
        counts = dict(collections.Counter(row.get('kind') for row in rows))
        results = [row for row in rows if row.get('kind') == 'result']
        finals = [row for row in rows if row.get('kind') == 'final_audit']
        assert len(results) == len(finals) == 1
        assert results[0]['passed'] is False and results[0]['reason'] == 'handoff_test_budget'
        assert int(read(directory / 'exit.txt')) == 1
        checkpoint_path = ROOT / '.batch-checkpoints' / (name + '.json')
        checkpoint = json.loads(read(checkpoint_path))
        checked = []
        for relative, expected in (checkpoint.get('sources', {}) | checkpoint.get('results', {})).items():
            if expected is not None:
                p = ROOT / relative
                assert not pathlib.PurePosixPath(relative).is_absolute() and '..' not in pathlib.PurePosixPath(relative).parts
                assert sha(p.read_bytes()) == expected, 'checkpoint哈希不匹配：' + relative
                checked.append(relative)
        installed = json.loads(read(directory / 'installed-hashes.json'))
        # 仅计算少量构建产物的哈希，不收录任何二进制内容。
        binary_hash_checks = {}
        for filename in ('ToolAgentGate.dll', 'Survivalcraft.dll', 'Westworld.Runtime.Core.dll', 'Westworld.Scapi.Adapter.dll', 'Westworld.Decisions.Local.dll'):
            observed = sha((directory / filename).read_bytes())
            assert observed == installed[filename]
            binary_hash_checks[filename] = observed
        host_binary_key = 'work/visible-facts-host-isolated/bin/ToolAgentGate/debug/ToolAgentGate.dll'
        assert build_manifest['files'][host_binary_key] == installed['ToolAgentGate.dll']
        matched_source_paths = [path for path, digest in build_manifest['files'].items() if path in inventory and inventory[path] == digest]
        assert len(matched_source_paths) == 7
        for path, digest in build_manifest['files'].items():
            if path in inventory:
                assert inventory[path] == digest
        summary = {'hostExit': 1, 'driverExit': int(read(directory / 'driver-exit.txt')),
                   'hostResult': results[0], 'eventCounts': counts,
                   'prefetchTaken': finals[0]['prefetchTaken'], 'overlapSeconds': finals[0]['overlapSeconds'],
                   'pathMeters': finals[0]['path'], 'modelInFlightAtEnd': finals[0]['modelInFlight'],
                   'nativeTerminals': [row for row in rows if row.get('kind') == 'native_terminal_observed'],
                   'driverResultPresent': 'direct-driver-result.json' in logs,
                   'independentValidationPresent': 'independent-validation.json' in logs,
                   'checkpointStateAsRecorded': checkpoint['state'], 'checkpointFinishedUtcAsRecorded': checkpoint['finishedUtc']}
        if name == 'direct29-early':
            assert not summary['driverResultPresent'] and summary['driverExit'] == 1
            assert counts.get('native_call_started', 0) == 0
            assert 'fixture_number_not_offered' in read(directory / 'driver.log').decode()
            summary['finding'] = '驱动timeout=8超过菜单上限4，schema拒绝；没有动作启动。driver-result缺失，checkpoint仍running/null，原样保留。'
        else:
            assert summary['driverResultPresent'] and summary['driverExit'] == 0
            assert counts['prefetch_setup_failure'] == 220 and summary['prefetchTaken'] == 0
            assert json.loads(read(directory / 'independent-validation.json'))['verdict'] == 'fail'
            summary['finding'] = 'timeout修正为4后原生MoveTo成功，但deferred菜单仅承认旧host导航，220次预取setup失败；预取0、独立验收fail。'
        entry = {'summary': summary, 'logs': logs,
                 'sourceSnapshot': bundle('source-inventory', {'inventory': inventory, 'files': source_refs, 'count': len(inventory)}),
                 'checkpoint': evidence(checkpoint_path), 'verifiedCheckpointHashPaths': checked,
                 'buildCorrespondence': {'hostManifest': 'work/preceding29/host-manifest.json',
                     'matchedFrozenHostSources': matched_source_paths, 'verifiedInstalledBinaryHashes': binary_hash_checks,
                     'limit': '宿主清单7份源码与产物哈希匹配；staging本身并不证明整个游戏二进制由所有冻结源码完整重建。'},
                 'missingEvidence': [f for f in ('direct-driver-result.json', 'independent-validation.json') if f not in logs]}
        data = json.dumps(entry, ensure_ascii=False, indent=2).encode() + b'\n'
        run_manifest = name + '-manifest.json'
        if args.write:
            (HERE / run_manifest).write_bytes(data)
        index['runs'][name] = {'manifest': run_manifest, 'sha256': sha(data), 'verdict': 'fail'}
    for path in BUILD:
        index['buildEvidence'][path] = evidence(ROOT / path, 'test-build-file')
    index['buildEvidenceScope'] = '仅两场失败前已留存的旧构建、纯测试与部署测试记录；旧首次编译2错误及重建0错误均保留。不重跑测试，不混入host-build03及后续修复。'
    index['archiveComplete'] = True
    if args.write:
        (HERE / 'manifest.json').write_bytes(json.dumps(index, ensure_ascii=False, separators=(',', ':')).encode() + b'\n')
    print(json.dumps({'runs': len(index['runs']), 'bundles': len(index['bundles']),
                      'sourceVersions': sum(k.startswith('source-file-') for k in index['bundles']),
                      'buildFiles': len(index['buildEvidence']), 'write': args.write}, ensure_ascii=False))
if __name__ == '__main__':
    main()
