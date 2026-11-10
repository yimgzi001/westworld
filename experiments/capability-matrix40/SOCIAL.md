# 阶段40双角色社交原生矩阵

`CapabilitySocialMatrix40.cs` 是独立 `IUpdateable` 场景，接收恰好两个已完成加载的 `IsExternalActor` 玩家。外层必须在全新隔离工作目录创建一次性世界，然后构造本类并注册到 `SubsystemUpdate`。不得先对相同玩家创建 `ToolAgentScenario` 或其他 `AtomicActions/NativeToolSession`，否则会重复占有原生角色。

接口：构造器 `new CapabilitySocialMatrix40(players)`；每帧由游戏调用 `Update`；读取 `Done/Passed/Reason`；结束调用 `Dispose`，随后关闭/卸载一次性世界。100秒墙钟硬截止。真实启动由总集成协调，本模块只完成编译，未启动游戏。

## 场景

1. 原生下坐骑，等待两个角色不再骑乘。
2. 明确人工夹具：平地、邻接站位、相向朝向。无库存、生命或饥饿注入；物品取发送者真实初始库存。
3. 两个角色分别 `observe`，从实际公开实体引用识别对方；原生 Speak，接收者独立 read_social 验证实听内容。
4. OfferItem不立刻转移；接收者仅使用自己收件箱里的offer引用Accept；原生双方库存计数与接口库存回执同时留证，检查守恒。
5. 已消费offer重放必须失败且不能复制物品。
6. 接收者反向Offer，Decline必须不转移。
7. 再反向Offer，等待真实游戏时间超过16秒再Accept；必须拒绝且库存不变。
8. 无AI视口；显式取消残留动作/任务、复原地形、释放兴趣点。角色和社交注册的最终释放依赖立即卸载一次性世界，不能重用这些角色。

JSONL输出由现有 `ToolAgentGate.Log` 写入当前隔离运行目录，包括 `matrix40_receipt`、断言、库存前后、fixture、result、cleanup。此驱动验证原生接口，不经过模型推理，也不证明阶段39动态菜单或模型自主采用。无效或过期引用如果进入账本未决状态，仅在读取真实库存且动作空闲后核对解除。

## 已完成验证

只编译生产宿主：`./work/dotnet/dotnet build experiments/scapi-tool-agent/ToolAgentGate.csproj --no-restore -m:1 -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false`。
当前0错误，39既有警告；未启动游戏、未调用模型、未进行旧全量回归。实跑结果待总集成记录，不能预先称通过。

## 停止与清理协议

`RequestStop(reason)` 不立即Done；Update停止场景派发，双角色cancel任务/动作并读取task_status/action_status，确认无运行owner后才Done。场景成功与异常同走该drain；7秒仍未知则结果`cleanup_uncertain`且失败。`Dispose`未获idleVerified时不复原地形、不假称清理成功，抛出明确错误并要求外层卸载世界。Dismount只对实际parented玩家调用；人工地形写入前等待两角色观察区域chunk达到Valid。
