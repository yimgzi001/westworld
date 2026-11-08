# Version adapter boundary

Only tested target: community SCNET release X26.07.01.01, approved release ZIP hash 8078d67a6696cb86ef50ba84013301257da1d4f1a28498b08b1d698d5918e230. Running title reports x26.06.19, API 1.42. All current C# references are bound to this installed binary, not to the user's repository.

The repository main reportedly targets SCAPI 1.9.5.1 with MaxPlayers=4 and automatic GameWidget/input lifecycle. No code from this experiment has been asserted compatible, committed, or pushed there. The 50-local-role result must NOT be reported as main supporting 50 roles.

A later adapter must individually implement and verify:
- World/session identity and explicit game-thread dispatch.
- Spawn/remove/restore local PlayerData and IsMainPlayer behavior.
- Observation schema: pose, health, air, vital stats, inventory, terrain and safe navigation metadata.
- Input ordering after the game's input clearing, including unfocused windows.
- Finite action leases, per-session monotonic IDs, cancellation and command-result distinction.
- Native mining/placing and atomic inventory processing for eating, never synthetic inventory/health changes in normal skills.
- Visibility management, saved role restoration, and resource measurements.

Acceptance gates for any new version: 1 role -> 2 independently controlled -> save/reload -> terrain/inventory effects -> input loss/focus cases -> bounded skill tests -> measured role staircase. Repeat real tests; do not copy a passed label across binaries.

Current gaps: game-time leases are not wall-clock hard realtime; command IDs are only deduplicated within the current in-memory session, not across crash/restart; a receipt ack is not a transaction-success ack. A production adapter requires explicit world/session IDs, durable or reconciled effect records, bounded dispatch, and unexpected-exception recovery without blind retries.
