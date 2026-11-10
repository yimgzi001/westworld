#!/usr/bin/env python3
"""Offline N35-R verifier. Missing evidence is unknown, missed airborne window uncovered."""
import argparse
import importlib.util
import json
import math
from pathlib import Path
import uuid

_spec = importlib.util.spec_from_file_location('readiness_driver', Path(__file__).with_name('driver.py'))
driver_module = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(driver_module)


def validate(rows, driver):
    failures, unknown = [], []
    uncovered = False
    def check(ok, label):
        if not ok: failures.append(label)
    def one(kind, **matches):
        found = [r for r in rows if r.get('kind') == kind and all(r.get(k) == v for k, v in matches.items())]
        if len(found) != 1:
            unknown.append('unique_' + kind + ':' + str(matches)); raise ValueError()
        return found[0]
    def uid(value): return isinstance(value, str) and uuid.UUID(value).int != 0
    def finite(value): return type(value) in (int, float) and math.isfinite(value)
    try:
        result = one('n35_readiness_fixture_result')
        scope = (result['sessionId'], result['actorId'])
        check(all(uid(v) for v in scope), 'valid_scope')
        samples = [r for r in rows if r.get('kind') == 'n35_readiness_fixture_sample']
        check(bool(samples) and all((r['sessionId'], r['actorId']) == scope for r in samples), 'sample_scope')
        times = [r['monotonicSeconds'] for r in samples]
        check(all(finite(t) for t in times) and times == sorted(times), 'sample_time_order')
        calls = [r for r in rows if r.get('kind') == 'native_call_started']
        check([r.get('step', {}).get('Tool') for r in calls[:3]] ==
              ['act_Jump', 'host_navigation_status', 'host_navigate_local'], 'jump_query_navigation_order')
        check(len(calls) >= 3 and all(r.get('step', {}).get('Tool') == 'host_navigation_status' for r in calls[3:]), 'no_retry_or_other_write')
        jump = one('native_call_returned', tool='act_Jump')
        nav = one('native_call_returned', tool='host_navigate_local')
        jr, nd = jump['data']['runId'], nav['data']
        check(uid(jr) and jump['state'] == 'returned' and uid(nd['request_id']), 'actual_run_and_host_identity')
        terminal = one('native_terminal_observed', tool='act_Jump')
        check(terminal['runId'] == jr and terminal['sequence'] == jump['sequence'] and terminal['status'] == 'succeeded', 'same_jump_succeeded')
        check(calls[0]['monotonicSeconds'] <= terminal['monotonicSeconds'] <= calls[1]['monotonicSeconds'] <= calls[2]['monotonicSeconds'], 'jump_success_before_query_and_attempt')
        check(calls[2]['step']['Arguments'] == driver_module.NAV_ARGS, 'explicit_one_metre_four_second_navigation')
        check(all(r.get('decisionId') == calls[0].get('decisionId') for r in calls[:3]), 'one_original_plan')
        receipts = [r['receipt'] for r in rows if r.get('kind') == 'tool_receipt']
        check(all((r.get('SessionId'), r.get('ActorId')) == scope for r in receipts), 'receipt_scope')
        for item in [jump, nav]:
            check(any(r.get('Sequence') == item['sequence'] and r.get('Tool') == item['tool'] and r.get('State') == item['state'] and r.get('Data') == item['data'] for r in receipts), 'raw_receipt_binding_' + item['tool'])
        check(any((r.get('Data') or {}).get('runId') == jr and (r.get('Data') or {}).get('status') == 'succeeded' for r in receipts), 'raw_jump_success')
        failure = nd.get('start_failure_readiness')
        airborne = isinstance(failure, dict) and failure.get('Grounded') is False
        uncovered = not airborne
        if airborne:
            check(nav['state'] == 'returned' and nd['status'] == 'failed' and nd['reason'] == 'body_not_ready'
                  and nd['busy'] is False and nd['segments'] == 0 and nd['path'] == 0, 'airborne_attempt_rejected_without_segment')
            check(failure.get('Ready') is False and 'not_grounded' in failure.get('Blockers', []), 'failure_actual_grounded_blocker')
            vy = failure.get('VerticalVelocity')
            check(finite(vy) and (abs(vy) <= .1 or 'vertical_motion' in failure.get('Blockers', [])), 'vertical_motion_explained')
            check(not nd.get('runId') and nd['budget_seconds'] == 4 and finite(nd['elapsed_seconds']) and nd['elapsed_seconds'] >= 0, 'host_identity_budget_not_fake_native_run')
            check(not any(r.get('kind') in ['bounded_navigation_segment', 'observed_flat_route_started'] for r in rows), 'rejection_started_no_segment')
            check(any(r['monotonicSeconds'] >= terminal['monotonicSeconds'] and r['monotonicSeconds'] <= calls[2]['monotonicSeconds']
                      and r.get('action', {}).get('runId') == jr and r['action'].get('status') == 'succeeded'
                      and r.get('navigation', {}).get('readiness', {}).get('Grounded') is False for r in samples), 'jump_success_while_airborne')
        final_nav = result['navigation']
        check(final_nav['request_id'] == nd['request_id'] and final_nav.get('start_failure_readiness') == failure, 'failure_sample_not_rewritten_after_landing')
        check(driver_module.ready_sample(final_nav.get('readiness')) and result['nativeRunning'] is False and result['hasViewport'] is False and result['health'] >= .25, 'actual_stable_final_body')
        stable = [r for r in samples if r['monotonicSeconds'] >= calls[2]['monotonicSeconds']
                  and driver_module.ready_sample(r.get('navigation', {}).get('readiness'))
                  and r.get('nativeRunning') is False and all(finite(v) and abs(v) <= .001 for v in r.get('walk', []) + r.get('turn', []))]
        if len(stable) < 2 or stable[-1]['monotonicSeconds'] - stable[0]['monotonicSeconds'] < .15:
            unknown.append('stable_sample_coverage')
        if airborne:
            check(result['localRequestId'] == str(uuid.UUID(int=0)) and final_nav['segments'] == 0 and final_nav['path'] == 0, 'no_hidden_retry_or_local_run')
        check(not any(r.get('monotonicSeconds', -1) >= result['monotonicSeconds'] for r in calls), 'no_late_dispatch')
        check(not any(r.get('kind') == 'prefetch_handoff_taken' for r in rows), 'no_successor_handoff')
        deliveries = [e for e in driver.get('events', []) if e.get('delivered')]
        check(driver.get('case') == 'direct_readiness' and driver.get('error') is None and 1 <= len(deliveries) <= 4, 'bounded_driver_success')
        check(all((e['sessionId'], e['actorId']) == scope for e in deliveries), 'driver_scope')
        check(deliveries[0]['requestId'] == calls[0]['decisionId'], 'driver_plan_binding')
        drained = one('stop_drained'); host = one('result'); audit = one('final_audit')
        check(drained.get('modelCompleted') is True and audit.get('modelInFlight') is False and audit.get('requiresReconciliation') is None, 'clean_native_and_controller_drain')
        check(host.get('updateFailures') == 0, 'zero_update_failures')
        if airborne: check(result['Passed'] is True and host['passed'] is True, 'host_agrees_with_derived_pass')
    except (KeyError, ValueError, TypeError, IndexError, AttributeError) as exc:
        unknown.append('missing_or_invalid_evidence:' + type(exc).__name__)
    return {'schema': 'n35-readiness-validation-v1', 'verdict': 'fail' if failures else 'unknown' if unknown else 'uncovered' if uncovered else 'pass',
            'failures': sorted(set(failures)), 'unknown': sorted(set(unknown)),
            'scope': 'Artificial non-model Jump/input-pulse and default navigation readiness gate only.'}


def read_rows(path):
    path = driver_module.safe(path)
    if path.stat().st_size > 128 * 1024 * 1024: raise ValueError('log_size_bound')
    rows = []
    with path.open() as stream:
        for line in stream:
            if len(line) > 2 * 1024 * 1024: raise ValueError('line_size_bound')
            rows.append(json.loads(line))
    return rows


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    try:
        result = validate(read_rows(args.directory / 'tool-agent.jsonl'),
                          json.loads((args.directory / 'readiness-driver-result.json').read_text()))
    except (OSError, ValueError) as exc:
        result = {'schema': 'n35-readiness-validation-v1', 'verdict': 'unknown', 'failures': [],
                  'unknown': [type(exc).__name__ + ':' + str(exc)]}
    if args.output: driver_module.write_new(args.output, result)
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result['verdict'] == 'pass' else 2 if result['verdict'] in ['unknown', 'uncovered'] else 1)
