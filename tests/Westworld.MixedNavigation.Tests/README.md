# 混合导航纯回归

将 `LocalRouteCandidates` 的平地候选和 `ObservedStepRoutes` 的一格上下台阶候选送入 `LocalExploration`。场景是显式构造的完整方块阶地快照，不读取真实地形，不伪装角色真实视野。

覆盖：同一快照中平地/上行/下行共存；证据关联；单在途；错误完成和虚假到达拒绝；阻塞/超时回退与冷却后新观察重试；角色/会话隔离；观察与历史证明过期；跨阶地局部探索前沿排序。测试到达报告为确定性替身，不能证明游戏能跳跃或完成寻路。

```sh
./work/dotnet/dotnet build tests/Westworld.MixedNavigation.Tests/MixedNavigation.Tests.csproj -c Release --disable-build-servers -m:1
./work/dotnet/dotnet tests/Westworld.MixedNavigation.Tests/bin/Release/net10.0/MixedNavigation.Tests.dll
```

只提供离线几何与选择契约覆盖。原生碰撞、跳跃、移动超时和实际观察来源必须另行实测。

`ObservedStepGuard` 额外覆盖已知支撑接触：上阶最大 0.5 米且受配置上限约束，下阶源支撑最多 0.02 米；缺少证明、液体、越界、过大/无效身体拒绝。冻结证明不会替代原生碰撞检测。
