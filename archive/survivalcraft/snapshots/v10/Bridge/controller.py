"""File-transport controller. A real LLM can choose these calls; no LLM called here."""
import argparse,json,pathlib,time,os

def issue(directory,player,action,x=0,y=0,z=0,seconds=.2):
    if action not in {'move','dig','place','stop'}: raise ValueError('Unknown action')
    if not 0 <= seconds <= .5: raise ValueError('Duration must be 0..0.5 seconds')
    # Player ids must come from observed filenames. Reject traversal.
    if not player or any(c not in '0123456789abcdefABCDEF-' for c in player): raise ValueError('Invalid player GUID')
    root=pathlib.Path(directory); root.mkdir(parents=True,exist_ok=True)
    target=root/(player+'.command.json'); tmp=target.with_suffix('.tmp')
    payload={'Id':time.time_ns(),'Action':action,'X':x,'Y':y,'Z':z,'Seconds':seconds}
    tmp.write_text(json.dumps(payload,allow_nan=False),encoding='utf-8'); os.replace(tmp,target)
    return payload

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('directory');p.add_argument('player');p.add_argument('action',choices=['move','dig','place','stop']);p.add_argument('--x',type=float,default=0);p.add_argument('--y',type=float,default=0);p.add_argument('--z',type=float,default=0);p.add_argument('--seconds',type=float,default=.2)
    a=p.parse_args();print(json.dumps(issue(a.directory,a.player,a.action,a.x,a.y,a.z,a.seconds)))
