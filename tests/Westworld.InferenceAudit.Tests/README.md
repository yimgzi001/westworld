# 本地推理失败审计

11 项纯测试：正常/部分/缺失/畸形/溢出性能行、输入计数与生成评估计数区分、预检失败、启动前取消。测试不启动任何子进程或模型。

```sh
./work/dotnet/dotnet build tests/Westworld.InferenceAudit.Tests/InferenceAudit.Tests.csproj -c Release -m:1 -nodeReuse:false --artifacts-path "$PWD/work/audit-isolated-artifacts"
./work/dotnet/dotnet work/audit-isolated-artifacts/bin/InferenceAudit.Tests/release/InferenceAudit.Tests.dll
```

`LlamaProcessProvider.LastAudit` 是新独立审计记录，不改变既有 `LocalInferenceMetrics`：
- `Started` 区分启动前失败；`Outcome` 区分预检、启动、进程、非零退出、响应解析、外部取消、超时及成功。
- 墙钟从调用核心入口至清理结束，含最多两段有界清理等待，不等同纯推理时间。
- CPU 与 RSS 是采样值，没采样到则 null，不代表实际零使用；不是累计整机资源。
- 仅当 stderr 有界读取完整成功时解析已输出性能计数。取消导致读取任务取消时，即便进程可能已经消耗 token，也必须记 null，不能记零。
- 生成评估次数不是已证明的计费输出 token。
- 不保留 stderr 原文、环境变量或完整命令行。
- 解析失败可同时保留先前成功进程的 `LastMetrics` 和 `LastAudit.Outcome=parser_failure`。

运行中取消/超时、非零退出、启动失败及真实模型解析失败需要后续真实进程验收；本组测试不冒充覆盖它们。调用者须将 `LastAudit` 写入成功/失败/停机日志，否则内存审计不会持久化。原有 `LastFailure` 行为不变。
