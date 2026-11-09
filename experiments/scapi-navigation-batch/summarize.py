#!/usr/bin/env python3
"""Offline JSONL evidence summary. Does not start games, models or alter worlds."""
import argparse
import collections
import json
import math
from pathlib import Path


def summarize(path):
    events = []
    errors = []
    with Path(path).open(encoding='utf-8') as stream:
        for number, line in enumerate(stream, 1):
            if not line.strip():
                continue
            try:
                obj = json.loads(line, parse_constant=lambda v: (_ for _ in ()).throw(ValueError(v)))
                if not isinstance(obj, dict) or not isinstance(obj.get('kind'), str):
                    raise ValueError('object with kind required')
                events.append(obj)
            except (ValueError, TypeError) as exc:
                errors.append({'line': number, 'error': str(exc)})
    kinds = collections.Counter(e['kind'] for e in events)
    start = next((e for e in events if e['kind'] == 'start'), {})
    finals = [e for e in events if e['kind'] == 'result']
    final = finals[-1] if finals else {}
    outcomes = collections.Counter(e.get('outcome', 'unknown') for e in events if e['kind'] == 'local_route_result')
    stops = [e for e in events if e['kind'] == 'route_safety_stop']
    observations = [e for e in events if e['kind'] == 'local_route_observed']
    rejection = collections.Counter(e.get('reason', 'unknown') for e in observations if e.get('safeCandidates') == 0)
    provenance = [c for e in events if e['kind'] == 'local_route_started' for c in e.get('provenance', []) if isinstance(c, dict)]
    ages = [c.get('Age') for c in provenance if isinstance(c.get('Age'), (int, float)) and not isinstance(c.get('Age'), bool)]
    result_ids = [e.get('choice') for e in events if e['kind'] == 'local_route_result' and e.get('choice')]
    duplicates = len(result_ids) - len(set(result_ids))
    warnings = []
    if len(finals) != 1:
        warnings.append('expected_exactly_one_terminal_result')
    if errors:
        warnings.append('malformed_log_lines')
    if duplicates:
        warnings.append('duplicate_route_results_counts_not_unique')
    if not provenance and kinds['local_route_started']:
        warnings.append('route_provenance_missing')
    if any(a < 0 or a > 3 for a in ages):
        warnings.append('route_evidence_age_outside_3_seconds')
    if final.get('updateFailures') is None:
        warnings.append('update_failure_count_missing')
    # The runner's passed flag is retained, never promoted to natural survival success.
    return {
        'path': str(path), 'mode': start.get('mode'), 'seed': start.get('seed'),
        'fixture': start.get('fixture'), 'runner_passed': final.get('passed'),
        'terminal_reason': final.get('reason'), 'seconds': final.get('seconds'),
        'update_failures': final.get('updateFailures'), 'route_starts': kinds['local_route_started'],
        'route_results': dict(outcomes), 'duplicate_route_results': duplicates,
        'zero_candidate_observations': sum(e.get('safeCandidates') == 0 for e in observations),
        'projection_rejections': dict(rejection),
        'expiry_stops': sum(e.get('expired') is True for e in stops),
        'nonexpiry_safety_stops': sum(e.get('expired') is False for e in stops),
        'unclassified_safety_stops': sum(not isinstance(e.get('expired'), bool) for e in stops),
        'historical_proof_cells': sum(c.get('IsCurrent') is False for c in provenance),
        'maximum_logged_proof_age': max(ages) if ages else None,
        'resource_observed_events': kinds['resource_observed'],
        'food_chain_verified': None,
        'food_chain_note': 'Requires correlated harvest, acquisition and consumption receipts; observation or runner pass alone is insufficient.',
        'events': dict(kinds), 'warnings': warnings, 'parse_errors': errors,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('logs', nargs='+', type=Path)
    args = parser.parse_args()
    rows = [summarize(p) for p in args.logs]
    print(json.dumps(rows, ensure_ascii=False, indent=2))
    return 2 if any(r['parse_errors'] or 'expected_exactly_one_terminal_result' in r['warnings'] for r in rows) else 0


if __name__ == '__main__':
    raise SystemExit(main())
