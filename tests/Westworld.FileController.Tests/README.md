# 文件决策协议回归

26 项断言：正确绑定/错 scope/重复字段、非法工具、接收不重放、32 次总领取硬上限（重复与超大文件均占一次）、取消和发布 IO 失败。无游戏或模型运行。

隔离构建：
```sh
./work/dotnet/dotnet build tests/Westworld.FileController.Tests/FileController.Tests.csproj -c Release -m:1 -nodeReuse:false --artifacts-path "$PWD/work/file-cap-isolated-artifacts"
./work/dotnet/dotnet work/file-cap-isolated-artifacts/bin/FileController.Tests/release/FileController.Tests.dll
```

每个请求最多领取 32 个 response 文件。32 次均未接受则终止请求，hash 去重集合最多 32 项。读取时最多分配/读取 65537 字节，防预读长度检查后的文件增长导致无界读取。UTC 只作标签，耗时来自 Stopwatch，并发布 monotonicTicks/monotonicFrequency；收到事件额外记录 responseObservedTicks。

此限制不是文件系统安全沙箱：不能阻止外部进程直接写入大量额外文件、预先写超大文件占盘，或其他同机文件攻击。本测试不覆盖敌对文件系统洪泛、链接攻击或真实游戏动作。桥接仍只接收数据，实际动作由宿主重验执行；没有原生连续行动声明。
