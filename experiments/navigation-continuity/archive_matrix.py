#!/usr/bin/env python3
"""Archive a completed staged 9-positive/4-negative matrix. No execution or networking.
Only named evidence text + staged sources, never runtime binaries/worlds/environment.
Default invocation verifies readiness without writing. Use --write after all exits/checkpoints.
"""
import argparse,base64,gzip,hashlib,json,pathlib,re

def digest(b):return hashlib.sha256(b).hexdigest()
def encode(value):return json.dumps(value,ensure_ascii=False,sort_keys=True).encode()
def main():
 ap=argparse.ArgumentParser();ap.add_argument('--label',default='tool-route18');ap.add_argument('--write',action='store_true');a=ap.parse_args()
 if not re.fullmatch(r'tool-route[0-9]+',a.label):raise ValueError('restricted matrix label required')
 root=pathlib.Path(__file__).resolve().parents[2];out=root/'experiments/navigation-continuity/evidence'/a.label
 cases=[f'{a.label}-delay{d}-repeat{n}' for d in (1,3,8) for n in (1,2,3)]+[f'{a.label}-{c}-delay3-repeat1' for c in ('route_cancel_before','route_cancel_after','route_wall','route_support')]
 prepared=[];pending=[]
 for name in cases:
  d=root/'work'/name;cpname=name.removeprefix('tool-');cpname=re.sub(r'-(route_[a-z_]+)-delay3-repeat1$',r'-\1',cpname);cp=root/'.batch-checkpoints'/f'{cpname}.json'
  if not (d/'exit.txt').exists() or not cp.exists():pending.append(name);continue
  checkpoint=json.loads(cp.read_text())
  if not checkpoint.get('finishedUtc'):pending.append(name+':checkpoint_unfinished');continue
  files={}
  for n in ('tool-agent.jsonl','console.log','exit.txt','driver-exit.txt','driver.log','driver.py','run.sh','continuity-driver-result.json','continuity-driver-started.json','stage.json'):
   p=d/n
   if p.exists():files[n]=p.read_text()
  sources={str(p.relative_to(d/'sources')):p.read_text() for p in sorted((d/'sources').rglob('*')) if p.is_file()}
  if not sources:raise ValueError('missing source snapshot '+name)
  installed=json.loads((d/'installed-hashes.json').read_text())
  for relative,h in checkpoint.get('results',{}).items():
   p=root/relative
   if not p.exists() or digest(p.read_bytes())!=h:raise ValueError('checkpoint result hash mismatch '+relative)
  prepared.append((name,files,sources,installed,checkpoint))
 print(json.dumps({'ready':len(prepared),'expected':13,'pending':pending}))
 if pending or not a.write:return
 out.mkdir(parents=True,exist_ok=True);index={};shared={}
 def bundle(kind,raw):
  if re.search(rb'sk-[A-Za-z0-9_.-]{16,}|Bearer [A-Za-z0-9_.-]{16,}',raw):raise ValueError('credential-like text in evidence')
  h=digest(raw);key=kind+'-'+h[:20]
  if key in shared:return key
  z=gzip.compress(raw,mtime=0);b=base64.b64encode(z);parts=[]
  for i in range(0,len(b),29000):
   p=out/f'{key}.gz.b64.part{i//29000+1:03}';part=b[i:i+29000];p.write_bytes(part);parts.append({'file':p.name,'bytes':len(part),'sha256':digest(part)})
  assert gzip.decompress(base64.b64decode(b))==raw
  shared[key]={'rawSHA256':h,'rawBytes':len(raw),'gzipSHA256':digest(z),'parts':parts};return key
 for name,files,sources,installed,checkpoint in prepared:
  item={'logs':bundle('logs',encode(files)),'sources':bundle('sources',encode(sources)),'installed':bundle('installed',encode(installed)),'checkpoint':checkpoint,'driverSHA256':digest(files.get('driver.py','').encode()),'exit':files['exit.txt'].strip()}
  p=out/f'{name}-manifest.json';p.write_text(json.dumps(item,ensure_ascii=False,indent=2)+'\n');index[name]={'manifest':p.name,'sha256':digest(p.read_bytes())}
 for key,item in shared.items():
  p=out/f'{key}-manifest.json';p.write_text(json.dumps(item,indent=2)+'\n');index[key]={'manifest':p.name,'sha256':digest(p.read_bytes())}
 (out/'manifest.json').write_text(json.dumps(index,indent=2)+'\n');print(json.dumps({'archived':len(prepared),'sharedSourceBundles':sum(k.startswith('sources-') for k in shared),'sharedInstalledBundles':sum(k.startswith('installed-') for k in shared),'maxFileBytes':max(p.stat().st_size for p in out.iterdir())}))
if __name__=='__main__':main()
