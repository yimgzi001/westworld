# 原生工具Offer工厂链接测试

链接生产工厂 `experiments/scapi-tool-agent/NativeToolOffers.cs`，用与原生记录同形的 `ActorToolDefinition` stub 避免载入/构建整个游戏。协议使用真实 `ToolDecisionPrompt`，测试不是原生工具执行证明。

工厂保留并列的导航、查看、库存、生产、攻击工具，不按饥饿或固定任务链筛选。引用分类来自当次宿主观察：实体只用于近战，方块用于挖掘/放置，掉落用于拾取，存储与配方引用另列。缺必填引用则不提供工具；缺可选destination仍保留相对bearing移动。

recentResults接受原生成功 `{ok:true,data:...}`，配方为data数组中的 `{reference,recipe}`。外部制作区需read_storage证明crafting预览，宿主wrapper额外给顶层 `storage` 标明本次请求的实际存储引用，不能凭物品槽推断工作台。调用者负责同actor来源及当前时效。

局限：slot/value与source-storage当前分别枚举，可能组合不匹配；Move/Face参数互斥也必须由原生再次验证。模型提出计划不是成功。工厂不自动扩展上下文，28工具描述/schema仍需实际推理token预算验证。

超时按动作区分：Dig 0.1～30秒（仍可取消），Move/Face 0.1～4秒；声明timeout字段时改为必填，防止省略后回落原生15秒默认值。Wait最多10秒。observe保留原生16米look/object上限，仅返回条数limit限制1～4，不将性能条数预算误作视距能力收缩。
