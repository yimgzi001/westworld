# SCAPI 双角色、局部感知与动作队列实验

这是当前仓库SCAPI源码游戏的固定脚本实验，不是模型自主生存。沿用独立entry assembly/output隔离，默认拒绝覆盖已有doc/Mods。没有HTTP端口、动态代码执行、模型调用或用户世界操作。

## 2026-11-09真实运行
最终运行目录work/dual-smoke-04，总耗时约14.86秒；已保存关闭。
- 标准出生两个角色，PlayerIndex分别2、3，未改4人上限。
- 每人从真实眼睛位置/旋转发射63条原生射线，最长8格，在水平90°/垂直70°视锥内部采样（边缘留10%余量）。
- 原生Raycast第一命中去重后分别观测14、13个方块，45条没有命中。没有命中不代表已知空旷。
- 第一阶段仅给角色2前进请求并提前stop：角色2水平位移0.21124524，角色3位移0。
- 第二阶段仅给角色3前进请求，按时长自然完成：角色3水平位移0.38482723，角色2位移0。
- 两阶段生命和背包保持；没有异常垂直位移。物理后零输入，最终速度约4.35e-18、2.24e-8。
- 3次受理、3次完成、6次拒绝；duplicate/wrong-session/unknown-player均有证据，stop及duration完成事件分别验证。
- 保存XML角色ID集合与实际绑定2/3一致，最终result=true。

第一轮work/dual-smoke-01的动作测试通过，但感知失败（68个候选中心全部被拒）。原因是沿地面方块中心的射线往往先穿过相邻地面，不能判断地面可见表面。保留该失败记录，改用原生第一命中，不是删除遮挡检查或放宽未知地形条件。

## 感知边界
返回内容是实际射线第一命中方块的坐标、contents/data及距离，不扫描全图给角色。不把背后/未命中的候选交给模型。
所有命中路径按真实眼睛→命中点的XZ包围范围及边界余量检查区块Valid，目标和Y范围也检查。范围检查偏保守，可能拒绝实际可见点；不会把未加载当空气。
此版基于原生**碰撞盒**，不是完整视觉渲染/透明度模型；63条采样会漏细小物体，也未证明自然资源发现和自然遮挡的全部正反例。没有修改地形建立人工夹具。

## 队列协议
Queue.cs不依赖游戏；后台Submit只处理不可变意图/逻辑状态，游戏线程Tick才输出每角色输入：
- session UUID、PlayerId、正递增RequestId；
- issued/expires与单调墙钟，有限值校验；
- Move向量长度<=1、0<duration<=0.4秒；这是此实验的短脉冲协议，不是全游戏能力设置；
- 每角色独立单pending/active；Stop取消本角色排队/活动意图，下个Tick清零；
- accepted只代表排队，不是游戏移动成功；completed是协议结束，世界效果另读坐标验证；
- 拒绝事件在下个Tick报告；其他角色不会因一人的busy或stop被改动；
- 修复后台采样now后等待锁、Tick先推进导致合法Submit误拒的竞态，用max(now,lastTick)判过期。

纯协议测试42/42通过，包括100次并发重复提交只接受一次、跨角色、超时、NaN、stop优先和时钟竞争。
这只是进程内邮箱，不是公网鉴权接口；事件缓冲与外部请求速率没有生产级硬化，不能直接暴露给不可信客户端。

## 复跑
```powershell
dotnet run --project experiments/scapi-dual-smoke/QueueTests.csproj -c Release
dotnet build experiments/scapi-dual-smoke/DualSmoke.csproj -c Release -o work/dual-run-NEW
dotnet work/dual-run-NEW/Westworld.DualSmoke.dll --new-disposable-dual-world
```
须新的输出目录、保持窗口活动。自动保存关闭；180秒Frame级墙钟超时不是线程死锁看门狗。
统一tooling/verify-scapi.ps1现在包含队列测试，总343项离线检查。
原始world、日志、runtime和构建产物不提交，game/源码、冻结归档、Minecraft和main不改。

## 下一步
接入模型只使用局部观测与本人状态，模型返回结构化意图；沿用异步线程边界，补会话恢复/取消和失败结果回传，再做更长时段双人采集。尚未验证挖掘/放置、云端模型、多人长期生存、超过4人或资源容量。
