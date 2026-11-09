# 工具上下文去重纯测试

宿主 API：ToolContextMemory.CompactBounded(JsonElement context, int maxChars=14000, int maxUtf8Bytes=16000) → JsonElement。
分析 API：Compact(JsonElement context) 保留用于回放。
输入为 ToolResultProjection.BuildContext 的输出，返回同结构并附 memoryCompaction。原始 JSON 与审计不变。

- 同引用配方集中到 facts.learnedRecipes，查询结果替换为 recipeRefs；只省略已知 description，完整材料、布局、产出、工作站、热量、等级及未知扩展字段保留。
- 同引用内容冲突时保留原始事实与事件，明确列出冲突；失败、未知结构、分页及遗漏信息不静默删除。
- 历史库存不与当前库存合并，也不合并两个相同 storage 引用的快照。
- 仅精确 {slot,value:0,count:0} 空槽编码为 emptySlotIds，带额外字段的槽位完整保留。炉状态不改。
- CompactBounded 在去重和元信息添加后检查最终字符与 UTF-8 字节预算，参数各限256..16000；超限抛 InvalidOperationException，不返回截断事实。参数越界抛 ArgumentOutOfRangeException。不能用分析入口 Compact 代替宿主预算入口。

纯测：dotnet run --project tests/Westworld.ToolContextMemory.Tests -c Release。
可传入原生日志路径追加真实 round2 回放：work/tool-agent-llama06-craft/tool-agent.jsonl。该日志若未持久化，基础测试仍可独立运行；回放报告不可视为模型加速实测。
本次回放 context 8817→5826 字符，总提示 13333→10342 字符（约减少22.4%）。没有运行模型或GUI。

本次隔离测试含回放共33项通过，涵盖精确预算、元信息增长、多字节UTF-8和参数上下界。接口已冻结。
