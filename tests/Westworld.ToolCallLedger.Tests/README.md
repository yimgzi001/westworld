# 工具调用内存账本回归

```
./work/dotnet/dotnet build tests/Westworld.ToolCallLedger.Tests/ToolCallLedger.Tests.csproj -c Release -m:1 -nodeReuse:false
./work/dotnet/dotnet tests/Westworld.ToolCallLedger.Tests/bin/Release/net10.0/ToolCallLedger.Tests.dll
```

61/61 纯核心断言通过：session/actor 隔离，严格递增序号，缺口拒绝，非法字段无副作用，完全重复返回同回执，工具/参数/JSON空白变化冲突，容量淘汰后不重放，异常只暴露类型并标 uncertain，uncertain 不重复执行，后端原生错误保留，重入拒绝，线程约束，请求及响应 JSON 在原文档释放后仍有效，后端非法结果保持不确定而不倒退 watermark。

这不是跨进程事务或原生世界测试。账本在 uncertain 或 native_error 写结果后硬性阻止下一写，仅允许只读调用。Reconcile 必须引用原问题序号及随后仍在缓存中的成功只读回执。宿主仍必须判断该读取是否足够解释实际副作用，不能拿任意无关读取自动解除后重试；不能将同命令换新序号当作幂等重试。重新创建账本不得复用旧 session/actor 生命周期标识，否则内存水位不会自动恢复。raw JSON 精确比较意味着空白/属性次序变化会视为冲突，而非语义等价。

新增覆盖：写门阻断且不推进水位、失败/不确定读取不能对账、旧/淘汰读取无效、错误原序号拒绝、读取失败不覆盖原问题、重复对账拒绝、回调内对账拒绝、只读属性重试冲突、原问题回执淘汰后仍保持门直到新成功读取、线程对账限制。原生adapter分类和每player唯一wrapper属于整合测试边界，未在纯核心测试中假装覆盖。

StopOnly 增补：不确定状态允许停止；停止和只读互斥；重复序号修改StopOnly拒绝；停止成功不能充当读取证据或清除旧门；失败/抛异常停止继续阻止新写，记录最新问题序号；再次成功停止仍不清门。宿主对账需要覆盖之前所有未决副作用，不能只检查最后取消命令。
