# Structured 4B tool agent (experimental)

This host supplies capabilities and a mode goal, not a programmed digging/crafting/cooking sequence. One real external Player, zero AI GameWidgets, independent activity centre, one AtomicActions/ActorTools owner and one NativeToolSession. All actor reads/writes and status polls go through that session. Fixture setup/audit are explicitly host-only; no fixture coordinates or scripted next actions are inserted into model context.

## Modes

- inventory: untouched native initial inventory; verify a native Eat consumes an owned edible item, increments FoodItemsEaten and raises Food.
- craft: artificial flat space, two world oak blocks, no inventory injection or crafted output. Goal is a real extra wooden club. Model chooses queries/recipe/transfer/place/action steps.
- cook: artificial furnace and raw meat1/plank1 world drops, not hunting or furnace-production proof. Goal requires actual cooked-meat89 inventory decrease through successful native Eat plus FoodItemsEaten/Food increase; eating spawn fish cannot pass.
- hunt: artificial Cow_Brown arena plus world wooden-club drop. HuntingFixture audits native player damage, corpse delay and attributed meat pickup; fixture audit is never supplied to the model.
- natural: explicitly pending (exit3), not a fabricated pass from eating/aging native spawn inventory. Natural acquisition needs provenance rather than naive new-content counting.

Static manual contains item ID/name labels only. Recipe/slot/reference information comes from native results. NativeToolOffers binds current actor-private references; recent read_storage preserves its input storage field and find_recipes supplies native recipe references. No fabricated Smelt or AddSlotItems. Public recipe guide is explicit.

## Async loop and results

Game thread reads observe/inventory and collects actual receipts. Background llama-completion uses verified local Qwen3.5-4B Q4_K_M, context8192/output512, <=120s per call, process-per-request (not persistent pooling). Up to12 model rounds; each proposes <=4 steps through ToolDecisionPrompt. Prompt, schema, raw response and inference metrics are logged. Prompt/schema/token errors remain failures; capabilities are not silently removed to force an answer.

Each pending step obtains new observation/inventory/offers and validates the SAME arguments/opaque references, not a substituted target. Invalid/stale steps clear the remaining plan and return feedback. plan_craft waiting/running means accepted, not succeeded. Every action/task waits for native terminal state before the next step. No next-action guessing while a native task runs.

Any ledger uncertain/native-write-error permits emergency cancel_task/cancel_action through gateway StopOnly without opening permission for another action. Reconciliation first cancels running/waiting/blocked work, then waits for actual terminal state and requires successful inventory/status/observation reads before acknowledging. Any ledger uncertain/native-write-error requires read-only status/inventory/observation reconciliation with actual successful read sequence and no running task/action before new model decisions. Old steps are discarded; no automatic same-command retry. Ledger itself is in-memory, not crash-transactional. Cancel does not roll back inventory or stop independent furnace combustion.

Health<.25 terminates with cancellation. Overall wall deadline595s reserves up to3s for model cancellation under a600s budget. Logs retain failures, actual path/net displacement, native inventory and effects. Native UI/game work is only on the dot cloud desktop in a new isolated work/<run>, never the user's computer. Production/cooking scripted gate results do not constitute success of this free tool-agent host.

## Build / staged run

Build ToolAgentGate.csproj using work/dotnet/dotnet; stage only runtime DLL/JSON/PDB/ZIP/runtimes files into a new work/<name>. Keep ../local-model available. Run `../dotnet/dotnet ToolAgentGate.dll inventory` (or craft/cook) from the authorized normal desktop session. Existing doc/Mods world data causes refusal. `tool-agent.jsonl` and console.log are run evidence.

Current status: inventory free01 native/model positive control passed; craft/cook/hunt not yet run. Native observe/object radius16m, limit4 visible entries per group; not omniscient radius queries.

## Inventory baseline measurement

`evidence/inventory-free01-passed.jsonl`: one real4B round, autonomous act_Eat(slot1,value162,count1), native fish count1->0 and Food0.53145593->1, whole run75.8667784s, UpdateFailureCount0. Prompt10209chars/23tools/3123tokens; inference70.4586561s, prefill62425.44ms, generation37evaluations/3974.46ms, peak sampled RSS4656496640bytes and sampled CPU266.73s. This baseline retains full offered descriptions and process-per-request loading, not a persistent inference pool. Prefill dominates. No other mode result is inferred.

Version boundary: free01 included StopOnly cancellation and cleanup failure handling. Subsequent source review added blocked-as-active and successful read_inventory as mandatory reconciliation evidence; free01 did not exercise those paths, and the deployed free01 binary was not modified during execution. A new deployment is needed to validate those edge cases. Shutdown cleanup errors force passed=false, and Window.Close is protected by finally.

## Compact host input and reproducible staging

The host supplies full fresh observation (including self), inventory and both native terminal statuses automatically. The model inventory encodes nonempty slots plus emptySlotIds and selectedSlot; native schemas, step revalidation and audit retain the full inventory. Idle menus omit redundant reads/status and cancellation; the gateway still supports them, and stop/reconciliation remains host-owned. Native gameplay capabilities are not filtered by scenario goal. Recent results omit duplicated automatic snapshots.

Use `bash experiments/scapi-tool-agent/stage.sh fresh-label /absolute/existing/runtime-assets`. It builds serially with `--no-restore -m:1`, stops on any nonzero build status, refuses existing run directories, copies existing Content.zip (no download), records source/assembly hashes, and writes per-mode run.sh with installed-DLL hash checks. Launch a selected run.sh from the normal dot desktop terminal; scripts do not start UI as part of staging. Model remains in work/local-model and is never copied into the repository.

