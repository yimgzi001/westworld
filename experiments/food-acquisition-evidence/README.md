# 世界新食物证据离线验证

`python3 validate.py summary.json` 输出 pass / partial / fail；仅 pass 返回 exit 0。`python3 -m unittest -v` 运行 11 个纯测试方法，其中拒绝测试含 20 个参数化反例。所有测试数据是 synthetic，不是实际原生/模型闭环成功证据。本工具不启动游戏、API、进程或执行模型代码。

## 输入契约

与原生 fixture worker 对齐 `food_chain_evidence` 摘要 schema 1（直接传摘要对象）：
- `sessionId, actorId, sourceId` 非空；完整 `value` 不等于 `initialFishValue`；`baselineCount=0, sourceCount=1, uniqueSourceThroughout=true, inventoryInjection=false`。
- `acquisition`：sourceId/value 匹配；inventoryBefore=0、inventoryAfter=1、worldBefore=1、worldAfter=0；method 为 native_automatic_collection 或 model_pickup_native_receipt；time 为单调秒。worldAfter 是原源剩余可获得量，原 Count/ToRemove/实际物体身份仍须原始日志保留，移除本身不是拾取证据。
- 显式模型 pickup 另需 `pickupReceipt {requestId,sequence,nativeStatus:'succeeded'}`，且 dispatches 中唯一 act_PickUp 对应；否则不能声称主动拾取。自动拾取明确归因原生 gatherer。
- `eat`：requestId/sequence；value/eventValue 都匹配新食物；inventoryBefore=1、inventoryAfter=0；foodBefore/After 有限且上升；statsBefore/After 恰好 +1；nativeStatus=succeeded；time。必须绑定 dispatches 中唯一 act_Eat；顺序是实际获得后 dispatch、再终态。
- `dispatches`：每项 requestId/sequence/tool/time，同一单调时钟，不倒退。
- `shutdown`：stopTime/drainedTime/budgetSeconds、idle=true、lateDispatches=0。停止到停稳不得超预算，没有 stopTime 后新 dispatch/食用终态。

缺项或未完成 null 返回 partial；存在具体矛盾、初始鱼冒充、重复请求、非有限数据或迟到执行返回 fail。保存部分完成日志，不补造成功值。

## 边界

仅验证宿主记录内部一致性，不是日志防篡改证明。sourceId 与真实 Pickable 对象关联、观察确实来自传感器、唯一来源持续监测等，仍须审阅原生宿主实现及原始事件。摘要不能用来替代原始记录。当前约束单个来源、单份新食物；多份/变质/其他来源需另建明确归因契约，不能自动放宽。关闭后的迟到执行只能在实际观察窗口内排除，不能证明未来永不发生。
