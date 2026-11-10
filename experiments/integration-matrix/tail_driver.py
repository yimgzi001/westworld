#!/usr/bin/env python3
"""Artificial two-step original tail; public offered tools only. Never a model."""
import argparse,importlib.util,json,pathlib
BASE=pathlib.Path(__file__).resolve().parents[1]/'direct-move-prefetch-evidence/driver.py'
def load_driver():
 s=importlib.util.spec_from_file_location('native_tail_protocol',BASE);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m

def decision(driver,payload,ordinal,case):
 if ordinal==1:
  steps=[{'tool':'act_MoveTo','arguments':{'bearing':0,'distance':4,'range':.25,'timeout':4}},
         {'tool':'act_FaceTowards','arguments':{'bearing':180 if case=='direct_tail_failure' else 90,'timeout':.1 if case=='direct_tail_failure' else 4}}]
 else:steps=[{'tool':'act_Wait','arguments':{'duration':.2}}]
 for step in steps:driver.validate_arguments(driver.schema_for(payload,step['tool']),step['arguments'])
 return {'goal':'Artificial original MoveTo plus FaceTowards continuation; not autonomous policy','steps':steps}

def run(directory,case):
 if case not in ('tail_early','tail_late','tail_failure'):raise ValueError('tail_case')
 driver=load_driver();driver.CASES['direct_tail_failure']={'delay':2,'retiredDelivery':True}
 driver.decision=lambda p,n,c:decision(driver,p,n,c)
 return driver.run(directory,'direct_early' if case=='tail_early' else 'direct_late' if case=='tail_late' else 'direct_tail_failure')
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('directory');p.add_argument('--case',required=True,choices=['tail_early','tail_late','tail_failure']);a=p.parse_args();print(json.dumps(run(a.directory,a.case)))
