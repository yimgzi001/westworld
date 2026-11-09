# Native navigation + asynchronous handoff gate

Full survival external Player, no GameWidget, one AtomicActions/ActorTools/gateway and observation-only session. Controlled flat-floor fixture with deterministic delayed decisions, NOT a model. No inventory changes or omniscient route construction. Direction points come from actually observed ground. LocalNavigationSkill constructs its own bounded local route proof. All actor actions participate in one host lease.

Overlap request starts only while navigation Moving AND real AtomicActions.IsActionRunning. Delayed immutable candidate arrives after150ms; only game thread receives it. Movement/time overlap counts adjacent sampled frames where decision is pending and native movement runs. Previous skill's Arrived, stable lease release and fresh target observation precede next segment.

Cancel: invalidate first, skill.Cancel and drain. Late result rejected.
Obstacle: artificial wall inserted after start; fail/stop discards successor. Observed01 reason evidence_expired, NOT native Blocked classification.
Late01: previous skill Arrived; host immediately invalidated revision and rejected later reply. This is terminal-time revocation, not elapsed request-deadline testing.
Late02 native PASS7.570s: preceding Arrived at2.80258s, occupied=true/no successor during stable hold; deadline3.00258s, invalidated3.00576s, late reply3.28100s rejected. Final speed.000232m/s, health1, no quarantine, nextSubmissions0, cleanupVerifiedtrue, updateFailureCount0. Explicit GameWidget-null guard and final hasAiGameWidget=false. Evidence: evidence/late-02.jsonl and late02-manifest.json.

The older01 cases used external-actor configuration but did not log or assert GameWidget-null explicitly; do not retrofit the02 assertion onto those records.

75s host budget starts stop/drain,90s absolute wall limit. Local skill20s and5s settling bounds retained. Failed/quarantined cleanup cannot pass.

Native01: overlap8.050s pass (.1522s real overlap/.1707m overlap movement/second segment once); cancel6.705s pass; obstacle9.165s negative pass; late7.714s immediate-revocation pass. All health1, update failures0, cleanupVerifiedtrue, no quarantine. No real4B overlap, natural terrain or step success claimed. Evidence JSONL plus exact source and deployed DLL hashes in evidence/run01-manifest.json.

## Reproduce on the authorized dot cloud desktop

After provisioning the documented .NET SDK and the existing SCAPI dependency/assets locally, run:

```sh
bash experiments/scapi-navigation-handoff-gate/stage.sh nav-handoff-new /absolute/path/to/existing/runtime-assets
```

This builds serially, creates four fresh `work/<label>-<mode>` directories, copies the locally provided Content.zip and runtime output, and records source/deployed DLL hashes. It refuses existing directories. Launch the printed `run.sh` for one mode from the supported dot desktop terminal, never the user's computer. Each launch verifies DLL hashes and refuses existing world/mod/log output. Game assets and generated worlds are not committed. These scripts do not download/install software or run a model; expected nonzero exits are preserved as failed evidence.
