# 原生 MoveTo 异步后继：独立回归驱动与证据验收

本目录仅包含受审固定数据驱动、离线断言和合成纯测试。它不启动游戏、GUI、模型、网络或真实 API，不执行模型生成代码。执行协调者在 dot 云电脑串行运行已冻结原生宿主，使用 fresh work 目录和 batch checkpoint。不能把本目录的纯测试通过称作原生实验通过。

## 场景

首行动是**一个**原生 `act_MoveTo`，相对 bearing=0、distance=4、range=.25。人工平地角色朝 +X。timeout=4 秒，保持当前公开菜单上限，不能绕过宿主 schema。后继单个 `act_Wait(duration=.2)`，用独立 runId 与终态验证实际执行一次。

- `direct_early`：第二文件请求延迟 .2 秒，必须由日志证明回包早于原生前序成功且停稳。
- `direct_late`：延迟 8 秒，必须证明回包晚于前序成功停稳。原地等待是真实结果，不为制造重叠延长移动。
- `direct_failure`：宿主在正在移动时插入真实墙。第二回复延迟 2 秒，允许候选先返回再发生前序失败。失败终态/具体原生错误必须保留；超时不得冒称碰撞机制。
- `direct_cancel`：宿主经现有接口取消前序。2 秒后只向原请求目录写迟包；必须观察 canceled 标记已存在，证明迟包未被接续执行。
- `direct_replace`：宿主取消后通过相同 native 工具接口启动另一 run。2 秒后写旧请求迟包；必须有不同实际 runId 与身份替换拒绝证据。
- `direct_stale_reference`：预留。仅从第二请求公开 offers 选真实 PickUp 引用，不编造引用/坐标；必须先补齐精确失效证据和 PickUp schema 字段，不能计入首五项验收。自然自动拾取导致旧引用失效与人工强制删除不是同一机制。

所有延迟是固定驱动等待值，不代替实际日志时序。人工夹具不是自然生存、模型自主、长期可靠性或 50 人能力证据。

## 驱动

```sh
python3 -B <stage>/sources/experiments/direct-move-prefetch-evidence/driver.py <stage> --case direct_early
```

同 stage 需保留 `sources/experiments/navigation-continuity/driver.py`，复用其 256KiB 上限协议读取与 UUID 校验。驱动只读公开 request 与取消/退出标记，不读日志、身体、世界或隐藏审计。读取当前 offers，限定固定工具和参数，宿主仍做权威完整 schema 复验。

最多两个响应、65 秒；唯一开始标记拒重放、原子硬链接拒覆盖、拒路径符号链接、拒 actor/session/observation/request 错配。正控尊重取消。负控 deliberate retired delivery 只是向**原已退休请求**写固定 JSON，不能产生新请求或绕过宿主。结果记录是否真的在 canceled 后投递、响应 SHA 和单调时间。请求没到、driver没投递或host提前退出均保持事实，不算迟包测试通过。

运行结果 `direct-driver-result.json` 只证明驱动行为，不证明游戏动作。每个请求在 driver 本进程只处理一次。无需网络或模型密钥。

## 离线验收

```sh
python3 -B experiments/direct-move-prefetch-evidence/validate.py <stage>/tool-agent.jsonl \
  --driver-result <stage>/direct-driver-result.json --case direct_early
```

CLI 退出码：0=pass，1=fail，2=unknown。输出包含两份输入 SHA256、逐项断言及物理采样指标。缺失身份、终态或日志是 unknown；明确矛盾/额外派发是 fail，绝不根据 fixture 的 Passed=true 自动补证。

证据合同沿用统一宿主：

- `predecessor_bound`：sessionId、actorId、receiptSequence、runId、precedingKind、tool、arguments、position、monotonicSeconds。
- `predecessor_physical_sample`：同身份、status/moving/settled/cancelRequested、position/velocity/walk/turn、monotonicSeconds。定时采样之外，身份/状态/停稳变化须有边沿样本。
- `predecessor_settled`：成功前序真正停稳的身份与单调时刻。
- `prefetch_requested`：真实 lease 与 precedingRunId；response/invalidated/taken 绑定同 requestId，taken 给 fresh validationObservationId。
- `native_call_started` 每条显式 `candidateRequestId`，普通调用为 null；native receipt 以 `data.runId` 绑定实际运行。terminal 对应 runId/sequence。
- 负控 `direct_fixture_injection` 用 `before.runId` 与 `replacement.runId`，不把工具名、请求序号或时间邻近当作 native run 身份。

