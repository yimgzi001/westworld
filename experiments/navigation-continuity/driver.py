#!/usr/bin/env python3
"""Bounded deterministic JSON fixture driver; no game introspection/network/model."""
import argparse,json,os,pathlib,time,uuid
IDS=('sessionId','actorId','observationId','requestId')
def read_request(path):
 if path.is_symlink() or path.parent.is_symlink():raise ValueError('symlink')
 with path.open('rb') as f:raw=f.read(262145)
 if len(raw)>262144:raise ValueError('request_bound')
 p=json.loads(raw)
 if p.get('protocol')!='westworld-tool-file-v1':raise ValueError('protocol')
 for k in IDS:
  if not isinstance(p.get(k),str) or uuid.UUID(p[k]).int==0:raise ValueError('identity')
 return p

def run(directory,delay,clock=time.monotonic,sleep=time.sleep):
 if delay not in (1,3,8):raise ValueError('delay')
 root=pathlib.Path(directory).absolute()
 if any(p.is_symlink() for p in (root,*root.parents)) or not root.is_dir():raise ValueError('root')
 # A second start must not replay requests from a previous driver process.
 with (root/'continuity-driver-started.json').open('x') as f:json.dump({'delaySeconds':delay,'maximumResponses':2},f)
 end=clock()+65;seen=set();sent=0;ids=set()
 def expired():return clock()>=end or (root/'exit.txt').exists()
 while not expired() and len(seen)<2:
  control=root/'control'
  if control.is_symlink():raise ValueError('control_symlink')
  requests=sorted(control.glob('*/request.json'))
  for request in requests:
   if expired() or len(seen)>=2:break
   if request in seen:continue
   seen.add(request);p=read_request(request)
   if p['requestId'] in ids:raise ValueError('duplicate_identity')
   ids.add(p['requestId']);ordinal=len(seen);ready=clock()+(0 if ordinal==1 else delay)
   while clock()<ready and not expired() and not (request.parent/'canceled.json').exists():sleep(min(.05,ready-clock(),max(0,end-clock())))
   if expired():break
   if (request.parent/'canceled.json').exists():continue
   response={k:p[k] for k in IDS};response['decision']={'goal':'Controlled 3m then 1m continuity fixture; not model policy','steps':[{'tool':'host_navigate_local','arguments':{'bearing':90,'distance':3 if ordinal==1 else 1,'max_seconds':12}}]}
   output=request.parent/'response.json';temporary=request.parent/'continuity-response.tmp'
   if output.exists():raise ValueError('response_exists')
   with temporary.open('x') as f:json.dump(response,f);f.flush();os.fsync(f.fileno())
   try:
    if expired() or (request.parent/'canceled.json').exists():continue
    # Hard-link creates destination only if absent, never replaces another response.
    os.link(temporary,output);sent+=1
   finally:temporary.unlink()
  if not expired() and len(seen)<2:sleep(min(.05,max(0,end-clock())))
 result={'receivedRequests':len(seen),'responses':sent,'delaySeconds':delay,'elapsedSeconds':65-(end-clock()),'reason':'host_exit' if (root/'exit.txt').exists() else 'request_limit' if len(seen)>=2 else 'deadline'}
 with (root/'continuity-driver-result.json').open('x') as f:json.dump(result,f)
 return result
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory');p.add_argument('--delay',type=int,choices=[1,3,8],required=True);a=p.parse_args();print(json.dumps(run(a.directory,a.delay)))
