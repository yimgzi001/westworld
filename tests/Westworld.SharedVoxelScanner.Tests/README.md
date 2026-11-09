# 共享局部视野扫描器纯契约

`Game.Westworld.SharedVoxelScanner` 是 Game 层的有界、只读、单次扫描，不引用 Adapter 或 Runtime.Core。`VoxelSurfaceGeometry` 为迁移自18阶段已验证的可见面/超覆盖DDA几何；Adapter旧类只作类型转换，不保留第二份算法。

原生入口：

```csharp
SharedVoxelScanner.Scan(terrain, bodyOrigin, eye, forward,
    above: 1, maximumSamples: 1944, maximumVoxelReads: 4096);
SharedVoxelScanner.ReobserveCell(terrain, bodyOrigin, eye, forward,
    x, y, z, above: 1, maximumSamples: 6, maximumVoxelReads: 4096);
```

调用者必须在游戏线程采样。默认仍为身体原点±4格、Y-2至Y+1的324格，above只允许1至3。中心半径5m与每个表面样点半径5m、120°视锥、浮点实现后的样点仍在目标格、完整已加载矩形、opaque超覆盖DDA、未知遮挡均保留。不是全周扫描，也不加载未知区块。

快照保留原BodyOrigin/Eye/Forward/Above、不可变Cells与Statistics。每格含Value或unknown(null)、实际通过检查的VisibleSample；表面样点不是原生碰撞命中面，不据此编造Face。Complete仅表示指定格网在预算内处理完成，不代表所有格可见。sample/read预算用尽则BudgetExhausted=true、Complete=false，剩余格明确unknown，不填旧值。统计包含候选格/样点/LOS次数/实际voxel读取/同次缓存命中/已知格/已加载矩形检查。

缓存仅存在于这一次调用，不按tick/actor全局复用；原生读取value与opacity被同次缓存。下一次扫描或单格复验重新读世界。结果不带执行授权，不刷新旧ref或旧TTL；ActorQueries及动作仍负责角色、session、目标身份与派发前复验。扫描范围外的ReobserveCell返回unknown，不借单格接口扩大感知。

纯测试32项通过，包含324格与前版逐格算法逐项值/样点一致、预算截断、加载失败零读、未知、墙后、表面地板、转身、重新观察世界改变、read缓存隔离和非法预算。另迁移兼容回归为可见性22项、导航补看30项。未在本目录声明原生世界或多角色性能通过；原生门槛由独立共享观测gate验证。

运行：`dotnet run --project tests/Westworld.SharedVoxelScanner.Tests -c Release`。只链接Game层两个纯文件，无游戏、GUI或模型调用。
