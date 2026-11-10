# 阶段39：工具覆盖与限制分层审查

状态：2026-11-10只读源码审计；本文未运行游戏、模型或新增接口。工作目标是统一能力适配，不把有限命名工具变成任意代码执行器。当前工具存在于原生catalog，不等于现有模型菜单已开放，也不等于已实测成功。

## 1. 三层边界

1. **游戏/原生接口有效性**：真实可达性、可见引用、材料/工具、库存数量和槽值、骑乘/身体状态、原生冷却、碰撞、原生事务结果。不得为验收通过而改世界结果或绕过校验。
2. **运行时正确性**：唯一角色owner、游戏线程、session/actor/sequence、同序号同参数幂等、uncertain后对账、精确取消、隔离不放写、真实终态、模型候选fresh复验。扩大工具覆盖不能取消这些约束。
3. **实验profile**：工具白名单、每次列出工具/引用数量、结果页长、动作显式超时、模型4步/请求次数/墙钟。它们不是游戏天然能力上限；应显式标记并在统一adapter配置，而不是伪装成原生不支持。

源码依据：`game/Survivalcraft/Westworld/Upper/ActorTools.Catalog.cs`、`ActorTools.cs`、`ActorTools.Plan.cs`；`runtime/Westworld.Scapi.Adapter/NativeToolSession.cs`、`runtime/Westworld.Runtime.Core/ToolCallLedger.cs`；`experiments/scapi-tool-agent/NativeToolOffers.cs`、`Program.cs`、`ToolScenarioBudget.cs`。

## 2. 已有catalog但当前profile缺少的能力

| 类别 | 当前菜单未纳入 | 接线需要，不是只追加字符串 |
|---|---|---|
| 身体/交通 | act_SetPosture, act_ClimbTo, act_Mount, act_Steer | 枚举姿态；梯子块引用与真实高度；Mount必须实体而非默认块target；Steer turn/throttle的-1..1不能套通用0..4 |
| 物品/装备 | act_Drop, act_Equip, act_Unequip, read_equipment | 当前自身槽/value/count关联；装备槽来自read_equipment；物品移动真实差量/返回物 |
| 世界交互 | act_Interact, act_UseItem, read_sign, read_block_settings, read_item_settings | WorldTarget可为不同类型；UseItem可无目标，不能误要求块target；按真实可编辑对象读取能力 |
| 编辑/家具 | act_ConfigureBlock, act_ConfigureItem, act_BuildFurniture | 按实际可读settings/options提供闭合typed schema；当前工厂直接跳过object，不能开放任意JSON设置对象 |
| 远程武器 | act_UseAimedItem | 自身工具槽与可见目标；原生装填/蓄力/弹药/掉落与失败结算；并非开启任意射程 |
| 社会/交换 | read_social, act_Speak, act_OfferItem, act_RespondToOffer | 真实可听/可见的人；speech mode和目标互斥范围；offer来自本人收件事实，双方原生库存守恒；不能从全世界人物表生成引用 |
| 有限原生计划 | submit_plan | catalog已支持1..12声明步骤、0..2安全前效重试、后继/fallback及PickUp→Eat绑定；当前模型协议仅0..4独立步骤，不等同原生计划不存在 |

特别注意：当前通用target规则除Melee/PickUp外默认使用blocks。如果直接把Mount、Speak、OfferItem、Interact、UseAimedItem加入Profile，会产生错误引用类型。需按工具的原生引用种类注册binding resolver。

`submit_plan`是有界命名动作结构，不是任意脚本；如复用，应与现有唯一ActorTaskExecutor统一所有权，不能另建第二套重试/动作队列。预取屏障目前只支持host导航和act_尾，不应把task执行默认视为已支持。

## 3. 参数范围：原生与profile区别

| 参数 | 原生依据 | 当前实验菜单 |
|---|---|---|
| MoveTo相对bearing/distance | ActorTools.RelativePoint强制-180..180、1..4 | 相同，不能称人为缩距；引用目的地可来自真实记忆，仍需执行时可达 |
| Face pitch | TurnCommand强制-82..82，只有bearing分支可带 | 相同；destination与bearing互斥是原生语义 |
| host_navigate_local | 独立有限宿主技能，绝对bearing0..360、distance1..4、max_seconds1..12 | 不是原生相对MoveTo；不能混方向语义 |
| 动作timeout | MoveTo默认15秒、Face默认5秒、Dig默认15秒；原生实现检查正有限并按游戏时间结算 | 要求显式timeout，普通动作.1..4、Dig.1..30。这是实验deadline，非原生最大值 |
| range | 原生MoveCommand默认引用.75/方向.4，具体动作还校验合法正值 | .1..1为profile限制 |
| count | catalog只声明整数；具体操作检查正数、当前数量、材料及产出倍数，不能从JSON schema假定游戏固定64 | 通用profile上限64；Eat再收窄到对应真实槽数量，不得混不同槽和值 |
| 查询limit | knowledge find为1..100；read_map实际1..32，catalog共享描述100不够精确 | 强制显式1..4；可分页适度增加，必须整体上下文预算与omitted标记 |
| observe | forward/object≤16；block输出1..128，地形扫描半径5，视线/加载仍限制 | 宿主固定look16/object16/limit4/block_limit32；不是全世界感知 |
| offset/text/ref | 原生按查询/记忆边界验证 | offset map≤128、其他≤4096；普通字符串max96；refs与整数enum取前128，是传输profile截断 |
| 元协议 | ActorTools submit_plan最多12，部分retry≤2 | ToolDecisionPrompt≤32工具、每plan≤4步骤、response≤16384；可发现工具分组，不应静默丢失能力 |

