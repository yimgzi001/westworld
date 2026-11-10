# 阶段 37：采样降噪、历史结果说明与三场原生回归

本包为可直接阅读的 UTF-8 证据，不压缩、不包含游戏资产、二进制、世界、存档或密钥。三场均为确定性文件驱动，**没有 API 或外部模型调用，不证明自然食物自主成功、长期生存、无停顿或性能提升**。

## 版本与纯回归

- 首轮：`pure-preliminary`，40 套、2,613 项；宿主 `629216e618365c31ed4cfd14d74dc8dcfe39326f3e5e671563c9686e64be9365`。40 份单套输出与最终版本逐字节相同，清单通过 `preliminaryTestAliases` 复用相同明文，不重复存储。
- 最终：`pure-final`，41 套、2,631 项，包含 NavigationSampleGate 的 1,022 项（其中 1,000 项是重复终态压力循环）及 HistoricalOutcomeNote 18 项。数字不是独立场景数量。
- 最终宿主：`3e406ddf7df7197a75b129b793e9be08aa28d9d88171b52c147ca3c886f04a33`。137 个构建源码检查无变动；三场各 151 个冻结源码检查一致，5 个安装二进制只存哈希。
- 旧 `integration37` 三场只完成 staging，零启动，已 superseded，**不是三次失败或三次成功**。保留原 preparation、stage、哈希和后续未运行说明，不制造结果。最终运行使用 `integration37-final`。
- `preparation/tests.log` 为旧 12 项；`tests-final.log` 为最终冻结验收器 18 项。另收协调线程对同一最终冻结文件重跑的 18 项日志，不累计成 36 项。

最终生产更改为 schema2 采样门控，以及历史动作结果快照的一句固定语义说明；不是选择 Eat 的策略。source manifest 对最终构建与已核验基线作差异，旧 `sampling/manifest.json` 仅作首轮记录，不能用它的旧 run-contract-tests 哈希冒充最终 41 套版本。

## 三场真实结果

宿主均退出 0、驱动退出 0、原始验收 pass、更新异常 0，checkpoint 均为 passed。保持失败动作本身的原始事实，负控通过不等于导航动作成功。

| 场景 | 宿主秒数 | 协调总秒数 | 采样结果及限制 |
| --- | ---: | ---: | --- |
| arrive | 10.1196 | 10.4885 | 58 样本，记录抑制 1,351；19 个相邻 Moving 对在本次阈值内，但另有 1 个 coverageUnknown 样本，整场连续运动仍 unknown |
| cancel | 7.0432 | 7.4555 | 20 样本，记录抑制 417；实际净位移约 0.27053 米后取消，唯一 Moving 对只跨约 0.007435 秒，不是整场覆盖 |
| readiness | 6.3573 | 6.6948 | 2 样本，记录抑制 181；真实 body_not_ready，原生导航 segments 0；无 Moving 对，覆盖 unknown |

新离线验收明确使用 0.15 秒间隔阈值，未修改旧分析器的 0.1 秒或追改历史结果。抑制计数仅为已输出记录携带的候选计数，不包含最后未输出尾段；不能换算为全帧数量或 FPS。`coverageUnknown=false` 也不能证明两采样间每一瞬间持续移动。请求/回复边界强制采样尚未接入，不宣称覆盖。

## 食物时序复盘

`food-review` 保留原时点报告与 9 项只读回放、3 项隔离投影测试，11 个输入通过 references 引用已发布阶段 35/36 脱敏文件，不复制原始大日志。原报告在生产固定说明落地前形成，因此保留其历史建议措辞。隔离投影不是最终生产实现；下一批 eat38 和阶段 38 的隔离 C# 草案不在本包。

## 独立检查与重建

```sh
python3 -B experiments/navigation-readiness-evidence/evidence/integration37/verify.py
python3 -B experiments/navigation-readiness-evidence/evidence/integration37/verify.py --check-sources
```

检查逐文件原始/脱敏 SHA256、构建源码链接、checkpoint、runner/矩阵结论和原始证据哈希；从最终冻结验收器抽取纯函数重新验证三场，并复验 readiness 旧语义。不会导入 runner、执行驱动、启动游戏或 API。源码由同批提交提供，未重复整套源码快照。

每场 console 全部 JSONL 行已按顺序共享 `tool-agent.jsonl`，独有的 71 行启动/音频/字体诊断保留在 `console-unique.log`；清单 `consoleReconstruction` 的分段行数可无损重建完整脱敏 console，verify 核对重建 SHA256 和字节数。未删除异常或失败。

私有绝对路径仅替换为标记，原始用例不改。保留工具收据和原始失败/未知；哈希与逻辑一致性不构成防篡改证明。需要既有阶段 35 与 assistant36 证据才能验证食物回放引用。

当原工作记录仍存在时，可按 `work/integration37-publication/collection-plan.json` 复现：`python3 -B experiments/navigation-readiness-evidence/evidence/integration37/archive.py --write`。门禁仅在所有三场已结束后开放，脚本拒绝覆盖不同已存内容。
