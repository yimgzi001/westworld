#!/usr/bin/env python3
"""N35-R controlled file-protocol gate. No model, network, native logs, or world reads."""
import argparse
import datetime as dt
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import time

# Staged top-level execution MUST use its own frozen dependency, never today's repo.
HERE = Path(__file__).resolve()
PROTOCOL = (HERE.parent / 'sources/experiments/food-acquisition-evidence/deterministic_driver.py'
            if (HERE.parent / 'sources').is_dir()
            else HERE.parents[1] / 'food-acquisition-evidence/deterministic_driver.py')
_spec = importlib.util.spec_from_file_location('n35_frozen_file_protocol', PROTOCOL)
_protocol = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_protocol)
safe, read_request = _protocol.safe, _protocol.read_request
IDS = _protocol.IDS
MAX_REQUESTS, MAX_SECONDS = 4, 65
NAV_ARGS = {'bearing': 90, 'distance': 1, 'max_seconds': 4}


def offered(request, tool, arguments):
    choices = [o for o in request['offers'] if o.get('Name') == tool]
    if len(choices) != 1 or not _protocol.valid(arguments, choices[0].get('Parameters', {})):
        raise ValueError('required_offered_schema:' + tool)
    return {'tool': tool, 'arguments': arguments}


def ready_sample(sample):
    """No missing/boolean-as-number coercion; ready means actual body predicates, not a route."""
    if not isinstance(sample, dict):
        return False
    numbers = ['Immersion', 'VerticalVelocity', 'HorizontalSpeed', 'Health']
    if any(type(sample.get(k)) not in (int, float) or not math.isfinite(sample[k]) for k in numbers):
        return False
    return (sample.get('Ready') is True and sample.get('Blockers') == []
            and sample.get('Grounded') is True and sample.get('Attached') is False
            and 0 <= sample['Immersion'] <= .1 and abs(sample['VerticalVelocity']) <= .1
            and 0 <= sample['HorizontalSpeed'] <= .1 and sample['Health'] > 0)


class Policy:
    def __init__(self):
        self.scope = None
        self.issued = False

    def decide(self, request):
        scope = (request['sessionId'], request['actorId'])
        if self.scope is not None and scope != self.scope:
            raise ValueError('scope_changed')
        self.scope = scope
        facts = _protocol.facts_from_prompt(request)
        phase = facts.get('planningPhase')
        if phase is not None:
            if self.issued and isinstance(phase, dict) and phase.get('phase') == 'AfterPrecedingSuccess':
                return {'goal': 'Readiness gate has no successor; never retry navigation', 'steps': []}
            raise ValueError('ordinary_initial_request_required')
        navigation = facts.get('terminal', {}).get('navigation', {})
        if not self.issued:
            own = facts.get('observation', {}).get('self', {})
            if own.get('isMounted') is not False or own.get('hasParentBody') is not False:
                raise ValueError('explicit_unmounted_unparented_self_required')
            if navigation.get('busy') is not False or not ready_sample(navigation.get('readiness')):
                raise ValueError('initial_stable_readiness_required')
            # Same accepted plan, not three file round trips. Jump's native pulse is unchanged.
            steps = [offered(request, 'act_Jump', {}),
                     offered(request, 'host_navigation_status', {}),
                     offered(request, 'host_navigate_local', dict(NAV_ARGS))]
            self.issued = True
        else:
            # Defensive fallback only. The fixture normally holds read-only sampling after
            # one attempt, without opening another decision. NEVER Jump/nav/Wait again.
            steps = [offered(request, 'host_navigation_status', {})]
        return {'goal': 'N35-R non-model capability gate: one native Jump pulse, read status, '
                        'one immediate explicit navigation attempt; then observe only', 'steps': steps}


def deadline(request):
    value = dt.datetime.fromisoformat(request['deadlineUtc'].replace('Z', '+00:00'))
    if value.tzinfo is None:
        raise ValueError('deadline_timezone_required')
    return value


def write_new(path, value):
    path = safe(path)
    with path.open('x') as stream:
        json.dump(value, stream, indent=2, allow_nan=False)
        stream.flush()
        os.fsync(stream.fileno())


def run(directory, clock=time.monotonic, sleep=time.sleep,
        utcnow=lambda: dt.datetime.now(dt.timezone.utc)):
    root = safe(directory)
    if not root.is_dir():
        raise ValueError('existing_fresh_stage_required')
    write_new(root / 'readiness-driver-started.json', {
        'schema': 'n35-readiness-driver-v1', 'case': 'direct_readiness',
        'nonModelControlledFixture': True, 'maximumRequests': MAX_REQUESTS,
        'maximumSeconds': MAX_SECONDS, 'navigationArguments': NAV_ARGS})
    start, policy = clock(), Policy()
    seen, ids, observations, events = set(), set(), set(), []
    error = None

    def stopped():
        return clock() - start >= MAX_SECONDS or safe(root / 'exit.txt').exists()

    try:
        while not stopped() and len(seen) < MAX_REQUESTS:
            control = safe(root / 'control')
            for path in sorted(control.glob('*/request.json')):
                if stopped() or len(seen) >= MAX_REQUESTS:
                    break
                if path in seen:
                    continue
                request = read_request(path)
                safe(path.parent)
                if request['requestId'] in ids or request['observationId'] in observations:
                    raise ValueError('duplicate_request_or_observation')
                ids.add(request['requestId']); observations.add(request['observationId']); seen.add(path)
                event = {k: request[k] for k in IDS}
                event.update(delivered=False, observedMonotonicSeconds=clock())
                events.append(event)
                canceled = safe(path.parent / 'canceled.json')
                expires = deadline(request)
                if canceled.exists() or utcnow() >= expires:
                    event['reason'] = 'canceled_or_expired'
                    continue
                decision = policy.decide(request)
                event['steps'] = decision['steps']
                response = {k: request[k] for k in IDS}
                response['decision'] = decision
                raw = json.dumps(response, separators=(',', ':'), allow_nan=False).encode()
                output, temporary = safe(path.parent / 'response.json'), safe(path.parent / 'readiness-response.tmp')
                if output.exists():
                    raise FileExistsError('response_exists')
                with temporary.open('xb') as stream:
                    stream.write(raw); stream.flush(); os.fsync(stream.fileno())
                try:
                    if stopped() or canceled.exists() or utcnow() >= expires:
                        event['reason'] = 'stopped_before_atomic_publish'
                        continue
                    os.link(temporary, output)  # exclusive; never replace/replay any response
                    event.update(delivered=True, writtenMonotonicSeconds=clock(),
                                 responseSha256=hashlib.sha256(raw).hexdigest())
                finally:
                    temporary.unlink()
            if not stopped() and len(seen) < MAX_REQUESTS:
                sleep(min(.01, max(0, MAX_SECONDS - (clock() - start))))
    except Exception as exc:
        error = type(exc).__name__ + ':' + str(exc)
    result = {'schema': 'n35-readiness-driver-v1', 'case': 'direct_readiness',
              'nonModelControlledFixture': True, 'receivedRequests': len(seen),
              'responses': sum(e['delivered'] for e in events), 'elapsedSeconds': clock() - start,
              'events': events, 'error': error,
              'reason': 'error' if error else 'host_exit' if (root / 'exit.txt').exists()
                        else 'request_limit' if len(seen) == MAX_REQUESTS else 'deadline',
              'claim': 'No native success claim; independent validator required.'}
    write_new(root / 'readiness-driver-result.json', result)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory')
    args = parser.parse_args()
    result = run(args.directory)
    print(json.dumps(result))
    raise SystemExit(1 if result['error'] else 0)
