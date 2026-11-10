# 阶段39：同帧公开事实映射纯测试

生产文件：`experiments/scapi-tool-agent/CapabilityFacts.cs`。

输入必须是在同一个 world/session/actor/capture 下实际采集的公开原生结果；`CapabilityRead.Data` 是原生结果的 data 部分，`Arguments` 是该次调用参数。失败结果不构成事实。不得给历史缓存换 capture ID；历史位置仅经 `read_map` 进入导航引用，不能作为实时交互目标。

`currentNativeToolNames` 来自同角色当前 `ActorTools.GetTools()`；此入口已经以实际 `GetCapabilities()` 过滤动作。缺失快照为未知，不能由空装备或库存反推组件存在。身体族通过 SupportByTool 按具体动作确认支持，不再因为 ClimbTo 不支持而屏蔽 MoveTo。旧调用不传字典时保持兼容。`ownerIdle` 由宿主当前唯一执行所有者状态提供；本映射器不建立执行器。

社交只消费已有 `read_social` 结果，不触发读取或清空收件箱。原生字段为 `offerReference`；offer 接收者由该角色读取作用域确定。任务完成/转交成功仍须原生回执与库存证据。

原生 `Craft` 的 count 是产物数量，不是批次数；可用数量采用 `materialBatches * recipe.resultCount`，checked 防溢出，并携带原生 ResultCount 作为最小数量和数量倍数。菜单只提供完整批次产物的有界 enum，不引入协议不支持的 multipleOf。制作仍由原生执行验证空间、材料和物品容量。

核对来源：`ActorTools.cs`、`ActorTools.Catalog.cs`、`ActorQueries.cs`、`GameKnowledge.cs`、`AtomicActions.Inventory.cs`、`AtomicActions.Social.cs`。

验证：36 项纯断言通过，零构建警告/错误。覆盖未知/缺失、跨捕获、重复结果、库存配对、装备层次、历史引用隔离、设置目标和持有物绑定、合成产出倍数、社交offer、未来事件拒绝。未运行游戏、模型或API，不代表真实闭环已验收。

运行：

```sh
dotnet build tests/Westworld.CapabilityFacts.Tests -m:1 -nodeReuse:false
dotnet tests/Westworld.CapabilityFacts.Tests/bin/Debug/net10.0/Westworld.CapabilityFacts.Tests.dll
```

本环境默认多节点构建曾返回 `Build FAILED` 但没有编译错误；单节点构建成功。以单节点最终结果为准。
