# 公开工作区投影回归

```sh
python3 tests/Westworld.WorkspaceProjection.Tests/run.py
# 若 SDK 不在默认位置：追加 --dotnet /path/to/dotnet
```

每次从当前 `experiments/scapi-tool-agent/Program.cs` 提取 `OwnWorkspace` 方法，再与模拟 `Call` 回执编译。模板不保存生产逻辑副本；方法边界发生变化会明确失败，避免测试旧代码。输出提取方法的 SHA256。

10 条断言覆盖：
- `returned + Data:null` 标记不可用，保留真实公开引用，不生成槽位。
- Undefined 回执同样不可用。
- 正常工作区保留非空槽位、空槽索引和 null crafting。
- failed 回执不可用。
- 无公开 craftingReference 返回 null 且不读取存储。

仅标准库和 .NET 10 SDK；临时工程禁用 NuGet 包来源，没有游戏依赖，不启动游戏、不调用 API。此测试验证宿主投影逻辑，不替代真实睡眠/醒来矩阵。
