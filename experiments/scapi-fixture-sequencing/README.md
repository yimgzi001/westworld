# SCAPI 接入前的跨 tick 夹具协议验证

来源：`archive/survivalcraft/snapshots/cross-tick-wip/recovery-vision-fixture-next`，基线提交 `504e4051d419b47a190ccae1450b3c03120ae17c`。这是新实验分支的纯状态机，不修改冻结快照，也尚未接入 SCAPI 游戏。

## 修复
- 进入 NeedsReconcile 后拒绝迟到 ACK，不能恢复成可继续写入的状态。
- 即使中间没有 Step，超过写入 ACK 截止时间也进入 NeedsReconcile。
- 写入确认后的观察必须晚于最近观测 tick；无效地形 tick 不可在同一 tick 被重新计为稳定观察。
- 保留两次不同且递增 tick 的稳定要求；保留内容/数据、光照、raw 值分别报告。

这是验证协议的正确性，不是削减 AI 游戏能力。没有新增模型费用、角色数或游戏动作限制。

## 运行
要求 .NET 10 SDK，无第三方测试包：
```powershell
dotnet run --project experiments/scapi-fixture-sequencing/Tests.csproj -c Release
```
2026-11-09：67/67；新增测试用于旧 Sequencer 时，首先在“late acknowledgement cannot revive timed-out sequence”失败。原冻结43项仍通过，视觉核心191项通过。

## 尚未完成
这不是生存闭环、游戏模组或原生地形测试。状态机不读取世界、不写方块、不调用模型。
实际适配器必须在游戏线程采集数据、先记写入意图再调用原生接口、确认后回读，不能将网络 ACK 当游戏完成。
参见原快照 README 的完整接入合同；不允许在日常世界自动运行旧夹具。

下一步先用当前 SCAPI 的单个已有角色完成只读观测和有时限输入，再验证2人隔离。超过4人的方案另行设计，不能套用SCNET的50人结果。


ACK接口不带GameTick：适配器须在同一游戏tick内执行Step、写入及ACK，或在ACK前提供写入tick的最新快照；否则不能保证是实际写入后的两tick。恢复超时检查优先于成功计数，迟到第二个样本不得判通过。

统一回归：`./tooling/verify-scapi.ps1 -Dotnet ./work/dotnet/dotnet.exe -BuildGame`。使用全局SDK时可省Dotnet参数。脚本不启动游戏或模型。
