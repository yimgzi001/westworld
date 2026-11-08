# Food and survival interface research

## Isolation
The 50-role world is preserved in bot-runtime and was normally saved before closing. food-runtime is an independent cloned real-foraging world. fixture-runtime is a second independent clone for explicitly injected unit-test food. Never merge fixture saves into real-foraging or baseline worlds.

## Verified results
- Runtime vital observation reports Food=0 for Walter, matching repeated native starvation damage events (0.05 about every 50 game seconds). Air remains 1. This identifies current recorded events, not unrecorded historical damage.
- Native Eat alone does not decrement inventory. The implemented eat action uses InventorySlotWidget.HandleDragDrop with SingleItem, the player's clothing inventory, and processingOnly=true.
- Non-food clothing is rejected without losing the item or increasing Food. Repeating the same command ID in the same session adds no second transaction result.
- **Explicit fixture only:** one mature pumpkin was supplied by a test-only command in fixture-runtime. Native consumption changed its slot count 1 -> 0 and Food 0 -> 0.15. This proves the consumption interface, NOT natural acquisition or sustainable survival.
- The real-foraging world contains no injected food. Three bounded ordinary walk/jump exploration segments covered roughly 27 blocks. Loaded surface searches found no mature pumpkin; this does not prove absence elsewhere. A terrain-drop guard stopped exploration.
- Water drift invalidated a cached shore target; the first attempt rejected it and stopped. A refreshed read-only terrain scan plus quaternion-to-local movement feedback then reached a nearby sand bank using ordinary movement/jumps, with health/air 1 and stop acknowledgement. No teleportation.

## Earlier observations before natural-chain completion
FoodBridge observes health, air, food/stamina/sleep/temperature/wetness, body rotation, inventory nutrition/sickness metadata, nearby cells, and loaded surface top block/collision/headroom data. scanfood searches existing loaded columns only; it does not generate terrain or create food.

At that earlier checkpoint the real food chain had NOT been completed; see the final verified-chain section below for the later success. Required next work is conservative ground navigation and food-source discovery, then normal mining/pickup or a verified cooking chain. Mature pumpkin is the preferred first source; raw meat and sea urchins carry disease risk and are not treated as safe emergency items.

## Limits
Receipt lastCommand is not success. Verify eat result plus inventory/Food. In-memory IDs do not guarantee cross-restart exactly-once. An exception after inventory processing may leave an effect without a log; do not blindly retry or add items back. Repeated consumption of the same food can cause native satiation sickness beyond the block's base sickness probability. Nonfinite nutrition checks were compiled successfully (zero warnings/errors) and deployed to food-runtime/wet-runtime after the reported positive/negative tests; those transaction tests have not been repeated against this revision.

All results bind to the approved SCNET binary. They do not assert compatibility with repository main SCAPI 1.9.5.1; see ADAPTER-CONTRACT.md.

## Grass route follow-up
First five-waypoint navigation stopped on starvation damage (health 1 -> 0.952), with stop acknowledged. After natural recovery to health 1, the same bounded route completed all five waypoints, including a one-block step via normal jump; final (554.5001,66,-146.44522), stop acknowledged. This is scripted observed-map execution, not model-led foraging. Fresh scan at the destination checked 3072 already-loaded columns and found no pumpkin. The naturally acquired food chain remains incomplete.

## Habitat and wet-world experiment (ongoing)
The actual dry-world generator is TerrainContentsGenerator24, not merely assumed from reference code. Its observed surface humidity ranged 0–5, temperature 7–12, and no observed grass met the humid pumpkin habitat threshold. Runtime DLL analysis confirms pumpkin generation needs a warm/humid candidate center (humidity >=10, temperature >6), sparse chunk/group random gates, then grass. Scanning currently checks top-3 through top+1, so it can miss beneath tall tree canopies.

Original dry world was normally saved and preserved. An independent wet-runtime world was created through the native UI, then its serialized native settings were configured before any terrain region existed: humidity offset16, temperature offset4, Harmless mode. Native generator24 subsequently produced observed humidity12–15. No terrain blocks or inventory were injected. Native starting bread162 x1 exists and is explicitly excluded from natural gathering evidence and consumption tests. The first 8+1 waypoint route reached grass using ordinary movement, with health/air1 and final stop acknowledgement. No naturally collected food has yet been obtained. This deliberately favorable climate test is not proof of general autonomous foraging.

## Verified natural food chain
In WetFoodLab, native generation produced mature pumpkin (-139,66,70). Hidden-view terrain streaming initially remained fixed at 3328 columns because native camera PrepareForDrawing normally updates loading centers. Adding native SetUpdateLocation per ready role once/second (visibility0/content64) raised observed loaded columns to12832 and revealed the pumpkin. Single-role hidden-view idle 30.03 seconds after this change measured meanRSS678.02MiB, CPU91.58% (100%=one core), game/wall1.00021; no model. This is NOT a dispersed50-role resource result.

Normal bounded navigation approached the target. Tree-canopy top heights caused a correct conservative stop; a new read-only walkSurface finds actual sand/grass support and records two actual air cells, preserving original canopy topY. A subsequent downward transition stalled under leaves despite clear destination feet space. The guard stopped; evidence remains in wet-detour-result-2.json. A stricter drop-transition body-headroom check plus avoidance of that observed blocked cell allowed an alternative known route. No teleport or collision bypass.

Normal digging changed the natural pumpkin cell131 toair; inventory pumpkin0->1. Native eat transaction then changed count1->0 and Food0.27469644->0.42469645. A later fresh state confirmed pumpkin0, Food0.42010888, native starting bread count1 untouched, health/air1. Final stop was acknowledged. The first state read lagged behind the completed transaction and initially reported false; it was retained with a later read-only verification, with NO repeated eat. Evidence: natural-food-chain-results.json. The script now waits for a post-transaction state update; that polling refinement was syntax checked, not another consumption run.

This is a real natural acquisition/consumption minimum chain in deliberately favorable native-generated climate, executed by bounded scripted skills. It is not autonomous model-led survival, repeatability across seeds, sustained nutrition, or proof of arbitrary-world exploration. Main SCAPI compatibility remains unproven. Original dry and50-role worlds remain separate and untouched by this test.
