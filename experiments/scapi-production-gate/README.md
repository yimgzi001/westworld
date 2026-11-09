# Native production positive control

Compiled and native-tested on the dot cloud desktop, 2026-11-09. No model calls. Successful second run: 18.1458746 seconds, health 1, zero GameWidgets, UpdateFailureCount 0.

Run ProductionGate.dll `fixture` only from a new isolated `work/<run>` deployment containing the game runtime assets, following the existing native gate staging pattern. The executable refuses an existing doc/Mods data directory. `production.jsonl` retains stage/failure/result evidence. Wall timeout150s, simulation timeout100s. Parent must allocate the GUI first.

Artificial positive-control world: flattened dirt platform, cleared local space, two placed oak logs, repositioned initial actor. Inventory is NOT injected or cleared. Native spawn provides one cooked fish (162), retained unchanged. Initial production materials reject; exact production deltas are checked against this untouched baseline. Full surviving IsExternalActor Player has no GameWidget, independent active centre, one native action owner. Public recipe guide is explicitly enabled. This is not a natural resource-discovery or model-planning test.

Verified full chain: eye-ray confirms each log -> native dig -> natural pickup -> own2x2 plan_craft8planks -> workbench item(4planks consumed) ->4sticks(2planks consumed) -> return to previously occupied fixture origin -> observed support/native placement of earned workbench -> new eye observation/storage ref ->3row native wood-club recipe (2planks+1stick). Verified final carry inventory: wooden club1, sticks3, untouched spawn cooked fish1; world contains the earned workbench. No stone, furnace, animals or cooked food claimed in this gate.

Stage assertions check exact inventory totals and content deltas, two dig-count increments, real log disappearance, native action/task success and real workbench placement. Recipe tasks use find_recipes/plan_craft, not direct item insertion. Returning to fixture origin is a predeclared test movement, not a navigation autonomy claim. The actual native run verified spawn, pickup, support face placement and recipe task JSON for this one positive-control seed. It does not establish natural-resource discovery or multi-seed robustness.

## Evidence

`evidence/production-native-01-failed.jsonl` preserves the initial failure: the original empty-spawn-inventory assumption was false. The fix preserves and checks native spawn inventory rather than clearing it. `evidence/production-native-02-passed.jsonl` is the full successful stage log. Both original console logs and isolated worlds remain under cloud `work/production-native-01` and `work/production-native-02`; worlds/build binaries are not included here. Native second-run exit code:0. No furnace, hunting or cooking result is claimed.

Third native run added final full workspace conservation assertions: own crafting grid all6slots and placed workbench all11slots (including result/remains) must have count0. `evidence/production-native-03-conservation-passed.jsonl` passed in18.1457041s, exit0, health1, zero views/update failures. Prior02 evidence is retained. Stage3 collection succeeded after the approach attempt; this is pickup evidence, NOT a navigation-success gate, and does not assert that approach action succeeded.
