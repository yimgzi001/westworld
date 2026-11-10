#!/usr/bin/env python3
"""阶段38最小明文封存；只读已结束输入，不运行游戏/驱动/API。"""
import argparse,hashlib,json,pathlib,re
from receipt_table import encode_log,decode_log
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
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--write',action='store_true');a=ap.parse_args()
 plan=json.loads((ROOT/'work/integration38-publication/collection-plan.json').read_text());assert plan['readyToArchive'],'实际运行尚未结束，禁止最终封存'
 files={};meta={};consoles={};sources={};tables={}
 def add(src,dest):
  p=ROOT/src;assert p.is_file() and not p.is_symlink();assert p.suffix.lower() in ('.json','.jsonl','.log','.txt','.exit','.py','.sh','.js','.md'),src
  b=p.read_bytes();t=b.decode('utf-8');assert not SECRET.search(t),src;t,n=sanitize(t);clean=t.encode();files[dest]=clean;meta[dest]={'source':src,'originalSHA256':sha(b),'archivedSHA256':sha(clean),'originalBytes':len(b),'bytes':len(clean),'pathRedactions':n}
 for pair in plan['files']:add(pair['source'],pair['destination'])
 build=json.loads((ROOT/plan['finalPureDirectory']/'source-before.json').read_text())
 for case in plan['cases']:
  label=case['label'];d=ROOT/case['directory']
  for name in case['files']:
   if name not in ('console.log','tool-agent.jsonl'):add((d/name).relative_to(ROOT).as_posix(),'cases/'+label+'/'+name)
  if (d/'tool-agent.jsonl').is_file():
   raw=(d/'tool-agent.jsonl').read_bytes();original=raw.decode();assert not SECRET.search(original);clean,n=sanitize(original);templates,stream,stats=encode_log(clean);assert decode_log(templates,stream)==clean
   dests=[]
   for suffix,t in (('receipt-templates.jsonl',templates),('events-readable.jsonl',stream)):
    dest='cases/'+label+'/'+suffix;b=t.encode();files[dest]=b;dests.append(dest);meta[dest]={'source':(d/'tool-agent.jsonl').relative_to(ROOT).as_posix(),'originalSHA256':sha(raw),'originalBytes':len(raw),'archivedSHA256':sha(b),'bytes':len(b),'pathRedactions':n,'transform':'仅host_navigation_status数值表，其他行原样；完整还原见logTables'}
   tables[label]=stats|{'templatesFile':dests[0],'eventsFile':dests[1],'originalSHA256':sha(raw),'originalBytes':len(raw),'decodedSHA256':sha(clean.encode()),'decodedBytes':len(clean.encode()),'pathRedactions':n}
  else:
   assert case['state']=='blocked_before_process_start';outcome=json.loads((d/'model-outcome.json').read_text());assert outcome['state']==case['state'] and outcome['apiRequests']==0 and outcome['apiUsage'] is None
   assert not any((d/n).exists() for n in ('api-audit.jsonl','api-audit.jsonl.events.jsonl','control','console.log','exit.txt','doc'))
  if (d/'console.log').is_file():
   raw=(d/'console.log').read_bytes();t=raw.decode();assert not SECRET.search(t);log=(d/'tool-agent.jsonl').read_text().splitlines(keepends=True);i=0;extra=[];segments=[]
   for line in t.splitlines(keepends=True):
    kind='jsonl' if i<len(log) and line==log[i] else 'unique'
    if kind=='jsonl':i+=1
    else:extra.append(line)
    if segments and segments[-1]['kind']==kind:segments[-1]['lines']+=1
    else:segments.append({'kind':kind,'lines':1})
   assert i==len(log),'console不是完整JSONL按序超集，保留原文后另审，不丢行'
   unique,n=sanitize(''.join(extra));clean,fulln=sanitize(t);dest='cases/'+label+'/console-unique.log';b=unique.encode();files[dest]=b
   meta[dest]={'source':(d/'console.log').relative_to(ROOT).as_posix(),'originalSHA256':sha(raw),'archivedSHA256':sha(b),'originalBytes':len(raw),'bytes':len(b),'pathRedactions':n,'transform':'console独有行；完整顺序见consoleReconstruction'}
   consoles[label]={'uniqueFile':dest,'jsonlFile':'cases/'+label+'/tool-agent.jsonl','segments':segments,'originalSHA256':sha(raw),'originalBytes':len(raw),'reconstructedSHA256':sha(clean.encode()),'reconstructedBytes':len(clean.encode()),'fullPathRedactions':fulln,'uniqueLines':len(extra),'sharedJsonlLines':len(log)}
  for p in sorted((d/'control').glob('[0-9]*/*.json')):
   if p.name in ('request.json','accepted.json','canceled.json') or p.name.endswith('.received.json'):add(p.relative_to(ROOT).as_posix(),'cases/'+label+'/'+p.relative_to(d).as_posix())
  inv=json.loads((d/'source-hashes.json').read_text());actual={p.relative_to(d/'sources').as_posix():sha(p.read_bytes()) for p in (d/'sources').rglob('*') if p.is_file() and '__pycache__' not in p.parts};assert inv==actual
  linked={p:h for p,h in inv.items() if p in build};assert all(build[p]==h for p,h in linked.items())
  installed=json.loads((d/'installed-hashes.json').read_text());binary={}
  for name in ('ToolAgentGate.dll','Survivalcraft.dll','Westworld.Runtime.Core.dll','Westworld.Scapi.Adapter.dll','Westworld.Decisions.Local.dll'):
   binary[name]=sha((d/name).read_bytes());assert binary[name]==installed[name]
  sources[label]={'state':case['state'],'sourceFiles':len(inv),'buildSourceLinks':len(linked),'installedHashesVerified':binary}
 manifest={'schema':'westworld-integration38-plaintext-v1','complete':True,'baseCommit':plan['baseCommit'],'baseTree':plan['baseTree'],'files':meta,'consoleReconstruction':consoles,'logTables':tables,'runs':sources,'scope':plan['completedScope'],'limits':plan['limits'],'excluded':['密钥/环境变量','游戏资产/二进制','世界/存档','旧未知API重放']}
 files['manifest.json']=encode(manifest)
 if a.write:
  for name,b in files.items():
   p=HERE/name
   if p.exists() and p.read_bytes()!=b:raise ValueError('拒绝覆盖不同已存文件：'+name)
   p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b)
 print(json.dumps({'evidenceFiles':len(meta),'sourceChecks':sum(r['sourceFiles'] for r in sources.values()),'write':a.write},ensure_ascii=False))
if __name__=='__main__':main()
