#!/usr/bin/env python3
"""阶段30食物用例归档。只读两场已结束的云端文件；--write仅写本证据目录。
不启动游戏、不运行归档源码、不调用API、不访问网络，不读取凭据或世界存档。
复用food28逐文件SHA256寻址、确定性gzip与Base64分片方法，保存完整冻结源码。
"""
import argparse, base64, collections, gzip, hashlib, json, pathlib, re
HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[3]
RUNS = ('food30-terra', 'food30-deterministic')
ALLOW = ('tool-agent.jsonl', 'console.log', 'exit.txt', 'run.sh', 'init.js', 'stage.json',
         'source-hashes.json', 'installed-hashes.json', 'analysis.json', 'deterministic_driver.py',
         'food-driver-started.json', 'food-driver-result.json', 'driver-exit.txt', 'driver.log', 'api-driver-exit.txt')
BUILD = tuple(path.relative_to(ROOT).as_posix()
    for folder in ('direct30-model-tests', 'direct30-model-build-logs')
    for path in sorted((ROOT / 'work' / folder).rglob('*'))
    if path.is_file() and path.suffix in ('.log', '.txt', '.json'))
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
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--write', action='store_true')
    args = parser.parse_args()
    index = {'schema': 'westworld-food30-evidence-v1', 'runs': {}, 'bundles': {}, 'buildEvidence': {},
             'scope': '模型候选用例基础设施中断、API与计费未知；确定性人工来源原生自动拾取和食用通过。非自然生存，不评价模型质量。',
             'sourceBasis': '仅每场sources与source-hashes冻结文件，不读取当前生产工作树或正在修订的驱动审计代码。',
             'sanitization': '日志/构建/配置中的云仓库绝对路径替换为<CLOUD_REPO>，原始与归档SHA256分别保存；冻结源码原样保存。凭据特征命中即拒绝。',
             'excluded': ['凭据和环境变量', '私密文件和游戏存档', '游戏二进制与内容资产', '第三方依赖', '非空原始API账本', '未完成修改'],
             'archiveCompleteMeaning': '封存完整不等于模型用例通过或已知API消耗。缺失的驱动结果/退出文件保持缺失。'}
    def bundle(kind, value):
        raw = packed(value); assert not SECRET.search(raw), kind
        key = kind + '-' + sha(raw)[:20]
        if key not in index['bundles']:
            zipped = gzip.compress(raw, mtime=0); encoded = base64.b64encode(zipped); parts = []
            for offset in range(0, len(encoded), 29000):
                filename = f'{key}.gz.b64.part{offset // 29000 + 1:03}'; part = encoded[offset:offset+29000]
                parts.append({'file': filename, 'bytes': len(part), 'sha256': sha(part)})
                if args.write: (HERE / filename).write_bytes(part)
            assert gzip.decompress(base64.b64decode(encoded)) == raw
            meta = packed({'rawSHA256': sha(raw), 'rawBytes': len(raw), 'gzipSHA256': sha(zipped), 'parts': parts})
            if args.write: (HERE / (key + '-manifest.json')).write_bytes(meta)
            index['bundles'][key] = {'sha256': sha(meta)}
        return key
    def evidence(path, kind='evidence-file'):
        raw = read(path); text = raw.decode(); safe = text.replace(str(ROOT), '<CLOUD_REPO>')
        return {'bundle': bundle(kind, safe), 'originalSHA256': sha(raw),
                'archivedTextSHA256': sha(safe.encode()), 'pathRedactions': text.count(str(ROOT))}
    build_result = json.loads(read(ROOT / 'work/direct30-model-build-logs/result.json'))
    changed = json.loads(read(ROOT / 'work/direct30-model-tests/changed-sources.json'))
    for name in RUNS:
        d = ROOT / 'work' / name; inventory = json.loads(read(d / 'source-hashes.json'))
        actual = {p.relative_to(d / 'sources').as_posix(): sha(read(p)) for p in sorted((d / 'sources').rglob('*')) if p.is_file()}
        expected_count = 120 if name == 'food30-terra' else 121
        assert inventory == actual and len(inventory) == expected_count
        sources = {}
        for path, digest in sorted(inventory.items()):
            rel = pathlib.PurePosixPath(path); assert not rel.is_absolute() and '..' not in rel.parts and rel.suffix in TEXT
            raw = read(d / 'sources' / path); assert sha(raw) == digest
            sources[path] = bundle('source-file', raw.decode())
        logs = {path: evidence(d / path) for path in ALLOW if (d / path).is_file()}
        rows = [json.loads(line) for line in read(d / 'tool-agent.jsonl').decode().splitlines() if line.strip()]
        counts = dict(collections.Counter(row.get('kind') for row in rows))
        results = [row for row in rows if row.get('kind') == 'result']
        food = [row for row in rows if row.get('kind') == 'food_chain_evidence']
        final = [row for row in rows if row.get('kind') == 'final_audit']
        assert len(results) == len(food) == len(final) == 1
        assert results[0]['updateFailures'] == 0 and final[0]['modelInFlight'] is False
        checkpoint_path = ROOT / '.batch-checkpoints' / (name + '.json')
        checkpoint = json.loads(read(checkpoint_path)); assert checkpoint['finishedUtc']
        checkpoint_links = []
        for path, digest in (checkpoint['sources'] | checkpoint['results']).items():
            rel = pathlib.PurePosixPath(path); assert not rel.is_absolute() and '..' not in rel.parts and digest
            assert sha(read(ROOT / path)) == digest
            checkpoint_links.append(path)
        installed = json.loads(read(d / 'installed-hashes.json')); binaries = {}
        for path in ('ToolAgentGate.dll', 'Survivalcraft.dll', 'Westworld.Runtime.Core.dll', 'Westworld.Scapi.Adapter.dll', 'Westworld.Decisions.Local.dll'):
            digest = sha((d / path).read_bytes()); assert installed[path] == digest; binaries[path] = digest
        analysis = json.loads(read(d / 'analysis.json'))
        summary = {'hostResult': results[0], 'hostExit': int(read(d / 'exit.txt')), 'eventCounts': counts,
                   'foodEvidence': food[0], 'prefetchTaken': final[0]['prefetchTaken'],
                   'pathMeters': final[0]['path'], 'modelInFlightAtEnd': final[0]['modelInFlight']}
        if name == 'food30-terra':
            # 只接受已知为空的审计文件；不采集非空原始API账本。
            audit = read(d / 'openai-audit.jsonl'); assert audit == b''
            logs['openai-audit.jsonl'] = evidence(d / 'openai-audit.jsonl')
            assert analysis['classification'] == 'infrastructure_interrupted_model_outcome_unknown'
            assert analysis['model_quality_verdict'] == 'not evaluated' and analysis['audit_bytes'] == 0
            assert analysis['host_result'] == results[0] and analysis['responses'] == 0
            assert summary['hostExit'] == 1 and results[0]['passed'] is False and results[0]['reason'] == 'wall_budget_exhausted'
            assert counts.get('native_call_started', 0) == counts.get('controller_response_received', 0) == 0
            assert counts['controller_request_published'] == 4 and not food[0]['dispatches']
            assert 'food-driver-result.json' not in logs and 'api-driver-exit.txt' not in logs
            summary.update({'classification': analysis['classification'], 'modelQualityVerdict': 'not_evaluated',
                            'apiOutcome': 'unknown', 'usageAndBilling': 'unknown', 'auditBytes': 0,
                            'finding': '驱动会话已丢失，宿主有界超时；0字节audit不是零API请求或零计费的证明。'})
            correspondence = {'basis': 'stage.json明确使用阶段29已冻结build04；不把本场归入新build05。',
                              'verifiedInstalledBinaryHashes': binaries, 'buildLabel': 'build04'}
        else:
            assert analysis['host'] == results[0] and analysis['evidence'] == food[0]
            assert analysis['validation']['verdict'] == 'pass' and not analysis['validation']['failures'] and not analysis['validation']['missing']
            assert summary['hostExit'] == 0 and results[0]['passed'] is True
            assert results[0]['reason'] == 'new_world_food_and_native_eating_verified'
            assert food[0]['inventoryInjection'] is False and food[0]['uniqueSourceThroughout'] is True
            assert food[0]['acquisition']['method'] == 'native_automatic_collection'
            assert binaries['ToolAgentGate.dll'] == build_result['sha256'] and build_result['buildExit'] == 0
            matches = [path for path, digest in changed.items() if path in inventory and inventory[path] == digest]
            for path, digest in changed.items():
                if path in inventory: assert inventory[path] == digest
            assert len(matches) == 5
            summary.update({'classification': 'deterministic_native_food_pass', 'modelQualityVerdict': 'not_applicable',
                            'apiOutcome': 'not_a_model_run', 'finding': '人工来源、确定性控制；原生自动拾取后食用，不是模型主动PickUp。'})
            correspondence = {'buildLabel': 'build05', 'buildResult': 'work/direct30-model-build-logs/result.json',
                              'verifiedInstalledBinaryHashes': binaries, 'matchedFrozenChangedSourcePaths': matches,
                              'unmatchedChangeInventoryPaths': [path for path in changed if path not in inventory],
                              'limit': '5份已冻结变更源码及宿主DLL对应；不声称整个游戏或全部测试源码从归档完整重建。'}
        entry = {'summary': summary, 'logs': logs,
                 'sourceSnapshot': bundle('source-inventory', {'inventory': inventory, 'files': sources, 'count': len(inventory)}),
                 'checkpoint': evidence(checkpoint_path), 'verifiedCheckpointHashPaths': checkpoint_links,
                 'buildCorrespondence': correspondence,
                 'missingEvidence': [path for path in ('food-driver-started.json','food-driver-result.json','driver-exit.txt','api-driver-exit.txt') if path not in logs]}
        raw = json.dumps(entry, ensure_ascii=False, indent=2).encode() + b'\n'; filename = name + '-manifest.json'
        if args.write: (HERE / filename).write_bytes(raw)
        index['runs'][name] = {'manifest': filename, 'sha256': sha(raw), 'classification': summary['classification']}
    for path in BUILD: index['buildEvidence'][path] = evidence(ROOT / path, 'test-build-file')
    suite_counts = {}
    for path in sorted((ROOT / 'work/direct30-model-tests/contracts').glob('*.test.log')):
        last = read(path).decode().strip().splitlines()[-1]
        match = re.search(r'(\d+)/(\d+)', last) or re.search(r'TOTAL (\d+)', last) or re.search(r'PASS (\d+) endpoint', last)
        assert match
        if len(match.groups()) == 2: assert match[1] == match[2]
        suite_counts[path.name] = int(match[1])
    assert len(suite_counts) == 28 and sum(suite_counts.values()) == 1094
    assert build_result['stageTests'] == 13 and build_result['endpointChecks'] == 10
    index['supportEvidence'] = bundle('support-index', {'buildEvidence': index.pop('buildEvidence'),
        'pureTests': {'suiteCounts': suite_counts, 'suiteCount': 28, 'assertionCount': 1094, 'stageTests': 13,
                      'endpointChecks': 10, 'endpointAlreadyIncludedIn1094': True},
        'scope': '既有build05构建与纯测文本，不重跑、不将其当作模型或自然生存结果。'})
    index['archiveComplete'] = True
    refs = index.pop('bundles'); items = list(refs.items()); index['bundleIndexes'] = []
    for offset in range(0, len(items), 120):
        filename = f'bundle-index-{offset // 120 + 1:03}.json'; raw = packed(dict(items[offset:offset+120]))
        index['bundleIndexes'].append({'file': filename, 'sha256': sha(raw)})
        if args.write: (HERE / filename).write_bytes(raw)
    if args.write: (HERE / 'manifest.json').write_bytes(packed(index) + b'\n')
    print(json.dumps({'runs': len(index['runs']), 'bundles': len(refs),
                      'sourceVersions': sum(key.startswith('source-file-') for key in refs),
                      'buildEvidenceFiles': len(BUILD), 'pureSuites': 28, 'pureAssertions': 1094, 'write': args.write}, ensure_ascii=False))
if __name__ == '__main__':
    main()
