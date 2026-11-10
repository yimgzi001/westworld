# 阶段 33：fresh 模型自然沙砾获取证据

`natural33-terra` 是新的真实模型控制原生运行，与阶段 30 的 API 结果未知场景不同。本场宿主 **52.6629928 秒通过**，冻结离线 `validate_log` 重算通过。实际 seed 是 `tool-agent-20261109`。

## 已证实结果与边界

- 请求和实际模型均为 `gpt-6.1-terra`，effort=`none`。3 次请求均收到结果并发布，主审计、生命周期事件、宿主响应及请求 ID/角色 ID 对齐。总用量 **28187 tokens**：输入 27969、输出 218、reasoning 0；三次总量分别为 9186、9267、9734。这里保存 provider 返回的用量，不推算美元账单。
- 第一次 `Dig` 原生成功，产生 1 份 value=79 掉落，未拾取；第二次 `Dig` 原生成功，产生 1 份 value=6 沙砾；随后 `MoveTo` 原生成功，发生 **automatic transfer**，沙砾库存 0→1、来源数量 1→0。
- 三条动作链均核对调用 receipt 与同 runId 的 `action_status=succeeded` 和终态，不能仅凭模型计划或驱动自述判成功。
- 模型第三次回复确实包含 `MoveTo` 后的 `PickUp`，但本场只有两次 Dig 和一次 MoveTo 实际启动。**PickUp 只是已排队计划，没有实际调用 receipt；取得资源来自移动期间原生自动拾取。**
- 初始熟鱼 value=162 不算新获取；eaten=0。本次是非食物自然资源获取，不是食物获取、长期生存或多种子模型普适通过。
- prefetchTaken=0、overlapSeconds=0。宿主三次等待响应约 12.257、21.162、5.615 秒，不是“无停顿”。
- updateFailures=0、AI 视口=0、actor GameWidget 为空；无地形/位置/库存/食物/生命注入，收尾 idle、lateDispatches=0。

## 冻结来源

133 份运行源码与阶段 32 相同，按各自冻结 sources 和 source-hashes 校验；内容直接引用旧包，没有再次复制。模型控制器 SHA256 为 `9bb584494d1776994df5e6c4c753fdb083d2dde5ebc5340cfa203fabf5b31501`。

- source-hashes.json：`2c8a0fd6a4f9de948eaef3f8bbd22e9875c29a6da892cea7ac3dcab0a7c87e21`
- ToolAgentGate.dll：`53da6b020b9a3f1a26e1161abd506edeb0040cf63e1d2aed403e1f0463b82ff0`
- 构建复用阶段 32 的最终产物及 `natural32-validation` 记录，不声称阶段 33 又重新执行了一轮构建/回归。
- stage.json 原始状态仍为 staged_not_run，这是原有的预运行记录，不改写；完成事实由 finished checkpoint、exit=0、宿主结果和生命周期 ended=completed 分别提供。
- 没有独立 driver 退出码文件，不伪造该文件；控制器结束状态来自生命周期事件。

## 内容和验证

保存获准脱敏的宿主/控制台日志、模型动作与用量审计、生命周期 events、3 份请求及其 schema/接受/响应记录、analysis、checkpoint、冻结源码和构建证据。请求的 prompt/schema 哈希与 API 审计逐项关联；保留原始与脱敏哈希，云仓库路径替换为 `<CLOUD_REPO>`。不包含 Key、环境变量、世界/存档、游戏二进制/资产或依赖；有限凭据特征扫描无命中，不是绝对保密证明。

仓库根目录运行：

```sh
python3 -B experiments/natural-resource-evidence/evidence/natural33/verify.py
python3 -B experiments/natural-resource-evidence/evidence/natural33/test_cas.py -v
```

如原始 work 输入仍在，可重复归档：

```sh
python3 -B experiments/natural-resource-evidence/evidence/natural33/archive.py --write
```

只读取结束用例，脚本不启动游戏、驱动或 API。独立验证器解码所有内容并执行固定 SHA256 的纯离线验证器，复核动作 receipts 和用量链。gzip/base64 内容寻址分片不超过 29000 字节；发布须包含全部 `*.gz.b64.part*`，并保留 manifest 所列旧包依赖。只复制本目录不完整。旧包完全未修改。

日志一致性不是不可篡改证明；来源归因仍限单角色、同格、活跃 Dig 窗口，不是多角色严格矿工因果 ID。没有动物杀手归属覆盖。本次原方块 value=6 不含光照位，不能宣称非零光照位运行覆盖。

## 发布前路径脱敏修正

发布扫描发现构建警告中的本机NuGet缓存路径和控制台系统字体路径仍未替换。本包已在不修改原始work证据、不改已发布旧包的前提下，分别替换为 `<USER_HOME>` 与 `<SYSTEM_FONTS>`，同步重建内容地址和依赖；原始SHA256仍保存，checkpoint对应的原始哈希不变。旧打包版本保留本地，不混入发布清单。
