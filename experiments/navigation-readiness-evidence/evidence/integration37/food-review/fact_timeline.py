"""Candidate metadata projection, offline prototype only. Does not mutate feedback or select actions."""
def describe(facts):
    outcome=facts.get('outcomeFeedback')
    if not isinstance(outcome,dict) or outcome.get('kind')!='tool_outcome_delta':
        return None
    progress=(facts.get('goalExecution') or {}).get('progress') or {}
    return {
        'outcomeScope':'historical_action_terminal_delta',
        'outcomeBeforeObservationId':outcome.get('beforeObservationId'),
        'outcomeAfterObservationId':outcome.get('afterObservationId'),
        'currentObservationId':progress.get('observationId'),
        'currentObservedAt':progress.get('observedAt'),
        'note':'Outcome delta describes its recorded action boundary, not all later changes. Current own inventory and goal progress may include subsequent automatic collection. Empty historical delta does not negate current possession. IDs alone do not establish temporal order.'
    }
