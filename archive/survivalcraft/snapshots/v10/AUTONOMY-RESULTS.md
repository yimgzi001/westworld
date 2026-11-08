# Local model skill-choice experiments

## What is autonomous, and what is not
The local Qwen3.5-2B Q4 model chooses one implemented skill and a resource index from real compact observations. It does not invent coordinates. Deterministic actuators plan observed safe paths, apply normal inputs, check health/air/freshness, verify inventory transactions and stop. The experiment must not equate scripted actuator execution with autonomous choice of goals or successful survival.

Available skills: inspect_food, explore_safe_ground, approach_food, gather_food, eat, stop. Gathering and eating are separate choices. Only food verified as naturally gathered in the current adapter session can be eaten; native starting bread and prior fixtures are excluded. An uncertain eating transaction disables further eating for that session rather than blindly retrying.

Observation privilege: the API scans already-loaded terrain columns within a bounded radius, including beneath tree canopies. This is game-state access, not human line-of-sight perception. Terrain loading follows roles using native content streaming, without player-view rendering. Neither this setup nor the favorable wet climate is an arbitrary-world visual survival benchmark.

## Live round 1: no task progress
Evidence: qwen-lab/autonomy/qwen-autonomous-20261107-140050.jsonl and corresponding summary.
10/10 model choices were inspect_food. No exploration, movement, collection or consumption occurred. Final stop acknowledged; model exited. Total47.27s, request latency3.46–6.05s (median3.97), cumulative inference request CPU82.23s, peak2005348KiB (~1.91GiB), input3347/output454 tokens. These are model-process/request metrics, not total game-machine costs.

## Live round 2: stagnation facts did not resolve loop
Evidence: qwen-lab/autonomy/qwen-autonomous-v2-20261107-140307.jsonl and corresponding summary.
Only truthful feedback was added: observation was freshly scanned, consecutive inspection count, unchanged position/resources and elapsed no-movement time. No skill was removed or silently substituted. Again10/10 inspect_food, no actual task progress. Last observation showed9 repeated inspections and53.37s without displacement. Total60.05s, latency4.48–7.64s (median5.31), cumulative request CPU106.83s, peak2051832KiB (~1.96GiB), input4889/output512 tokens. Final stop acknowledged; model exited.

The live game was normally saved, backed up and closed after round2. Failures remain preserved. They demonstrate a policy/prompt capability issue in this setup, not a failure of the previously verified scripted natural-pumpkin chain. They do not establish that all2B models or all prompts cannot perform the task.

## Next controlled diagnostic
A bounded offline six-call comparison uses the same real round2 snapshot, explicitly marked stale/offline, and never executes game actions. Original prompt/state versus concise English prompt with the same state versus concise English prompt with an equivalent simplified state; two calls per condition. Results are diagnostic samples, not a live completion or statistically established capability.

## Offline A/B results
Six calls completed with no game actions. A(original prompt/state):2/2 inspect_food with valid -1 target. B(concise English/same state):2/2 inspect_food but target_index0 invalid for a targetless action. C(concise English/equivalent simplified state):2/2 explore_safe_ground but target_index1 invalid. All invalid outputs were rejected as emitted, not repaired. Thus representation changed action choice, but did not yet produce executable exploration under the original interface. Total request CPU53.84s, peakabout2.10GiB, request latency2.35–8.15s; cache effects prevent attributing latency differences solely to prompt design. Full conditions/output: qwen-lab/autonomy/offline-ab-results.json. A separate two-call schema experiment is pending, not part of these six.

## Offline C2 interface-only result
Two further offline calls kept C prompt/state/actions unchanged and only constrained targetless action indices in an explicit anyOf tool schema. Both raw responses selected explore_safe_ground with target_index=-1 and passed existing validation, without post-hoc correction. Latency6.32/2.92s (cache differs), totalCPU18.34s, peak2036112KiB (~1.94GiB). No game actions occurred. This is a valid offline exploration choice, not live autonomous food acquisition. Evidence: qwen-lab/autonomy/offline-c2-results.json.
