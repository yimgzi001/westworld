# 统一视野快照原生验收

## 当前实测结论（snapshot23）

在 dot 云电脑使用完整生存外部 Player、零 AI 视口、人工平地夹具运行。没有模型/API，没有注入观察或库存。两场均 exit 0、result passed、UpdateFailure=0：

| 规模 | 断言数 | 总墙钟 | 有效 observe 微基准样本 | 单次毫秒 min / median / max |
|---|---:|---:|---:|---|
| 1 角色 | 35 | 10.683 秒 | 10 | 1.1163 / 1.3446 / 3.6099 |
| 5 角色 | 47 | 10.118 秒 | 50 | 0.8773 / 1.08575 / 4.3624 |

原始证据为 `work/snapshot23-actors{1,5}/snapshot.jsonl`、`exit.txt`，运行前 `source-hashes.json`、`installed-hashes.json` 固定源码与二进制。发布归档采用已验证 base commit `50e3c1007bdfc0f813195671b737180f47f59ebc` / tree `71bc1a4107b526b0caa25f19db5a68ad44c064e5` 加变更源码及完整哈希，不重复上传全部源码。

### 已证明的功能

- 一份原生 `observe` 同时返回两个不同材质、非中心准星方块，以及真实生成的可见棕奶牛；自身库存摘要按原生槽位值/数量核对。
- `block_limit=1` 的 omitted 数量明确；`maximumSamples=1` 的原生 scanner 明确 BudgetExhausted / 非 Complete，未扫描格不带 value/sample。
- 背后、扫描范围外目标不在当前方块列表。墙遮挡断言要求真实 knownCells 和 LOS 工作非零，拆墙后重新可见。
- 原生转身、背后变块、转回重新观察得到新值；旧引用拒绝且无动作/世界副作用。
- fresh 批量 `visibleBlocks` 引用进入原生 `act_Dig`，实际终态 succeeded 且世界格变为空气。并未验收拾取或库存增长。
- 5 角色各自库存 reference 不同，全部无 GameWidget。私有引用断言不是完整权限隔离渗透测试。

### 成本有效性与限制

计时前等所有被测角色各自 5m 扫描区的真实区块 Valid，并对每角色预检；每个计时样本再次强制 knownCells、lineOfSightChecks、voxelReads 都大于零。23 的实际范围：

| 规模 | knownCells | LOS checks | voxel reads | 每次当前线程托管分配 |
|---|---|---|---|---|
| 1 | 83 | 224 | 91 | 190456–194632 字节 |
| 5 | 71–83 | 123–224 | 73–91 | 159928–213976 字节 |

这是同帧同步循环的观察微基准，不是共享世界调度预算已实现，不是持续帧率/整机 CPU/RSS 压测。CPU 秒保持 null。角色视角与实际工作量不同，不能由五人平均比一人低推断扩容提速，不能线性推算 50 角色。cleanupVerified 只表示 Dispose 未抛异常，并非独立验证所有资源释放。

## 夹具与复现

参数 `--new-disposable-world --actors 1|5`；仅 work 下 fresh 目录，不覆盖已有世界。先原生 Dismount 成功且 ParentBody=null，才明确调整夹具位置/地形；不强清 ParentBody。夹具活动半径 FullReady=32 / Content=48，等待实际观察区块有 8 秒上限并记录状态。这不是生产默认预算修复，也不是模型自主下船。

棕奶牛由原生 SpawnCreature 作为夹具生成，不证明自然动物刷新。可见实体可能是不带 ComponentCreature 的身体，因此 name 合法可缺；测试记录此数量，仍要求命名牛实际存在，不补造名称。

原 `observe` 保留 look_distance / object_distance / limit（只限制对象），block_limit 单独限制方块。采样点不是保证可交互的面；本场只验收近距泥土 Dig，不外推所有方块均可挖/放。

## 保留的失败与无效测量

- snapshot19：1 人 FaceTowards 失败；5 人拆墙后扫描区块未 Valid，恢复目标断言失败。当时墙后全 unknown 的负断言也不足以证明遮挡，不能单独引用为成功。
- snapshot20：两场前置等待超时。直接放置仍乘船的角色触发亲体约束，且等待整个布景矩形超出原 FullReady=16 的有效覆盖。保留失败，不倒改日志。
- snapshot21：1 人 34 / 5 人 42 项功能断言通过；Dig 后所有 10/50 成本样本 known/LOS/read 为零，因此成本无效，不用于性能结论。
- snapshot22：1 人通过有效成本检查；5 人因合法无 name 的可见实体导致测试 GetProperty 抛错。23 仅修可选字段读取并保留真实牛匹配条件。

本 gate 不证明自然生存、自主模型策略、长时稳定性或 50 角色扩展。完整场景由父协调者串行运行，runner 不启动外部模型或付费请求。