“放开”建议：先将这些限制登记为命名profile并报告effective bounds与原生默认值；扩大查询分页、动作timeout和count时仍保留有限预算、实际库存/材料绑定和取消。不要简单移除全部上限。

## 4. 当前适配器可立即复用的边界

- NativeToolSession默认allowed=真实GetTools全集，实验NativeToolOffers.Profile才是主要模型菜单过滤。所有新能力仍走同一Call/Invoke账本。
- query分类不能信catalog的Query字样：cancel与submit_plan等在catalog是Query构造，但有副作用；Session按真实read/find/observe/status分类，StopOnly独立。
- 已有导航Busy/OwnerQuarantined应继续阻止其他写；原生cancel_action不伪装能取消host导航。
- 执行前Snapshot→最终公开事实→菜单schema→ParseResponse复验适用于新增能力；历史位置只支持回访，不恢复旧资源操作权限。
- Eat的slot/value/count、Transfer的source/destination各槽值、装备槽、settings模式都应关联约束，避免独立枚举笛卡尔积产生原生无效请求。
- 模型只见真实公共事实；来源审计证明归属，不允许把隐藏源位置/其他角色库存拿来生成可操作引用。
- 初始同value食物不能计新来源是验收保守限制，不是菜单应禁止食用；动物/加工来源未验收不意味着应移除原生工具。

## 5. 一次完整真实验收矩阵（建议，不宣称已测）

每行是一组连贯能力流程，同一版本、同owner；使用自然场景与明确人工正控分层，避免每原子动作另开长期任务。

| 场景组 | 正向覆盖 | 同批负控/终态 |
|---|---|---|
| 身体与交通 | 姿态→跳跃→落地→梯子/骑乘→操舵→下骑 | 空中/骑乘不满足导航、失去目标、取消后真实停稳；Jump成功≠落地 |
| 采集与持有 | 可见资源接近→挖→拾→选择/丢/转移→再读实际库存 | 错slot/value、满包、同值其他来源、失视、旧ref；无资源复制 |
| 食物与生产 | 真实采料→配方查询→有限计划制作→炉原生燃料/加热→吃 | 材料不足、错误工作区、产出倍数、燃料不足、已吃初始鱼不算新目标；不过度要求一次自然模型全链成功 |
| 装备与交互 | 读装备→穿脱；读可编辑设置→合法修改→读回；工具使用 | 不可编辑对象、过期设置、物品变化；共享家具影响明确，不任意重写世界 |
| 战斗与物品使用 | 持械近战、装填/瞄准/释放，实际损伤/弹药 | 原生冷却、目标离开、无弹药、健康停机；不改伤害强行成功 |
| 两角色社会交付 | 原生可听消息→Offer→接受→双方库存与来源守恒 | 错recipient、重复请求、拒绝/离开、未决恢复；P4纯契约不冒充真实转移 |
| 原生计划与异步 | submit_plan有限尾、真实执行中预取、完整尾后接纳 | 前步失败/取消/换run/截止、晚回、自动拾取导致旧参数失效；不自动重放未知副作用 |
| 可观测与预算 | 分页/覆盖/partial/readiness，旧/新快照与菜单一致 | 稀疏/未知≠空、截断≠全部、prompt/schema超限明确失败，token未知不填0 |

全批共同记录：请求/接纳/真实run/终态身份，动作前后实际状态、资源与失败、取消排空、源与程序集hash。先离线schema与适配契约覆盖，再同版本短原生分组正负控，最后少量有界模型端到端。报告采用能力×场景覆盖矩阵，不要求每工具在每时刻/每种环境都实测，不把全工具列入菜单当成功率。

## 6. 发布建议

统一新增能力descriptor（native名、有效schema、profile边界、引用/库存binding、read/write/stop、异步终态类型、证据来源、覆盖说明），由同一工厂生成菜单。保持原生游戏代码优先不改。完整catalog超过当前32工具时，应采用可发现、可切换的能力分组或有界工具目录，而不是固定删掉尾部工具或提高上下文上限掩盖问题。每次执行仍检查当前可用条件；这是能力发现，不是脚本执行或固定生存策略。
