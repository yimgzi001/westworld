# 阶段37最小原生降噪回归

仅采样审计变更，未启用 ToolAuditCoalescer。全部非模型file控制，零API。此目录只准备/验证，不修改主源码或旧证据。

三场串行，同一冻结host：
- arrive：`handoff file early`，复用原 continuity driver，delay1，3m→1m。检查两个原生host请求、真实路径、Running/Settling/Arrived、单次后继、无迟派发。
- cancel：`handoff file route_cancel_after`，同driver。既有fixture在真实位移>.1m后原接口取消，检查Stopping→Cancelled、无后继派发、停稳。短运动可能不足两个Moving样本，持续运动覆盖必须unknown，不为过线推迟取消。
- readiness：`handoff file direct_readiness`，复用35 driver及其独立validator。Jump脉冲成功后即时导航body_not_ready、segments0、失败首样本保留；真实稳定后收尾，不自动重试。错过离地窗口保持未覆盖。

这是最小能力回归，不涵盖碰墙执行失败、run替换、伤害/来源/Eat新现场；这些旧原始证据只读保留。没有FPS/CPU/模型速度提升结论。

## 准备与单入口

`prepare.py --root <repo> --build <冻结输出> --content <Content.zip> --expected-host <SHA>` 使用food stage显式build_output，再仅在全新stage替换mode；不使用旧continuity stage的硬编码Release输出。

生成 `work/integration37-{arrive,cancel,readiness}` 和 `work/integration37-native-batch/run-matrix.sh`。准备时只复制文件/hash/begin checkpoint，不启动进程。协调者GUI终端运行：

```
bash <repo>/work/integration37-native-batch/run-matrix.sh
```

每场driver65秒，native watchdog80秒，终止只针对runner创建的进程组，不全局kill；按90秒级有界收尾。失败仍记录并继续，唯owner排空不确定则不启动下一场。checkpoint仅预声明runner-outcome，原始缺失明确missing，不造结果。原生日志只读验证、写独立native-validation与evidence-hashes；矩阵结束matrix-result。

## 采样合同与覆盖口径

schema2真实字段 `boundedState/segmentRequestId/sampleTrigger/sampleIntervalSeconds/coverageUnknown/suppressedSamples`。只验已记录数据，不从删去的帧推断真实位置。常规periodic>=.1秒；边界允许快于10Hz。首样本、运行/停止/终态不可消失，idle periodic不得刷屏。样本间隔与真实单调时钟相等，>.15必须标unknown，回退明确记录。

新validator显式使用**.15秒**，不改旧analyze.py的.1或重算旧通过结论。覆盖分母为同request、相邻Moving样本对及其实际秒数，分子还需同segment/lease、真实间隔(0,.15]且无coverageUnknown。分别报告相邻对覆盖比例与时间比例、gap、observed chord。它们不是整场连续运动比例，边界外/缺点区间不能补点。

取消极短运动可由真实前后位置与native path证明发生位移，但Moving对覆盖仍为unknown。生命周期审计verdict与continuousMotionCoverage分列，不能将audit pass写成连续覆盖100%。终态后角色其他动作不借用旧导航采样声明运动归属。请求/回复瞬间强采样尚未接线，不能宣称已覆盖。

抑制量只统计已输出记录携带的suppressedSamples，最终未输出尾段没有凭空计入；sampleCount+suppressed是已记candidate，不是游戏全帧数。字节/条数可报告，不推导FPS。保留旧失败原件，未采样到的事件不凭host Passed替代。

纯测试：`python3 -B -m unittest discover -s work/integration37-cases -v`。

## 收尾审查加强

取消/fixture停止边界到 final 全区间不得出现新普通写，精确同request停止与只读清理允许。Arrived 要求 Remaining≤.4，终态样本与原始 terminal 同request且真实时间差≤.15、无lease与低速；末次public状态要求owner_quarantined=false。以同session/actor的最后predecessor物理样本验证速度与Walk/Turn静默，缺字段为unknown。readiness另核stop_drained的同Jump run与host request；旧early/route模式本来没有stop_drained，使用既有同owner原始终态与cleanup action/task收据，不制造不存在的drain事件。18项纯测包括Remaining10、末态仍moving、stop到final间偷偷派发、quarantined与缺输入反例。

caseMaximumSeconds=90是记录的预算意图；实际runner实现80秒watchdog加两个自有进程组各有界终止，并非独立90秒硬实时外层。读文件/hash/OS调度不承诺硬实时。
