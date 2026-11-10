#!/usr/bin/env python3
"""Bounded local host launcher; incremental event tail, Linux /proc resource samples."""
import argparse,datetime,hashlib,json,os,pathlib,resource,subprocess,time

def utc_seconds(s):return datetime.datetime.fromisoformat(s.replace('Z','+00:00')).timestamp()
def proc_snapshot(root_pid,known):
 rows={}
 for p in pathlib.Path('/proc').iterdir():
  if not p.name.isdigit():continue
  try:
   stat=(p/'stat').read_text(); v=stat[stat.rindex(')')+2:].split()
   rows[int(p.name)]={'parent':int(v[1]),'cpu':(int(v[11])+int(v[12]))/os.sysconf('SC_CLK_TCK'),'rss':int(v[21])*os.sysconf('SC_PAGE_SIZE')/1024,'start':v[19]}
  except (OSError,ValueError,IndexError):pass
 selected={root_pid};changed=True
 while changed:
  new={pid for pid,r in rows.items() if r['parent'] in selected};changed=not new.issubset(selected);selected|=new
 for pid in selected:
  if pid in rows:known[(pid,rows[pid]['start'])]=rows[pid]['cpu']
 return {'wallEpoch':time.time(),'cpuSeconds':sum(known.values()),'rssKiB':sum(rows[p]['rss'] for p in selected if p in rows),'liveProcesses':len(selected & rows.keys())}
def window_summary(events,samples):
 starts=[e for e in events if e.get('phase')=='steady_start'];ends=[e for e in events if e.get('phase')=='steady_end']
 if len(starts)!=1 or len(ends)!=1:return {'available':False,'reason':'missing unique steady boundaries'}
 a,b=starts[0],ends[0]
 try:ta,tb=utc_seconds(a['wallUtc']),utc_seconds(b['wallUtc'])
 except (ValueError,KeyError):return {'available':False,'reason':'invalid host UTC boundaries'}
 if tb<=ta or not samples:return {'available':False,'reason':'invalid span'}
 x=min(samples,key=lambda s:abs(s['wallEpoch']-ta));y=min(samples,key=lambda s:abs(s['wallEpoch']-tb));inside=[s for s in samples if ta<=s['wallEpoch']<=tb]
 offsets=[x['wallEpoch']-ta,y['wallEpoch']-tb]; span=y['wallEpoch']-x['wallEpoch']
 if span<=0 or not inside:return {'available':False,'reason':'no window samples'}
 cpu=y['cpuSeconds']-x['cpuSeconds'];game=b['gameTime']-a['gameTime']
 return {'available':max(map(abs,offsets))<=.5,'boundaryOffsetsSeconds':offsets,'hostWallSeconds':tb-ta,'sampleWallSeconds':span,'gameSeconds':game,'gameToWallRatio':game/(tb-ta),'cpuSeconds':cpu,'averageCpuCores':cpu/span,'rssMeanKiB':sum(s['rssKiB'] for s in inside)/len(inside),'rssPeakKiB':max(s['rssKiB'] for s in inside),'sampleCount':len(inside),'scope':'sum live process-tree RSS; CPU retained last observations of exited children; not per-agent allocation','accuracy':'100ms target sampling; boundary offsets reported; short-lived children between samples may be missed'}
def main():
 ap=argparse.ArgumentParser();ap.add_argument('--sdk',required=True);ap.add_argument('--mode',choices=['matrix_scale47','matrix_recovery47'],required=True);ap.add_argument('--env',required=True);a=ap.parse_args()
 for name,want in json.loads(pathlib.Path('installed-hashes.json').read_text()).items():
  p=pathlib.Path(name);assert p.is_file() and hashlib.sha256(p.read_bytes()).hexdigest()==want,name
 assert not any(pathlib.Path(p).exists() for p in ['doc','Mods','tool-agent.jsonl','console.log'])
 env=dict(os.environ,**json.loads(a.env));samples=[];events=[];known={};offset=0;partial='';start=time.monotonic()
 with open('console.log','w') as output:
  proc=subprocess.Popen(['timeout','--signal=TERM','--kill-after=5s','140',a.sdk,'ToolAgentGate.dll',a.mode,'file'],env=env,stdout=output,stderr=subprocess.STDOUT)
  while True:
   samples.append(proc_snapshot(proc.pid,known))
   p=pathlib.Path('tool-agent.jsonl')
   if p.exists():
    with p.open() as f:f.seek(offset);new=f.read();offset=f.tell()
    lines=(partial+new).split('\n');partial=lines.pop()
    for line in lines:
     try:r=json.loads(line)
     except json.JSONDecodeError:continue
     if r.get('kind')=='batch47_window':events.append(r)
   if proc.poll() is not None:break
   time.sleep(.1)
 usage=resource.getrusage(resource.RUSAGE_CHILDREN)
 result={'total':{'wallSeconds':time.monotonic()-start,'userCpuSeconds':usage.ru_utime,'systemCpuSeconds':usage.ru_stime,'peakRssKiB':usage.ru_maxrss,'scope':'whole bounded subprocess tree; rusage maxRSS not simultaneous sum'},'steady':window_summary(events,samples),'exitCode':proc.returncode,'modelApi':False}
 pathlib.Path('resource-samples.json').write_text(json.dumps(samples));pathlib.Path('resources.json').write_text(json.dumps(result,indent=2));pathlib.Path('exit.txt').write_text(str(proc.returncode)+'\n')
 raise SystemExit(proc.returncode)
if __name__=='__main__':main()
