#!/usr/bin/env python3
"""Checkpoint audit only; never executes games, HTTP requests, or replay."""
import argparse, contextlib, datetime, hashlib, json, os, pathlib, re, stat, tempfile
SCHEMA='westworld-batch-checkpoint-v1'
class RecoveryError(Exception): pass

def label(v):
    if not isinstance(v,str) or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]{0,79}',v): raise RecoveryError('invalid_label')
    return v

def root_path(v):
    p=pathlib.Path(os.path.abspath(v))
    if any(n.is_symlink() for n in (p,*p.parents)): raise RecoveryError('symlink_rejected')
    if not p.is_dir(): raise RecoveryError('root_missing')
    return p

def relative(root,v):
    if not isinstance(v,str) or not v or '\\' in v or pathlib.PurePosixPath(v).is_absolute() or any(x in ('','.','..') for x in v.split('/')): raise RecoveryError('invalid_relative_path')
    p=root
    for part in v.split('/'):
        p/=part
        if p.is_symlink(): raise RecoveryError('symlink_rejected')
    return p

def digest(root,v):
    p=relative(root,v)
    fd=os.open(p,os.O_RDONLY|getattr(os,'O_NOFOLLOW',0)|getattr(os,'O_NONBLOCK',0))
    with os.fdopen(fd,'rb') as f:
        before=os.fstat(f.fileno())
        if not stat.S_ISREG(before.st_mode) or before.st_size>256*1024*1024: raise RecoveryError('file_bound_or_type')
        h=hashlib.sha256();size=0
        while block:=f.read(65536):
            size+=len(block)
            if size>256*1024*1024: raise RecoveryError('file_bound_or_type')
            h.update(block)
        after=os.fstat(f.fileno())
        if (before.st_size,before.st_mtime_ns)!=(after.st_size,after.st_mtime_ns): raise RecoveryError('changed_during_hash')
        return h.hexdigest()

def atomic_json(path,record):
    fd,name=tempfile.mkstemp(prefix='.checkpoint-',dir=path.parent)
    try:
        with os.fdopen(fd,'w',encoding='utf-8') as f:
            json.dump(record,f,sort_keys=True,separators=(',',':'));f.flush();os.fsync(f.fileno())
        os.replace(name,path)
        fd=os.open(path.parent,os.O_RDONLY)
        try: os.fsync(fd)
        finally: os.close(fd)
    finally:
        if os.path.exists(name):os.unlink(name)

