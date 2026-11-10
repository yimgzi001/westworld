# 39 原生能力与当前AI接口矩阵（只读）

源码快照时间：当前工作树，含38 Eat尚在集成部分；不冒称已发布。52个ActorTools目录项（31 action、21 query/task），其中30项在NativeToolOffers白名单；另外3个host导航工具。目录可用还依赖ActorTools.GetTools的实际组件能力。Program.Menu再移除自动供给的observe/self/inventory/action_status/task_status/host_navigation_status，所以“白名单”不是实时菜单数量。

## 逐项矩阵

参数完整定义、注册位置、原始目录片段、实际回执路径/行号见native-matrix.json。这里不把receipt returned当动作成功。未找到证据是本轮有界扫描未找到，不声称历史从未测试。

| 工具 | 当前AI | 参数 | 真实前提/缺口 |
|---|---|---|---|
| act_MoveTo | 条件开放 | 无 | 真实/历史destination或相对bearing+distance互斥；生命、未骑乘、独占移动；当前AI1..4m/timeout≤4；游泳由原生Immersion>0.5分支，不是陆地导航技能 |
| act_FaceTowards | 条件开放 | 无 | 当前可见destination或相对bearing；pitch仅bearing；不能用历史map做瞄准；受真实转向/挂载前提 |
| act_SetPosture | **未开放** | posture | standing/crouching；蹲伏需CanCrouch+地面，未挂载/冰封 |
| act_Jump | 条件开放 | 无 | 真实locomotion，可地面/梯子或浸水>0.5；成功代表输入消费和起跳，不是落地 |
| act_ClimbTo | **未开放** | target, height, timeout | 可见梯子block ref、目标绝对feet height、timeout；不能裸传隐藏坐标 |
| act_Mount | **未开放** | target | 当前可见实体且真实rider/mount原生可挂载；不能将block ref当mount |
| act_Dismount | 条件开放 | 无 | 自身当前真实isMounted，原生脱离；现AI菜单仅isMounted=true |
| act_Steer | **未开放** | turn, throttle, duration | 当前实际坐骑/船；turn/throttle[-1,1]、duration原生约束；AI未接 |
| act_Wait | 条件开放 | duration | 游戏时间duration；现菜单.1..10；不是自动等待落地 |
| act_Eat | 条件开放 | slot, value, count | 同次自有slot/fullvalue/count+营养；38关联分支正集成；fresh再验，初始食物保留 |
| act_Sleep | 条件开放 | 无 | ComponentSleep.CanSleep且实际sleep状态；成功是入睡不是恢复完成 |
| act_WakeUp | 条件开放 | 无 | 真实sleep组件；实际醒来 |
| act_PickUp | 条件开放 | 无 | 可见/触觉可及drop ref或附近真实可食fullvalue；count与reach/背包/归属检查 |
| act_Drop | **未开放** | count, slot, value | 自有实际slot/fullvalue/count；真实世界掉落 |
| act_SelectItem | 条件开放 | slot, value | 自有slot及expected fullvalue |
| act_TransferItems | 条件开放 | source, source_slot, source_value, destination, destination_slot, destination_value, count | 当前可达source/destination storage refs，双方slot/expected fullvalue及count；目标value为空槽则0 |
| act_Equip | **未开放** | slot, value | 自有slot/fullvalue、实际clothing组件与可穿物品 |
| act_Unequip | **未开放** | equipment_slot, value | read_equipment命名槽及expected value；真实衣物层 |
| act_Dig | 条件开放 | target, timeout | 真实可见block ref+expected value/face/raycast/reach；timeout≤30；挖完不等拾取 |
| act_Place | 条件开放 | target, slot, value | 可见可交互face、实际持有slot/value；不能把可见中心当合法放置面 |
| act_Interact | **未开放** | target | 真实当前world target，原生行为/raycast/reach |
| act_UseItem | **未开放** | target, slot, value | 实际owned item+可选当前target，原生物品行为；不可一律把target绑block |
| act_ConfigureBlock | **未开放** | target, settings | 真实可达block及其read_block_settings对应typed settings；动态物品/电路/家具不同 |
| act_ConfigureItem | **未开放** | settings, slot, value | owned slot/value+read_item_settings类型化配置，装弹等真实材料检查 |
| act_MeleeAttack | 条件开放 | target | 当前可见entity、实际reach/raycast/cooldown，命中≠击杀≠获得掉落 |
| act_UseAimedItem | **未开放** | target, duration, slot, value | 自有可瞄准物品、真实弹药/等级、当前world target、hold duration；原生Aim/释放效果，不能笼统宣称所有远程武器可用 |
| act_Craft | 条件开放 | workspace, result_value, count | 可达实际grid/workbench，正确排料后preview结果及数量；不自动填料、不烹饪 |
| act_BuildFurniture | **未开放** | target, options, slot, value | 真实连接材料target+owned hammer+options结构；已存在原生实现，未AI接线 |
| act_Speak | **未开放** | text, mode, target | text/mode/可选真实entity；原生可听距离，非全知群聊 |
| act_OfferItem | **未开放** | target, count, slot, value | visible近距recipient+owned slot/value/count；发出offer不是交付 |
| act_RespondToOffer | **未开放** | offer, accept | read_social发出的真实offer ref、accept；物品真实转移 |
| observe | 自动供给，不主动调用 | look_distance, object_distance, limit, block_limit | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| read_self | 自动供给，不主动调用 | 无 | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| read_environment | 条件开放 | 无 | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| read_inventory | 自动供给，不主动调用 | 无 | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| read_equipment | **未开放** | 无 | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| read_storage | 条件开放 | storage | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| read_sign | **未开放** | target | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| read_block_settings | **未开放** | target | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| read_item_settings | **未开放** | slot, value | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| read_map | 条件开放 | offset, limit | live LRU历史位置，offset/limit真分页；仅导航，非当前资源/攻击权限 |
| read_social | **未开放** | 无 | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| read_item | 条件开放 | value | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| find_items | 条件开放 | search, offset, limit | ActorKnowledge个人已知或宿主publicGuide；原生limit1..100，现AI必填1..4；知识不是世界实体坐标/物资 |
| find_creatures | 条件开放 | search, offset, limit | ActorKnowledge个人已知或宿主publicGuide；原生limit1..100，现AI必填1..4；知识不是世界实体坐标/物资 |
| find_recipes | 条件开放 | result_value, ingredient_value, offset, limit | ActorKnowledge个人已知或宿主publicGuide；原生limit1..100，现AI必填1..4；知识不是世界实体坐标/物资 |
| action_status | 自动供给，不主动调用 | 无 | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| cancel_action | 条件开放 | 无 | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| task_status | 自动供给，不主动调用 | 无 | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| cancel_task | 条件开放 | 无 | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| submit_plan | **未开放** | steps | 读取角色私有真实状态/知识；引用类先由观察或对应query签发，分页遵守offset/limit，详见catalogSource |
| plan_craft | 条件开放 | workspace, recipe, count | 已知recipe ref、真实inventory/grid workspace；count为产出件数且整倍resultCount；非燃烧配方；不是采集材料 |

