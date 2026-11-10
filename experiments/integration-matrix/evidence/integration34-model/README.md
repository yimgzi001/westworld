# 阶段34：模型食物目标与基础设施失败子包

本包封存3场结束输入，不推送、不重放请求、不修改原始记录。复用同阶段native子包及32/33的内容地址。

| 场次 | 分类 | 已知证据 |
|---|---|---|
| food-0 | 基础设施网络错误后中断，模型质量不评估 | 2个不同请求network_error，0模型回复、0动作，主审计usage=null。host/driver均-15且drainCertain=true；没有host最终结果或exit.txt，不补造。 |
| food-1 | 真实模型取得天然海胆但未食用，目标失败 | 58.7716493秒预算耗尽、host exit1；8回复及8宿主接收，89744 tokens。真实Dig→origin→automatic transfer取得1份value=226海胆，未Eat。 |
| food-2 | 真实模型无新资源、未食用，目标失败 | 48.6179289秒预算耗尽、host exit1；8次尝试中6次API回复、宿主接收5次，2次host_canceled用量未知。已知59248 tokens，不能称完整总量。 |

合计18次尝试、14次有用量的API回复、13次宿主接收；已知输入148137、输出855，共148992 tokens。4次用量未知（food0网络错误2次、food2主机取消2次），因此**总tokens与费用均未知**，不能把未知计成0或把生命周期重复记录再加一次。已收到回复的实际模型均为gpt-6.1-terra、effort=none，返回的reasoning tokens均0。

food0原execution.json仍为error:null，这是旧runner中断审计缺陷，不能解释成运行无错误。独立食物validator因为缺失最终证据返回fail，也不等于模型失败或已完成自然食物试验。该场没有ended事件，不伪造成功结束；停止证据来自原execution的进程退出与drainCertain。

## 真实运动与边界

food1两个官方请求在真实同run运动期间尚未返回，连续有效样本分别覆盖约2.224463秒/3.459809米和2.126760秒/2.274088米；不跨缺失样本插值。宿主prefetchTaken=2，但这些运动区间不等于服务器推理时间、零等待或整个任务成功。food2有运动请求区间，但没有成功handoff，须按请求分开读独立摘要，不能仅凭overlapSeconds字段宣称续接通过。

food1最终health约0.8323且未Eat；food2原导航终态包含segment_start_InvalidOperationException。均保留原日志，不用零updateFailures掩盖动作失败。两场无夹具注入、0AI视口，结束排空idle、lateDispatches=0。初始熟鱼仍在，不算新获取。食物来源/食用全链、长期生存与多人能力仍未通过。

## 执行与修复版本

三场149份冻结源码相同，source-hashes SHA256为 `be471c7923419a415d5221d4468dfaae5584efd56c3817b80e669ffad97fad3a`；宿主SHA256为 `b862f2a423d5c5daffa95a15d7be22134d4dc9953bc8f11ddc3b8aed0926a7eb`。模型控制器仍为9bb58449…版本。

food1/2由获准的exec网络driver加云GUI宿主编排，没有改网络设置或更换服务。driver会话60045/43515的exit0由协调者核验后提供，单列为协调来源备注；不得伪装成不存在的原始execution.json，也不编造墙钟。food1原external-driver-launch记录保留。

failfast runner与46项离线测试是**后验修复**，不是这三场使用的旧frozen版本；修复针对网络错误立即停组、等待进程结束、持久化中断分类。测试只用fake subprocess与离线审计，不读取凭据、不发网络、不运行游戏、不重放旧请求。旧food1-v1摘要和最终model-batch-independent-v2均保留，后者按原始审计校正food2未完成用量。

## 校验

仓库根目录执行：

```sh
python3 -B experiments/integration-matrix/evidence/integration34-model/verify.py
python3 -B experiments/integration-matrix/evidence/integration34-model/test_cas.py -v
```

若原始work及固定后验源码仍在，可执行 `archive.py --write`。脚本只读已结束输入，不启动驱动、游戏或API。verify解码全部引用，核对请求/角色/观察ID、prompt和schema哈希、API用量与宿主响应、冻结源码/安装哈希和独立摘要；只执行固定SHA256的纯离线食物验证器及提取出的纯运动度量函数，不执行runner。

云仓库、用户目录和系统字体路径均脱敏；原始与脱敏SHA256分列。保存获准的动作、control请求/响应和用量记录，不含Key、环境变量、游戏二进制/资产、世界或存档。有限凭据扫描不是绝对安全证明。发布需包含全部base64分片及manifest所列依赖，不能仅复制本目录。stage的预运行状态和原始失败保留，不回写历史。记录一致性不是不可篡改证明。
