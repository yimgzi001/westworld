import pathlib,json,time,subprocess,os,sys
p=pathlib.Path(__file__).resolve().parent;d=p/'ipc-bot'
for n in [1,2,4]:
 (d/'target-count').write_text(str(n));deadline=time.monotonic()+90
 while time.monotonic()<deadline:
  try:
   roster=json.loads((d/'roster.json').read_text());ready=[x for x in roster['players'] if x['main'] and x['ready'] and x['health'] and x['health']>0]
   if len(ready)==n:break
  except (OSError,ValueError):pass
  time.sleep(.5)
 else:
  (p/'cohort-progress.json').write_text(json.dumps({'phase':n,'status':'blocked_waiting_ready'}));sys.exit(1)
 time.sleep(5)
 (p/'cohort-progress.json').write_text(json.dumps({'phase':n,'status':'sampling','seconds':120}))
 env=dict(os.environ,SC_SAMPLE_IPC=str(d),SC_SAMPLE_SECONDS='120')
 subprocess.run([sys.executable,str(p/'sample_resources_ticks.py'),f'cohort-{n}-resources.json'],env=env,check=True)
 (p/'cohort-progress.json').write_text(json.dumps({'phase':n,'status':'completed'}))
