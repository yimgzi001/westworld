# Stage17真实连续性复核与严格矩阵准入

来源：`work/tool-route17-delay1-repeat1/tool-agent.jsonl`；脱敏结构化统计 `native17-analysis.json` 包含原日志 SHA256，不包含模型prompt/key。只读复核，没有重新运行。

## 实际结果：首3m成立，整链失败

- 首次只选择1条完整3m route，3个waypoint，原生结算Segments=1，耗时2.6501341s，身体路径2.939888m，终点剩余0.060112m，Arrived。
- route后的连续Moving样本289个，跨度1.32657s，最大相邻间隔0.03960s。两个内部waypoint半径0.2m内分别53/44个速度样本，最低2.20625/2.25147m/s，低于0.1m/s均为0。支持两个内部连接未采到停车，不宣称采样间瞬时保证。
- 预取请求→回复1.13276s，宿主报告移动推理重叠1.12681s/2.47712m。原生终态→下一派发0.16137s，其中宿主终态观察延迟0.13380s、随后派发0.02756s。
- **第二个1m任务失败**：`segment_no_untried_missing_cells`，路径仅0.000191m，仍差0.999809m。整场 `passed=false / controlled_prefetch_lifecycle_failed`，无UpdateFailure。不能因首路线改善而记整链通过。
- 全日志2次 `observed_flat_route_not_selected`，不要省略失败的fallback。状态总耗时包含两个任务，不能都归首3m。

## 现有early PASS仍不足以验收连续route

只读当前HandoffFixture：early/late成功条件检查2次导航、最终Arrived、1次handoff及移动重叠，但**不强制首3m使用完整route**。旧逐米fallback同样可能通过。stage矩阵计划不能把这个旧passed直接当连续化完成。

建议每个正控同时保存两层结果，不改生产能力：

1. **生命周期结果**：旧host结果保留，整链失败不能忽略。
2. **完整route证据**：首receipt实际命令distance=3，绑定其boundedRequestId→localRequestId→routeId；首条route起点对应真实初始姿态，最终waypoint等于获准3m终点，三个waypoint或等价已证实内部边界；首bounded只有1个实际segment，不是最后1m才切换route。
3. **真实连续性**：同owner/route内逐帧速度、姿态、持租、状态，两个内部waypoint前后都有样本，间隔≤0.1s；报告低速次数/持续区间与最低速度。首次起步与最终停稳不混为内部停车。缺帧、没有内部附近样本或缺明确ID只能unknown，不能pass。
4. **安全终态**：首Arrived且目标误差在既有允许范围，原生停止/稳定，随后候选由真实新观察准入，第二任务也真实完成；无quarantine、越权、未处理update错误。保持距离/速度/期限不放宽。
5. **配对矩阵**：1/3/8s各3次全部保留，包括失败；同固定3m目标、源码/DLLhash、世界种子及姿态。8s允许已完成目标后真实等待，禁止为覆盖延迟多走；不靠改模型回应来凑成功。
6. **route专属负控**：必须证实route已运行再注入取消/墙/支撑变化，记录真实机制与停稳；TTL失败不算碰撞机制通过。

目前样本缺routeId、route事件本身无monotonic时间，分析器明确串行事件关联；若要自动严格验收第2/3条，需要宿主额外写上述ID链/route时标，不能仅由离线脚本猜。此次只提交分析与建议，未修改其他worker源码，也未把矩阵状态改为已通过。