free03 was accidentally staged from old DLLs after a failed parallel build reported zero compiler errors. It was explicitly stopped and is **not** optimization evidence. The build exit status, not error-count text, is now authoritative; free04 includes the compact-host-free04 startup marker and SHA256 manifest. free01 and free02 were not overwritten.

## Reproducible verification

`bash experiments/scapi-tool-agent/run-contract-tests.sh` builds and runs twenty pure suites, failing immediately on any build/test error. Dependencies must already be restored. These do not prove native world or model outcomes. Run them between resource-measured model trials, not during a trial.

`bash experiments/scapi-tool-agent/stage.sh <fresh-label> <existing-runtime-assets-directory>` serially builds and stages four modes only after a successful build. Each fresh directory records source/DLL hashes, and its run.sh verifies installed DLL hashes before execution. Launch that run.sh through the authorized dot desktop terminal. Do not reuse existing world/evidence directories.

## Compressed-input inventory replication

fresh04 retains native full observations for validation/audit; the model receives only nonempty inventory slots plus empty slot IDs. Idle status/cancel/self/inventory queries are managed by the host, not repeated in its decision menu. Available goal-directed actions remain; this is not a scripted food policy. Compact signatures preserve the full strict JSON response schema, bounds and reference values.

Actual repeat: 1769 input tokens (3123 baseline), 44.738s inference (70.459 baseline), 34.128s prefill (62.425 baseline); complete run49.762s, updateFailures0. The model again selected Eat162 itself and native inventory/Food proved consumption. This is still process-per-request, one observation-to-plan round; no claim of persistent pooling, continuous same-actor replanning or natural food acquisition.

A stale-binary fresh03 run was stopped after its prompt proved the optimization absent. It is not counted as an optimization result. Build success now requires actual exit0, not `0 Error(s)` text, plus deployed marker/hash checks. Existing ImageSharp vulnerability warnings remain unresolved.

## free04 failures and free05 boundary

All original free04 scenarios ended: craft576.41s/health guard, cook504.70s/health guard, hunt505.80s/fixture deadline failed with zero update exceptions. Hunt target health remained1 and attributed damage events0. Failure JSONL and summary are preserved; raw planned actions are not executed successes. The summary includes metrics from a failed model parse, but missing canceled-process tokens remain unknown.

free05 source adds real parameter oneOf alternatives, precise production preconditions, native owned-item and personal-grid facts, bounded learned recipe references, local native attack-readiness waiting, split stop reasons, host-only injury auditing, and cancellation/failure inference audit. Eight pure suites324/324 and serial native build passed; separate free05 worlds were staged and launched. Their actual production/hunt/cook outcomes are pending, not inferred from these tests. The attack timing probe compiles but is not independently launched/native-verified. See the Chinese failure analysis and continuous-skill integration roadmap.

Final free05 results: craft199.60s failed on oversized observation context after find_items; cook534.95s failed with native cold/sickness injury audit; hunt504.77s hit fixture deadline with zero dispatched MeleeAttack. All three updateFailureCount0. Full failure data is losslessly stored under evidence/archives/ with hashes; see free05-summary.json. No natural survival success or native cooldown pass is claimed.

## Ledger local navigation integration (post Terra07)

Marker ledger-local-navigation08 adds explicit host_navigate_local/status/cancel via the same NativeToolSession ledger. The original act_MoveTo remains unchanged. A single same-player actions/skill/lease is attached, with fresh navigation status provided to offer construction. Host polls host_navigation_status until arrived/failed/cancelled; it never substitutes native action_status for skill completion. Owner quarantine fails closed. Health/budget cancellation uses precise navigation request ID then continues per-frame drain; absolute602s closure cannot claim clean shutdown if still active. The original595s work budget remains.

Native ledger behavior was verified separately in scapi-navigation-ledger-gate (flat3m/cancel/wall); this ToolAgent integration is compiled, not yet a real model selecting the new tool. CanPrefetchNavigation is only an eligibility hook, and final audit explicitly says prefetchImplemented=false. Real-model AsyncDecisionHandoff remains the next step; do not describe current ToolAgent as overlapping inference and movement.

## PlanningPhase 回归接入

批测 runner 已加入 `Westworld.PlanningPhase.Tests`（实际 csproj/assembly 名均带 Westworld 前缀），此前默认共17套。新增16条纯断言检查前序真实运行状态与身份、原始目标/参数保留、非未来事实说明及剩余预算边界。不启动模型/游戏；不证明提前规划实测成功。可单独运行 `dotnet tests/Westworld.PlanningPhase.Tests/bin/Release/net10.0/Westworld.PlanningPhase.Tests.dll`。构建失败与测试失败均中止批测，不跳过此套。历史8套统计仍是当时的运行记录，不追溯改成当前套数。

## ObservedFlatRoutes 接入批测

runner追加第18套 `tests/Westworld.ObservedFlatRoutes.Tests/ObservedFlatRoutes.Tests.csproj`，程序集 `ObservedFlatRoutes.Tests.dll`。该套31条是纯观察路线几何检查，不等价于原生连续走路验证。其csproj引用 adapter 输出中的Core DLL，完整聚合前须先确认当前adapter原生编译成功；当前只更新runner并通过bash语法检查，未声称18套完整聚合已通过。历史17套669条仍保留其当时含义。

## Stage17 感知契约批测

新增 `VoxelSurfaceVisibility.Tests.csproj`/同名DLL 与 `NavigationLook.Tests.csproj`/同名DLL，默认20套，沿用串行build与禁用MSBuild server。新增两套只测体素可见性/补看计划契约，不等价于原生感知或游戏运行；ObservedFlatRoutes当前43条也保持纯几何定位。完整本批运行日志独立保存在work/tool-contract17，不覆盖旧批结果。
