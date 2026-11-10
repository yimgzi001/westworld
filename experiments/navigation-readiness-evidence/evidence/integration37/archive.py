#!/usr/bin/env python3
"""阶段37精简明文归档：仅收已结束的三场、首轮40套和最终41套纯日志和必要构建记录。"""
import argparse,hashlib,json,pathlib,re
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[3]
SECRET=re.compile(r'(?:sk-[A-Za-z0-9_.-]{16,}|Bearer\s+[A-Za-z0-9_.-]{16,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|(?:api[_-]?key|access[_-]?token|password)\s*[:=]\s*[\x22\x27][A-Za-z0-9_./+\-=]{20,}[\x22\x27])',re.I)
def sha(b):return hashlib.sha256(b).hexdigest()
def encode(x):return (json.dumps(x,ensure_ascii=False,sort_keys=True,indent=2)+'\n').encode()
def sanitize(t):
 n=0
 for pattern,repl in ((re.escape(str(ROOT)),'<CLOUD_REPO>'),(r'/(?:home|Users)/[^/\s]+/','<USER_HOME>/'),(r'/usr/share/fonts/','<SYSTEM_FONTS>/')):
  t,k=re.subn(pattern,repl,t);n+=k
 return t,n

def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--write',action='store_true');args=ap.parse_args()
 plan=json.loads((ROOT/'work/integration37-publication/collection-plan.json').read_text());assert plan['readyToArchive'],'三场未全部结束，禁止最终封存'
 assert len(plan['cases'])==3;files={};meta={};runs={};aliases={};consoles={}
 def add(src,dest):
  p=ROOT/src;assert p.is_file() and not p.is_symlink();assert p.suffix.lower() in ('.json','.jsonl','.log','.txt','.exit','.py','.sh','.js','.md'),src
  b=p.read_bytes();t=b.decode('utf-8');assert not SECRET.search(t),src;t,n=sanitize(t);clean=t.encode();files[dest]=clean;meta[dest]={'source':src,'originalSHA256':sha(b),'archivedSHA256':sha(clean),'bytes':len(clean),'originalBytes':len(b),'pathRedactions':n}
 pure=ROOT/plan['finalPureDirectory']
 for label,directory,n in (('pure-preliminary',ROOT/plan['preliminaryPureDirectory'],40),('pure-final',pure,41)):
  assert int((directory/'contracts.exit').read_text())==int((directory/'host-build.exit').read_text())==0
  for name in ('contracts.log','contracts.exit','host-build.log','host-build.exit','source-before.json','source-frozen-check.json'):add((directory/name).relative_to(ROOT).as_posix(),label+'/'+name)
  tests=sorted((directory/'contracts').glob('*.test.log'));assert len(tests)==n
  for p in tests:
   target='pure-final/contracts/'+p.name
   if label=='pure-preliminary' and (pure/'contracts'/p.name).read_bytes()==p.read_bytes():aliases['pure-preliminary/contracts/'+p.name]={'target':target,'source':p.relative_to(ROOT).as_posix(),'sha256':sha(p.read_bytes())}
   else:add(p.relative_to(ROOT).as_posix(),label+'/contracts/'+p.name)
 add(plan['finalSourceManifest'],'final-source-manifest.json')
 # 旧host三场只完成staging；保留准备记录，不伪造实测结果。
 old=ROOT/plan['supersededBatch'];oldmatrix=json.loads((old/'matrix.json').read_text());unrun=[]
 for name in ('matrix.json','preparation.json','bundle-hashes.json'):add((old/name).relative_to(ROOT).as_posix(),'superseded/'+name)
 for case in oldmatrix['cases']:
  d=pathlib.Path(case['directory']);assert not any((d/n).exists() for n in ('runner-started.json','tool-agent.jsonl','exit.txt'))
  for name in ('stage.json','source-hashes.json','installed-hashes.json'):add((d/name).relative_to(ROOT).as_posix(),'superseded/'+case['case']+'/'+name)
  unrun.append({'case':case['case'],'status':'superseded_not_run','checkedAbsent':['runner-started.json','tool-agent.jsonl','exit.txt']})
 for p in sorted((ROOT/'work/sampling37').iterdir()):
  if p.is_file() and p.suffix in ('.json','.log','.txt'):add(p.relative_to(ROOT).as_posix(),'sampling/'+p.name)
 build=json.loads((pure/'source-before.json').read_text());freeze=json.loads((pure/'source-frozen-check.json').read_text());assert not freeze['changed']
 for case in plan['cases']:
  label=case['label'];directory=ROOT/case['directory'];assert (directory/'runner-outcome.json').is_file()
  for name in case['files']:
   if name!='console.log':add((directory/name).relative_to(ROOT).as_posix(),'cases/'+label+'/'+name)
  # console按序包含全部JSONL，仅保存独有行；分段计数可无损重建全部脱敏console。
  raw=(directory/'console.log').read_bytes();t=raw.decode();assert not SECRET.search(t);log=(directory/'tool-agent.jsonl').read_text().splitlines(keepends=True);i=0;extra=[];segments=[]
  for line in t.splitlines(keepends=True):
   kind='jsonl' if i<len(log) and line==log[i] else 'unique'
   if kind=='jsonl':i+=1
   else:extra.append(line)
   if segments and segments[-1]['kind']==kind:segments[-1]['lines']+=1
   else:segments.append({'kind':kind,'lines':1})
  assert i==len(log);unique,n=sanitize(''.join(extra));clean,fulln=sanitize(t);dest='cases/'+label+'/console-unique.log';b=unique.encode();files[dest]=b
  meta[dest]={'source':(directory/'console.log').relative_to(ROOT).as_posix(),'originalSHA256':sha(raw),'archivedSHA256':sha(b),'originalBytes':len(raw),'bytes':len(b),'pathRedactions':n,'transform':'仅保留console独有行，完整顺序见consoleReconstruction'}
  consoles[label]={'uniqueFile':dest,'jsonlFile':'cases/'+label+'/tool-agent.jsonl','segments':segments,'originalSHA256':sha(raw),'originalBytes':len(raw),'reconstructedSHA256':sha(clean.encode()),'reconstructedBytes':len(clean.encode()),'fullPathRedactions':fulln,'uniqueLines':len(extra),'sharedJsonlLines':len(log)}
  for p in sorted((directory/'control').glob('[0-9]*/*.json')):
   if p.name in ('request.json','accepted.json','canceled.json') or p.name.endswith('.received.json'):add(p.relative_to(ROOT).as_posix(),'cases/'+label+'/'+p.relative_to(directory).as_posix())
  info={}
  if (directory/'source-hashes.json').is_file():
   inv=json.loads((directory/'source-hashes.json').read_text());actual={p.relative_to(directory/'sources').as_posix():sha(p.read_bytes()) for p in (directory/'sources').rglob('*') if p.is_file() and '__pycache__' not in p.parts};assert actual==inv
   joined={p:h for p,h in inv.items() if p in build};assert all(build[p]==h for p,h in joined.items());info={'sourceFiles':len(inv),'buildSourceLinks':len(joined)}
  if (directory/'installed-hashes.json').is_file():
   installed=json.loads((directory/'installed-hashes.json').read_text());hashes={}
   for name in ('ToolAgentGate.dll','Survivalcraft.dll','Westworld.Runtime.Core.dll','Westworld.Scapi.Adapter.dll','Westworld.Decisions.Local.dll'):
    hashes[name]=sha((directory/name).read_bytes());assert hashes[name]==installed[name]
   assert hashes['ToolAgentGate.dll']==freeze['hostSha256'];info['installedHashesVerified']=hashes
  runs[label]=info
 for pair in plan['supplementaryFiles']:add(pair['source'],pair['destination'])
 m={'schema':'westworld-integration37-plaintext-v1','complete':True,'baseCommit':plan['baseCommit'],'baseTree':plan['baseTree'],'files':meta,'runs':runs,'preliminaryTestAliases':aliases,'consoleReconstruction':consoles,'superseded':unrun,'scope':'首轮40套与最终41套纯回归及实际完成的3场原生确定性导航对照；不包含模型/API试验','sourceDelivery':'源码由同批提交提供，逐场冻结源码清单及构建来源哈希保留；不复制整套源码或二进制','exclusions':['密钥及环境变量','游戏本体/资产/二进制','世界/存档','未运行模型用例'],'limits':['失败及未知保留原样','coverageUnknown=false不能证明采样之间每一瞬间连续运动','0.15秒分析阈值若使用须明确为新版，不追改旧0.1秒结果','未接入的请求/回复边界强制采样不宣称已覆盖']}
 files['manifest.json']=encode(m)
 if args.write:
  for name,b in files.items():
   p=HERE/name
   if p.exists() and p.read_bytes()!=b:raise ValueError('不覆盖不同已存文件：'+name)
   p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b)
 print(json.dumps({'evidenceFiles':len(meta),'write':args.write,'sources':sum(v.get('sourceFiles',0) for v in runs.values())},ensure_ascii=False))
if __name__=='__main__':main()
