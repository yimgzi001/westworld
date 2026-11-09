# 云端 Linux 构建门槛与零AI视口风险（2026-11-09）

这是ADR-001的前置环境里程碑，不是五角色验收通过。

## 环境与验证
- 仅使用dot云电脑Linux x86_64，未访问用户电脑。
- 云端没有预装dotnet、DISPLAY、Wayland、Xvfb；有libGL。
- 官方.NET SDK10.0.403下载后，SHA512与微软release metadata一致再解压。
- 私有仓库git clone缺HTTPS认证，不能将其解释为仓库不存在。使用已授权GitHub连接器读取/提交。
- 本地构建源码来自官方SC-SPM/SurvivalcraftApi的API_1.9.5.1，提交fb1e8aca50aacdfef766518222b20cbae6a805f6，与game/UPSTREAM一致。
- 对角色/输入/GUI/PlayerData/地形/生成/视口及构建配置12份关键文件按Git blob SHA-1与cjh比对，12/12相同。不是声称已逐字校验全部上游文件。

## 真实发现与修复
Linux工程引用Survivalcraft/Content.zip，但它是被忽略的生成文件。Windows工程已有ZipDirectory的PreBuild目标，Linux工程缺失。
首次构建restore后失败；单节点诊断明确报MSB3030：Content.zip不存在。给Linux工程补上同样的资源压缩目标后：
- Release构建成功，0错误、3条既有ImageSharp3.1.12高危依赖警告。
- 生成Survivalcraft.dll与Content.zip。
- 本次没有压制漏洞警告或未经评估升级依赖。
- 修改仅构建资源打包，不改角色/游戏规则，不提交生成ZIP或依赖二进制。

复跑（.NET10，仓库根）：
```sh
dotnet build game/Survivalcraft.Linux/Survivalcraft.Linux.csproj -c Release -m:1 -nodeReuse:false
```

## 运行阻碍
隔离输出目录短时启动原生游戏后，进程退出134：
`Silk.NET.GLFW.GlfwException: GLFW Init failed, 65550: Failed to detect any supported platform`
调用链在Engine.Window.Run→Game.Program.EntryPoint。尚未创建测试世界，未进行五角色、物理、生存、存读档或GPU性能测试。
当前上游无AI视口不等于无图形平台启动依赖；需要另行验证可用离屏图形后端或真正无头宿主，不能用空UI代称已完成headless。

## 零AI视口的正式约束
AI只通过接口，不需要每AI一个GameWidget或摄像机。目标是五个真实生存actor、零AI视口；人类视口可选（0或1），移除人类视口不能停止模拟。
当前耦合至少包括：
1. SubsystemPlayers.MaxPlayers=4和SubsystemGameWidgets每PlayerAdded创建视口。
2. PlayerData.GameWidget缺失抛异常，出生/死亡/相机绑定依赖呈现。
3. ComponentInput依赖GameWidget，ComponentGui.Load依赖按钮，ComponentPlayer槽位规则读GUI。
4. TerrainUpdater.PrepareForDrawing用摄像机注册加载中心。
5. SubsystemSpawn.SpawnChunks/DespawnChunks用视口摄像机决定生存实体活动。
6. TerrainUpdater仅ContentDistance不一定推进到Valid；现有局部感知要求Valid，需区分模拟就绪状态与网格就绪状态。

下一实现切点是独立ActorIdentity/Control、SimulationInterestCenters与RenderViews，生命周期不要求展示。活动中心服务区块和实体生成/卸载，观察者摄像机只服务显示。不能只改4为50，不能以5个描述对象或5只简化动物当5个玩家身体。

## 持久化范围
提交构建修复与本报告。SDK、上游checkout、构建输出、原生启动日志只在云端work内，不提交密钥/签名材料/私人数据。旧Windows实验未提交源码仍按交接报告记录，本次未从用户电脑取回。后续每完成可验证里程碑继续推cjh。
