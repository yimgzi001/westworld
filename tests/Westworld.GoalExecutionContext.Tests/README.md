# 目标执行进度投影

运行 `./work/dotnet/dotnet run --project tests/Westworld.GoalExecutionContext.Tests/GoalExecutionContext.Tests.csproj`。只链接纯源码，不启动世界或模型。当前61项通过，旧阶段35草案38项不是最终生产结果。

## 宿主接线

`NaturalGoalProgressBridge.Project(initial, inventory, knownNutrition, ledger, meals, publicEvidence, verifiedHarvestSources)`：
- initial为真实出生库存，inventory是本次既有Snapshot的本人完整库存，未知传null；nutrition只能来自本人已知物品资料。
- verifiedHarvestSources只能包含已真实观察、当前角色真实获得且对应同run原生Dig成功的来源；宿主负责筛选，不能把全部origin审计塞入。
- publicEvidence只链接本人可知的经历摘要，不用隐藏sourceId/世界坐标作公开证据。
- 未获得且未在本人初始/当前库存的来源物品值不输出。一个同值集合混入未经验证的获得来源，保守不声称已验证新来源或消费。
- 原生食用通过`NaturalFoodEvidence.Verified`原验收，不放松receipt/run/来源/数量/事件/food增长/统计约束，并要求消费计数已经入账。

`GoalExecutionProjection.Build`输出不可变快照。六类事实分开：当前可食持有、任意资源历史验证新来源、任意新资源当前仍持有、新可食来源验证、新可食来源当前持有、食用验收。new_source_*只是任意资源，不能当食物条件；new_edible_source_*要求Nutrition>0，未知营养为Unknown。初始同值排除新来源目标；未知来源背包增长为Unknown。晚到自动拾取更新跨动作进度，不回写旧动作的空delta。丢失不等于食用。

预算取权威已发送计数，准备但未发布的request不要冒称sent；已取消不退计数。无统一取消日志则CancelledRequests=null。未知usage为null/UsageComplete=false；已知部分用量不冒称完整费用。请求deadline未分配可null。

本模块不执行动作，不给下一步策略，不判断全部任意原文目标完成。无原生/模型对照结果；缺失/过时Snapshot与账本未知必须由宿主分别传inventoryComplete=false/auditComplete=false。

## 验证范围

包括保留草案38检查，加后台未获来源不可见、未成功来源不认证、实际消费未入账不认证、未知营养、证据容量/空ID、部分usage与未知取消等边界，另外10项覆盖沙砾不会完成食物阶段、初始鱼混合背包、未知营养与食物丢失。中间一次测试暴露未验证来源消费数仍带出导致输入矛盾，修为不投影未验证消费；失败日志保留于work/goal35-production-tests/tests-untrusted-consumed-failed.log。
