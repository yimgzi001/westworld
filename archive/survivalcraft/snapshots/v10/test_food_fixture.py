import pathlib,json,time,os
p=pathlib.Path(__file__).parent;d=p/'ipc-fixture';f=next(f for f in d.glob('*.state.json') if time.time()-f.stat().st_mtime<3);q=f.with_name(f.name.replace('.state.json','.command.json'))
def read():return json.loads(f.read_text())
def send(action,**kw):
 c=dict(Id=time.time_ns(),Action=action,Seconds=.5,**kw);t=q.with_suffix('.tmp');t.write_text(json.dumps(c));os.replace(t,q);deadline=time.time()+5
 while time.time()<deadline:
  s=read()
  if s['lastCommand']==c['Id']:break
  time.sleep(.1)
 return c
before=read();send('testfoodfixture');time.sleep(.4);fixture=read();slot=next(s for s in fixture['state']['slots'] if s['count']>0 and s['nutrition']>0)
c=send('eat',Slot=slot['slot'],ExpectedValue=slot['value'],ExpectedCount=slot['count']);deadline=time.time()+5
while time.time()<deadline:
 if (d/'eat-results.jsonl').exists():break
 time.sleep(.1)
after=read();result=json.loads((d/'eat-results.jsonl').read_text().splitlines()[-1]);out={'label':'INJECTED TEST FIXTURE ONLY, NOT NATURAL FOOD ACQUISITION','fixture':json.loads((d/'TEST-FOOD-FIXTURE.json').read_text()),'transaction':result,'food_after_observed':after['state']['vitals']['food'],'health':after['state']['health'],'air':after['state']['air'],'real_world_modified':False};(p/'food-fixture-positive.json').write_text(json.dumps(out,indent=2));print(json.dumps(out))
