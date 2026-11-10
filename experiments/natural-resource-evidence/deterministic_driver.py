#!/usr/bin/env python3
"""Bounded no-model natural-resource regression policy, not a production strategy.
Reads only the final public prompt OBSERVATION, offers and file-control metadata.
Never reads host logs, source audits, hidden world state, or executes generated code.
"""
import argparse
import datetime
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import time

_here = Path(__file__).absolute()
# Standalone staged entry uses only its own frozen dependency, never the live repo.
_staged_shared = _here.parent / 'sources/experiments/food-acquisition-evidence/deterministic_driver.py'
if _staged_shared.is_file():
    _shared_path = _staged_shared
elif _here.parent.name == 'natural-resource-evidence':
    _shared_path = _here.parent.parent / 'food-acquisition-evidence/deterministic_driver.py'
else:
    raise FileNotFoundError('frozen food protocol dependency required')
_spec = importlib.util.spec_from_file_location('natural_public_file_protocol', _shared_path)
shared = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(shared)
IDS = shared.IDS
MAX_REQUESTS, MAX_SECONDS = 8, 180
MAX_SCAN_TURNS = 3
SCAN_HEADING_TOLERANCE = 10
# Public base-game content indexes, not inventory/source assumptions.
HARVESTABLE = {2: 'dirt', 8: 'grass', 9: 'oak', 10: 'birch', 11: 'spruce'}
LIQUIDS = {18, 92}
RESOURCE_VALUES = {2, 8, 9, 10, 11}
TERMINAL = {'succeeded', 'failed', 'cancelled', 'rejected', 'skipped'}


def finite(v):
    return type(v) in (int, float) and math.isfinite(v)


def point(v):
    if not isinstance(v, dict) or not all(finite(v.get(k)) for k in ('x', 'y', 'z')):
        return None
    if any(abs(v[k]) > 1000000 for k in ('x', 'y', 'z')):
        return None
    return (v['x'], v['y'], v['z'])


def cell(v):
    if not isinstance(v, dict) or not all(type(v.get(k)) is int for k in ('x', 'y', 'z')):
        return None
    p = point(v)
    return p if p and 0 <= p[1] <= 1024 else None


def reference(v):
    return isinstance(v, str) and 0 < len(v) <= 160 and not any(ord(c) < 32 for c in v)


def contents(v):
    return v & 1023 if type(v) is int and 0 <= v <= 2147483647 else None


def offered(request, tool, args):
    matches = [o for o in request['offers'] if o.get('Name') == tool]
    if len(matches) != 1 or not shared.valid(args, matches[0].get('Parameters', {})):
        return False
    offer = matches[0]
    bindings = offer.get('ReferenceValues') or {}
    for key in ('target', 'destination'):
        if key in args:
            # A visible string alone is not authority. Require the actual current tool enum.
            field = offer['Parameters'].get('properties', {}).get(key, {})
            if args[key] not in field.get('enum', []):
                return False
            if key in bindings and args[key] not in bindings[key]:
                return False
    branches = offer.get('ParameterBranches')
    if branches is not None:
        if sum(all(k in args for k in b.get('Required', [])) and
               all(k not in args for k in b.get('Forbidden', [])) for b in branches) != 1:
            return False
    return True


def footprint_clear(position, target):
    # Exclude the entire .65m expanded horizontal body footprint, including straddled cells.
    x, _, z = position
    tx, _, tz = target
    dx = max(tx - x, x - (tx + 1), 0.)
    dz = max(tz - z, z - (tz + 1), 0.)
    return math.hypot(dx, dz) >= .65


def horizontal_heading(facing):
    p = point(facing)
    if p is None or p[0] * p[0] + p[2] * p[2] < .0001:
        return None
    # Diagnostic world heading: +Z=0, +X=90. This is NOT the native argument.
    return math.degrees(math.atan2(p[0], p[2])) % 360


def heading_distance(a, b):
    return abs((a - b + 180) % 360 - 180)


def native_turn_heading(current, relative_bearing):
    # ActorTools.RelativePoint rotates around +Y by -bearing. Therefore native
    # +90 maps +Z to -X, unlike host_navigate_local's absolute +90=+X.
    return (current - relative_bearing) % 360


