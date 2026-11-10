#!/usr/bin/env python3
"""Compare already completed isolated runs; no execution or API."""
import argparse,json,pathlib

def compare(directories):
 runs=[]
 for directory in directories:
  p=pathlib.Path(directory);summary=json.loads((p/'summary.json').read_text());resources=json.loads((p/'resources.json').read_text())
  runs.append({'run':p.name,'actualActors':summary.get('actorCount'),'passed':summary.get('passed') is True and resources.get('exitCode')==0,'wallSeconds':resources.get('wallSeconds'),'userCpuSeconds':resources.get('userCpuSeconds'),'systemCpuSeconds':resources.get('systemCpuSeconds'),'peakRssMiB':resources['peakRssKiB']/1024,'gameTimeSpan':summary.get('gameTimeSpan')})
 return {'runs':runs,'allPassed':bool(runs) and all(r['passed'] for r in runs),'scope':'Entire game process tree including startup; independently run short scenes, not steady-state or per-actor cost. No 10/50-actor extrapolation.','modelApi':False}
def main():
 ap=argparse.ArgumentParser();ap.add_argument('directories',nargs='+');a=ap.parse_args();r=compare(a.directories);print(json.dumps(r,ensure_ascii=False,indent=2));raise SystemExit(0 if r['allPassed'] else 1)
if __name__=='__main__':main()
