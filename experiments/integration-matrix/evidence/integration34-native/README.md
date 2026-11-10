# 阶段34原生控制子包

仅封存已结束的六场非模型、人工生命周期控制；后续三场真实模型自然食物实验不在本包。两类证据不得混称。复用32/33的内容寻址CAS，没有改写原始work、旧包或失败记录。

## 原始与重验并存

- direct_late、direct_failure、direct_cancel：首次独立验收均pass。
- tail_early、tail_late、tail_failure：首次独立验收均unknown。原因是旧validator遇到合法无关的idle action_status.Data=null引发AttributeError，不是尾步骤已经失败或游戏崩溃。
- 修正后的v2验证器仅读同一份原始日志，六场重验均pass；没有重跑游戏。旧frozen脚本、旧run-deterministic-result以及新v2结论分别保存，verify重新执行两版固定SHA的纯离线验证器，旧unknown不能抹去。
- failure/cancel控制通过只表示失败或取消被安全处理，不代表受阻动作成功。tail_failure真实尾转向timedOut，候选派发0次。

三场tail依次验证原始MoveTo、真实FaceTowards尾部、候选Wait屏障。早回尾部停稳至handoff约0.028864秒，晚回约5.658312秒；晚回响应在停稳后约5.646924秒。无失败尾候选越过屏障。所有宿主与驱动退出码0、drainCertain=true、更新异常0。没有统一.batch-checkpoint文件，完成依据是执行结果与矩阵，不伪造checkpoint。

## 源码和回归层次

- 六场各140份stage冻结源码逐一核对，共840次；执行后生成的__pycache__不是源码，已排除；source-hashes SHA256为 `439d9655af8310513d03cf3ab576eefbea03cf397153a5bfbbf8d013dc6af2b0`。
- 宿主ToolAgentGate.dll SHA256为 `b862f2a423d5c5daffa95a15d7be22134d4dc9953bc8f11ddc3b8aed0926a7eb`；仅保存哈希，不包含二进制。
- unified记录1784份来源构建前后未变，36套纯回归逐日志末行合计1461项，及额外DirectTailFailure记录完整保留；20项尾失败测试已经是36套之一，不重复累计。构建manifest的native_pending是运行前状态，不回写成运行后结论。
- P5最初34项离线测试、新版38项均保留；diagnosis-v2较早写37项也原样保留，最终以tests-v2-final及其固定哈希记录的38项为准。
- 补充测试/P5源码单独固定，不冒充140份运行来源。三个tail_actual34夹具是原始日志的删字段子集，保留原行号和原log/driver哈希；verify逐行核对，不接受虚构事件。
- P3/P4纯组件源码/测试不代表已接入原生、实际感知或多人任务投递。这里没有这类原生接线通过声明。

## 校验与后续模型包

仓库根目录执行：

```sh
python3 -B experiments/integration-matrix/evidence/integration34-native/verify.py
python3 -B experiments/integration-matrix/evidence/integration34-native/test_cas.py -v
```

原始work和固定补充源码仍在时，可运行 `archive.py --write` 重建同内容。脚本不会启动宿主、驱动、模型或网络。当前模型场次即使已开始也不读入本子包；收到结束通知后另加模型子包，复用本包及旧包的内容地址，不覆盖本包。

发布必须包含全部 `*.gz.b64.part*` 和manifest列出的旧包依赖。每片最多29000字节。证据中本机仓库、用户目录和字体路径均脱敏，保留原始哈希和脱敏哈希；冻结源码按原字节。禁止密钥、游戏资产/二进制、世界和存档。有限凭据特征扫描不构成绝对保密证明。内容哈希及验证证明记录内部一致性，不是防篡改或独立重建完整游戏的证明。
