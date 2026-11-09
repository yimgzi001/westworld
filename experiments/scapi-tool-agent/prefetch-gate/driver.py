#!/usr/bin/env python3
"""Deterministic JSON-only fixture driver. No network, API, game access or hidden observations."""
import json,pathlib,sys,time,os
root=pathlib.Path(sys.argv[1]);case=sys.argv[2];assert case in ('early','late','failure','cancel','expired')
seen=set();deadline=time.monotonic()+65
while time.monotonic()<deadline:
 if (root/'exit.txt').exists():break
 for request in sorted((root/'control').glob('*/request.json')):
  if request in seen:continue
  seen.add(request);payload=json.loads(request.read_text());ordinal=len(seen)
  if ordinal>2:continue
  delay=0 if ordinal==1 else {'early':.2,'late':8,'failure':1.5,'cancel':1.5,'expired':1.5}[case]
  time.sleep(delay)
  response={k:payload[k] for k in ['sessionId','actorId','observationId','requestId']}
  response['decision']={'goal':'Deterministic bounded-navigation lifecycle fixture, not a model policy','steps':[{'tool':'host_navigate_local','arguments':{'bearing':90,'distance':3 if ordinal==1 else 1,'max_seconds':12}}]}
  tmp=request.parent/'response.tmp';tmp.write_text(json.dumps(response));os.replace(tmp,request.parent/'response.json')
 time.sleep(.05)
