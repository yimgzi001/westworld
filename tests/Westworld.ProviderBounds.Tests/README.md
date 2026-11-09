# 本地推理提供器参数边界

纯构造器测试，无 CLI、模型推理或游戏启动。使用已存在的测试依赖程序集仅满足路径存在性检查，不将其当可用模型或执行文件。

覆盖上下文 2048–8192 token、输出 64–512 token、超时 (0,120] 秒及越界拒绝。共 42 断言。此测试不能证明提示词实际可容纳于给定上下文，也不验证模型速度、能力、截断策略或子进程取消。

```sh
./work/dotnet/dotnet build tests/Westworld.ProviderBounds.Tests/ProviderBounds.Tests.csproj -c Release -m:1 -nodeReuse:false
./work/dotnet/dotnet tests/Westworld.ProviderBounds.Tests/bin/Release/net10.0/ProviderBounds.Tests.dll
```
