# 原生资源来源事件纯契约

本目录单独链接新的审计文件，引用最近已构建的云端游戏程序集（`work/visible-facts-host-isolated/bin/ToolAgentGate/debug`）。它不会启动游戏、创建掉落物、调用模型或修改共享游戏构建。运行 `./work/dotnet/dotnet run --project tests/Westworld.NaturalResourceOrigin.Tests/NaturalResourceOrigin.Tests.csproj`。

21项检查覆盖稳定来源/世界身份、完整value/count与方块原值、动物原实体与实际Owner分离、唯一事件ID、有限时间、空返回/无效事实、监听异常隔离与取消订阅。捕获类本身已编译；两处生产调用钩子还需统一游戏构建及原生验收。

来源事件证明的是特定原生调用返回此掉落对象，不证明资源天然生成、不证明某角色挖掘或击杀。角色归因与首次真实观察由宿主账本另行关联。模块回调可能替换返回的物品或Owner，事件记录真实返回字段，不自动改正其归属。

RecordFailures记录无有效事实或读取失败；ListenerFailures逐监听失败计数，其他监听继续收取。不更改原生AddPickable错误行为。事件在原生PickableAdded回调之后，若该回调中发生重入拾取，来源与转移账本须正确缓冲或标记unknown，不能重排为伪造因果。监听者必须及时取消订阅，不能将此host-only记录投递给模型。
