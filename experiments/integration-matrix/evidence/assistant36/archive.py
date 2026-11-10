#!/usr/bin/env python3
"""阶段36明文封存；只读已结束用例，不运行游戏、驱动或API。"""
import argparse,hashlib,json,pathlib,re
HERE=pathlib.Path(__file__).resolve().parent; ROOT=HERE.parents[3]
SECRET=re.compile(r'(?:sk-[A-Za-z0-9_.-]{16,}|Bearer\s+[A-Za-z0-9_.-]{16,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|(?:api[_-]?key|access[_-]?token|password)\s*[:=]\s*[\x22\x27][A-Za-z0-9_./+\-=]{20,}[\x22\x27])',re.I)
def sha(b):return hashlib.sha256(b).hexdigest()
def gitblob(b):return hashlib.sha1(b'blob '+str(len(b)).encode()+b'\0'+b).hexdigest()
def encode(o):return (json.dumps(o,ensure_ascii=False,sort_keys=True,indent=2)+'\n').encode()
def sanitize(t):
 n=0
 for pattern,replacement in ((re.escape(str(ROOT)),'<CLOUD_REPO>'),(r'/(?:home|Users)/[^/\s]+/','<USER_HOME>/'),(r'/usr/share/fonts/','<SYSTEM_FONTS>/')):
  t,k=re.subn(pattern,replacement,t);n+=k
 return t,n

def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--write',action='store_true');args=ap.parse_args()
 stage=ROOT/'work/assistant36-natural'; assert int((stage/'exit.txt').read_text())==0
 files={};meta={}
 def add(p,dest):
  assert p.is_file() and not p.is_symlink();raw=p.read_bytes();text=raw.decode('utf-8');assert not SECRET.search(text)
  text,n=sanitize(text);data=text.encode();files[dest]=data
  meta[dest]={'source':p.relative_to(ROOT).as_posix(),'originalSHA256':sha(raw),'archivedSHA256':sha(data),'bytes':len(data),'pathRedactions':n}
 for name in ('tool-agent.jsonl','console.log','stage.json','source-hashes.json','installed-hashes.json','run.sh','init.js','exit.txt','food-validation.json'):add(stage/name,'run/'+name)
 for p in sorted((stage/'control').glob('[0-9]*/*.json')):
  assert p.name in ('request.json','accepted.json','response-0001.received.json'),p.name
  add(p,'run/'+p.relative_to(stage).as_posix())
 validator='experiments/integration-matrix/validate_food.py';add(stage/'sources'/validator,'frozen-minimal/validate_food.py')
 inv=json.loads((stage/'source-hashes.json').read_text());actual={p.relative_to(stage/'sources').as_posix():sha(p.read_bytes()) for p in (stage/'sources').rglob('*') if p.is_file() and '__pycache__' not in p.parts};assert inv==actual
 base=json.loads((ROOT/'work/base73dc1d7-tree.json').read_text());links={}
 for p,h in inv.items():
  b=(stage/'sources'/p).read_bytes();blob=gitblob(b);assert blob==base[p];links[p]={'sha256':h,'gitBlob':blob}
 installed=json.loads((stage/'installed-hashes.json').read_text());binary={}
 for name in ('ToolAgentGate.dll','Survivalcraft.dll','Westworld.Runtime.Core.dll','Westworld.Scapi.Adapter.dll','Westworld.Decisions.Local.dll'):
  binary[name]=sha((stage/name).read_bytes());assert binary[name]==installed[name]
 prior=ROOT/'experiments/navigation-readiness-evidence/evidence/integration35/manifest.json';pm=json.loads(prior.read_text());assert binary==pm['runs']['readiness']['installedHashesVerified']
 m={'schema':'westworld-assistant36-plaintext-v1','archiveComplete':True,'files':meta,'sourceReference':{'commit':'73dc1d7','verifiedAgainst':'协调线程已核验远端树；逐文件Git blob与冻结sources一致','files':links,'count':len(links)},'installedHashesVerified':binary,'priorBuildReference':{'repositoryPath':prior.relative_to(ROOT).as_posix(),'sha256':sha(prior.read_bytes()),'copied':False},'coordinatorDisclosure':{'controller':'assistant_manual_file_decisions','externalApiCalls':0,'assistantDecisionTokens':None,'assistantDecisionTokensNote':'未单独计量；不等于零token或零总成本','priorSameSeedExperience':True,'source':'协调线程阶段36任务及本场stage声明；非独立网络流量证明'},'limits':['不是Terra独立模型运行或自主改善','有同种子先验经验，不是公平A/B','单次短场，不证明长期生存','原始stage.status仍为staged_not_run；以exit及raw result确认结束','日志哈希与一致性校验不是防篡改证明'],'excluded':['密钥/环境变量','游戏资产/二进制','世界/存档','重复全源码与阶段35纯回归包']}
 files['manifest.json']=encode(m)
 if args.write:
  for name,b in files.items():
   p=HERE/name
   if p.exists() and p.read_bytes()!=b:raise ValueError('不覆盖不同已存文件：'+name)
   p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b)
 print(json.dumps({'evidenceFiles':len(meta),'sourceChecks':len(links),'binaryHashChecks':len(binary),'write':args.write},ensure_ascii=False))
if __name__=='__main__':main()
