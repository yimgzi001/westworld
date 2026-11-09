# 零视口周期动物生成回归

```
./work/dotnet/dotnet build tests/Westworld.CreatureSpawn.Tests/CreatureSpawn.Tests.csproj -c Release -m:1 -nodeReuse:false
./work/dotnet/dotnet tests/Westworld.CreatureSpawn.Tests/bin/Release/net10.0/CreatureSpawn.Tests.dll
```

12/12 原生类派生探针断言通过；不启动窗口、世界或模型。实际运行原生 `SpawnRandomCreature` 与新的位置重载，覆写地形采样/数量查询，验证中心选择、空集合不回退、精确重复中心去重、无效中心、10 次候选尝试、原版随机偏移/高度限制、全局与区域上限、RandomSpawn/location 筛选及不适宜拒绝。

此测试不证明真实世界已触发周期事件、不证明动物出现在演员可见范围，也未启动普通人类 Camera 路径实测。旧 Camera 虚方法保留，原版无 provider 分支仍调用它；默认实现委托位置重载。
