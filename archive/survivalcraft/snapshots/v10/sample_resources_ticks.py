import os,time,json,pathlib,sys
rows=[];hz=os.sysconf('SC_CLK_TCK'); page=os.sysconf('SC_PAGE_SIZE');root=pathlib.Path(__file__).parent
for i in range(31):
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
  row['fresh_players']=len(states);row['game_tick_seconds']=max((j['tickSeconds'] for j in states),default=None)
 rows.append(row);time.sleep(1)
(root/sys.argv[1]).write_text(json.dumps({'sample_seconds':30,'samples':rows}))
