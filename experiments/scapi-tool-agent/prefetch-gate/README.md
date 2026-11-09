# Real host asynchronous prefetch lifecycle

Runs the actual ToolAgentScenario, full native survival Player, one action owner/gateway and file controller. Artificial flat terrain, no inventory injection, no API/model in deterministic tests. The external driver sends data-only named-tool JSON; it deliberately delivers old packets even after canceled.json to test retirement, unlike the production paid driver.

A candidate is requested only while the current bounded navigation is actually Moving, native action is running, request displacement>=.15m and no old plan steps remain. One inference slot/one handoff slot; same request ID connects file protocol and PendingDecisionLease. Binding includes session, actor, epoch, revision, predecessor receipt and navigation ID. Overall navigation success and stable release precede fresh Immediate-offer validation of the ENTIRE candidate plan. Ordinary tools still dispatch through NativeToolSession. No skill budget is extended to hide a delayed reply.

## Final controlled matrix11

| case | total wall | actual pending/moving sampled interval | sampled body path | outcome |
|---|---:|---:|---:|---|
| early |12.953s|.286s|.604m|early response buffered; predecessor finished; fresh validation; second real navigation|
| late |16.478s|1.245s|2.109m|predecessor stopped before 8s delayed reply; then second navigation|
| failure |8.842s|1.611s|.340m|candidate actually received/buffered; predecessor wall/TTL failure retired it|
| cancel |7.918s|.101s|.242m|exact request cancelled and drained; old response file delivered but never dispatched|
| expired |11.447s|.300s|.597m|.35s decision deadline retired candidate; original navigation finished normally; old response file never dispatched|

All11 passed/updateFailures0. Moving-interval seconds are adjacent sampled flags, not proof every instantaneous velocity was nonzero. Report the measured body path alongside time. This is deterministic asynchronous infrastructure validation, not real-model inference performance or frictionless walking: segments still observe and settle.

Failures are retained:09-early proved buffer/handoff but fixture ended before its queued successor dispatched.10-cancel stopped safely and retired the candidate, but fixture let a third fresh decision start before late-file arrival; the two-packet driver did not answer, causing60s test timeout. This was not an old packet executing. Other10 cases passed.11 fixes fixture gating, adds post-validation/TryTake and first-dispatch deadline checks. After11, an additional empty-plan fix ensures a no-op cannot leave a stale first-dispatch deadline on a later ordinary plan;12/12 pure DecisionDispatchWindow checks passed. That final small fix plus model-case metadata is not represented as an11 native rerun.

## Evidence/recovery

`evidence/manifest.json` indexes per-run manifests, rawJSONL gzip+base64 chunks (each<=34KB), exact63DLL hashes and begin/finish checkpoint records. Concatenate a record's parts in order, base64-decode and gzip-decompress, then verify rawSHA256. Source snapshots10/11 contain JSON filename→source text maps;09 has logs/hashes only, not a complete old source snapshot. Worlds/assets/keys are excluded.

Before each native case use batch-recovery begin, then normal dot terminal run.sh, inspect actual result and finish passed/failed before editing source. Serial build ToolAgentGate.csproj with -m:1, then `python3 experiments/scapi-tool-agent/prefetch-gate/stage.py NEWLABEL`; stage requires fresh dirs, snapshots6 sources and records DLL hashes. Driver only handles first navigation3m plus one prospective1m; it is not a production survival policy.

## Model case (prepared separately)

`ToolAgentGate.dll handoff file model` does not start the deterministic driver and logs modelDriver=true. Goal requests +X3m then +X1m, but no returned steps are deleted or rewritten. If model prequeues two actions, no prefetch is claimed. Endpoint acceptance checks physical X displacement3.2..4.8m, |Z|<=.5m and idle/stable body; actual receipts determine whether the model chose the requested segmentation. Model/API usage belongs to external driver, not the game. A single real model trial is pending coordination.

## Terra12：真实云模型重叠，但没有有用的后继衔接

