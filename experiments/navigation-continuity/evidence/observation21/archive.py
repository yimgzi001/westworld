#!/usr/bin/env python3
"""Read-only evidence collector; --write writes ONLY this archive directory.
No game launch, API, network, credentials, binary assets or world saves.
Requires the publisher's baseline Git tree (path -> blob SHA1, or GitHub tree response).
"""
import argparse,base64,gzip,hashlib,importlib.util,json,pathlib,re
HERE=pathlib.Path(__file__).resolve().parent
ROOT=HERE.parents[3]
BASE='50e3c1007bdfc0f813195671b737180f47f59ebc'
RUNS=[f'snapshot{s}-actors{n}' for s in (19,20,21,22,23) for n in (1,5)]+['tool-route19-delay1-repeat1','tool-route23-delay1-repeat1']
ALLOW=('snapshot.jsonl','tool-agent.jsonl','console.log','exit.txt','run.sh','driver.py','driver.log','driver-exit.txt','continuity-driver-result.json','continuity-driver-started.json','stage.json','source-hashes.json','installed-hashes.json')
SECRET=re.compile(rb'(?:sk-[A-Za-z0-9_.-]{16,}|Bearer\s+[A-Za-z0-9_.-]{16,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)')
TEXT={'.cs','.csproj','.props','.targets','.json','.xml','.md','.py','.sh','.yml','.yaml','.txt','.sln','.slnx','.config','.resx','.js','.ts','.gitignore','.editorconfig'}
def sha(raw):return hashlib.sha256(raw).hexdigest()
def blob(raw):return hashlib.sha1(b'blob '+str(len(raw)).encode()+b'\0'+raw).hexdigest()
def packed(value):return json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',',':')).encode()
def baseline(path):
 data=json.loads(path.read_text())
 if 'tree' in data:return {r['path']:r['sha'] for r in data['tree'] if r.get('type')=='blob'}
 return data

