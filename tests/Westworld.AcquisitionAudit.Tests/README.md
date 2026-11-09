# 原生获取审计纯契约测试

13 项检查：弱对象身份稳定且隔离、监听器抛异常时其他监听器继续收到同一不可变事实、失败计数增加、原始数量与时间传递、不可读库存为 null、取消订阅后不再接收。

隔离构建：`dotnet build tests/Westworld.AcquisitionAudit.Tests/Westworld.AcquisitionAudit.Tests.csproj -c Release -m:1 --artifacts-path work/acquisition-audit-isolated`。

这是事件传输纯测试，不是新原生拾取验收。没有调用模型、GUI 或实例化世界。真正拾取逻辑仍由原有 GathererPlayer / AtomicActions 执行；新增旁路只读数量快照和通知，不能通过测试替代原生插入、部分拾取或经验球回归。

## 宿主接入约束

- 订阅者仅获得不可变标量事实；用 IdentityFor(真实对象) 在宿主映射来源/角色。弱表不保活对象，事件订阅需要 finally 解除。
- Recorded 在原生调用线程同步触发，不允许监听器执行游戏写操作；监听异常单独计数，不传播到游戏逻辑。没有线程调度器或异步交付保证。
- 自动路径 SourceAfter 已是 AcquireItems 返回并写回的剩余 Count，此时 ToRemove 可能尚未赋值；显式路径 Count 和 ToRemove 均已写。不要从事件推断监听时已经完成实体移除。
- ListenerFailures 在验收区间增长、库存计数 null、缺失预期事件、未知库存增加，都应归因 unknown。异常中断的原生操作不补造成功事件。
- 经验球不是物品入库，不发此库存审计。其他自定义 BlockBehavior / mod 的旁路变化可能没有事件，仍须库存对账。
- 这是受信宿主诊断，不是防恶意插件伪造的安全边界。不得给模型额外世界身份/全图信息。
