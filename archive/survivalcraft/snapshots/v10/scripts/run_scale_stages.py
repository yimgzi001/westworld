import pathlib,json,time,subprocess,os,sys
p=pathlib.Path(__file__).resolve().parent;d=p/'ipc-bot'
for n in [8,16,32,50]:
 (d/'target-count').write_text(str(n));deadline=time.monotonic()+120
 while time.monotonic()<deadline:
  try:
   roster=json.loads((d/'roster.json').read_text());ready=[x for x in roster['players'] if x['main'] and x['ready'] and x['health'] and x['health']>0]
   if len(ready)==n:break
  except (OSError,ValueError):pass
  time.sleep(.5)
 else:
  (p/'scale-progress.json').write_text(json.dumps({'phase':n,'status':'blocked_waiting_ready'}));sys.exit(1)
 time.sleep(5)
 (p/'scale-progress.json').write_text(json.dumps({'phase':n,'status':'sampling','seconds':120}))
 env=dict(os.environ,SC_SAMPLE_IPC=str(d),SC_SAMPLE_SECONDS='120')
 subprocess.run([sys.executable,str(p/'sample_resources_ticks.py'),f'scale-{n}-resources.json'],env=env,check=True)
 rows=json.loads((p/f'scale-{n}-resources.json').read_text())['samples']
 a,b=rows[0],rows[-1];ratio=(b['game_tick_seconds']-a['game_tick_seconds'])/(b['time']-a['time']) if a['game_tick_seconds'] is not None and b['game_tick_seconds'] is not None else 0
 safe=ratio>=.8 and all(r['fresh_players']==n and r['alive_players']==n and len(r['processes'])==1 for r in rows) and max(sum(q['rss_bytes'] for q in r['processes']) for r in rows)<4*1024**3
 (p/'scale-progress.json').write_text(json.dumps({'phase':n,'status':'completed' if safe else 'stopped_at_safety_gate','speed_ratio':ratio}))
 if not safe:sys.exit(2)