`work/tool-prefetch12-terra`，同一完整原生角色，人工平地正控；没有注入背包。三次真实云模型响应由外部驱动交付，本宿主不持有 API key。原生日志及六个部署源码快照见 `evidence/12-terra-manifest.json`、`sources12-manifest.json`；API 计费审计由独立 driver 保管，不包含在此归档。

- 36.5325 秒结束，endpoint PASS、更新异常 0、健康 1，出生鱼保留、进食 0；两次实际导航请求（3 米及 1 米），水平累计路径 3.720047 米，日志水平净位移 3.7200775 米。终态 Arrived、Busy=false、modelInFlight=false、无需对账。
- 第一次导航中真实预取，等待响应期间相邻 Moving/原生动作运行样本覆盖 1.283269 秒、实际身体路径 2.103622 米。不是用全程运行时间冒充推理重叠。
- 预取模型返回空步骤，理由是等待正在执行的 3 米导航。宿主在前序成功且 fresh validation 后接纳了 **no-op**，并非已取得有用后继动作。第三次普通决策才提出再走 1 米。
- 按原始单调时钟重新计算：第 1/3 请求发布到原生调用分别 10.894537 / 7.244128 秒；第 2 请求发布到 no-op 接纳 9.636081 秒。第一个有界导航停稳终态到 no-op 接纳 4.592197 秒，到下一次原生调用 11.864379 秒；no-op 接纳到下一次调用另需 7.272182 秒。
- 上述是控制请求、传输、API/驱动等待及宿主调度合计，**不是纯 API 推理延迟，也不是玩家聊天延迟**。调用时刻也不等于首次物理移动。局部观察和稳定停顿依然存在，不宣称无停顿。
- 顶层 start 的通用 `artificialFixture=false` 对 handoff 模式不准确；紧随其后的 `handoff_fixture` 与本节明确人工平地、modelDriver=true。此处保留原始日志，不修改已测源码掩盖标签缺口。

### 下一语义改进建议（本批未改源码）

将规划请求明确标为 `AfterPrecedingSuccess`，单列前序工具、目标、请求 ID，以及“真实当前状态仅为规划时背景；这些候选只会在前序成功且新观察复验后执行”。告诉模型不必返回等待来保护仍在运行的前序，宿主已保证不会提前抢占；应根据总体目标提出成功之后的下一项工作。空计划仍合法，用于目标完成或缺乏证据，不能强制有动作，也不默认复制任何下一导航。应继续展示真实当前观测，不能将预计终点伪装成已发生事实。下一轮分别评估：有无真实推理重叠、有用后继占比、停稳到下一动作延迟，不能只看 endpoint PASS 或 prefetchTaken=1。

## 13：明确规划阶段后的接线回归

新增 `PlanningPhaseContext`，预取事实明确标注 AfterPrecedingSuccess；保留真实前序模型目标、工具原参数、实际导航请求 ID / receipt sequence、真实 running 状态、当前 observation ID 与剩余期限。普通决策和执行复验不带此未来条件阶段。没有构造未来位置或库存，空步骤仍合法，前序成功/新观察/截止/所有权门不变。handoff 的顶层人工夹具标签修正为 true，12 原证据不回写。

`tool-prefetch13-early` 是确定性 file driver 接线回归，不是模型优化效果：13.901641 秒 PASS，更新异常 0，两次导航、一次交接；pending 期间实际身体移动 0.598480 米、Moving 采样覆盖 0.265306 秒。16 套历史纯回归通过；后续完整17套在FileController期限分类测试失败，待修复验收，新 helper 正式套件 16/16，另由 helper 作者完成 Terra12 第二请求状态回放 1 项。日志、七份源码快照和部署 DLL 哈希在 evidence；随后 Terra13 已准备独立空世界目录，是否真正改善后继计划要以其实际模型响应判断。

最终回归：期限取消分类修复后，work/tool-contract13-fixed 的17套669项全部通过。修后分类仅纯测，Terra13仍对应修前sources13，不能声称原生重测；修后日志与源码hash另见contract13-fixed-manifest.json。首次失败证据保留。
