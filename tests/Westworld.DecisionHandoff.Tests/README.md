# 异步决策交接契约

43 项纯测试，无模型、游戏或 GUI。构建输出与游戏运行目录隔离：

```sh
./work/dotnet/dotnet build tests/Westworld.DecisionHandoff.Tests/DecisionHandoff.Tests.csproj -c Release -m:1 -nodeReuse:false --artifacts-path "$PWD/work/handoff-isolated-artifacts"
./work/dotnet/dotnet work/handoff-isolated-artifacts/bin/DecisionHandoff.Tests/release/DecisionHandoff.Tests.dll
```

`AsyncDecisionHandoff<T>` 不是调度器，不执行工具，不推断世界状态。T 必须为宿主持有的不可变候选数据。所有操作限创建线程。

顺序：`Begin` 绑定请求、session、actor、controllerEpoch、planRevision、原观察 ID、前调用 receipt 序号；`Receive` 仅接收完整相同 lease 一次。`CompletePreceding` 必须由宿主从原生终态给出成功，失败则候选作废。没有前动作的空闲请求无需此步。`TryAcquire` 只提供待重验候选，不允许执行；宿主实际重新观察后 `ConfirmValidation(..., valid)`，再 `TryTake` 一次消费。

宿主必须在取消、替换、计划改变时 `Invalidate` 递增版本；切控制者用从未出现的新 epoch。不能凭模型输出确认成功或重验证成功。TTL、世界变化、停止输入后物理稳定、资源条件和目标可见性仍由宿主检查；本类不作隐式许可。保留原 planningObservationId，不以新观察替换原绑定。

单槽实现有意比“一在途加一候选”更保守：占用期间拒绝新请求。每 epoch 默认 4096 请求 tombstone，满时拒绝而非淘汰重放；epoch 历史最多 65536，满需新会话。本实现是内存协议，不提供跨进程持久幂等。

测试包括早回复缓冲、终态先到、失败/取消/替换、错scope/前序、重复及冲突、显式验证、验证后失效、controller切换与epoch复用拒绝、容量及线程限制。尚未接入原生连续动作测试，不能称已实现真实推理/运动重叠。
