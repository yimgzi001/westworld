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
 rows.append({'time':time.monotonic(),'processes':procs});time.sleep(1)
(root/sys.argv[1]).write_text(json.dumps({'sample_seconds':30,'samples':rows}))
