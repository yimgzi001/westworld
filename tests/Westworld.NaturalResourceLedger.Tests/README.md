# NaturalResourceAcquisitionLedger 纯审计契约

新 ledger 在 experiments/scapi-tool-agent/NaturalResourceAcquisitionLedger.cs，不依赖 Game；宿主 natural 模式已接入，纯测试本身不执行动作。构造绑定 world/actor 原生身份、初始完整 value 库存、审计失败计数及单调时间。

- ObserveTarget(evidenceId, ResourceTarget, now)：目标是 world+格坐标+去光照但保留内容及data的方块身份，或 world+creature 实体身份。宿主负责确保这是实际观察。
- Origin(evidenceId, ResourceOrigin)：宿主将 NaturalResourceOriginFact 映射为 Target 与其原始 EventId/SourceId/完整 value/count/时间。必须精确匹配已登记目标，观察年龄不超过 30 秒。来源事实只证明产出，不证明由该 actor 挖掘/击杀，不证明自然生成而非夹具。
- Transfer(ResourceTransfer)：原生 NativeAcquisitionFact 映射 SourceId/ActorId/数量等，并由宿主为每次实际事件分配一次 EventId（重复投递必须复用）。此身份不是模型请求 ID。逐 source 守恒，允许部分取得、多 source 同 value 汇总；同值库存增加本身不证明来源。
- Consume：独立可选的原生食用事件加数量变化。仅初始该 value 为零的已归因集合可消费，不分配某个同值单件到特定 source，也不代替 Food/统计量的闭环核验。
- Reconcile：核对实际库存、已跟踪源剩量、失败计数。缺源但账本剩量为零允许原生移除；未知增长、变值、丢事件、失败计数变动均锁定 Unknown。

原生身份均须 NativeAcquisitionAudit.IdentityFor：world=Project，actor=Entity，source=实际返回 Pickable。Kind=block_drop 或 creature_loot。OriginEntityId 不等于 OwnerEntityId 时不得猜采集者；本账本根本不依靠 OwnerEntityId 声称采集行为。

界限：最多 128 观察、256 事件及256种库存值，不淘汰重复记录造成重放。限额/时间回退/来源冲突锁 unknown。来源回调前发生重入转移也 unknown，不补造先后次序。其他 actor 的无关来源忽略；同一跟踪来源转移给别的 actor 当前保守 unknown，尚未实现多人共享分账。

纯测试47项（含5项natural预算隔离及19项光照/内容/data边界）覆盖多来源、部分获取、重复幂等、冲突、缺来源、伪库存增长、时间回退、丢失计数、无关同值资源、独立消费与快照。没有运行真实采集/狩猎，不能用纯通过宣称自然资源链已打通。
