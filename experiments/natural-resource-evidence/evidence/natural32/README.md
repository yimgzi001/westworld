# 阶段 32：真实身体状态与有界转向扫描证据

本包已封存三场结束的确定性、非模型原生实验。结果为 **a 失败、b 失败、c 通过**。失败如实保留；这里不是模型能力、自动脱骑、食物获取或长期自然生存的通过报告。

## 实际结果

| 用例 | 实际 seed | 结果 | 原始证据 |
|---|---|---|---|
| natural32-a | natural31-a | 14.265059 秒，预算耗尽，0 个原生动作 | observe 中 isMounted=true、hasParentBody=true、grounded=false；菜单提供 Dismount，但驱动没有执行。8 轮空计划，来源账本 awaiting_origin。 |
| natural32-b | natural31-b | 12.3370956 秒，预算耗尽，未取得资源 | 未骑乘、无父刚体、着地、immersion=0。3 次 FaceTowards 均有原始调用 receipt、同 runId 的 action_status=succeeded 及终态事件；转向成功不等于采集成功。 |
| natural32-c | tool-agent-20261109 | 8.7890005 秒，通过 | 实际可见方块 → 同 runId 的 Dig 成功 → 自然掉落来源 → MoveTo 成功及原生 automatic transfer。取得 **1 份 value=2 的土块**，不是 2 份物品。 |

- a 的 immersion 随时间变化，8 份原始 observe 范围是 0 至 0.042977065。约 0.028 只能描述某一次采样，不能代表整场或首帧；首份 observe 和首份提示投影都是 0。
- b 三次 FaceTowards 的调用序号为 19、39、59，原始 action_status 成功序号为 21、41、61。c 的 Dig 调用序号 19、MoveTo 调用序号 46，成功状态 receipt 首次分别为 28、234。
- c 有 1 条 origin、1 条 transfer，世界数量 1→0，土块库存 0→1；没有食用。初始熟鱼 value=162 是既有库存，不计作新获取。
- 三场 updateFailures=0、AI 视口数为 0、actor GameWidget 为空；没有地形、位置、库存、食物或生命夹具注入；收尾 idle 且 lateDispatches=0。
- a/b 的 `model_round_budget_exhausted` 是沿用的宿主原因标签。本批没有模型 API，不能据此认定模型失败。

## 源码与构建

三场各自的 133 份冻结源码完全一致；本包逐份核对，不用当前工作树冒充运行源码。

- source-hashes.json SHA256：`2c8a0fd6a4f9de948eaef3f8bbd22e9875c29a6da892cea7ac3dcab0a7c87e21`
- 最终宿主 ToolAgentGate.dll SHA256：`53da6b020b9a3f1a26e1161abd506edeb0040cf63e1d2aed403e1f0463b82ff0`
- `work/natural32-validation` 为最终构建/回归证据：宿主 0 错误、29 警告；28 套回归逐日志末行合计 **1110 项**；其中组件状态 23、菜单 135、投影 42 已含于该总数，不重复累计。Python 61 项通过。
- `source-before.json` 记录最终构建的源码哈希清单，结果报告 changedSources 为空。运行冻结清单与构建哈希仍是不同层次的证据，不能单凭哈希宣称游戏全部由本包独立重建。
- 7 份 body 相关 C# 测试文件是另行固定的归档时源码，不在 133 份运行源码清单内。没有把它们冒充 stage 冻结文件，也没有声称它们受 source-before 完整覆盖。
- `work/body32-*.log` 的 10 份前置记录单独标记，包括初次和第一次修正后的序列化异常，以及后续局部通过日志。不能把这些日志冒充最终完整构建。

## 校验与重建

在仓库根目录执行：

```sh
python3 -B experiments/natural-resource-evidence/evidence/natural32/verify.py
python3 -B experiments/natural-resource-evidence/evidence/natural32/test_cas.py -v
```

若原始已结束 `work/natural32-*` 输入仍在，可重复生成相同内容：

```sh
python3 -B experiments/natural-resource-evidence/evidence/natural32/archive.py --write
```

归档器先检查三个 finished checkpoint、host 退出文件和 batch finished，再读取场景日志。只读取白名单文本及二进制哈希，绝不运行游戏、驱动或 API。独立验证器解码全部引用，验证源码、分片、压缩内容、checkpoint 与退出状态，并重新执行固定 SHA256 的纯离线 `validate_log`。本阶段还直接检查原始 observe 和 action_status receipts，不能仅凭 driver 自述宣称动作成功。

继续复用阶段 31 的内容寻址 gzip/base64 方式；每片不超过 29000 字节。同内容不重复打包，依赖既有 `natural31`、`food30` 和 `food30-controller-audit` 包的已归档分片。**必须保留 manifest 所列依赖；只复制 natural32 目录不完整。** 没有改动旧包。发布需包含 `*.gz.b64.part*` 全部分片。

日志及配置中的本云仓库绝对路径替换成 `<CLOUD_REPO>`，同时保留原始哈希和脱敏文本哈希；源码按原字节归档。逐内容凭据特征扫描无命中，但有限规则扫描不是绝对保密证明。本包不包含世界、私密存档、游戏二进制/资产、依赖、密钥或模型原始账本。

## 保留的边界

纯离线验证确认的是记录内部一致性，不是不可篡改证明。来源绑定仍采用单角色、同格、活跃 Dig 窗口，不是多角色严格矿工因果 ID；不支持动物杀手归属。仅剥除原方块光照位 15360 的逻辑沿用阶段 31，但本次成功方块原始值 8 不含光照位，不能当作非零光照位实跑覆盖。b 证明三次有限转向可执行，未证明全向探索完备；a 未自动脱骑，不代表该动作已被测试。

## 发布前路径脱敏修正

发布扫描发现构建警告中的本机NuGet缓存路径和控制台系统字体路径仍未替换。本包已在不修改原始work证据、不改已发布旧包的前提下，分别替换为 `<USER_HOME>` 与 `<SYSTEM_FONTS>`，同步重建内容地址和依赖；原始SHA256仍保存，checkpoint对应的原始哈希不变。旧打包版本保留本地，不混入发布清单。