class Policy:
    def __init__(self):
        self.scope = None
        self.baseline = None
        self.attempted = set()
        self.dug_cells = set()
        self.pending = None
        self.harvest = None
        self.no_drop_observations = 0
        self.last_observation = None
        self.unsafe_outcome = False
        self.scan_attempts = 0
        self.scan_blocked = None
        self.scan_headings = []

    def decide(self, request):
        scope = request['sessionId'], request['actorId']
        if self.scope is not None and self.scope != scope:
            raise ValueError('scope_changed')
        self.scope = scope
        facts = shared.facts_from_prompt(request)
        observation = facts.get('observation')
        if not isinstance(observation, dict):
            raise ValueError('actual_observation_required')
        own = observation.get('self', {})
        position = point(own.get('position'))
        if position is None:
            raise ValueError('actual_position_required')
        slots = facts.get('inventory', {}).get('slots')
        if not isinstance(slots, list):
            raise ValueError('actual_inventory_required')
        inventory = shared.counts(slots)
        if self.baseline is None:
            self.baseline = inventory.copy()

        def empty(reason):
            return {'goal': reason, 'steps': []}

        def plan(reason, tool, args, target=None):
            if not offered(request, tool, args):
                return None
            ref = args.get('target', args.get('destination'))
            if ref and (tool, ref) in self.attempted:
                return None
            if ref:
                self.attempted.add((tool, ref))
            self.pending = {'tool': tool, 'observationId': request['observationId'], 'target': target}
            return {'goal': reason, 'steps': [{'tool': tool, 'arguments': args}]}

        phase = facts.get('planningPhase')
        if phase is not None and not isinstance(phase, dict):
            raise ValueError('planning_phase_shape')
        if phase and phase.get('phase') == 'AfterPrecedingSuccess':
            return empty('Require actual movement terminal and fresh observation, not predicted resource ownership')
        if self.last_observation == request['observationId']:
            return empty('Repeated observation cannot authorize another attempt')
        self.last_observation = request['observationId']
        feedback = facts.get('outcomeFeedback', {})
        if feedback.get('status') == 'outcome_unknown':
            self.unsafe_outcome = True
        if self.unsafe_outcome:
            return empty('Previous native outcome is unknown; no blind replay or further action')
        terminal = facts.get('terminal', {})
        if any(isinstance(terminal.get(k), dict) and terminal[k].get('status') in
               ('running', 'waiting', 'blocked', 'stopping') for k in ('action', 'task', 'navigation')):
            return empty('Actual native action still active; request a later current observation')
        if not finite(own.get('health')) or own['health'] < .5 or own.get('isSleeping') is True:
            return empty('Actor health or sleeping state prevents this bounded resource check')
        if self.pending:
            old = self.pending
            preceding = feedback.get('preceding', {})
            action = terminal.get('action') or {}
            status = preceding.get('status') if preceding.get('tool') == old['tool'] else None
            if status not in TERMINAL and action.get('action') == old['tool'].removeprefix('act_'):
                status = action.get('status')
            if status not in TERMINAL:
                return empty('Previous attempted action has no known actual terminal; reobserve without replay')
            self.pending = None
            if old['tool'] == 'act_Dig':
                if status == 'succeeded':
                    self.harvest = old['target']
                    self.no_drop_observations = 0
                else:
                    self.harvest = None
            elif old['tool'] == 'act_FaceTowards':
                current_heading = horizontal_heading(own.get('facing'))
                if status != 'succeeded':
                    self.scan_blocked = 'Previous scan turn ended ' + status + '; no blind turn retry'
                elif current_heading is None or heading_distance(current_heading, old['target']['heading']) > SCAN_HEADING_TOLERANCE:
                    self.scan_blocked = 'Successful turn has no matching fresh self.facing; no blind turn retry'
                else:
                    self.scan_headings.append(current_heading)
        if self.dug_cells and any(contents(value) in RESOURCE_VALUES and count > self.baseline.get(value, 0)
                                 for value, count in inventory.items()):
            return empty('Current inventory increased; native evidence must independently prove natural acquisition')

        if self.harvest is not None:
            h = self.harvest
            allowed = {2, 8} if h['contents'] in (2, 8) else {h['contents']}
            drops = []
            for drop in observation.get('pickables', []):
                q = point(drop.get('position'))
                ref = drop.get('reference')
                if drop.get('sense') != 'vision' or not reference(ref) or q is None or \
                        contents(drop.get('value')) not in allowed or type(drop.get('count')) is not int or drop['count'] <= 0:
                    continue
                if ref in h['oldDropReferences'] or math.dist(q, h['center']) > 2.5:
                    continue
                distance = math.dist(position, q)
                horizontal = math.hypot(position[0] - q[0], position[2] - q[2])
                if distance <= 3 and abs(q[1] - position[1]) <= 1:
                    drops.append((distance, horizontal, ref, drop))
            for distance, horizontal, ref, drop in sorted(drops, key=lambda v: (v[0], v[2])):
                if horizontal <= 1.25:
                    decision = plan('Collect an actually visible post-dig resource; do not assume its source',
                                    'act_PickUp', {'target': ref, 'count': 1})
                else:
                    decision = plan('Approach an actually visible nearby drop; native movement may still fail',
                                    'act_MoveTo', {'destination': ref, 'range': .75, 'timeout': 4})
                if decision:
                    return decision
            self.no_drop_observations += 1
            if self.no_drop_observations <= 2:
                return empty('No unattempted legal visible post-dig drop; reobserve without inventing one')
            self.harvest = None

        candidates = []
        blocks = observation.get('visibleBlocks', [])
        if not isinstance(blocks, list):
            raise ValueError('visible_blocks_required')
        liquid_cells = [cell(b.get('cell')) for b in blocks if contents(b.get('value')) in LIQUIDS]
        liquid_cells = [c for c in liquid_cells if c is not None]
        for block in blocks:
            c, q = cell(block.get('cell')), point(block.get('position'))
            ref, material = block.get('reference'), contents(block.get('value'))
            if material not in HARVESTABLE or c is None or q is None or not reference(ref):
                continue
            # A visible face point must belong to this actual cell (within surface epsilon).
            if any(not c[i] - .01 <= q[i] <= c[i] + 1.01 for i in range(3)):
                continue
            if c in self.dug_cells or ('act_Dig', ref) in self.attempted or not footprint_clear(position, c):
                continue
            if not math.floor(position[1]) - 1 <= c[1] <= math.floor(position[1]) + 1:
                continue
            if any(max(abs(c[i] - liquid[i]) for i in range(3)) <= 1 for liquid in liquid_cells):
                continue
            distance = math.dist(position, q)
            if distance > 2.75 or not finite(block.get('distance')) or not 0 <= block['distance'] <= 2.75:
                continue
            candidates.append((0 if material in (2, 8) else 1, distance, ref, c, material))
        for _, _, ref, c, material in sorted(candidates):
            old = {d['reference'] for d in observation.get('pickables', []) if reference(d.get('reference'))}
            target = {'cell': c, 'center': tuple(v + .5 for v in c), 'contents': material,
                      'oldDropReferences': old}
            decision = plan('Try one nearby observed non-support block, then reobserve actual result',
                            'act_Dig', {'target': ref, 'timeout': 4}, target)
            if decision:
                self.dug_cells.add(c)
                return decision
        # Bounded active observation only. Missing/null/coerced facts are not false.
        # Native FaceTowards cannot run while parented; never infer riding from drift,
        # wetness, nearby boats, or an offer alone, and never automatically dismount.
        if own.get('isMounted') is not False or own.get('hasParentBody') is not False:
            return empty('No nearby legal target; scan blocked: explicit self.isMounted=false and '
                         'self.hasParentBody=false required; no inferred riding state or automatic dismount')
        if self.scan_blocked:
            return empty('No nearby legal target; scan blocked: ' + self.scan_blocked)
        if self.scan_attempts >= MAX_SCAN_TURNS:
            return empty('No nearby legal target; three bounded scan turns exhausted; no blind exploration move')
        current_heading = horizontal_heading(own.get('facing'))
        if current_heading is None:
            return empty('No nearby legal target; scan blocked: finite nonvertical self.facing required')
        if not any(heading_distance(current_heading, old) <= SCAN_HEADING_TOLERANCE for old in self.scan_headings):
            self.scan_headings.append(current_heading)
        target_heading = native_turn_heading(current_heading, 90)
        if any(heading_distance(target_heading, old) <= SCAN_HEADING_TOLERANCE for old in self.scan_headings):
            return empty('No nearby legal target; scan blocked: next relative turn would repeat an observed direction')
        decision = plan('No nearby legal target; turn 90 degrees once, then await actual terminal and fresh observation',
                        'act_FaceTowards', {'bearing': 90, 'timeout': 4}, {'heading': target_heading})
        if decision:
            self.scan_attempts += 1
            return decision
        return empty('No nearby legal target; scan blocked: current act_FaceTowards offer does not permit a bounded 90 degree turn')