## 原生真实证据分层

1. `work/assistant36-natural/tool-agent.jsonl`：同真实接口的助手控制自然海胆获取与食用，父已验收；本次扫描保留各工具真实receipt所在行，不冒称是模型独立探索。`integration34-food-1/2`有Move/Face/Dismount/Dig及失败/cancel的真实收据，food-1取得未食用、food-2导航失败，不能作为全部生存成功。
2. `experiments/scapi-production-gate/evidence/production-native-03-conservation-passed.jsonl`：确定性人工资源夹具，原生材料转换和工作台制作，result passed/18.1457s/updateFailures0。支持Dig/PickUp/Transfer/Craft等链路，但不是任意配方全部验证。
3. `experiments/scapi-cooking-gate/evidence/cooking-native-03-passed.jsonl`：真实燃料/槽/熔炉处理/食用，160.241s，updateFailures0；生肉是人工世界drop，**不是狩猎**。熔炉是原生更新+Transfer，不存在独立Cook工具。
4. `experiments/scapi-hunting-gate/evidence/hunting-native-04-cow-passed.jsonl`：两人工木块制棒，人工Cow_Brown，原生命中12/未命中4、死亡肉归属和数量守恒，50.458s、updateFailures0。不是野生全环境猎物发现，也未证明远程瞄准/投掷。
5. `experiments/scapi-step-gate/README.md`与对应开发报告：上下阶受控几何，StepTo仅可信host+observed envelope；不应为了“全部接口”把裸坐标StepTo直接开放绕过证据。
6. 装备/脱装、攀梯、Mount/Steer、远程/物品编辑、家具、社交的本次已读真实证据没有建立通过链。源码存在、纯测试存在或catalog注册都不等价真实验收，下一批需分组native gate。

## 容易误称“没有能力”的底层行为

