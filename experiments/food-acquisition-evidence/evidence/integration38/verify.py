#!/usr/bin/env python3
"""阶段38完整还原扫描与纯离线验收；不启动游戏、驱动或API。"""
import argparse,ast,collections,json,math,re
from archive import HERE,ROOT,SECRET,sha
from receipt_table import decode_log,encode_log
PRIVATE=re.compile(r'/(?:home|Users)/[^/\s]+/|/workspace/(?:scratch|shared)/[^\s\"\']+|/usr/share/fonts/|[A-Za-z]:\\Users\\')
def scan(t):assert not SECRET.search(t) and not PRIVATE.search(t)
def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--check-sources',action='store_true');ap.add_argument('--write-review',action='store_true');a=ap.parse_args();m=json.loads((HERE/'manifest.json').read_text());assert m['complete'];contents={};allowed={'archive.py','verify.py','receipt_table.py','test_receipt_table.py','README.md','manifest.json','content-review.json'}
 for name,v in m['files'].items():
  p=HERE/name;assert p.is_file() and not p.is_symlink();b=p.read_bytes();assert sha(b)==v['archivedSHA256'] and len(b)==v['bytes'];t=b.decode('utf-8');scan(t)
  if v['pathRedactions']==0 and not v.get('transform'):assert v['originalSHA256']==v['archivedSHA256']
  contents[name]=t;allowed.add(name)
 actual={p.relative_to(HERE).as_posix() for p in HERE.rglob('*') if p.is_file() and '__pycache__' not in p.parts};assert not actual-allowed and allowed-actual<={'content-review.json'}
 decoded={};categories={}
 for label,t in m['logTables'].items():
  text=decode_log(contents[t['templatesFile']],contents[t['eventsFile']]);assert sha(text.encode())==t['decodedSHA256'] and len(text.encode())==t['decodedBytes'];scan(text)
  again=encode_log(text);assert again[0]==contents[t['templatesFile']] and again[1]==contents[t['eventsFile']]
  contents['cases/'+label+'/tool-agent.jsonl']=text;decoded[label]=[json.loads(line) for line in text.splitlines()];assert len(decoded[label])==t['originalRows'];categories[label]=dict(collections.Counter(r.get('kind','unknown') for r in decoded[label]))
 for label,layout in m['consoleReconstruction'].items():
  streams={k:iter(contents[layout[f]].splitlines(keepends=True)) for k,f in [('unique','uniqueFile'),('jsonl','jsonlFile')]};text=''.join(next(streams[p['kind']]) for p in layout['segments'] for _ in range(p['lines']))
  assert all(next(it,None) is None for it in streams.values());assert sha(text.encode())==layout['reconstructedSHA256'] and len(text.encode())==layout['reconstructedBytes'];scan(text)
 counts={}
 for name,t in contents.items():
  if name.startswith('pure/contracts/'):
   last=t.strip().splitlines()[-1];match=re.search(r'(\d+)/(\d+)',last) or re.search(r'(?:TOTAL|FINAL|PASS) (\d+)',last);assert match,(name,last)
   if len(match.groups())==2:assert match[1]==match[2]
   counts[name]=int(match[1])
 assert len(counts)==43 and sum(counts.values())==2822
 frozen=json.loads(contents['pure/source-frozen-check.json']);build=json.loads(contents['pure/source-before.json']);assert frozen['filesChecked']==len(build)==142 and not frozen['changed'] and frozen['pureSuites']==43 and frozen['assertionsExpected']==2822
 assert int(contents['pure/contracts.exit'])==int(contents['pure/host-build.exit'])==0
 sourcechecks=links=0
 for label,run in m['runs'].items():
  inv=json.loads(contents['cases/'+label+'/source-hashes.json']);assert len(inv)==run['sourceFiles'];sourcechecks+=len(inv);joined={p:h for p,h in inv.items() if p in build};assert len(joined)==run['buildSourceLinks'] and all(build[p]==h for p,h in joined.items());links+=len(joined)
  installed=json.loads(contents['cases/'+label+'/installed-hashes.json']);assert all(installed[p]==h for p,h in run['installedHashesVerified'].items());assert installed['ToolAgentGate.dll']==frozen['hostSha256']
  if a.check_sources:
   for p,h in inv.items():
    b=contents['batch/frozen/'+p.split('/')[-1]].encode() if p.startswith('work/integration38-cases/') else (ROOT/p).read_bytes();assert sha(b)==h,p
 contract=contents['batch/frozen/validate.py'];assert sha(contract.encode())=='33d72abe4e48d0592c5c1bee7623343dca8494a12a6aec583b62bccf82d7b2be'
 node=next(n for n in ast.parse(contract).body if isinstance(n,ast.FunctionDef) and n.name=='validate');cn={};exec(compile(ast.Module(body=[node],type_ignores=[]),'frozen-pure-eat38-validator','exec'),cn)
 food=contents['frozen-minimal/food-validate.py'];inv=json.loads(contents['cases/valid_eat/source-hashes.json']);assert sha(food.encode())==inv['experiments/food-acquisition-evidence/validate.py'];node=next(n for n in ast.parse(food).body if isinstance(n,ast.FunctionDef) and n.name=='validate');fn={'math':math};exec(compile(ast.Module(body=[node],type_ignores=[]),'frozen-pure-food-chain-validator','exec'),fn)
 matrix=json.loads(contents['batch/matrix-result.json']);assert len(matrix['results'])==4 and matrix['remainingCasesNotRun']==0;results={}
 for label,rows in decoded.items():
  prefix='cases/'+label+'/';audit=json.loads(contents[prefix+'eat-contract-driver-result.json']);chains=[r for r in rows if r.get('kind')=='food_chain_evidence'];v=cn['validate'](rows,audit,fn['validate'](chains[0]) if len(chains)==1 else None);v['verdict']=v['contractVerdict'];assert v==json.loads(contents[prefix+'eat-contract-validation.json']) and v['contractVerdict']=='pass'
  outcome=json.loads(contents[prefix+'runner-outcome.json']);assert outcome==next(r for r in matrix['results'] if r['case']==label);assert outcome['passed'] and outcome['driverExit']==0 and not outcome['drainUncertain'];assert int(contents[prefix+'exit.txt'])==outcome['hostExit']==(0 if label=='valid_eat' else 1)
  end=next(r for r in rows if r.get('kind')=='result');assert end['passed']==(label=='valid_eat') and end['updateFailures']==0
  checkpoint=json.loads(contents['checkpoints/'+label+'.json']);assert checkpoint['state']=='passed'
  for source,h in (checkpoint['sources']|checkpoint['results']).items():assert any(meta['source']==source and meta['originalSHA256']==h for meta in m['files'].values()),source
  for name,h in json.loads(contents[prefix+'evidence-hashes.json']).items():
   actualh=m['logTables'][label]['originalSHA256'] if name=='tool-agent.jsonl' else m['consoleReconstruction'][label]['originalSHA256'] if name=='console.log' else m['files'][prefix+name]['originalSHA256'];assert actualh==h,name
  results[label]={'contract':'pass','hostGoalPassed':end['passed'],'hostExit':outcome['hostExit'],'hostSeconds':end['seconds'],'coordinatorSeconds':outcome['elapsedSeconds'],'submittedEat':audit['attempt']['submittedEat']}
 for name,h in json.loads(contents['batch/bundle-hashes.json']).items():assert m['files']['batch/'+name]['originalSHA256']==h,name
 natural=json.loads(contents['cases/natural/model-outcome.json']);assert natural['state']=='blocked_before_process_start' and natural['apiRequests']==0 and natural['apiUsage'] is None and not natural['modelRunPerformed'] and not natural['nativeNaturalWorldStarted'] and not natural['credentialEntered'] and not natural['replayedOldRequest']
 assert not any(p.startswith('cases/natural/') and any(x in p for x in ('api-audit','/control/','tool-agent','console','exit.txt')) for p in m['files'])
 review={'schema':'integration38-readable-full-content-review-v1','verdict':'pass','scope':'授权游戏测试角色状态/库存/公开工具菜单与schema/请求响应/原生收据/纯测构建记录；不是私人游戏存档、密钥或个人健康数据','encoding':'全部UTF-8；仅重复host_navigation_status采用原样JSON模板+命名数值字符串表，其他事件行逐字节保留；无base64/gzip或二进制','allDecodedLogsScanned':True,'credentialPrivatePathPatternFindings':[],'eventCategories':categories,'logs':m['logTables'],'nativeResults':results,'natural':natural,'pureSuites':43,'pureAssertions':2822,'sourceChecks':sourcechecks,'buildSourceLinks':links,'binaryHashLinks':5*len(m['runs']),'finiteScanLimits':'模式扫描与一致性核验不是绝对安全或防篡改证明。'}
 target=HERE/'content-review.json';raw=(json.dumps(review,ensure_ascii=False,sort_keys=True,indent=2)+'\n').encode()
 if a.write_review:
  assert not target.exists() or target.read_bytes()==raw;target.write_bytes(raw)
 else:assert target.read_bytes()==raw
 files=[p for p in HERE.rglob('*') if p.is_file() and '__pycache__' not in p.parts];print(json.dumps({'verdict':'pass','files':len(files),'bytes':sum(p.stat().st_size for p in files),'manifestSHA256':sha((HERE/'manifest.json').read_bytes()),'decodedRows':sum(len(r) for r in decoded.values()),'decodedBytes':sum(t['decodedBytes'] for t in m['logTables'].values()),'pureSuites':43,'pureAssertions':2822,'nativeResults':results,'natural':'blocked_before_process_start','sourceChecks':sourcechecks,'repositorySourcesChecked':a.check_sources,'orphans':[]},ensure_ascii=False))
if __name__=='__main__':main()
