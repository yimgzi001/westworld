# 历史上下文单次本地推理回放

状态：2026-11-09 已执行一次历史回放，返回 JSON；不是游戏自主成功。

读取 llama06 craft JSONL 的唯一 model_prompt round2，仅以 CompactBounded 替换 OBSERVATION 后的上下文；原工具目录、任务文字、响应 schema 均不变。使用现有 LlamaProcessProvider，4B Q4_K_M、8192上下文、512输出、CPU4线程、GPU0、temperature0、seed42、关闭思考、120秒墙钟上限，只调用一次。

该观察是历史快照，不能用于现实动作。控制台不引用游戏程序集、不执行回复工具、不运行生成代码。原 schema 交给生成语法约束，回复只做 JSON 对象解析；并非当前游戏引用/能力有效性验收。token、CPU、RSS与耗时仅报告实际 LastMetrics / LastAudit，失败字段为空时不估算。

预先构建（仅获空闲通知后，避免共享输出冲突）：

    work/dotnet/dotnet build experiments/local-context-replay/LocalContextReplay.csproj -c Release --disable-build-servers -m:1

在通知模型空闲后运行一次，输出目录必须不存在：

    work/dotnet/dotnet experiments/local-context-replay/bin/Release/net10.0/LocalContextReplay.dll work/tool-agent-llama06-craft/tool-agent.jsonl work/local-model/llama-b12211/llama-completion work/local-model/Qwen3.5-4B-Q4_K_M.gguf work/local-context-replay-fresh01

仅向新 work 目录写 manifest、紧凑提示、原schema、result。manifest记录提示/schema哈希及原后字符数；result记录原始结构化回复和实际资源/失败诊断。不读取或写入密钥。不将未经审查的work日志自动提交；持久化证据由协调者审查、脱敏后归档。与原轮比较属于单次历史重放，缓存/负载等不同，不能作为稳定性能结论。

## 单次回放结果

work/local-context-replay-fresh01：原提示13333字符，压缩后10342字符。真实本地4B耗时100.3506秒（prefill80.5153秒，generation16.3008秒），4021输入token，142生成评估步；CPU累计387.54秒，采样RSS峰4658688000字节。原历史第2轮曾超时，本次在120秒限内返回；不是同负载重复基准，不据此保证加速比例。

回复为 Jump → Wait(0.5) → Jump → Wait(0.5)，没有体现有效采集/制作策略。仅解析、未执行任何游戏动作。完整原回复及LastAudit/LastMetrics位于fresh目录result.json，manifest记录原/后提示和不变schema的哈希。模型请求结束后已释放CPU，未追加重试。
