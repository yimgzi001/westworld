# 局部短距离对齐

`LocalNavigationAlignment.Project(window)` 将站立角色的当前格中心作为同层短目标，水平距离限定 0.05～0.7 米。它提供技能内部阶段，不经 LLM 逐步指令，不接入要求至少一米的探索选择器。

完整身体扫掠使用宽 0.5 米、高 1.9 米的矩形包络；每列支撑必须是已观察稳定完整方块，每个身体格必须是已观察空气。历史证明最多三秒，保留来源/时间/历史标签。没有证据就不输出路线。它不证明支撑未来不会变化，不替代原生碰撞、实时观察、中断和超时。

调用者决定是否需要对齐，通常用于没有可执行轴向台阶时；投影器不自行触发移动或模型调用。终点保持原来的 Y，未落地状态拒绝。

测试使用合成几何快照，覆盖短距离阈值、负坐标、当前身体侧边支撑、头部障碍、空气/支撑不足及历史过期，不代表真实游戏动作已通过。

```sh
./work/dotnet/dotnet restore tests/Westworld.Alignment.Tests/Alignment.Tests.csproj --disable-parallel -m:1
./work/dotnet/dotnet build tests/Westworld.Alignment.Tests/Alignment.Tests.csproj -c Release --no-restore --disable-build-servers -m:1
./work/dotnet/dotnet tests/Westworld.Alignment.Tests/bin/Release/net10.0/Alignment.Tests.dll
```
