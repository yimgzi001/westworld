# Natural mode provenance integration

Run `ToolAgentGate.dll natural file SEED` in a new staged work directory. SEED is optional, 1–64 ASCII letters/digits/underscore/hyphen. The recorded WorldSettings seed is the actual selected seed. Budget: 8 decisions, 180 wall seconds, each request at most 45 seconds, then bounded 7-second shutdown. Food fixture retains its separate four-decision budget.

Natural mode makes no fixture terrain, food, inventory, health, rotation, position or dismount edits. Normal world generation and native player spawn apply, including possible boat spawn. Controller receives only existing public observations/tools. Being naturally mounted or unable to reach a block is a legitimate failure, not bypassed.

This first integration accepts a block source only when a current actual visibleBlocks reference supplied the exact world/cell/original value, a real act_Dig was submitted, native origin callback matches that target during its active attempt, the same native run reports succeeded, and a native source-to-actor transfer conserves world and inventory quantities. Initial supplies or unexplained inventory growth cannot pass. Creature attribution is not enabled without entity reference/action proof. Origin facts alone do not prove who broke a block or killed an animal.

The host subscribes to immutable native origin/acquisition facts. Hidden provenance identities and coordinates remain in audit logs, never prompt facts. A bounded ledger preserves partial acquisitions and unknown reasons. Final evidence includes original observations, attempts/receipts/run identities, origin/transfer facts, full-value inventories, failure counters and shutdown state. Observe aliases use native object identity separately from gateway actor IDs.

Success is the first proven acquired resource, not mandatory eating. It requests normal cancellation and drains action/task/model activity. Evidence is rechecked after drainage; invalidated success becomes a failure. No automatic retry of uncertain game writes. Maximum 128 attempts and 256 origin/transfer entries; exhaustion remains unknown.

At implementation freeze only build and pure contracts are verified. The synthetic example in work/natural31-evidence-schema.json is schema documentation, not native evidence. Actual natural seeds require independent staged runs and raw-log validation before claiming a working natural resource chain.

Block target identity strips only native lighting bits (mask 15360), preserving contents/data; original raw origin facts remain untouched in the audit. Success additionally requires the actual actor GameWidget to be null and the external-actor viewport count to be zero. This single-actor same-cell active-attempt timing association is not a strict multiplayer miner-causality ID; concurrent actors breaking the same cell are outside this acceptance claim.
