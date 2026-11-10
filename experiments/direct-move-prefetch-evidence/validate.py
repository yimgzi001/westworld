#!/usr/bin/env python3
"""Strict offline direct MoveTo evidence contract. Missing evidence is unknown, never success."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import uuid

CASES = ('direct_early', 'direct_late', 'direct_failure', 'direct_cancel',
         'direct_replace', 'direct_stale_reference')


def finite(value):
    return type(value) in (int, float) and math.isfinite(value)


def guid(value):
    try:
        return isinstance(value, str) and uuid.UUID(value).int != 0
    except ValueError:
        return False


def point(value):
    return isinstance(value, list) and len(value) == 3 and all(finite(v) for v in value)


def horizontal(a, b):
    return math.hypot(a[0] - b[0], a[2] - b[2])


def validate(rows, driver, case):
    if case not in CASES:
        raise ValueError('case_not_allowed')
    checks, metrics = [], {}

    def check(name, value):
        checks.append({'check': name, 'status': 'unknown' if value is None else 'pass' if value else 'fail'})

    def finish():
        return {'schema': 'direct-move-validation-v1', 'case': case,
                'verdict': 'fail' if any(c['status'] == 'fail' for c in checks) else
                           'unknown' if any(c['status'] == 'unknown' for c in checks) else 'pass',
                'checks': checks, 'metrics': metrics,
                'scope': 'Single artificial native MoveTo fixture; no model, natural-world or scale claim.'}

    def events(kind):
        return [e for e in rows if e.get('kind') == kind]

    def unique(kind):
        found = events(kind)
        check(kind + '_unique', None if not found else len(found) == 1)
        return found[0] if len(found) == 1 else None

    result = unique('result')
    check('native_result_passed_zero_update_failures', None if result is None else
          result.get('passed') is True and result.get('updateFailures') == 0)
    final = unique('final_audit')
    check('final_inference_drained', None if final is None or 'modelInFlight' not in final else
          final['modelInFlight'] is False)
    check('driver_case_matches', driver.get('case') == case)
    deliveries = driver.get('events', [])
    check('driver_two_bound_responses', len(deliveries) == 2 and
          all(e.get('delivered') is True for e in deliveries))
    move_calls = [e for e in events('native_call_started') if e.get('step', {}).get('Tool') == 'act_MoveTo']
    check('one_model_move_dispatch', len(move_calls) == 1)
    if len(move_calls) != 1:
        return finish()
    check('authorized_4m_relative_bearing0', move_calls[0].get('step', {}).get('Arguments') ==
          {'bearing': 0, 'distance': 4, 'range': .25, 'timeout': 4})
    request = unique('prefetch_requested')
    if request is None:
        return finish()
    lease = request.get('lease', {})
    rid, run = lease.get('RequestId'), request.get('precedingRunId')
    session, actor, sequence = lease.get('SessionId'), lease.get('ActorId'), lease.get('PrecedingReceiptSequence')
    check('actual_predecessor_binding', None if any(v is None for v in (rid, run, session, actor, sequence)) else
          all(guid(v) for v in (rid, run, session, actor)) and type(sequence) is int and sequence > 0)
    if not all(guid(v) for v in (rid, run, session, actor)) or type(sequence) is not int:
        return finish()
    check('driver_second_matches_prefetch', None if len(deliveries) != 2 else
          all(deliveries[1].get(k) == v for k, v in
              [('requestId', rid), ('sessionId', session), ('actorId', actor),
               ('observationId', lease.get('PlanningObservationId'))]))
    receipts = [e for e in events('native_call_returned') if e.get('tool') == 'act_MoveTo']
    check('move_receipt_matches_real_run', None if len(receipts) != 1 or 'runId' not in receipts[0].get('data', {}) else
          receipts[0].get('sequence') == sequence and receipts[0]['data']['runId'] == run and
          receipts[0].get('state') == 'returned' and receipts[0]['data'].get('status') == 'running')
    binding = unique('predecessor_bound')
    check('explicit_native_predecessor_binding', None if binding is None else
          binding.get('sessionId') == session and binding.get('actorId') == actor and
          binding.get('receiptSequence') == sequence and binding.get('runId') == run and
          binding.get('precedingKind') == 'NativeMoveTo' and binding.get('tool') == 'act_MoveTo')
    samples = [e for e in events('predecessor_physical_sample') if e.get('runId') == run]
    check('actual_body_samples_present', True if len(samples) >= 2 else None)
    if len(samples) < 2:
        return finish()
    check('samples_bound_to_actor_receipt', all(e.get('sessionId') == session and e.get('actorId') == actor and
          e.get('receiptSequence') == sequence for e in samples))
    valid = all(point(e.get('position')) and finite(e.get('monotonicSeconds')) for e in samples)
    check('finite_body_geometry_and_monotonic_time', valid)
    if not valid:
        return finish()
    times = [e['monotonicSeconds'] for e in samples]
    check('strictly_increasing_sample_time', all(b > a for a, b in zip(times, times[1:])))
    metrics['sampledPathMetres'] = sum(horizontal(a['position'], b['position'])
                                     for a, b in zip(samples, samples[1:])
                                     if 0 < b['monotonicSeconds'] - a['monotonicSeconds'] <= .15)
    origin = binding.get('position') if binding else None
    check('actual_dispatch_origin_present', True if point(origin) else None)
    origin = origin if point(origin) else samples[0]['position']
    metrics['netDisplacementMetres'] = horizontal(origin, samples[-1]['position'])
    metrics['eastwardDisplacementMetres'] = samples[-1]['position'][0] - origin[0]
    metrics['lateralDisplacementMetres'] = abs(samples[-1]['position'][2] - origin[2])
    metrics['sampleGapsOver150ms'] = sum(b - a > .15 for a, b in zip(times, times[1:]))
    check('body_sample_continuity_150ms', True if metrics['sampleGapsOver150ms'] == 0 else None)
    start = request.get('monotonicSeconds')
    responses = [e for e in events('prefetch_response') if e.get('requestId') == rid]
    responses_end = responses[0].get('monotonicSeconds') if len(responses) == 1 else None
    # Driver clocks and host clocks share monotonic OS time on the same dot computer.
    # For an invalidated request, use the earlier invalidation, not the later file injection.
    invalidations = [e for e in events('prefetch_invalidated') if e.get('requestId') == rid]
    ends = [e.get('monotonicSeconds') for e in invalidations] + [responses_end]
    end = min((v for v in ends if finite(v)), default=None)
    check('decision_overlap_interval_known', True if finite(start) and finite(end) and end >= start else None)
    overlap, overlap_path, overlap_pairs = 0., 0., 0
    if finite(start) and finite(end):
        for a, b in zip(samples, samples[1:]):
            ta, tb = a['monotonicSeconds'], b['monotonicSeconds']
            if start <= ta < tb <= end and tb - ta <= .15 and all(
                    s.get('status') == 'running' and s.get('moving') is True and
                    s.get('settled') is False and s.get('cancelRequested') is False for s in (a, b)):
                overlap_pairs += 1
                overlap += tb - ta
                overlap_path += horizontal(a['position'], b['position'])
    metrics.update(sampledInferenceMotionSeconds=overlap, sampledInferencePathMetres=overlap_path, sampledInferenceMotionPairs=overlap_pairs)
    check('real_displacement_while_inference_pending', None if end is None or overlap_pairs == 0 else overlap > 0 and overlap_path > .01)
    terminals = [e for e in samples if e.get('status') in ('succeeded', 'failed', 'cancelled') and
                 e.get('settled') is True and e.get('moving') is False]
    terminal = terminals[0] if terminals else None
    settled = [e for e in events('predecessor_settled') if e.get('runId') == run]
    if case in ('direct_early', 'direct_late', 'direct_stale_reference'):
        check('actual_predecessor_settled_event', None if not settled else len(settled) == 1 and
              settled[0].get('receiptSequence') == sequence and settled[0].get('sessionId') == session and
              settled[0].get('actorId') == actor and finite(settled[0].get('monotonicSeconds')))
    takes = [e for e in events('prefetch_handoff_taken') if e.get('requestId') == rid]
    successors = [e for e in events('native_call_started') if e.get('candidateRequestId') == rid]
    # A missing candidateRequestId on a dispatch is missing binding, not proof of zero dispatch.
    unbound = [e for e in events('native_call_started') if 'candidateRequestId' not in e]
    check('all_dispatches_declare_candidate_binding', True if not unbound else None)
    positive = case in ('direct_early', 'direct_late')
    check('no_unexpected_action_dispatches', len(events('native_call_started')) == (2 if positive else 1))
    if positive:
        check('predecessor_succeeded_and_settled', None if terminal is None else terminal['status'] == 'succeeded')
        check('actual_4m_completion', 3.5 <= metrics['netDisplacementMetres'] <= 4.5 and
              metrics['eastwardDisplacementMetres'] >= 3.5 and metrics['lateralDisplacementMetres'] <= .5)
        check('one_accepted_response', None if not responses else len(responses) == 1 and responses[0].get('accepted') is True)
        check('one_handoff_take', len(takes) == 1)
        check('one_bound_successor_dispatch', None if unbound else len(successors) == 1)
        if len(takes) == 1:
            check('fresh_validation_observation', guid(takes[0].get('validationObservationId')) and
                  takes[0]['validationObservationId'] != lease.get('PlanningObservationId'))
        if terminal and finite(responses_end):
            metrics['responseMinusNativeTerminalSeconds'] = responses_end - terminal['monotonicSeconds']
            check('response_timing_exercises_case', responses_end < terminal['monotonicSeconds'] if case == 'direct_early'
                  else responses_end > terminal['monotonicSeconds'])
        else:
            check('response_timing_exercises_case', None)
        if len(successors) == 1 and terminal:
            dispatch = successors[0]
            t = dispatch.get('monotonicSeconds')
            check('successor_only_after_settled_and_response', None if not finite(t) or not finite(responses_end) else
                  t >= max(terminal['monotonicSeconds'], responses_end))
            check('successor_is_controlled_wait', dispatch.get('step', {}).get('Tool') == 'act_Wait' and
                  dispatch.get('step', {}).get('Arguments') == {'duration': .2})
            metrics['terminalToSuccessorSeconds'] = t - terminal['monotonicSeconds'] if finite(t) else None
        waits = [e for e in events('native_terminal_observed') if e.get('tool') == 'act_Wait']
        wait_receipts = [e for e in events('native_call_returned') if e.get('tool') == 'act_Wait']
        check('successor_actual_terminal_succeeded', None if not waits or not wait_receipts else
              len(waits) == len(wait_receipts) == 1 and waits[0].get('status') == 'succeeded' and
              waits[0].get('sequence') == wait_receipts[0].get('sequence') and
              guid(wait_receipts[0].get('data', {}).get('runId')) and
              wait_receipts[0]['data']['runId'] != run and
              waits[0].get('runId') == wait_receipts[0]['data']['runId'])
    else:
        check('no_stale_handoff_take', len(takes) == 0)
        check('no_stale_successor_dispatch', None if unbound else len(successors) == 0)
        check('candidate_invalidated', True if invalidations else None)
        if invalidations:
            first_invalid = min((e.get('monotonicSeconds') for e in invalidations
                                 if finite(e.get('monotonicSeconds'))), default=None)
            check('retired_response_never_accepted', None if first_invalid is None else
                  not any(e.get('accepted') is True and finite(e.get('monotonicSeconds')) and
                          e['monotonicSeconds'] >= first_invalid for e in responses))
        if case in ('direct_failure', 'direct_cancel'):
            check('actual_expected_terminal', None if terminal is None else
                  terminal['status'] == ('failed' if case == 'direct_failure' else 'cancelled'))
            injection = unique('direct_fixture_injection')
            check('injection_binds_original_run', None if injection is None else
                  injection.get('before', {}).get('runId') == run and injection.get('scenario') == case)
        elif case == 'direct_replace':
            injection = unique('direct_fixture_injection')
            new_run = injection.get('replacement', {}).get('runId') if injection and isinstance(injection.get('replacement'), dict) else None
            check('actual_identity_replacement', None if injection is None else
                  injection.get('before', {}).get('runId') == run and injection.get('scenario') == case and guid(new_run) and new_run != run)
            check('replacement_observed_in_native_samples', None if not guid(new_run) else
                  any(e.get('runId') == new_run for e in events('predecessor_physical_sample')))
        else:
            injection = unique('direct_fixture_reference_invalidated')
            ref = deliveries[1].get('arguments', {}).get('target') if len(deliveries) == 2 else None
            check('old_public_reference_binds_invalidation', None if injection is None or not ref else
                  injection.get('reference') == ref and injection.get('runId') == run)
            check('fresh_validation_rejected_old_reference', True if any(e.get('requestId') == rid
                  for e in events('prefetch_validation_failed')) else None)
            check('predecessor_still_succeeded', None if terminal is None else terminal['status'] == 'succeeded')
        if case in ('direct_cancel', 'direct_replace'):
            late = deliveries[1] if len(deliveries) == 2 else None
            check('actual_retired_response_injection', None if late is None else
                  late.get('delivered') is True and late.get('canceledBeforeWrite') is True)
    return finish()


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('log', type=Path)
    p.add_argument('--driver-result', required=True, type=Path)
    p.add_argument('--case', required=True, choices=CASES)
    a = p.parse_args()
    raw = a.log.read_bytes()
    driver_raw = a.driver_result.read_bytes()
    report = validate([json.loads(line) for line in raw.splitlines() if line.strip()],
                      json.loads(driver_raw), a.case)
    report.update(sourceSha256=hashlib.sha256(raw).hexdigest(),
                  driverSha256=hashlib.sha256(driver_raw).hexdigest())
    print(json.dumps(report, indent=2, allow_nan=False))
    raise SystemExit({'pass': 0, 'fail': 1, 'unknown': 2}[report['verdict']])


if __name__ == '__main__':
    main()
