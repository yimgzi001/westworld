import pathlib,json,statistics
p=pathlib.Path(__file__).resolve().parent
out=[]
for n in [8,16,32,50]:
 f=p/f'scale-{n}-resources.json'
 if not f.exists():continue
 rows=json.loads(f.read_text())['samples'];a,b=rows[0],rows[-1];dt=b['time']-a['time'];rss=[sum(q['rss_bytes'] for q in r['processes'])/2**20 for r in rows]
 out.append({'roles':n,'seconds':dt,'rss_mean_mib':statistics.mean(rss),'rss_peak_mib':max(rss),'cpu_percent':100*(sum(q['cpu_seconds'] for q in b['processes'])-sum(q['cpu_seconds'] for q in a['processes']))/dt,'speed_ratio':(b['game_tick_seconds']-a['game_tick_seconds'])/dt,'fresh_counts':sorted({r['fresh_players'] for r in rows}),'alive_counts':sorted({r['alive_players'] for r in rows}),'min_health':min(r['min_health'] for r in rows if r['min_health'] is not None),'pids':sorted({q['pid'] for r in rows for q in r['processes']})})
print(json.dumps(out,indent=2))
