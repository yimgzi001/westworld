#!/usr/bin/env python3
"""Reuse reviewed staging; prepare a fresh natural world, never launch or call API."""
import argparse,importlib.util,json,pathlib,re,shutil
BASE=pathlib.Path(__file__).resolve().parents[1]/'food-acquisition-evidence/stage.py'
def stage(root,label,build_output,content,seed,controller='deterministic'):
 if not isinstance(seed,str) or not re.fullmatch(r'[A-Za-z0-9_-]{1,64}',seed):raise ValueError('seed')
 spec=importlib.util.spec_from_file_location('food_stage_for_natural',BASE);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
 root=m.safe(root);driver=m.safe(root/'experiments/natural-resource-evidence/deterministic_driver.py')
 if not driver.is_file():raise FileNotFoundError('natural driver')
 m.SOURCE_FOLDERS=(*m.SOURCE_FOLDERS,'experiments/natural-resource-evidence')
 m.SOURCE_FILES=(*m.SOURCE_FILES,'game/Survivalcraft/Subsystem/SubsystemTerrain.cs','game/Survivalcraft/Component/ComponentLoot.cs','experiments/natural-resource-evidence/synthetic-schema.json')
 dest=m.stage(root,label,build_output,content,controller)
 run=dest/'run.sh';text=run.read_text();needle='ToolAgentGate.dll food_drop file';assert text.count(needle)==1
 run.write_text(text.replace(needle,'ToolAgentGate.dll natural file '+seed))
 shutil.copy2(driver,dest/'deterministic_driver.py')
 meta=json.loads((dest/'stage.json').read_text());meta.update(schema='natural-resource-stage-v1',mode='natural file '+seed,seed=seed,maximumDriverRequests=8,maximumDriverSeconds=180,fixture='none; newly generated world; production host must independently confirm no injection')
 (dest/'stage.json').write_text(json.dumps(meta,indent=2))
 hashes={p.relative_to(dest).as_posix():m.sha(p) for p in dest.rglob('*') if p.is_file() and p.name!='installed-hashes.json'}
 (dest/'installed-hashes.json').write_text(json.dumps(hashes,sort_keys=True,indent=2));return dest
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',type=pathlib.Path,default=pathlib.Path(__file__).resolve().parents[2]);p.add_argument('--label',required=True);p.add_argument('--build-output',type=pathlib.Path,required=True);p.add_argument('--content',type=pathlib.Path,required=True);p.add_argument('--seed',required=True);p.add_argument('--controller',choices=['deterministic','model'],default='deterministic');a=p.parse_args();print(stage(a.root,a.label,a.build_output,a.content,a.seed,a.controller))
