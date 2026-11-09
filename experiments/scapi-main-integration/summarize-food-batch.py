#!/usr/bin/env python3
"""Summarize real gate JSONL; never turns a missing/failed run into a pass.
Usage: python summarize-food-batch.py WORKDIR OUTPUT_JSON
Worlds and console/native copyrighted resources are not copied.
"""
import json, sys, hashlib
from pathlib import Path
root, out = map(Path, sys.argv[1:])
names = ['inventory', 'terraced', 'a', 'b', 'wall', 'obstacle']
keep = {'start','initial_condition','bound','food_strategy_requested','food_strategy_selected',
        'food_selection_invalidated','model_waited','resource_observed','native_pickup_confirmed',
        'harvest_plan_result','checkpoint','restored_meal_verified','obstacle_recovery_verified',
        'bounded_search_exhausted','no_safe_route','navigation_mode_unavailable','path_metrics',
        'scenario_error','failure','fatal','result'}
report = {'scope':'native isolated food-strategy gates; natural failure remains failure','runs':[]}
for name in names:
    directory=root/f'food-strategy-{name}-01'; path=directory/'integration.jsonl'
    run={'name':name,'present':path.exists()}
    if path.exists():
        raw=path.read_bytes(); records=[json.loads(line) for line in raw.decode().splitlines() if line.strip()]
        run.update(raw_sha256=hashlib.sha256(raw).hexdigest(),raw_bytes=len(raw),
                   exit=(directory/'exit.txt').read_text().strip() if (directory/'exit.txt').exists() else None,
                   reload_exit=(directory/'reload-exit.txt').read_text().strip() if (directory/'reload-exit.txt').exists() else None,
                   events=[x for x in records if x.get('kind') in keep],
                   route_outcomes=[{k:x.get(k) for k in ['routeKind','status','failure','outcome','totalHorizontalPath','netHorizontal']} for x in records if x.get('kind')=='local_route_result'],
                   animal_sighting_entries=sum(sum(r.get('Kind')==2 for r in x.get('resources',[])) for x in records if x.get('kind')=='food_environment_observed'))
        sightings=[r for x in records if x.get('kind')=='food_environment_observed' for r in x.get('resources',[]) if r.get('Kind')==2]
        run['animal_labels']=sorted({r['Risk'] for r in sightings})
        run['animal_evidence_examples']=sightings[:8]
        run['animal_count_note']='repeated sightings, not distinct animal population'
    report['runs'].append(run)
out.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(json.dumps({'runs':len(report['runs']),'bytes':out.stat().st_size}))