仅累加同 run/actor/receipt、两端实际 moving 且推理未完成的相邻身体样本；跨 >.15 秒缺口不插值，标 coverage unknown，不将调度迟缓误说动作错误。host 汇总 overlap 不是独立物理证据。路径是采样折线下界，不是每一帧真实轨迹；缺口保持公开。真实成功4m判定还要求朝 +X 净位移3.5–4.5m，横向≤.5m；后继仅在成功停稳和收包之后一次派发且真实 Wait 成功。

## 纯测试

```sh
python3 -B -m unittest discover -s experiments/direct-move-prefetch-evidence -v
```

测试覆盖固定延迟、拒第3请求、取消/退出、旧响应不覆盖、身份/符号链接/菜单界限；合成日志覆盖早/晚/失败/取消/换run/旧引用、缺证据unknown、假overlap不算真实位移、重复派发、身份错配、时钟倒退、采样缺口、旧观察、提前派发与未完成排空。合成日志在内存生成，不写入原生证据目录。

截至准备时原生首五场由协调者另行运行；本文件不预填任何原生通过结论。stage 源码与结果可分别冻结，任何最终验收修改要保留其版本与回归日志。

## 首场准备失败保留

协调者首场 `work/direct29-early` 使用 timeout=8，但当前公开 MoveTo 菜单上限4。driver严格拒绝，未投递动作；该目录保留，不当作MoveTo原生行为失败或通过。后续driver改timeout4，补现实菜单上限回归，必须新目录重跑。此修复不扩大生产菜单权限。

## 五场矩阵一键准备与显式执行

准备命令只创建文件和检查点，不启动游戏：

```sh
python3 -B experiments/direct-move-prefetch-evidence/matrix.py \
  --root /workspace/scratch/5b9c67580fe7/westworld-cloud \
  --label direct29-v3 \
  --build-output /workspace/scratch/5b9c67580fe7/westworld-cloud/work/visible-facts-host-isolated/bin/ToolAgentGate/debug \
  --content /workspace/scratch/5b9c67580fe7/westworld-cloud/work/tool-route23-delay1-repeat1/Content.zip
```

同一label不得重用。准备创建五个 fresh work 场景目录、独立 installed/source 哈希、同版本 driver/validator、五个 begin checkpoint 和一个 `work/<label>-matrix/run-matrix.sh`。五场间源码或二进制发生变化会拒绝，保留不完整准备目录，不删除或自行重放。源文件与DLL的一致构建关系仍由协调者通过冻结构建确认。

协调者在已获授权的 dot 云电脑 GUI 终端明确启动：

```sh
bash /workspace/scratch/5b9c67580fe7/westworld-cloud/work/direct29-v3-matrix/run-matrix.sh
```

runner串行启动固定file driver与既有run.sh，每场driver自身65秒，原生/driver共同80秒看门狗，必要时仅结束本runner创建的进程组；随后离线validator最多5秒，全场预算90秒，实际超限仍记录失败，不承诺操作系统硬实时。普通失败、退出非零、缺日志或验收unknown均继续下一场。唯有进程排空不确定时停止矩阵，避免重叠GUI。

每场写 `validation.json`、`matrix-case-result.json`，记录真实退出码、原始文件SHA、缺失文件和超时，不补造原始日志/driver结果。checkpoint只预声明这两份由runner生成的报告，避免driver异常导致原始结果缺失时无法finish；结束后原始文件hash在报告中关联。batch输出 `matrix-result.json`；重复执行入口拒绝自动重放。任何未完成检查点仍只表示未提交/中断，不以旧PID推断进程存活。

矩阵准备/控制纯测试使用合成DLL与受阻subprocess，未启动native进程。当前总纯测试53项通过；原生结论以每个冻结目录独立结果为准，不能由这53项推导。

## v3 五场实测复核与 v4 夹具变更理由

