#!/usr/bin/env python3
"""Reviewed, deterministic file-protocol fixtures. No network, model, GUI or game reads."""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import time

# Reuse the existing bounded protocol reader rather than inventing another bridge.
_reader = Path(__file__).resolve().parents[1] / 'navigation-continuity' / 'driver.py'
_spec = importlib.util.spec_from_file_location('continuity_protocol', _reader)
_protocol = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_protocol)
IDS = _protocol.IDS
CASES = {
    'direct_early': {'delay': .2, 'retiredDelivery': False},
    'direct_late': {'delay': 8, 'retiredDelivery': False},
    'direct_failure': {'delay': 2, 'retiredDelivery': True},
    'direct_cancel': {'delay': 2, 'retiredDelivery': True},
    'direct_replace': {'delay': 2, 'retiredDelivery': True},
    'direct_stale_reference': {'delay': .2, 'retiredDelivery': False},
}


def safe(path):
    if any(p.is_symlink() for p in (path, *path.parents)):
        raise ValueError('symlink')
    return path


def schema_for(payload, tool):
    offers = payload.get('offers')
    if not isinstance(offers, list):
        raise ValueError('offers_required')
    matches = [o for o in offers if isinstance(o, dict) and o.get('Name') == tool]
    if len(matches) != 1 or not isinstance(matches[0].get('Parameters'), dict):
        raise ValueError('unique_offered_tool_required:' + tool)
    return matches[0]['Parameters']


def validate_arguments(schema, arguments):
    # Narrow fixture validation only. The host remains the authoritative schema validator.
    props = schema.get('properties', {})
    if not set(schema.get('required', [])).issubset(arguments) or not set(arguments).issubset(props):
        raise ValueError('fixture_arguments_not_offered')
    for key, value in arguments.items():
        field = props[key]
        if field.get('type') == 'number':
            if type(value) not in (int, float) or not math.isfinite(value):
                raise ValueError('finite_number_required')
            if value < field.get('minimum', -math.inf) or value > field.get('maximum', math.inf):
                raise ValueError('fixture_number_not_offered')
        elif field.get('type') == 'string':
            if not isinstance(value, str):
                raise ValueError('string_required')
        else:
            raise ValueError('unsupported_fixture_field')
        if 'enum' in field and value not in field['enum']:
            raise ValueError('fixture_value_not_offered')


def decision(payload, ordinal, case):
    if ordinal == 1:
        tool, arguments = 'act_MoveTo', {'bearing': 0, 'distance': 4, 'range': .25, 'timeout': 4}
    elif case == 'direct_stale_reference':
        tool = 'act_PickUp'
        refs = schema_for(payload, tool).get('properties', {}).get('target', {}).get('enum', [])
        if not refs or any(not isinstance(r, str) or not 0 < len(r) <= 160 for r in refs):
            raise ValueError('public_pickup_reference_required')
        arguments = {'target': sorted(refs)[0]}
    else:
        tool, arguments = 'act_Wait', {'duration': .2}
    validate_arguments(schema_for(payload, tool), arguments)
    return {'goal': 'Controlled native MoveTo successor fixture; not model policy',
            'steps': [{'tool': tool, 'arguments': arguments}]}


def run(directory, case, clock=time.monotonic, sleep=time.sleep):
    if case not in CASES:
        raise ValueError('case_not_allowed')
    root = safe(Path(directory).absolute())
    if not root.is_dir():
        raise ValueError('existing_fresh_root_required')
    config = CASES[case]
    with (root / 'direct-driver-started.json').open('x') as f:
        json.dump({'case': case, 'maximumResponses': 2, 'budgetSeconds': 65,
                   'retiredDelivery': config['retiredDelivery']}, f)
    start = clock()
    end = start + 65
    seen, request_ids, observation_ids, events = set(), set(), set(), []
    actor_scope = None

    def stopped():
        return clock() >= end or (root / 'exit.txt').exists()

    while not stopped() and len(seen) < 2:
        control = safe(root / 'control')
        for request in sorted(control.glob('*/request.json')):
            if stopped() or len(seen) >= 2:
                break
            if request in seen:
                continue
            safe(request)
            p = _protocol.read_request(request)
            scope = (p['sessionId'], p['actorId'])
            if actor_scope is not None and scope != actor_scope:
                raise ValueError('session_or_actor_replaced')
            actor_scope = scope
            if p['requestId'] in request_ids or p['observationId'] in observation_ids:
                raise ValueError('duplicate_request_or_observation')
            seen.add(request)
            request_ids.add(p['requestId'])
            observation_ids.add(p['observationId'])
            ordinal = len(seen)
            plan = decision(p, ordinal, case)
            ready = clock() + (0 if ordinal == 1 else config['delay'])
            retired_allowed = ordinal == 2 and config['retiredDelivery']
            canceled = safe(request.parent / 'canceled.json')
            while clock() < ready and not stopped() and (retired_allowed or not canceled.exists()):
                sleep(min(.05, ready - clock(), max(0, end - clock())))
            event = {k: p[k] for k in IDS}
            event.update(ordinal=ordinal, tool=plan['steps'][0]['tool'],
                         arguments=plan['steps'][0]['arguments'],
                         observedMonotonicSeconds=clock(), canceledBeforeWrite=canceled.exists(),
                         deliberatelyRetiredDelivery=retired_allowed, delivered=False)
            if stopped() or (canceled.exists() and not retired_allowed):
                event['reason'] = 'host_exit_or_budget' if stopped() else 'canceled'
                events.append(event)
                continue
            response = {k: p[k] for k in IDS}
            response['decision'] = plan
            data = json.dumps(response, separators=(',', ':')).encode('utf-8')
            temporary = safe(request.parent / 'direct-response.tmp')
            output = safe(request.parent / 'response.json')
            if output.exists():
                raise FileExistsError('response_exists')
            with temporary.open('xb') as f:
                f.write(data)
                f.flush()
                os.fsync(f.fileno())
            try:
                if not stopped() and (retired_allowed or not canceled.exists()):
                    # Never replace any other writer or replay a previous host request.
                    os.link(temporary, output)
                    event.update(delivered=True, writtenMonotonicSeconds=clock(),
                                 canceledBeforeWrite=canceled.exists(),
                                 responseSha256=hashlib.sha256(data).hexdigest())
                else:
                    event['reason'] = 'stopped_before_atomic_publish'
            finally:
                temporary.unlink()
            events.append(event)
        if not stopped() and len(seen) < 2:
            sleep(min(.05, max(0, end - clock())))
    result = {'schema': 'direct-move-driver-v1', 'case': case, 'artificialFixture': True,
              'receivedRequests': len(seen), 'responses': sum(e['delivered'] for e in events),
              'elapsedSeconds': clock() - start, 'events': events,
              'reason': 'host_exit' if (root / 'exit.txt').exists() else
                        'request_limit' if len(seen) == 2 else 'deadline'}
    with (root / 'direct-driver-result.json').open('x') as f:
        json.dump(result, f, indent=2)
    return result


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('directory')
    p.add_argument('--case', required=True, choices=CASES)
    a = p.parse_args()
    print(json.dumps(run(a.directory, a.case)))
