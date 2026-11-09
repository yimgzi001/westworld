# 动态小动作计划纯协议

ToolDecisionPrompt 不依赖游戏程序集。宿主按真实可用能力传入 ToolOffer：名称、描述、关闭额外字段的参数 schema，以及真实引用白名单。原版 ActorTools.GetTools 的参数描述没有完整数值范围，宿主应补充边界和当前库存/引用约束后再传入，不能原样当作执行许可。

模型输出自主阶段 goal 与最多4步工具调用，0步表示等待。没有固定造工具、狩猎、烹饪路线。schema和独立解析都限制工具/参数/引用；执行器每步仍需重新验证，失败或需要新观察就停止、观察、重新规划。不支持任意代码和跨步臆造输出引用。

支持有限JSON schema子集：object/array/string/number/integer/boolean，enum/const/oneOf，required，数值与长度边界；不支持的关键词拒绝。引用字段须由宿主明确标注；解析器不凭文字描述推断原生权限。参数关联约束（如slot/value配对）应以oneOf窄化，不能提供独立枚举的笛卡尔积。

测试仅验证协议，不运行模型/游戏/工具。
