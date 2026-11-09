# 原生攻击时序正控（待运行）

`NativeAttackReadinessProbe` 是可接入现有实验宿主的更新对象，不是可独立启动的游戏，也不是模型策略。当前仅隔离编译通过（0 错误/0 警告），未跑 GUI，未证明原生断言通过。

接入方式：用现有完整角色、单个 NativeToolSession 与 HuntingFixture 建立隔离人工场景。宿主通过真实 observe 引用正常接近、瞄准目标后，传入该引用构建 `new NativeAttackReadinessProbe(player,gateway,targetReference,log,mode)`，逐帧调用 Update()，读取 Done/Passed/Reason。不得为确保通过而改动物血量、攻击间隔、注入装备或位置瞬移；目标离开/反击均如实失败。三个模式应使用独立场景：

- cooldown：就绪后原生攻击，立即同 tick 再攻击应拒绝且 lastHitTime 不变；本地轮询最多 4 游戏秒，ready 后重新实际观察同一引用，再原生攻击须被接受且 lastHitTime 推进。不要求随机命中造成伤害。日志记录实际 HitInterval，不硬编码/修改 0.66 秒。拒绝回执保留以区分其他原因，不宣称所有拒绝均是冷却。
- cancel：第一对尝试后 cancel_action，丢弃本地后续攻击，越过真实间隔后确认没有第三次攻击。这验证该 probe 的队列撤销，不证明主宿主取消路径。
- lost：第一对尝试后原生 FaceTowards 转身，实际 observe 必须看不到旧引用，然后确认无第三次攻击。若目标仍可见，控制失败；不伪造观测。

宿主仍需全局墙钟预算（暂停时游戏时钟不推进）、角色健康检查和清理。此类不追踪隐藏实体坐标、没有模型调用、无材料/血量修改、不会轮询补打直到成功。setup 的接近/瞄准不在本类内，避免重复发明导航脚本。

编译项目只引用预先构建的 adapter/Core/Game/Engine DLL，并链接新计时源码，以免触发共享构建；宿主集成直接链接 NativeAttackReadinessProbe.cs 即可，若 adapter 已包含 LocalAttackReadiness 不要重复链接其源码。该编译控制不能替代原生运行证据。
