#!/usr/bin/env python3
"""阶段35明文归档：脱敏UTF-8直接保存，不压缩、不运行游戏/驱动/API。"""
import argparse,hashlib,json,pathlib,re
HERE=pathlib.Path(__file__).resolve().parent;ROOT=HERE.parents[3]
SECRET=re.compile(r'(?:sk-[A-Za-z0-9_.-]{16,}|Bearer\s+[A-Za-z0-9_.-]{16,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|(?:api[_-]?key|access[_-]?token|password)\s*[:=]\s*[\x22\x27][A-Za-z0-9_./+\-=]{20,}[\x22\x27])',re.I)
def sha(raw):return hashlib.sha256(raw).hexdigest()
def sanitize(text):
 n=0
 for pattern,replacement in ((re.escape(str(ROOT)),'<CLOUD_REPO>'),(r'/(?:home|Users)/[^/\s]+/','<USER_HOME>/'),(r'/usr/share/fonts/','<SYSTEM_FONTS>/')):
  text,k=re.subn(pattern,replacement,text);n+=k
 return text,n
def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--write',action='store_true');args=ap.parse_args()
 ready=ROOT/'work/integration35-readiness';food=ROOT/'work/integration35-food-0';pure=ROOT/'work/integration35-unified-v2'
 assert all(int((ready/p).read_text())==0 for p in ('exit.txt','driver-exit.txt','host-coordinator-exit.txt'))
 interrupted=json.loads((food/'interruption.json').read_text());assert interrupted['hostStoppedByCoordinator'] and interrupted['requestOutcome']=='unknown' and interrupted['nativeExitFileMissing']
 assert int((pure/'contracts.exit').read_text())==int((pure/'host-build.exit').read_text())==0
 files={};meta={}
 def add(src,dest,source=False):
  path=ROOT/src;assert path.is_file() and not path.is_symlink();raw=path.read_bytes();text=raw.decode();assert not SECRET.search(text),src
  clean,n=(text,0) if source else sanitize(text);data=clean.encode();files[dest]=data
  meta[dest]={'source':src,'originalSHA256':sha(raw),'archivedSHA256':sha(data),'bytes':len(data),'pathRedactions':n,'type':'frozen_source' if source else 'utf8_evidence'}
 common=('tool-agent.jsonl','console.log','stage.json','source-hashes.json','installed-hashes.json','run.sh','init.js')
 for label,directory,extras in (('readiness',ready,('readiness-driver-result.json','readiness-driver-started.json','readiness-validation.json','driver-console.log','exit.txt','driver-exit.txt','host-coordinator-exit.txt','run-gate.sh')),('food-0',food,('api-audit.jsonl','api-audit.jsonl.events.jsonl','external-driver-launch.json','interruption.json'))):
  for p in common+extras:add((directory/p).relative_to(ROOT).as_posix(),label+'/'+p)
  for p in sorted((directory/'control').glob('[0-9]*/*.json')):
   if p.name in ('request.json','accepted.json','canceled.json') or (p.name.startswith('response-') and p.name.endswith('.received.json')):add(p.relative_to(ROOT).as_posix(),label+'/'+p.relative_to(directory).as_posix())
 for p in ('contracts.log','contracts.exit','host-build.log','host-build.exit','host-sha.txt','source-before.json','stage-tests.log'):add('work/integration35-unified-v2/'+p,'pure/'+p)
 tests=sorted((pure/'contracts').glob('*.test.log'));assert len(tests)==39
 for p in tests:add(p.relative_to(ROOT).as_posix(),'pure/contracts/'+p.name)
 # 只保留离线验收器/常量及两版有差异的小stage脚本，不复制150/156份全源码。
 for p,name in (('experiments/navigation-readiness-evidence/validate.py','readiness-validate.py'),('experiments/navigation-readiness-evidence/driver.py','readiness-driver.py'),('experiments/food-acquisition-evidence/stage.py','readiness-stage.py')):add('work/integration35-readiness/sources/'+p,'frozen-minimal/'+name,True)
 add('work/integration35-food-0/sources/experiments/food-acquisition-evidence/stage.py','frozen-minimal/food0-stage.py',True)
 runs={};build=json.loads((pure/'source-before.json').read_text());HOST=(pure/'host-sha.txt').read_text().strip()
 for label,directory in (('readiness',ready),('food-0',food)):
  inventory=json.loads((directory/'source-hashes.json').read_text());actual={p.relative_to(directory/'sources').as_posix():sha(p.read_bytes()) for p in (directory/'sources').rglob('*') if p.is_file() and '__pycache__' not in p.parts};assert actual==inventory
  installed=json.loads((directory/'installed-hashes.json').read_text());hashes={}
  for name in ('ToolAgentGate.dll','Survivalcraft.dll','Westworld.Runtime.Core.dll','Westworld.Scapi.Adapter.dll','Westworld.Decisions.Local.dll'):
   hashes[name]=sha((directory/name).read_bytes());assert hashes[name]==installed[name]
  assert hashes['ToolAgentGate.dll']==HOST
  joined={p:h for p,h in inventory.items() if p in build};assert all(build[p]==h for p,h in joined.items())
  runs[label]={'sourceFilesVerified':len(inventory),'buildInventoryLinks':len(joined),'installedHashesVerified':hashes,'sourceOverrides':{'experiments/food-acquisition-evidence/stage.py':'frozen-minimal/'+('readiness-stage.py' if label=='readiness' else 'food0-stage.py')}}
 fixture='tests/Westworld.GoalContextPipeline.Tests/fixtures/stage34-contexts.json'
 m={'schema':'westworld-integration35-plaintext-v1','archiveComplete':True,'files':meta,'runs':runs,'pipelineFixture':{'repositoryPath':fixture,'sha256':sha((ROOT/fixture).read_bytes()),'copied':False},'sourceDelivery':'源码由本次仓库提交提供；本包只保存逐stage来源清单、哈希及4份必要小脚本。verify --check-sources可核对仓库源码。','scope':'39套纯回归、非模型readiness原生夹具、food0授权轮询受阻后的未知请求；food1/2未运行，不作模型自然成功声明。','excluded':['密钥与环境变量','游戏二进制/资产','世界与存档','未运行场景'],'completionEvidence':'readiness退出文件/driver结果/validator；food0原interruption协调记录，缺少正式exit/result/ended不伪造。'}
 files['manifest.json']=(json.dumps(m,ensure_ascii=False,sort_keys=True,indent=2)+'\n').encode()
 if args.write:
  for name,raw in files.items():
   p=HERE/name
   if p.exists() and p.read_bytes()!=raw:raise ValueError('不覆盖不同已存文件：'+name)
   p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(raw)
 print(json.dumps({'evidenceFiles':len(meta),'bytes':sum(len(x) for x in files.values()),'sourceHashChecks':sum(r['sourceFilesVerified'] for r in runs.values()),'write':args.write},ensure_ascii=False))
if __name__=='__main__':main()
