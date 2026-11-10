#!/usr/bin/env python3
"""仅离线检查阶段37明文清单、构建/源码对应和原始纯测计数；不运行游戏或API。"""
import argparse,ast,json,math,re,types,uuid
from archive import HERE,ROOT,SECRET,sha

def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--check-sources',action='store_true');a=ap.parse_args();m=json.loads((HERE/'manifest.json').read_text());assert m['complete'];allowed={'archive.py','verify.py','manifest.json','README.md'};contents={}
 for name,v in m['files'].items():
  p=HERE/name;assert p.is_file() and not p.is_symlink();b=p.read_bytes();assert sha(b)==v['archivedSHA256'] and len(b)==v['bytes'];t=b.decode();assert not SECRET.search(t);assert str(ROOT) not in t and not re.search(r'/(?:home|Users)/[^/\s]+/|/usr/share/fonts/',t)
  if v['pathRedactions']==0 and not v.get('transform'):assert v['originalSHA256']==v['archivedSHA256']
  allowed.add(name);contents[name]=t
 actual={p.relative_to(HERE).as_posix() for p in HERE.rglob('*') if p.is_file() and '__pycache__' not in p.parts};assert actual==allowed
 for label,layout in m['consoleReconstruction'].items():
  streams={k:iter(contents[layout[f]].splitlines(keepends=True)) for k,f in [('unique','uniqueFile'),('jsonl','jsonlFile')]};reconstructed=''.join(next(streams[part['kind']]) for part in layout['segments'] for _ in range(part['lines']))
  assert all(next(it,None) is None for it in streams.values());assert sha(reconstructed.encode())==layout['reconstructedSHA256'] and len(reconstructed.encode())==layout['reconstructedBytes']
 for alias,link in m['preliminaryTestAliases'].items():
  assert sha(contents[link['target']].encode())==link['sha256'];contents[alias]=contents[link['target']]
 purecounts={} 
 for label,suites,total,size in (('pure-preliminary',40,2613,135),('pure-final',41,2631,137)):
  count={}
  for p,t in contents.items():
   if p.startswith(label+'/contracts/'):
    last=t.strip().splitlines()[-1];match=re.search(r'(\d+)/(\d+)',last) or re.search(r'(?:TOTAL|FINAL|PASS) (\d+)',last);assert match,(p,last)
    if len(match.groups())==2:assert match[1]==match[2]
    count[p]=int(match[1])
  assert len(count)==suites and sum(count.values())==total
  build=json.loads(contents[label+'/source-before.json']);frozen=json.loads(contents[label+'/source-frozen-check.json']);assert frozen['filesChecked']==len(build)==size and not frozen['changed'] and frozen['assertions']==total and frozen['pureSuites']==suites
  assert int(contents[label+'/contracts.exit'])==int(contents[label+'/host-build.exit'])==0
  purecounts[label]={'suites':suites,'assertions':total}
 build=json.loads(contents['pure-final/source-before.json']);frozen=json.loads(contents['pure-final/source-frozen-check.json'])
 assert len(m['superseded'])==3 and all(c['status']=='superseded_not_run' for c in m['superseded'])
 sources=links=0
 for label,run in m['runs'].items():
  prefix='cases/'+label+'/';assert prefix+'runner-outcome.json' in contents
  if 'sourceFiles' in run:
   inv=json.loads(contents[prefix+'source-hashes.json']);assert len(inv)==run['sourceFiles'];sources+=len(inv);joined={p:h for p,h in inv.items() if p in build};assert len(joined)==run['buildSourceLinks'] and all(build[p]==h for p,h in joined.items());links+=len(joined)
   if a.check_sources:
    for p,h in inv.items():assert sha((ROOT/p).read_bytes())==h,p
  if 'installedHashesVerified' in run:
   inv=json.loads(contents[prefix+'installed-hashes.json']);assert all(inv[p]==h for p,h in run['installedHashesVerified'].items());assert inv['ToolAgentGate.dll']==frozen['hostSha256']
 # 以固定SHA抽取纯函数重验原生日志，不导入runner、驱动或启动进程。
 v=contents['batch/frozen/validate.py'];assert sha(v.encode())=='d8c9c460cb39782d7e0e8c331f6883c148f422a843054f30f3c2d1d3848bc461'
 nodes=[n for n in ast.parse(v).body if isinstance(n,ast.FunctionDef) and n.name in ('finite','vector','sampling','safety','validate')];ns={'math':math};exec(compile(ast.Module(body=nodes,type_ignores=[]),'frozen-pure-audit37-validator','exec'),ns)
 vd=contents['frozen-minimal/readiness-driver.py'];vv=contents['frozen-minimal/readiness-validate.py'];rinv=json.loads(contents['cases/readiness/source-hashes.json']);assert sha(vd.encode())==rinv['experiments/navigation-readiness-evidence/driver.py'] and sha(vv.encode())==rinv['experiments/navigation-readiness-evidence/validate.py']
 nodes=[n for n in ast.parse(vd).body if isinstance(n,ast.FunctionDef) and n.name=='ready_sample' or isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='NAV_ARGS' for t in n.targets)];dn={'math':math};exec(compile(ast.Module(body=nodes,type_ignores=[]),'pure-readiness-helpers','exec'),dn)
 vnode=next(n for n in ast.parse(vv).body if isinstance(n,ast.FunctionDef) and n.name=='validate');vn={'math':math,'uuid':uuid,'driver_module':types.SimpleNamespace(NAV_ARGS=dn['NAV_ARGS'],ready_sample=dn['ready_sample'])};exec(compile(ast.Module(body=[vnode],type_ignores=[]),'pure-readiness-validator','exec'),vn)
 matrix=json.loads(contents['batch/matrix.json']);results=json.loads(contents['batch/matrix-result.json']);assert results['remainingCasesNotRun']==0 and len(results['results'])==3;native={}
 for label in ('arrive','cancel','readiness'):
  prefix='cases/'+label+'/';rows=[json.loads(x) for x in contents[prefix+'tool-agent.jsonl'].splitlines() if x.strip()];old=vn['validate'](rows,json.loads(contents[prefix+'readiness-driver-result.json'])) if label=='readiness' else None
  verdict=ns['validate'](rows,label,old);rawmeta=m['files'][prefix+'tool-agent.jsonl'];verdict.update(rawLogSha256=rawmeta['originalSHA256'],rawLogBytes=rawmeta['originalBytes']);assert verdict==json.loads(contents[prefix+'native-validation.json']),label
  outcome=json.loads(contents[prefix+'runner-outcome.json']);assert outcome==next(r for r in results['results'] if r['case']==label);assert outcome['validationVerdict']==verdict['verdict']
  checkpoint=json.loads(contents['checkpoints/'+label+'.json']);assert checkpoint['state']==('passed' if outcome['passed'] else 'failed')
  for source,h in checkpoint['sources'].items():assert any(meta['source']==source and meta['originalSHA256']==h for meta in m['files'].values()),source
  for source,h in checkpoint['results'].items():assert any(meta['source']==source and meta['originalSHA256']==h for meta in m['files'].values()),source
  for name,h in json.loads(contents[prefix+'evidence-hashes.json']).items():assert (m['consoleReconstruction'][label]['originalSHA256'] if name=='console.log' else m['files'][prefix+name]['originalSHA256'])==h,name
  native[label]={'verdict':verdict['verdict'],'hostExit':outcome['hostExit'],'driverExit':outcome['driverExit'],'sampling':verdict['sampling']}
 for name,h in json.loads(contents['batch/bundle-hashes.json']).items():assert m['files']['batch/'+name]['originalSHA256']==h,name
 refs=json.loads(contents['food-review/references.json'])['inputs'];assert len(refs)==11
 for link in refs.values():assert sha((ROOT/link['path']).read_bytes())==link['archivedSHA256']
 assert 'Ran 9 tests' in contents['food-review/tests.log'] and 'Ran 3 tests' in contents['food-review/timeline-tests.log'] and 'Ran 18 tests' in contents['preparation/tests-final.log']
 assert 'Ran 18 tests' in contents['pure-final/native-validator-tests.log'] and int(contents['pure-final/native-validator-tests.exit'])==0
 assert int(contents['food-review/exit.txt'])==int(contents['food-review/timeline-exit.txt'])==0
 if a.check_sources:
  for p,h in json.loads(contents['final-source-manifest.json'])['files'].items():assert sha((ROOT/p).read_bytes())==h,p
 print(json.dumps({'verdict':'pass','files':len(actual),'bytes':sum((HERE/p).stat().st_size for p in actual),'pure':purecounts,'native':native,'terminalRepeatLoopAssertions':1000,'sourceHashChecks':sources,'buildSourceLinks':links,'repositorySourcesChecked':a.check_sources,'manifestSHA256':sha((HERE/'manifest.json').read_bytes()),'orphans':[]},ensure_ascii=False))
if __name__=='__main__':main()