def main():
 ap=argparse.ArgumentParser();ap.add_argument('--baseline-tree',type=pathlib.Path,required=True);ap.add_argument('--write',action='store_true');a=ap.parse_args()
 tree=baseline(a.baseline_tree);index={'schema':'westworld-observation-evidence-v2','baselineCommit':BASE,'baselineTreeInputSHA256':sha(a.baseline_tree.read_bytes()),'runs':{},'bundles':{},'pending':[]}
 baseInventory=None
 def bundle(kind,value):
  raw=packed(value)
  if SECRET.search(raw):raise ValueError('credential-like content in '+kind+'; must explicitly sanitize/review before archive')
  h=sha(raw);key=kind+'-'+h[:20]
  if key not in index['bundles']:
   z=gzip.compress(raw,mtime=0);encoded=base64.b64encode(z);parts=[]
   for offset in range(0,len(encoded),29000):
    name=f'{key}.gz.b64.part{offset//29000+1:03}';part=encoded[offset:offset+29000]
    parts.append({'file':name,'bytes':len(part),'sha256':sha(part)})
    if a.write:(HERE/name).write_bytes(part)
   assert gzip.decompress(base64.b64decode(encoded))==raw
   metadata=packed({'rawSHA256':h,'rawBytes':len(raw),'gzipSHA256':sha(z),'parts':parts})
   metadataName=key+'-manifest.json'
   if a.write:(HERE/metadataName).write_bytes(metadata)
   index['bundles'][key]={'manifest':metadataName,'sha256':sha(metadata)}
  return key
 for name in RUNS:
  d=ROOT/'work'/name;cp=ROOT/'.batch-checkpoints'/(name.removeprefix('tool-')+'.json')
  if not (d/'exit.txt').exists() or not cp.exists():index['pending'].append(name);continue
  checkpoint=json.loads(cp.read_text())
  if not checkpoint.get('finishedUtc'):index['pending'].append(name+':checkpoint_unfinished');continue
  files={n:(d/n).read_bytes().decode('utf-8') for n in ALLOW if (d/n).is_file()}
  for relative,expected in checkpoint.get('results',{}).items():
   p=ROOT/relative
   if not p.is_file() or sha(p.read_bytes())!=expected:raise ValueError('checkpoint hash mismatch '+relative)
  sourceRoot=d/'sources';inventory={};delta={};unchanged={}
  for p in sorted(sourceRoot.rglob('*')):
   if not p.is_file():continue
   relative=p.relative_to(sourceRoot).as_posix();raw=p.read_bytes();inventory[relative]=sha(raw)
   if tree.get(relative)==blob(raw):unchanged[relative]=tree[relative];continue
   if p.suffix not in TEXT and p.name not in TEXT:raise ValueError('non-text/source delta rejected '+relative)
   delta[relative]=raw.decode('utf-8')
  if not inventory:raise ValueError('missing staged sources '+name)
  if 'source-hashes.json' in files and json.loads(files['source-hashes.json'])!=inventory:raise ValueError('source inventory mismatch '+name)
  if baseInventory is None:baseInventory=inventory.copy()
  source={'baselineCommit':BASE,'inventoryBase':bundle('source-inventory',baseInventory),
   'inventoryChanges':{p:h for p,h in inventory.items() if baseInventory.get(p)!=h},
   'inventoryRemoved':[p for p in baseInventory if p not in inventory],
   'changedOrNewFiles':{p:bundle('source-file',text) for p,text in delta.items()},
   'restore':'All inventory paths absent from changedOrNewFiles are exact Git baseline files; verify final inventory SHA256. No deletion inferred outside staged inventory.'}

  rows=[json.loads(l) for l in files.get('snapshot.jsonl',files.get('tool-agent.jsonl','')).splitlines() if l.strip()]
  result=[r for r in rows if r.get('kind')=='result']
  entry={'exit':int(files['exit.txt'].strip()),'logs':{p:bundle('evidence-file',text) for p,text in files.items()},'sourceDelta':bundle('source-delta',source),'sourceCount':len(inventory),'changedSourceCount':len(delta),'checkpoint':checkpoint,'result':result[-1] if result else None}
  if name.startswith('snapshot21'):entry['performanceValidity']='invalid: post-dig benchmark scanned unloaded/invalid region, zero known cells/LOS/voxel reads; functional assertions only'
  if name.startswith(('snapshot22','snapshot23')):
   costs=[r for r in rows if r.get('kind')=='observation_cost']
   entry['performanceValidity']='not measured: no completed observation_cost event'
   if costs:
    cost=costs[-1];workloads=cost.get('workloads',[])
    valid=bool(workloads) and all(w.get('coverage',{}).get('scanStatistics',{}).get('knownCells',0)>0 and w.get('coverage',{}).get('scanStatistics',{}).get('voxelReads',0)>0 and w.get('coverage',{}).get('scanStatistics',{}).get('lineOfSightChecks',0)>0 for w in workloads)
    entry['performanceValidity']='valid nonzero scanning workload; controlled short benchmark, not long-running resource capacity' if valid else 'invalid/incomplete scanning workload'
    entry['performanceSamples']=len(cost.get('milliseconds',[]))
  if name.startswith('tool-route'):
   spec=importlib.util.spec_from_file_location('routevalidator',ROOT/'experiments/navigation-continuity/validate_route.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
   entry['strictRouteValidation']=module.validate(rows)
  runBytes=json.dumps(entry,ensure_ascii=False,indent=2).encode()+b'\n'
  if a.write:(HERE/(name+'-manifest.json')).write_bytes(runBytes)
  index['runs'][name]={'manifest':name+'-manifest.json','sha256':sha(runBytes),'exit':entry['exit'],'changedSourceCount':entry['changedSourceCount']}
 pure={}
 for p in sorted((ROOT/'work/tool-contract19').glob('*')):
  if p.is_file() and p.suffix in ('.log','.txt'):pure[p.name]=p.read_bytes().decode('utf-8')
 python=ROOT/'work/observation19-stage-test.log';pure['navigation-python-tests.log']=python.read_bytes().decode('utf-8')
 counts={}
 for name,text in pure.items():
  if name.endswith('.test.log'):
   matches=list(re.finditer(r'(\d+)/(\d+)|FINAL TOTAL (\d+)',text))
   if not matches:raise ValueError('no assertion count '+name)
   m=matches[-1];counts[name]=int(m[3] or m[2])
 pythonCount=re.search(r'Ran (\d+) tests',pure['navigation-python-tests.log'])
 index['pureTests']={'logs':bundle('pure-tests',pure),'suiteCounts':counts,'suites':len(counts),'assertions':sum(counts.values()),'pythonTests':int(pythonCount[1]) if pythonCount else None,'exit':pure.get('exit.txt','missing').strip(),'meaning':'offline regression only, not native acceptance'}
 supplement=ROOT/'work/visible-facts-validation23'
 if supplement.is_dir():
  extra={p.name:p.read_bytes().decode('utf-8') for p in sorted(supplement.iterdir()) if p.is_file() and p.suffix in ('.log','.json','.txt')}
  index['supplementalTests']={'logs':bundle('supplemental-tests',extra),'meaning':'separate later host tests (116 offers/39 projection); NOT a rerun of the entire 22-suite aggregate'}
 index['batchScripts']=bundle('batch-scripts',{p.name:p.read_bytes().decode('utf-8') for p in (ROOT/'work').glob('run-observation*.sh') if p.name in ('run-observation19.sh','run-observation20.sh','run-observation21.sh','run-observation22.sh','run-observation23.sh')})
 index['archiveComplete']=not index['pending']
 if a.write:(HERE/'manifest.json').write_bytes(json.dumps(index,ensure_ascii=False,indent=2).encode()+b'\n')
 print(json.dumps({'runs':len(index['runs']),'pending':index['pending'],'sourceDeltas':{k:v['changedSourceCount'] for k,v in index['runs'].items()},'write':a.write}))
if __name__=='__main__':main()
