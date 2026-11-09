#!/usr/bin/env python3
"""Read-only sampling of one explicitly supplied local PID. No process launch/kill."""
import argparse,json,pathlib,time,os

def parse_stat(raw):
    # comm may contain spaces/parentheses; fixed fields begin after final ')'.
    end=raw.rfind(')');start=raw.find('(')
    if start<1 or end<start:raise ValueError('stat_shape')
    pid=int(raw[:start].strip());fields=raw[end+1:].split()
    if len(fields)<20:raise ValueError('stat_fields')
    user,system,starttime=map(int,(fields[11],fields[12],fields[19]))
    if min(pid,user,system,starttime)<0:raise ValueError('stat_negative')
    return pid,user+system,starttime

def parse_rss(raw):
    for line in raw.splitlines():
        if line.startswith('VmRSS:'):
            parts=line.split()
            if len(parts)!=3 or parts[2]!='kB' or int(parts[1])<0:raise ValueError('rss_shape')
            return int(parts[1])*1024
    return None

def read_sample(pid,proc=pathlib.Path('/proc')):
    base=proc/str(pid)
    # Two stat reads bracket status, so reuse cannot mix another process's RSS.
    first=parse_stat((base/'stat').read_text());rss=parse_rss((base/'status').read_text());last=parse_stat((base/'stat').read_text())
    if first[0]!=pid or last[0]!=pid or first[2]!=last[2]:raise ValueError('pid_reused_during_read')
    return {'starttime':last[2],'cpuTicks':last[1],'rssBytes':rss}

def collect(pid,duration,interval=.2,reader=read_sample,clock=time.monotonic,sleep=time.sleep,hz=None):
    if type(pid)!=int or pid<=0:raise ValueError('pid_bound')
    if not 0<duration<=600 or not .05<=interval<=5:raise ValueError('sampling_bound')
    hz=os.sysconf('SC_CLK_TCK') if hz is None else hz
    if hz<=0:raise ValueError('tick_rate')
    began=clock();first=None;last=None;count=0;missingRss=0;peak=None;reason='duration_completed'
    while True:
        try:sample=reader(pid)
        except FileNotFoundError:reason='process_missing_or_exited';break
        except PermissionError:reason='permission_denied';break
        except ValueError as error:reason='pid_reused' if str(error)=='pid_reused_during_read' else 'sample_unavailable';break
        except OSError:reason='sample_unavailable';break
        stamp=clock()
        if first is not None and sample['starttime']!=first[1]['starttime']:reason='pid_reused';break
        if last is not None and (sample['cpuTicks']<last[1]['cpuTicks'] or stamp<last[0]):reason='counter_or_clock_regression';break
        count+=1
        if first is None:first=(stamp,sample)
        last=(stamp,sample)
        if sample['rssBytes'] is None:missingRss+=1
        else:peak=sample['rssBytes'] if peak is None else max(peak,sample['rssBytes'])
        elapsed=stamp-began
        if elapsed>=duration:break
        sleep(min(interval,duration-elapsed))
    wall=clock()-began
    sampledWall=last[0]-first[0] if first and last else None
    cpu=(last[1]['cpuTicks']-first[1]['cpuTicks'])/hz if count>=2 else None
    return {'schema':'westworld-process-metrics-v1','pid':pid,'starttimeTicks':first[1]['starttime'] if first else None,
            'scope':'local_process_only_not_cloud_model_server','outcome':reason,'samples':count,'wallSeconds':wall,
            'sampledWallSeconds':sampledWall,'sampledCpuSeconds':cpu,
            'averageCoresDuringSampledInterval':cpu/sampledWall if cpu is not None and sampledWall>0 else None,
            'peakSampledRssBytes':peak,'rssMissingSamples':missingRss,'fullIntervalKnown':reason=='duration_completed' and count>=2 and missingRss==0,
            'unsampledTailUnknown':reason!='duration_completed'}

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--pid',type=int,required=True);p.add_argument('--seconds',type=float,required=True);p.add_argument('--interval',type=float,default=.2);a=p.parse_args()
    print(json.dumps(collect(a.pid,a.seconds,a.interval),sort_keys=True))
if __name__=='__main__':
    try:main()
    except Exception as e:print(json.dumps({'error':type(e).__name__}));raise SystemExit(1)
