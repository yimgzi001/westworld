# Reconstructed native vision checkpoint

## Reconstructed native checkpoint, 2026-11-08

New recovery-vision-core and recovery-vision-game are reconstructions, not recovered late-stage originals. Core regression: 191/191; both core and game bridge build cleanly. The bridge provides read-only pumpkin vision plus tightly bounded real native yaw; no move/dig/place/eat/admin mutations. Commands use session, monotonic ID, age, same-tick safety checks, stop acknowledgment and budgets. Conservative loaded-Air DDA and radius 8, horizontal 90 / vertical 70 FOV were added.

New isolated default survival world Hollowbeach / player Basil successfully loaded in clean SCNET runtime with game views hidden and LAN connections disabled. Private library symlinks fixed missing unversioned X11 native dependencies; initial failed launch retained. Native smoke is SCRIPTED, not AI: next float above 0.1f (0.10000000894069672) rejected without motion; +0.1 and -0.1 each for 0.08 s accepted and turned approximately 5.511 and 5.503 degrees. Position stayed (1042.5,78,-160.5), health/air stayed 1, stop acknowledged, final active=false. Evidence: isolated-world-01/ipc/recovery-native-smoke.json and guard-audit.jsonl.

Native vision produced fresh empty resource arrays; no naturally visible pumpkin was present. Therefore positive native visibility, occlusion, model-guided discovery, and end-to-end mirror experiment remain unverified. No cloud model call or GitHub push.

Game window closed through window-manager close and absence verified in GUI inventory. Saved Project.json, Project.json.bak and Region 4,-1.dat exist under isolated-world-01/runtime/Worlds/World. Final IPC stop_ack, active=false, safe=true, 2 pulses, accumulated turn 11.01449 degrees. Not yet reopened to prove saved-world reload. Historical text above describes earlier checkpoints, not current dependency availability.
