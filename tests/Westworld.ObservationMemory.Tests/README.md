# 短时观察窗口回归

运行：
```
./work/dotnet/dotnet build tests/Westworld.ObservationMemory.Tests/ObservationMemory.Tests.csproj -c Release -m:1 -nodeReuse:false
./work/dotnet/dotnet tests/Westworld.ObservationMemory.Tests/bin/Release/net10.0/ObservationMemory.Tests.dll
```

27 个纯数据断言覆盖：3 秒时效边界、观察角度合并、actor/session 隔离、单调时间、异常无副作用、近期重复观察拒绝、最新可见值覆盖、未知不刷新历史、格预算与当前位置半径、逐格来源、历史非当前视觉、旧快照接口及障碍更新失效。另原投影测试 41 个断言继续通过。这些不是原生世界运行证明。

窗口仅代表 `Time` 时刻。执行前必须取得新的真实观察并更新窗口，不可保留旧窗口无限使用。核心仅接受受信任传感器，不能辨别伪造的观测 GUID 或时间。近期 ID 去重最多 4096 条且受 TTL 限制，不是永久防重放服务。窗口不含上游未提供的眼睛朝向。历史证据不保证世界没有变化，必须结合短步移动、原生碰撞与动态高度/液体监控，不能称为全知地图或未来安全保证。
