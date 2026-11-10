# natural_food：自然新食物审计（34）

入口 `ToolAgentGate.dll natural_food file SEED`。全新自然世界、正常出生与初始库存，不平地、不投食、不改健康/位置/饥饿，也不强制下船。预算复用 natural：8 请求、180 秒、单请求45秒及7秒排空。本批未运行 API 或原生世界，以下是实现契约，不是成功报告。

目标文本要求获取新可食资源并食用，由模型依据真实观察选工具与方向；没有南瓜策略或自动选择食物。原有动物/制作工具未移除。当前可证明的是已观察方块→同格实际成功 Dig→原生掉落→真实来源库存转移→该完整 value 的原生食用。动物战利品、制作/烹饪转换尚缺完整归因链，模型可以尝试但本验收不能凭同值库存增长算成功。

复用 `natural_resource_evidence`，新增 mode、meals、foodEvents、foodGoalVerified、conversionAttributionSupported=false。meal 包括 requestId、receiptSequence、runId、完整 Value、InitialCount/AcquiredCount、InventoryBefore/After、EventCount、MatchingEvents、Nutrition、FoodBefore/After、StatsBefore/After、NativeStatus。附带原始原生 FoodEaten 事件日志 `natural_food_event` 与终态 `natural_meal_terminal`。schema 是宿主记录，未喂入模型隐藏来源事实。

原生路径先扣库存，再处理食用并发 FoodEaten，随后增加 FoodItemsEaten。监听器只记事实、更新账本；终态必须同 run succeeded，并验证事件数量=库存减少=统计增加、Nutrition>0、Food实际提升。多份允许部分实际吃下，不凭请求 count 假设都吃完。只把初始该完整 value 为零且已有真实成功来源的食物计入新食物目标；初始熟鱼正常自保食用通过已知余额消费记账，但不算目标。初始与新来源同完整 value 混在一起不做单件猜测。

第一阶段仅取得资源不足以通过 natural_food，可保留部分进度。未知库存变化、无来源转换或事件不符如实归因为 unknown。成功后仍正常排空并复核账本、无迟到执行、真实零 AI 视口；没有以关闭进程掩盖未终止动作。

纯测20项包含完整候选、初始鱼排除、缺事件/回执/run、数量与生理不符及初始自保余额。纯契约不证明原生事件已发生；需后续独立真实场景与原始日志验收。单角色同格活动尝试关联仍不等于多人严格挖掘者因果 ID。