- **游泳**：`AtomicActions.cs:StartDirectMove`在SwimSpeed>0且浸水>0.5时用三维offset写SwimOrder，具有游泳路径；当前工具relative Move目标与实际可见引用不等价自由潜水深度控制。`host_navigate_local`反而要求稳定地面/浸水≤0.1，二者不能混同。当前没有独立Swim/Dive参数工具，也未验收氧气/水下目标/上浮链。
- **远程、投掷**：`StartAimedItem`有真实Aimable/弹药/等级/瞄准射线/释放处理；可能覆盖由原生物品定义的投掷，但没有单独Throw动作，必须用实际物品行为验收，不能宣称全武器通用。
- **箱子/熔炉**：read_storage获取可达IInventory/preview/FurnaceView，TransferItems移动真实槽；Interact可复用原生行为。无需虚构OpenChest/Cook工具。
- **骑乘**：Mount/Steer/Dismount原生齐备，但现在模型仅开放Dismount。boat或mount必须entity refs，不是当前工具工厂通用的block target路由。
- **制作**：plan_craft已能计划真实材料的库存转移与Craft，count是产出量；submit_plan原生支持最多12步，但当前高层模型协议仅4步且没有submit_plan菜单。两者不应被误当同一实现。
- **飞行**：未见独立Fly动作；不能把CreativeFly开关或God mode改设置当生存玩家能力。

## 原生查询/知识/引用链与runtime缺口

- ActorWorldMemory.Capture自动记录真实看到/站过位置并给live refs，ReadMap保留历史；没有目录`remember_location`，不要照搬MC名称说已存在。历史ref只能导航，见面后重新观察。
- ActorKnowledge分个人已知与publicGuide；TeachItem/Recipe/Creature是host知识授予，不是模型可调用世界造物。find_*只是知识查找。
- read_equipment/sign/block_settings/item_settings/social原生存在但白名单不开放；依赖它们的装备、编辑、社交动作一起接，否则模型缺真实参数来源。
- NativeToolOffers目前target只特判Melee→entities、PickUp→drops，其余→blocks。去掉白名单不能正确开放Mount/Speak/OfferItem/UseAimedItem等。
- 当前factory拒绝非标量对象/数组；Configure* settings、BuildFurniture options、submit_plan steps会被丢。ToolDecisionPrompt需有界封闭typed schema，不能把未知object原样放行。
- transfer source/destination slot/value仍按集合绑定，不等价严格storage-slot-value关联；Equip/Drop/UseItem同样需要真实库存配对。Eat38是单点先例，不应再逐个从头实现。
- 当前Prompt最多32 tools，完整52目录+host共55，且schema/context都有预算。统一开放必须设计**完整能力索引+有界可回查目录/按真实参数可用性的细菜单**或分层调用机制，不能偷偷截前32，也不能自动依据饥饿隐藏方向性能力。完整索引让模型知道可调用哪些能力，分页工具详情是接口读取不是策略决策。
- NativeActorSession旧MoveIntent和NativeToolSession/ActorTools都是可调用适配；一个角色只给一个动作owner，不能混用新旧入口造成双写。

## 下一大批建议：一次接成完整接口层，按能力族验收

A. **统一能力元数据与参数工厂**：从GetTools/原生schema建立所有能力索引；标supported/暂不可用/原因/参数来源/完成语义。抽象实体、方块、可交互target、inventory/store关联、知识recipe/offer refs，而非继续多个工具名特判。先保证全部目录能如实被模型发现，不承诺每刻可执行。

B. **统一有界查询投影**：read_equipment/settings/sign/social等typed结果、分页、未知/遗漏、公开来源；静态知识与实时观测分开。元数据能支撑所有动作参数，任何真实原生能力不应因现profile遗漏悄悄消失。

C. **三个并行native场景族**：
- 身体/导航：Walk/Face/Posture/Jump/梯子/游泳水下/船Mount-Steer-Dismount/Sleep-Wake，记录真实身体与取消/死亡/超时。
- 物品/生产/战斗：槽转移/drop/equip、block/item use/settings、弓/投掷/近战、制作工作台/熔炉/家具，逐项实物守恒和原生终态。
- 社交/任务：可听Speak/Offer/Respond和12步任务计划、取消/重载/依赖物品绑定，不能把聊天文字当完成交付。

各族用现有底层动作，不硬编码模型目标。先确定性同接口验证成功+前提失败，再给模型完整能力索引自由选择。日志/费用/请求预算一次统一，避免每个动作再做一套主循环。当前不改生产、不GUI/API；此盘点是现有实现与缺口，不是已交付新能力。
