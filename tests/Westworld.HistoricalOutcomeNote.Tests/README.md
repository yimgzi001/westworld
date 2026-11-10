# 历史动作结果快照说明回归

链接真实生产ToolOutcomeDelta.cs，与保留的改动前纯实现逐字段对照。18项通过；唯一变化应为固定note，所有身份、库存delta及遗漏数量不变。

说明使用action-result snapshot，不绝对声称action-terminal：ToolOutcomeFeedback.Unexecuted可能更新lastAfter，事实范围以before/afterObservationId为准。当前库存/目标状态可能包含更晚自动拾取，旧空delta不能否定当前持有；不要求下一步Eat或指定资源。

固定说明净增100 ASCII字符/UTF8字节，默认3000字符/6000字节预算不变。紧预算可能触发既有截断或拒绝，测试保证不会偷偷扩容。未测token数，无模型或原生运行。

运行：`./work/dotnet/dotnet run --project tests/Westworld.HistoricalOutcomeNote.Tests/HistoricalOutcomeNote.Tests.csproj`。