def run(directory, clock=time.monotonic, sleep=time.sleep, utcnow=None):
    utcnow = utcnow or (lambda: datetime.datetime.now(datetime.timezone.utc))
    root = shared.safe(directory)
    if not root.is_dir():
        raise ValueError('existing_root_required')
    with (root / 'natural-driver-started.json').open('x') as f:
        json.dump({'kind': 'deterministic_natural_resource_test_not_model',
                   'policyVersion': 'bounded_turn_scan_v2', 'maximumScanTurns': MAX_SCAN_TURNS,
                   'maximumRequests': MAX_REQUESTS, 'maximumSeconds': MAX_SECONDS}, f)
    start, policy = clock(), Policy()
    seen, request_ids, observation_ids, events = set(), set(), set(), []
    scope, error = None, None
    def expired():
        return clock() - start >= MAX_SECONDS or (root / 'exit.txt').exists()
    try:
        while not expired() and len(seen) < MAX_REQUESTS:
            for path in sorted(shared.safe(root / 'control').glob('*/request.json')):
                if expired() or len(seen) >= MAX_REQUESTS:
                    break
                if path in seen:
                    continue
                seen.add(path)
                p = shared.read_request(path)
                current_scope = p['sessionId'], p['actorId']
                if scope is not None and current_scope != scope:
                    raise ValueError('scope_changed')
                scope = current_scope
                if p['requestId'] in request_ids or p['observationId'] in observation_ids:
                    raise ValueError('duplicate_request_or_observation')
                request_ids.add(p['requestId'])
                observation_ids.add(p['observationId'])
                event = {k: p[k] for k in IDS}
                event.update(delivered=False, ordinal=len(seen))
                events.append(event)
                canceled = shared.safe(path.parent / 'canceled.json')
                deadline = datetime.datetime.fromisoformat(p['deadlineUtc'].replace('Z', '+00:00'))
                if deadline.tzinfo is None:
                    raise ValueError('deadline_timezone')
                if canceled.exists() or utcnow() >= deadline:
                    event['reason'] = 'canceled_or_request_expired'
                    continue
                decision = policy.decide(p)
                event.update(goal=decision['goal'], steps=decision['steps'])
                response = {k: p[k] for k in IDS}
                response['decision'] = decision
                raw = json.dumps(response, separators=(',', ':'), allow_nan=False).encode()
                if len(raw) > 65536:
                    raise ValueError('response_bound')
                output = shared.safe(path.parent / 'response.json')
                temporary = shared.safe(path.parent / 'natural-response.tmp')
                if output.exists():
                    raise ValueError('response_exists')
                with temporary.open('xb') as f:
                    f.write(raw)
                    f.flush()
                    os.fsync(f.fileno())
                try:
                    if expired() or canceled.exists() or utcnow() >= deadline:
                        event['reason'] = 'stopped_before_publish'
                        continue
                    os.link(temporary, output)
                    event.update(delivered=True, writtenMonotonicSeconds=clock(),
                                 responseSha256=hashlib.sha256(raw).hexdigest())
                finally:
                    temporary.unlink()
            if not expired() and len(seen) < MAX_REQUESTS:
                sleep(min(.05, max(0, MAX_SECONDS - (clock() - start))))
    except Exception as exc:
        error = type(exc).__name__ + ': ' + str(exc)
    result = {'schema': 'natural-resource-driver-v1', 'kind': 'deterministic_natural_resource_test_not_model',
              'policyVersion': 'bounded_turn_scan_v2', 'scanAttempts': policy.scan_attempts,
              'scanHeadings': policy.scan_headings, 'scanBlocked': policy.scan_blocked,
              'receivedRequests': len(seen), 'responses': sum(e['delivered'] for e in events),
              'elapsedSeconds': clock() - start, 'events': events, 'error': error,
              'reason': 'driver_error' if error else 'host_exit' if (root / 'exit.txt').exists() else
                        'request_limit' if len(seen) >= MAX_REQUESTS else 'deadline',
              'nativeAcquisitionClaim': 'none; only independent native provenance and inventory evidence can establish success'}
    with (root / 'natural-driver-result.json').open('x') as f:
        json.dump(result, f, indent=2, allow_nan=False)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory')
    args = parser.parse_args()
    result = run(args.directory)
    print(json.dumps(result, allow_nan=False))
    raise SystemExit(1 if result['error'] else 0)
