#!/usr/bin/env python3
"""Read existing matrix logs only. Does not run tests or contact services."""
import argparse,json,pathlib

def summarize(rows,mode,require_social_menu=False):
 failures=[r for r in rows if r.get('kind') in ('matrix40_assert','matrix40_assertion') and r.get('passed') is False]
 assertions=[r for r in rows if r.get('kind') in ('matrix40_assert','matrix40_assertion')]
 terminal_kind='matrix40_summary' if mode=='matrix_single' else 'matrix40_social_result'
 result=[r for r in rows if r.get('kind')==terminal_kind]
 outer=[r for r in rows if r.get('kind')=='result']
 adapter=sorted({r['tool'] for r in rows if r.get('kind') in ('matrix40_adapter_validated','social42_adapter_validated')})
 receipt_tools=sorted({r['tool'] for r in rows if r.get('kind')=='matrix40_receipt'})
 receipt_tools+=sorted({r['receipt']['Tool'] for r in rows if r.get('kind')=='tool_receipt' and isinstance(r.get('receipt'),dict) and 'Tool' in r['receipt']})
 errors=[r.get('kind') for r in rows if r.get('kind') in ('matrix40_error','failure','fatal','cleanup_failure')]
 if require_social_menu and (not any(r.get('kind')=='matrix40_social_start' and r.get('social42') is True for r in rows) or not adapter):errors.append('required_social_menu_not_exercised')
 passed=bool(result and result[-1].get('passed') is True and outer and outer[-1].get('passed') is True and not failures and not errors)
 suite=[r.get('suite') for r in rows if r.get('kind')=='matrix41_suite']
 return dict(mode=mode,suite=suite[-1] if suite else 'baseline',passed=passed,declaredFailedAttempts=sum(r.get('kind')=='combat41_declared_attempt_failed' for r in rows),assertions=len(assertions),failedAssertions=len(failures),errors=errors,adapterValidatedTools=adapter,nativeCalledTools=sorted(set(receipt_tools)),modelApi=False,autonomousSurvival=False,note='Called tools are not all success coverage; positive effects require corresponding assertions. Social42 positive actions use menus; explicit native negative probes are separate. Baseline social remains native-only.')
def main():
 ap=argparse.ArgumentParser();ap.add_argument('--require-social-menu',action='store_true');ap.add_argument('log');ap.add_argument('--mode',choices=['matrix_single','matrix_social'],required=True);args=ap.parse_args()
 rows=[]
 for line in pathlib.Path(args.log).read_text().splitlines():
  try:row=json.loads(line)
  except json.JSONDecodeError:continue
  if isinstance(row,dict):rows.append(row)
 result=summarize(rows,args.mode,args.require_social_menu)
 print(json.dumps(result,ensure_ascii=False,indent=2))
 raise SystemExit(0 if result["passed"] else 1)
if __name__=='__main__':main()
