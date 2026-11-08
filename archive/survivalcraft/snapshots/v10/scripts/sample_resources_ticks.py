import os,time,json,pathlib,sys
rows=[];hz=os.sysconf('SC_CLK_TCK'); page=os.sysconf('SC_PAGE_SIZE');root=pathlib.Path(__file__).parent
duration=int(os.environ.get('SC_SAMPLE_SECONDS','30'))
for i in range(duration+1):
 procs=[]
 for p in pathlib.Path('/proc').glob('[0-9]*'):
  try:
   cmd=(p/'cmdline').read_bytes().split(b'\0')
   if not cmd or not cmd[0].endswith(b'dotnet') or b'Survivalcraft.dll' not in cmd:continue
   st=(p/'stat').read_text().split();procs.append({'pid':int(p.name),'cpu_seconds':(int(st[13])+int(st[14]))/hz,'rss_bytes':int(st[23])*page})
  except (OSError,ValueError):pass
 row={'time':time.monotonic(),'processes':procs}
 if os.environ.get('SC_SAMPLE_IPC'):
  states=[]
  for f in pathlib.Path(os.environ['SC_SAMPLE_IPC']).glob('*.state.json'):
   try:
    if time.time()-f.stat().st_mtime<3:states.append(json.loads(f.read_text()))
   except (OSError,ValueError):pass
  row['fresh_players']=len(states);row['alive_players']=sum(j['state'].get('health',0)>0 for j in states);row['min_health']=min((j['state'].get('health',0) for j in states),default=None);row['game_tick_seconds']=max((j['tickSeconds'] for j in states),default=None)
 rows.append(row);time.sleep(1)
(root/sys.argv[1]).write_text(json.dumps({'sample_seconds':duration,'samples':rows}))
