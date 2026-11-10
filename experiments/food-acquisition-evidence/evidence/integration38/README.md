# 阶段 38：关联 Eat 参数与原生食用契约

四场人工确定性夹具的参数契约独立通过。**只有 valid_eat 完成食物目标，另外三场宿主目标失败原样保留；本批没有自然模型运行，也没有模型改善或长期生存结论。**

## 实际结果

| 场景 | 契约 | 宿主退出 | 宿主秒数 | 事实 |
| --- | --- | ---: | ---: | --- |
| wrong_slot_value | pass | 1 | 54.6957226 | slot 1/value 89 错配被真实公开 schema 拒绝，无原生 Eat |
| over_count | pass | 1 | 54.0789813 | 实有 1 个却提交 count 2，被拒绝，无原生 Eat |
| fresh_slot_change | pass | 1 | 9.1321727 | 同计划真实 TransferItems 成功后，旧 slot 的 Eat 在新快照重验被拒绝 |
| valid_eat | pass | 0 | 7.7331532 | 当前 slot 0/value 89/count 1 合法，来源、转移、原生食用事件与食物链验收通过 |

前两负控保留实际约 45 秒无有效回复等待及 `model_round_budget_exhausted` 原因；这是确定性文件控制器，不是供应商模型失败。协调总耗时另见 runner-outcome，不能与宿主时间混用。

`cases/natural` 只含 staging、预检、来源哈希与 `model-outcome`。官方驱动在进程创建前被审核阻断，未启动驱动/宿主、未输入凭据、无 API 请求或运行日志；API 尝试 0，usage 保持 null。没有另换路径或重试，没有重放阶段 35 的旧未知请求，旧账单未知结论不变。

## 来源与离线回归

最终宿主 SHA256：`d9232e5704fa24d249c8ac3fd8e324826935fb273e98d5946dcfd7f40b6d1ac0`。142 个构建源码检查无变化；43 个独立纯测试日志计 2,822 项。初次 IntegerBranches 类型读取异常、修复后结果及兼容检查均分开保留在 `preliminary/`，不拿初次失败当最终通过。重复压力循环属于断言数，不是独立能力场景。

`batch/frozen` 是实际准备/驱动/验收来源，保留前版和最终 Python 测试输出。最终原生验收器 10 项纯测，不与其他套数累计混算。生产源码通过同批提交提供，不复制整套源码。下一批能力 39 的文档是计划，隔离原型及未完成代码不进入本证据。

## 可读无损日志格式与审查

没有压缩包或不透明编码。每场：

- `receipt-templates.jsonl`：4 行完整、可直接读的原始导航状态收据模板。
- `events-readable.jsonl`：非导航状态事件原行保留；重复导航状态用 `_receipt_table` 引用模板，`columns` 列出字段名，`rows` 保存每条收据的原始数值文本。Sequence、时间、速度、状态等没有丢失或补造。相同字段在不同嵌套位置重复时以 `[2]` 区分。
- `console-unique.log`：完整 console 中不属于 JSONL 的独有诊断行。清单保存原行序分段，能够无损重建完整脱敏 console。
- `manifest.json`：原始文件 SHA256、脱敏重建 SHA256、字节数、行数及转换规则。原始本地文件不修改。`content-review.json` 给出完整还原后的事件类别、数量、验收、扫描结果和有限扫描边界。

例如表格 `{"_receipt_table":0,"columns":["Sequence","elapsed_seconds"],"rows":[["100","1.20"]]}` 意为取第 0 个完整收据模板，仅把这两个数字的原文替换为 `100` 和 `1.20`。这是命名数据表，不是指令。还原器不执行任何日志内容，不进行插值或补点。所有原始请求/响应、坏参数和拒绝证据均保留。

内容仅含获准测试角色游戏状态、库存、公开菜单/schema、随机会话 ID、动作与用量/预检结果；没有私人文件、密钥、游戏本体、世界或存档。测试角色 health 不是人的医疗信息，游戏 session ID 不是认证令牌。完整解码文本和重建 console 均扫描后再验收；有限模式扫描不能替代绝对安全或防篡改证明。

## 验证与还原

```sh
python3 -B experiments/food-acquisition-evidence/evidence/integration38/verify.py
python3 -B experiments/food-acquisition-evidence/evidence/integration38/verify.py --check-sources
python3 -B -m unittest discover -s experiments/food-acquisition-evidence/evidence/integration38 -p test_receipt_table.py -v
```

verify 完整还原四场，逐字节核对 SHA/大小，再以冻结纯验收函数重新核验参数契约和实际食物链，并核对 checkpoint、矩阵、构建与安装哈希。`--check-sources` 另核对生产仓库及归档的冻结准备脚本。不会执行游戏、驱动或 API。食物正控是人工夹具，不冒充自然场。

需要读取原始逻辑 JSONL 时，调用同目录 `receipt_table.decode_log(templates_text, events_text)` 即得完整脱敏文本。原始工作记录仍存在时，`archive.py --write` 按 `work/integration38-publication/collection-plan.json` 可重建相同包，拒绝覆盖不同已存内容。