@contextlib.contextmanager
def locked(root,case):
    label(case);d=relative(root,'.batch-checkpoints');d.mkdir(exist_ok=True)
    lock=relative(root,'.batch-checkpoints/'+case+'.lock')
    try: fd=os.open(lock,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
    except FileExistsError: raise RecoveryError('locked_manual_review_required') from None
    try: yield relative(root,'.batch-checkpoints/'+case+'.json')
    finally: os.close(fd);lock.unlink()

def paths(v):
    if not isinstance(v,list) or not 1<=len(v)<=64 or any(not isinstance(p,str) for p in v) or len(set(v))!=len(v):raise RecoveryError('path_list_bound')

def load(path):
    if path.is_symlink() or path.stat().st_size>65536:raise RecoveryError('checkpoint_bound')
    def unique(items):
        out={}
        for k,v in items:
            if k in out:raise RecoveryError('duplicate_key')
            out[k]=v
        return out
    r=json.loads(path.read_text(),object_pairs_hook=unique)
    if not isinstance(r,dict) or set(r)!={'schema','case','stage','environment','state','startedUtc','finishedUtc','sources','results'} or r['schema']!=SCHEMA:raise RecoveryError('schema')
    for k in ('case','stage','environment'):label(r[k])
    if r['state'] not in ('running','passed','failed'):raise RecoveryError('state')
    if not isinstance(r['startedUtc'],str) or not (r['finishedUtc'] is None if r['state']=='running' else isinstance(r['finishedUtc'],str)):raise RecoveryError('time')
    for key in ('sources','results'):
        if not isinstance(r[key],dict):raise RecoveryError('files')
        paths(list(r[key]))
        for h in r[key].values():
            if h is None and key=='results' and r['state']=='running':continue
            if not isinstance(h,str) or not re.fullmatch('[0-9a-f]{64}',h):raise RecoveryError('hash')
    return r

def now():return datetime.datetime.now(datetime.timezone.utc).isoformat()
def begin(root,case,stage,environment,sources,results):
    root=root_path(root);label(case);label(stage);label(environment);paths(sources);paths(results)
    if set(sources)&set(results):raise RecoveryError('source_result_overlap')
    for p in sources+results:
        relative(root,p)
        if p.startswith('.batch-checkpoints/'):raise RecoveryError('metadata_not_artifact')
    with locked(root,case) as path:
        if path.exists():raise RecoveryError('case_exists_no_replay')
        r=dict(schema=SCHEMA,case=case,stage=stage,environment=environment,state='running',startedUtc=now(),finishedUtc=None,sources={p:digest(root,p) for p in sources},results={p:None for p in results})
        atomic_json(path,r)
    return r

def finish(root,case,outcome):
    root=root_path(root)
    if outcome not in ('passed','failed'):raise RecoveryError('invalid_outcome')
    with locked(root,case) as path:
        r=load(path)
        if r['case']!=case or r['state']!='running':raise RecoveryError('not_running')
        if any(digest(root,p)!=h for p,h in r['sources'].items()):raise RecoveryError('source_changed')
        hashes={p:digest(root,p) for p in r['results']}
        r.update(state=outcome,finishedUtc=now(),results=hashes);atomic_json(path,r)
    return r

def inspect(root,case,environment=None):
    root=root_path(root);label(case);r=load(relative(root,'.batch-checkpoints/'+case+'.json'))
    if r['case']!=case:raise RecoveryError('case_mismatch')
    issues=[]
    if environment is not None and label(environment)!=r['environment']:issues.append({'kind':'environment_mismatch'})
    for group in ('sources','results'):
        for path,expected in r[group].items():
            try:
                actual=digest(root,path)
                if expected is None:kind='uncommitted_result'
                elif actual!=expected:kind='hash_changed'
                else:continue
            except FileNotFoundError:kind='missing'
            except (RecoveryError,OSError):kind='unsafe_or_unreadable'
            issues.append({'group':group,'path':path,'kind':kind})
    state='interrupted_unknown' if r['state']=='running' else ('integrity_mismatch' if issues else 'verified_record')
    return dict(schema=SCHEMA,case=case,stage=r['stage'],environment=r['environment'],declaredOutcome=r['state'],recoveryStatus=state,issues=issues,automaticReplay=False)

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',required=True);sub=p.add_subparsers(dest='command',required=True)
    b=sub.add_parser('begin');b.add_argument('--case',required=True);b.add_argument('--stage',required=True);b.add_argument('--environment',required=True);b.add_argument('--source',action='append',required=True);b.add_argument('--result',action='append',required=True)
    f=sub.add_parser('finish');f.add_argument('--case',required=True);f.add_argument('--outcome',required=True,choices=['passed','failed'])
    i=sub.add_parser('inspect');i.add_argument('--case',required=True);i.add_argument('--environment')
    a=p.parse_args()
    if a.command=='begin':r=begin(a.root,a.case,a.stage,a.environment,a.source,a.result)
    elif a.command=='finish':r=finish(a.root,a.case,a.outcome)
    else:r=inspect(a.root,a.case,a.environment)
    print(json.dumps(r,sort_keys=True))
if __name__=='__main__':
    try:main()
    except Exception as e:
        print(json.dumps({'error':str(e) if isinstance(e,RecoveryError) else type(e).__name__}));raise SystemExit(1)
