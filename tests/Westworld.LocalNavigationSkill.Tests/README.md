# 原生局部导航桥接

新增 `LocalNavigationSkill` 可被原生夹具直接实例化，不是纯状态壳，也没有自建 AtomicActions。

```csharp
// 必须同一 Project/角色，senses 已 Bind(actorId,p)，actionsEnabled:false。
// 宿主借出 actions 的独占使用权，期间禁止 gateway 或其他技能同时派发。
// ReadActualNativeIdle 必须读取实际 direct action 和 ActorTools task 状态。
var lease = new LocalNavigationSkillLease(existingActions, senses, ReadActualNativeIdle);
var skill = new LocalNavigationSkill(p, existingActions, senses, actorId, lease, Log);
skill.Start(Guid.NewGuid(), directionPoint);
// 游戏线程逐帧调用，不需要模型调用：
skill.Update();
// 取消后继续 Update 到 terminal，不能立刻换技能抢占。
skill.Cancel();
```

`directionPoint` 只是局部候选排序方向，不赋予隐藏地图/远端可达权限。每次请求最多完成一个经真实观察授权的局部段；只在投影候选中选择缩短该方向距离的段。成功后宿主需新请求以续段，没有内置找食物/追猎脚本。没有可用段时复用补看、对齐与跨阶投影；同样可能合理失败，尚非全局绕障导航。

复用：LocalObservationMemory、LocalRouteCandidates、ObservedStepRoutes/Guard、LocalNavigationAlignment、NavigationLookPlan、NavigationSettling。原实验的缺格生成与空气包络检查提取到独立 Geometry。仅持有/取消自己的 run；不销毁外借 owner/session。

时钟：NativeActorSession 的 Time 实际是其 Stopwatch 单调墙钟（不是 GameTime）。证据 Age 加派发后的 Stopwatch 间隔，最多 3 墙钟秒；绝不把观察未知刷新成已知。准备/移动总墙钟 20 秒；settling 用真实 GameTime 保证连续接地，另加 5 秒墙钟上限，暂停不会无限卡住。所有正常成功/失败/取消都先原生停止再 settling；游戏时钟回退无法可靠采样时只停止并明确失败。超时不声称已稳定，新 Start 仍检查接地、速度、生命等门槛。

当前验证：18 项几何/既有 settling 纯断言通过；完整新适配源码隔离编译通过。**未跑原生导航实例，未验证整段生命周期、取消与障碍表现**。先用原有台阶夹具实测，再连模型。引用现有 DLL、不重建共享工程；并行宿主若已把新增类编入 DLL，直接链接源码产生 CS0436（编译器使用当前源码），不表示原生测试通过。

后续原生准入：单 owner，真实对齐/补看/跨阶与接地续段；障碍/证据过期停稳；取消不新派发；累计路径单独于净位移；失败原样保存。不提供未观察绕路、动物实时位置追踪、半砖/复杂碰撞形状通用支持。

## 租约与时钟修正（尚未接入主宿主）

Start 必须取得同一 actions 对象的宿主租约，实际 direct action/task 都 idle 才接受；同 owner 的其他 wrapper 不能重复占用。宿主必须让所有 gateway、任务和技能入口参与这一仲裁。不经过 gate 直接操作原生 actions 仍可能冲突，因此这不是已经证明主宿主排他或安全沙箱。实际 idle 状态回调由宿主实现，不能返回固定 true；当前未修改 Program 接入它。

Start 接受后仍先做连续 0.25 游戏秒的稳定接地采样，单帧低速不等于恢复。正常成功、失败、取消也都先停稳。停稳失败/墙钟超时释放为 quarantine，禁止同 owner 再获租约；不能通过新包装器重置。时钟非有限/回退故障在 observation-session 对象上锁存，换 skill/gate 仍拒绝，必须建立新原生会话/控制 owner 并重建观察记忆，不能让旧数据追上新时钟继续用。

本轮纯测新增 direct/task busy、重复 wrapper 争租、错误释放、未稳隔离、新 wrapper 不可清隔离、时钟倒退锁存、替换 wrapper 不可恢复、真正新 session 可运行、非有限值锁存和单帧/稀疏采样不满足 dwell。仍未原生验收。
