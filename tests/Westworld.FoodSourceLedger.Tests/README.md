# 原生食物来源守恒账本

FoodSourceLedger 只审核宿主收到的原生插入事件，不改变世界，不向模型输出全局来源，也不授予动作权限。source 使用真实 Pickable 的稳定 Guid，actor 使用真实 Entity 的稳定 Guid，完整 value 不掩码。事件中的源数量减少必须等于本 actor 背包数量增加，先前数量必须衔接；允许部分拾取累计，但完全获得前不称 Acquired。

目标未转移完就消失、本 actor 获得竞争同值来源、库存无事件变化、目标 value 改变、原生监听失败计数变化、重复/不守恒事件均锁存 unknown。其他 actor 在其他地点获得同值物品不影响目标归因；全球同值数量仅为低频宿主诊断，不是模型事实。

原生 AtomicActions.Inventory.StartEat 先扣槽，再经 clothing 触发 FoodEaten，之后更新食用统计。账本支持该已扣减顺序；若某调用路径事件先于扣减，则只给0.5秒等待扣减，不能无限豁免。终态继续核对原生事件、实际库存、统计和饱食变化。初始同值食物拒绝冒充新来源，初始熟鱼事件不算熟肉获得。

当前29项纯契约通过；不是原生自动/显式拾取成功的证明。fixture已接 NativeAcquisitionAudit.Recorded，清理时先解除订阅，只有状态变化时记录完整账本。ListenerFailures为全局计数，因此其他监听者失败也保守unknown，不能将这种情况误写为已证明的本actor缺事件。