冻结 v3 宿主 DLL SHA256：`d4253c45cf045eef40f32949879fcc36a45af6e20c0fa3b34bd7399319ca9a53`。原始目录 `work/direct29-v3-direct_*` 与各自 validation/checkpoint 保持不变。独立验收为 **2 pass / 1 fail / 2 unknown**，不是五场通过；五场原生 UpdateFailure 均为0。

- early：35项全过。实际净位移3.93474m，横向0；仅累计合法相邻样本的推理运动0.20395s / 0.39867m。回包比成功停稳早1.75112s，成功停稳至唯一后继派发0.21858s，Wait以新run成功。
- late：35项全过。净位移3.95111m，横向0；推理运动1.53124s / 3.38974m。回包比成功停稳晚6.03906s，成功停稳至唯一后继派发6.05793s，等待不能描述成无停顿。
- failure：原生墙确实导致同run `failed / blocked`，并非timeout。相对预取请求，候选于2.04067s被接收，原生失败停稳于2.60066s可见；旧候选没有接续派发。夹具却持续等已被消费改名的response.json，最终60.02719s触发handoff_test_budget，整场fail。另有重叠采样unknown，不能掩盖。
- cancel：取消注入在请求后0.10329s，原生同run cancelled在0.11332s样本可见，停稳在0.49036s。旧响应在失效后1.93492s才投递，实际canceled标记已存在，零后继派发。其余断言通过，但重叠证据不足，整场unknown。
- replace：请求后0.10407s通过原生接口取消并替换为新Wait run，0.10994s发生identity_replaced失效。旧响应在失效后1.91875s才投递，零旧候选派发；替换run独立成功。其余断言通过，但重叠证据不足，整场unknown。旧run的最后采样净位移0.26271m只覆盖换run前，不是整场身体最终0.57057m。

v3三项负控的人工注入都在预取后约0.1秒，而身体采样约0.1秒一次，因此合法决策区间内各仅一条同run moving样本。host内部累计运动不能替代独立的两点位移证据；验证器保留unknown，不补点、不跨run/区间插值、不降低位移门槛或放宽采样间隔。

v4将人工墙/取消/替换注入从预取后至少0.1秒改为至少0.4秒，让相同真实原生行动仍在运行时有机会积累两条以上合法身体样本。这是**测试夹具时机变更**，不是生产行为等待、扩大MoveTo范围/timeout或修改验收标准；是否得到充分样本仍必须由新日志验证，不能预先宣称unknown已解决。early/late驱动延迟、MoveTo参数、身份校验、一次派发与fresh观察要求不变。v4另修已消费候选后失败的夹具收尾，并补同run失败终态处理；新构建与新目录单独冻结，不追溯覆盖v3结论。v4准备必须等协调者提供最终DLL hash，准备脚本本身不启动GUI。

## direct_model: bounded model endpoint fixture (not yet native/API verified)

`handoff file direct_model` reuses direct fixture dismount and artificial safe terrain,
without injected obstacles, cancellation, replacement, food or inventory. The goal is
an endpoint approximately 4 metres along absolute +X from the prepared start, not a
prescribed tool or segment sequence. All existing tool offers remain available.
Endpoint acceptance is independent of prefetch: X displacement 3.2–4.8m, lateral and
vertical error at most 0.5m, speed below 0.1m/s, health at least 0.25, no running
movement/action, quarantine or actor view. Completion cancels pending inference and
retires queued plans via the existing stop/drain path. Thus reaching the endpoint may
legitimately yield overlap without a subsequent handoff.

Limits are 4 model requests, 180 host seconds, at most 45 seconds/request, and 7
seconds drain; the older 60-second handoff-fixture ceiling does not apply to this
case. Stage with `--controller model --handoff-case direct_model`. Staging does not
launch a driver, game or API. Model/effort remain explicit official-controller CLI
parameters, not host defaults. Every run requires a fresh directory and world.

Report endpoint completion separately from chosen tool, NativeMoveTo async coverage,
actual in-flight movement, validated handoff and shutdown. Host cumulative overlap
fields are diagnostic; independent identity-bound physical samples and actual API
request/response evidence are required. A host-navigation choice or nonempty model
queue is not a NativeMoveTo regression. This artificial fixture is not natural
foraging or long-term autonomous survival.
