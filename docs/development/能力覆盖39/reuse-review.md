# 阶段39：统一工具代理与批量能力覆盖复用审查

2026-11-10。只读设计；未修改生产源码、未访问用户电脑、未运行 GUI 或 API。下列“已有”仅指代码存在，不能等同所有能力已由模型在自然世界验证。

## 结论

**先打通统一能力目录与通用宿主，再一次批量验收，不再把每种食物/动作做成新代理。** 当前瓶颈不只是缺少动作实现：Game 已有移动、姿态、骑乘、库存、制作、战斗、设备、社交及任务执行，而实验 ToolAgent 的静态 `NativeToolOffers.Profile` 只接入其中一部分。必须分别记录“原生实现 / 当前角色具备 / 当前事实可调用 / 参数正确 / 原生验收 / 模型使用”，不能将菜单缺项一概说成框架做不到，也不能直接删过滤就宣称全能力完成。

最值得复用的是现有 ActorTools、ActorTaskExecutor、RecipeTaskComposer、NativeToolSession 和异步交接，不是另造技能规则树。MC 借鉴接口分层与生命周期；SC 物理、制作、营养、遮挡、身份和持久化继续使用本项目原生事实。

## 证据范围和版本

- 本项目只读当前工作树（含阶段38接线）；下文末尾给出关键文件 SHA256。已知此前发布基线见 `docs/development/当前执行状态与恢复.md`，如阶段35 `73dc1d7243827412a4ac633deee087005327c6eb`、阶段36 `de95ecd9fb65a4a24127c697257c23d008232368`。**不为未发布工作树杜撰 commit。**
- MC 来源为已入库的固定版本源码核查 `docs/development/MC框架源码对照-2026-11-09.md`，本次未重新联网获取最新版，也未运行 MC：
  - [Mindcraft e1c1fc3](https://github.com/mindcraft-bots/mindcraft/tree/e1c1fc3ffa481506c2445fc639e16dba504b5920)
  - [Mineflayer 5a252e6](https://github.com/PrismarineJS/mineflayer/tree/5a252e6d258882b042e60e21c37d552cdf3b3380)
  - [pathfinder b07a419](https://github.com/PrismarineJS/mineflayer-pathfinder/tree/b07a419a4181d242bc4b9f8648a4f624e587c18f)
- main 的合入实现已经在当前 `game/Survivalcraft/Westworld/{Upper,Cognition,Models}` 可读，不需要再复制一份 main 任务系统。此前 main 50 Forager 夹具不代表50个完整Player生理或分散区块生存通过。

## 1. 成熟框架对照：采用、缺口、不适用

|能力|固定源码证据|本项目已有/可复用|缺口与不能照搬之处|
|---|---|---|---|
|环境与库存事实|Mindcraft `src/agent/library/world.js`，Mineflayer `lib/plugins/blocks.js`|`SharedVoxelScanner`、`ActorQueries`、`ActorWorldMemory.Capture`、本人库存及覆盖统计|MC附近实体/已加载块不是SC当前眼睛可见；不可把未知null当air。当前视觉、焦点和对象距离并非一个统一无限范围|
|地方记忆与摘要|Mindcraft `memory_bank.js`、`history.js`|`read_map`历史位置、`LocalObservationMemory`短证据窗、`ToolContextMemory`与结果投影|缺完整人格长期经历检索/持久化统一接线；命名地点可加但不是实时资源权限。不能以摘要覆盖库存/收据|
|连续寻路|Mindcraft `library/skills.js:goToGoal`；pathfinder `index.js`、`lib/human.js`|`LocalNavigationSkill`、`BoundedLocalNavigation`、真实走廊/跨阶证据、停止稳定与同owner连续段|局部短程不等于任意地形全局导航。复用增量搜索/partial思想，不复制MC碰撞尺寸、自动破坏搭桥、每AI40ms预算|
|复杂技能|Mindcraft `skills.js:craftRecipe/smeltItem/collectBlock/attackEntity`|SC `ActorTaskExecutor`、`RecipeTaskComposer.Compose`、`ActorTools.Plan`已经有顺序计划/部分结果/失败分支|需把合适任务入口接同一代理，避免每tick询问模型；不把“找木头→造武器→杀牛”写死为唯一生存策略|
|执行生命周期|Mindcraft `action_manager.js`；Mineflayer `physics.js`|唯一 `AtomicActions`、`NativeToolSession`、取消/隔离、原生run与收据、`AsyncDecisionHandoff`|MC async不证明同角色预取已正确；不能删除前序/身份/期限复验，也不靠延长旧动作遮掩慢推理|
|自动目标推进|Mindcraft `self_prompter.js`|`GoalExecutionContext`、当前食物进度、预算事实、`ToolOutcomeDelta`、当前事实重规划|应提供通用持续循环，而非每场景定制；不把空计划自动替换成行动、不让无进展回环无限消耗token|
|多人通信|Mindcraft `conversation.js`|SC Speak/OfferItem/RespondToOffer/read_social 原生目录，P4 `ResourceDeliveryTask`纯领取交付契约|当前模型Profile未接社交，P4未等同联机交付。复用消息合并，不照搬每条聊天暂停工作或全员广播触发N²推理|
|重启恢复|Mindcraft `history.js` save/load|SC `ActorTaskCheckpoint`、`CognitionRunLedger`、当前请求占位/对账|不能仅恢复聊天然后重跑旧动作；世界与角色任务checkpoint需匹配，外部请求未知状态不得重发|

## 2. 原生工具与模型菜单之间的真实缺口

依据 `ActorTools.Catalog.cs:28–181` 与 `NativeToolOffers.cs:11–18`：

### 已在当前 Profile 中（但逐场模型实测覆盖仍不同）

- 观察/知识：observe、read_self/environment/inventory/storage/item/map、find_items/creatures/recipes。
- 控制：action_status/cancel_action、task_status/cancel_task、host导航/状态/取消。
- 动作：Dismount、MoveTo、FaceTowards、Wait、Eat、PickUp、SelectItem、TransferItems、Dig、Place、MeleeAttack、Craft、Jump、Sleep、WakeUp。
- 组合制作：plan_craft。

实际还受当前actor能力、挂载状态、可见目标、槽位、工作台、配方、所有者忙闲和预算限制。以上列表不能证明“模型任何时候都能看到这些工具”。Eat38已加入真实槽/value/count配对，不从未来背包生成参数。

### Game 有、当前模型 Profile 未接

- 移动/姿态：SetPosture、ClimbTo、Mount、Steer。
- 装备：read_equipment、Equip、Unequip。
- 工具/远程：UseItem、UseAimedItem。
- 配置：read_sign、read_block_settings、read_item_settings、ConfigureBlock、ConfigureItem、BuildFurniture。
- 社交交付：read_social、Speak、OfferItem、RespondToOffer。
- 通用组合计划：submit_plan。

这不是建议立即把所有名称塞到Prompt：例如 Mount需要当前实体类型，ClimbTo需要真实梯子，Place需要可用面，配置需要settings结构，社交需要有效offer和真实听众。应一次性补齐这些**参数来源适配器和能力状态**，批量回归；无法满足的返回明确unsupported/not_currently_available，而不是静默归入“AI太笨”。

### 长期技能不能重复实现

`ActorTools.Plan.cs` 的 submit_plan 最多12声明步骤，支持受限的前向成功/失败跳转、有限重试；`ActorTaskExecutor.MaxSteps=64`是底层能力，不是外部计划可直接64步。已有 PickUp→Eat 的 `item_from_step` 只绑定真实前步骤收到物品，且拒绝会改库存的中间步骤。`RecipeTaskComposer`基于实际配方/槽容量生成TransferItems与Craft，终态验收AffectedCount。

建议接入时复用这些机制，但通用模型代理必须检查：任务归属、取消排空、step预算、fresh引用与恢复限制是否在 NativeToolSession 下统一。不要在外面再建一个同时驱动AtomicActions的executor；也不要将未完成的后继槽位提前伪装为当前库存。

## 3. 建议一个完整批次：统一工具代理 + 能力登记

### 接口结构，不新增生存策略

1. **CapabilityDescriptor**（可由现有目录派生）：工具名/原生action、当前角色支持、参数所需证据类型、读/写/任务/停止类别、当前可用性与缺失原因、执行反馈类型、恢复语义。
2. **Fact-derived bindings**：同次公开观察的当前实体/方块/掉落引用、本人槽/value/count、可达工作区、实际配方、社交offer。复用Eat38参数相关性机制，向Transfer/Select/Equip等推广，而非各写一套parser。
3. **统一代理循环**：同一snapshot→当前事实/目标进度→当前菜单→模型返回→现有parser→派发前fresh验证→唯一NativeToolSession→结果/当前新观察。普通与预取走同一事实投影，预取不能拥有未来物资。
4. **任务层可选执行**：模型可以选择原子动作或现有plan_craft/submit_plan；本地维护同一任务到终态，报告部分完成；模型不必每格/每0.66秒重新选择目标。
5. **反馈统一**：接受≠执行成功；区分Rejected/Running/Succeeded/Failed/Cancelled/Unknown。历史action-result delta与当前背包分开；原生来源/实际获取/实际食用证据属于验收，不能注入隐藏地形进Prompt。
6. **范围与预算**：不是每轮发全目录；先读能力摘要再按实际事实给可调用工具/分页详细schema。若增加目录发现接口，必须明确它是能力文档不是新的世界观察，避免扩大感知。整体Prompt/Schema限额、partial/omitted信息与未知usage维持现状，不静默删能力后声称完整。

## 4. 一次构建、多场景矩阵，而非一次动作一轮开发

先把接口绑定和结果适配放在同一批源码里，再固定SHA/输出，串行GUI批测、纯测并行。建议覆盖矩阵：

|组|正控组合|同批负控|成功证据|
|---|---|---|---|
|观察/记忆|转头发现、历史位置回访、本人库存|墙后、未知、输出裁剪、过期引用、跨actor/session|同snapshot、覆盖统计、无隐藏权限|
|移动/身体|步行/跨阶、蹲站、上下骑乘、可用梯子|取消、空中、支撑消失、目标离场、超时|真实路径/位移、动作run、稳定停下|
|库存/装备|选择、转移、装备/卸下、拾取、丢弃、食用|错槽/value/count、并发变化、满背包|真实前后槽差/来源，不因accepted判成功|
|采集/制作/烹饪|Dig取得物资→原生配方任务→取真实产物；炉输入/燃料/输出|材料不足、配方不符、工作区失效、任务中断|原生变化与部分完成；不是注入成品|
|战斗/使用|持工具→瞄准→近战或原生Use能力|冷却、失视、超距、弹药不足|hit/miss/unknown；死亡/掉落独立证据|
|社交/协作|真实听众、OfferItem→RespondToOffer、单任务部分交付|重复offer/领取、掉线、取消、恢复未知|双方库存及唯一交付事实，不能聊天当完成|
|持续代理/恢复|完整自然采集食用、慢决策继续有效任务、重载对账|晚回复/重复回复、来源迟到、未知API结果|目标事实、等待/行动时间、请求成本，无盲重放|

有人工夹具的组明确标component control；自然资源和真实模型留最后独立整体验收，不能用夹具通过替代模型自主。首批不必把每种装饰/电路配置都原生验证完成，但能力表须如实标“未适配/未测”，不要使其永久隐藏。

## 5. 优先级和文件所有权建议

- **核心集成**：NativeToolOffers/当前Prompt/NativeToolSession，一个负责人统一能力和绑定；其他人只新增分组factory及测试，不并发改同文件。
- **任务技能**：独立核对 ActorTools.Plan、ActorTaskExecutor、RecipeTaskComposer 与唯一owner；先复用再补缺失，不另造猎食流程。
- **批测**：独立通用fixture/validator，按能力family造正负输入；每例同构报告catalog/offer/dispatch/receipt/run/actualEffect。
- **独立审查**：覆盖表查遗漏、恢复/跨角色引用、秘密与预算；纯测、场景准备、审查并行，原生GUI/模型错峰。
- **第一验收重点**：统一代理确实能在同一运行宿主覆盖观察、移动、库存、采集、制作、食用和失败重规划；不要求每段都调用模型。后续再将相同代理实例化为多角色，不把通用化和50角色压测混为同一目标。

## 6. 风险与不作承诺

- 不能宣称生成代码是唯一复杂任务能力：现有声明式任务已有真实组合执行；也不能因有计划系统就称自主生存已通过。
- 不能照搬MC自动破坏寻路或全区块扫描，违反当前感知与资源语义。
- SC食物腐败、完整value、装备/炉容器和角色生理不同，必须以原生配方/库存效果为准。
- 人格/记忆可影响模型目标与协作，但不是本地硬编码选择食物/队长；本批只补接口和证据。
- 扩工具数量会增加参数空间和Prompt成本，需报告覆盖与真实使用，不拿工具名数量衡量自主能力。
- 现有阶段38是Eat配对及接口质量改善，不等同本报告整套工具族/长期任务/多人协作已验收。

## 本次实际读取的源码指纹

以下SHA256为本次本地读取工作树版本，供集成确认，不是发布commit：
- `docs/development/MC框架源码对照-2026-11-09.md`: `acb54b0a86cfd73f10508f15a4a592b7b69645c159b8af661de03506a6537429`
- `game/Survivalcraft/Westworld/Upper/ActorTools.Catalog.cs`: `fa22ab220a7d477351dd29ff37f10616b0d2371941d15e2bd58605fbc82ed1c7`
- `game/Survivalcraft/Westworld/Upper/ActorTools.Plan.cs`: `008e1c39064dcf722a741947fa1487d3abfeec4634233acc95565ea24a4687e3`
- `game/Survivalcraft/Westworld/Upper/ActorTaskExecutor.cs`: `1a95aedc3a8e836084af50b29cc8779e1da8df86bb1e16c7fcfaef917710bdfa`
- `game/Survivalcraft/Westworld/Upper/ActorTaskCheckpoint.cs`: `d85e6c02cf23f92f436e5d5ab028822aa0108adc6a779d5ac54d47d065d1c53d`
- `game/Survivalcraft/Westworld/Upper/RecipeTaskComposer.cs`: `fc1eac54c8580d76d2b4b54e102d217478dc29832ed7409038ba0cc794379d4a`
- `experiments/scapi-tool-agent/NativeToolOffers.cs`: `461f6483eb3c7e0b23b8119a40ea9ce50ebaef58bfd262f7a6c0062621da3ab9`
- `runtime/Westworld.Scapi.Adapter/NativeToolSession.cs`: `76807d219fe51cde9e85e7618b00df6dede6af762b84fd505e1cf87c3b2ca0fd`
