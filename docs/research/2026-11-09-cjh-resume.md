# 2026-11-09 cjh 接续验证

## 基线与本次范围
- 仓库 yimgzi001/westworld，分支 cjh，开始提交504e4051d419b47a190ccae1450b3c03120ae17c。
- 本地独立目录 D:\project\westworld-cjh。电脑上的Minecraft项目属于另一项目，本次未改动或启动。
- game/ 是SCAPI1.9.5.1，归档是SCNETX26.07.01.01，不假定二者兼容。
- 新实验见 [跨tick协议](../../experiments/scapi-fixture-sequencing/README.md)。冻结归档和游戏源码未改动。

## 已执行
1. 使用官方.NET10.0.403 SDK，下载包SHA512与微软release metadata一致。SDK和构建产物仅在忽略目录，不提交。
2. Windows Release游戏构建成功：0错误，6条同一ImageSharp3.1.12依赖告警（NU1903，GHSA-v76p-62qx-wwq2）。未擅自抑制告警或变更许可相关依赖。
3. 原跨tick43/43、视觉几何/遮挡/转向191/191通过。
4. 新协议67/67通过。新增用例在旧代码上真实复现迟到ACK复活终态的问题；新实验修复终态ACK、ACK截止时间和新tick计数。
5. 没有启动游戏、进入存档、调用模型或执行模型生成代码。编译通过不等于运行/渲染通过，不宣称AI生存闭环完成。

## SCAPI真实接入边界
- SubsystemPlayers.MaxPlayers=4，GamesWidget超4抛异常，TerrainChunk.HazeEnds固定4项。不能只改人数常量。
- ComponentInput每tick重置输入；失焦/玩家未ready再次清空。键鼠UpdateInput hook后仍有其他输入路径，须明确注入时机。
- 输入UpdateOrder=-10、玩家0、Locomotion1；同order按hash排序，不依赖同order的顺序。
- SubsystemUpdate吞组件异常，接入层需要明确记录失败，不能以无崩溃当成功。
- ComponentMiner单参Place会扣背包，pending返回false不一定失败；双参Place不扣背包，不作生存接口。
- IInventory可用于本人背包快照；感知需真实眼睛位置、局部范围、加载和遮挡条件，不向模型直接提供全图。

## 下一里程碑与验收
1. 绑定已有一个PlayerIndex，只读位置、朝向、生命、背包；验证存档重载仍绑定同角色。
2. I/O只排队；游戏线程应用有限时长转向/移动意图并回报实际结果。请求标识关联会话/角色，拒绝重放和旧会话回复。
3. 原生验证停止、超时、失焦、暂停、重载；不改变玩家已有世界，不自动增加角色。
4. 之后验证2角色隔离，再做原生挖掘/放置与模型决策异步闭环。
5. 大规模无视口角色另作设计与资源测量，不能引用旧50空闲角色数据作本版容量承诺。

项目未提供可发现的project-docs技能；本次仅补实验报告与索引，不更改14份设计目标。

## 后续原生里程碑

随后已在独立新世界完成真实单角色状态读取、0.4游戏秒低速移动、停止后的物理观察、同步保存和同角色重载；完整结果及验证边界见[原生smoke](../../experiments/scapi-native-smoke/README.md)。以上“未启动游戏”是第一提交阶段的记录，不是后续状态。游戏源码及原始用户世界未改动。
