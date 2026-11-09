# Native production capability profile

`NativeProductionTools` is a static profile for the shared host NativeToolSession, not another session/ledger or autonomous script. `Select(nativeTools)` preserves available native schemas/descriptions; missing native capabilities are never invented. `WorkspaceReferences(existingObservation)` extracts only the actor's issued own-crafting/focused-storage references. Optional `Read(existingActorTools, existingObservation, knownResultValue?)` bundles read_inventory/read_storage/find_recipes/action_status/task_status. It does NOT call observe again and invalidate live references during an active plan. The host must supply fresh authorized observation data and game-thread/actor identity binding through its unified gateway.

Tools: observe, read_self, read_inventory, read_storage, read_item, find_items, find_recipes, plan_craft, act_Craft, act_TransferItems, act_Place, act_SelectItem, act_Wait, act_Eat, action_status, task_status, cancel_action, cancel_task. This production subset is not a replacement for navigation/hunting profiles or the entire native framework.

Reuse details:
- `find_recipes`: native known recipe references, result/ingredient filters. `plan_craft` composes native inventory transfers plus Craft for the issued recipe/workspace.
- A returned `ok:true` with task `waiting`/`running` is acceptance, NOT completion. Poll task_status with a new request; succeeded plus native inventory deltas establish result. The shared gateway handles replay, uncertain outcomes and refresh policy.
- `read_storage` already provides CraftingPreview(result/remains slot) and FurnaceView(heat, progress, fuel seconds, fuel/result/remains slots) together with actual slots. No new game API is needed. Access/liveness/reach/visibility errors remain native errors in the snapshot.
- No `Smelt` action exists or is invented. Transfer actual raw material/fuel into native slots, wait, reread storage, transfer actual result out, then Eat. Never AddSlotItems, set smelting progress or fabricate cooked output.
- Cancel task/action stops the corresponding task/action, not a rollback of completed transfers and not a switch that stops native furnace combustion. Exceptions/partial effects must not cause blind repetition.
- `Read` is an optional trusted-host query helper; use each native query via shared NativeToolSession if every query needs an individually audited request. It neither replaces that gateway nor grants credentials, capabilities or world omniscience.

Pure tests:30/30 passed, `/tmp/production-tools-tests.log`. They cover profile/schema preservation, absent capabilities, no fake Smelt/code/inventory write tools, and reference extraction/malformed observations. No native world, model, furnace execution, host replay or thread-binding behavior is claimed. Actual native production/cooking evidence remains in the separate experiments; this new helper has not itself received a native end-to-end run.
