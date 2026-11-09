# Host 导航同账本路由

50 项纯断言，实际编译 `HostNavigationToolRouter.cs` 和 `NativeToolSession.cs`，Game/Bounded 类型使用显式无副作用替身。不是原生世界验收。

```sh
./work/dotnet/dotnet build tests/Westworld.HostNavigationTools.Tests/HostNavigationTools.Tests.csproj -c Release -m:1 -nodeReuse:false --artifacts-path "$PWD/work/nav-router-isolated"
./work/dotnet/dotnet work/nav-router-isolated/bin/HostNavigationTools.Tests/release/HostNavigationTools.Tests.dll
```

实际原生 Game/Adapter 类型兼容性另用 `--artifacts-path "$PWD/work/nav-adapter-isolated"` 构建 Adapter 验证；没有运行 GUI/世界。

接入：创建同一 player/actions/senses 的 BoundedLocalNavigation 后，调用 `gateway.AttachNavigation(bounded, existingActions)` 一次。注册检查 player 与实际 AtomicActions 对象身份。新增三个工具：
- `host_navigate_local`：bearing [0,360)、distance [1,4]、可选 max_seconds [1,12] 默认 12。
- `host_navigation_status`：只读，无参数。返回请求 ID、总体运行/停止/终态以及路径指标。初始无目标 remaining=null。
- `host_cancel_navigation`：StopOnly，必填当前 request_id。旧 ID 不得取消新导航；停止请求不是稳定完成。

所有调用共用原 ToolCallLedger；不要在宿主绕路直接 Start。首次实际 backend 才创建导航 ID，重复 seq 返回旧回执。已知无副作用参数/Busy/旧取消拒绝返回 `returned` + `data.status=rejected`；宿主必须检查 data.status，不能只看 returned 就声称成功。真正异常仍 uncertain，必须观察对账。

Busy 覆盖跨段与停止阶段，阻止其他普通写工具；真实只读及原生 StopOnly 保留。原生 cancel_action/status 不代表导航 run，使用专属导航接口。宿主仍负责逐帧 `bounded.Update()`、预算、终态观察及停止排空。绕过 NativeToolSession 直接调用 AtomicActions 的代码不受此门保护。

测试覆盖同序重复一次 Start、冲突不取消、参数边界、重复字段、取消 request 绑定、停止仍 Busy、异常对账、线程、错 player/owner、二次注册、实际源码 Busy 普通写拒绝与只读放行、未知/活动 task failclosed。原生绑定行为、物理稳定和完整模型接入需独立原生测试，替身不能证明。
