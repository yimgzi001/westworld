# 工具结果预算纯测试

只测试模型上下文投影，不执行游戏或模型。覆盖 18 条超大物品描述、真实营养/燃料字段、完整配方材料及引用、真实空槽与炉状态、未知形状、原生失败、原始审计不变、整体字符和 UTF-8 字节预算。

Project 是模型视图，不替换原始审计或执行引用账本。列表最多展示四条，omitted 为本次原生返回中未展示的条数；hasMore 仅复制原生明确提供的分页事实，否则为 null。原生尚有多少结果未知。物品说明/配方描述不是执行事实，按公开字段投影；材料、配方引用、槽位数据不截半条。单条过大明确 budget_exceeded，结构不支持明确 unsupported_shape，均不能当作原生成功或资源不存在。

BuildContext 保留完整当前 facts，按最新优先加入完整 recent 条目，并标明 omittedRecent。facts_too_large 时宿主不得继续向模型发送缺事实请求。默认总体预算 14000 字符 / 16000 UTF-8 字节，低于现有 16000 字符协议上限，不提高模型上下文。

运行：dotnet run --project tests/Westworld.ToolResultProjection.Tests -c Release。
