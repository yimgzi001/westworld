# 原生狩猎工具契约测试

本套直接链接 `runtime/Westworld.Scapi.Adapter/NativeHuntingTools.cs`，引用既有 Game/Core 类型，避免并行开发时重建共享适配器。它不是 mock 原生游戏运行报告。统一适配器集成构建仍由主开发线程执行。

2026-11-09：52/52 纯契约检查通过，编译0错误，存在既有ImageSharp漏洞警告。本套未启动游戏/GUI/模型，也未改变世界或调用外部API。

覆盖：真实schema子集不发明能力；实体/掉落引用类型；session/actor/当前observation隔离；3秒当前性边界；时间回退/NaN；重复/空/越界观测；目标消失；新看到掉落不归因；原生action身份和status校验；hit与死亡未知；无伤害、失败、取消、等待、拾取分别表达。

运行：

```sh
./work/dotnet/dotnet restore tests/Westworld.HuntingTools.Tests/HuntingTools.Tests.csproj -m:1
./work/dotnet/dotnet build tests/Westworld.HuntingTools.Tests/HuntingTools.Tests.csproj -c Release --no-restore -m:1 -p:BuildProjectReferences=false
./work/dotnet/dotnet tests/Westworld.HuntingTools.Tests/bin/Release/net10.0/HuntingTools.Tests.dll
```

`BuildProjectReferences=false` 要求已有匹配Game/Core构建产物，不能据此声称从空环境完整构建通过。

## 宿主集成边界

- 复用同一个 `ActorTools/AtomicActions` 和统一 `NativeToolSession`，此helper不注册动作、不开ledger、不发起动作。
- `Describe(nativeTools)` 原样保留实际tool名称与参数schema，附真实语义和建议单次时限；建议不是新框架全局限制。
- 对同一个已完成的原生 `observe` data调用 `ProjectObservation`。不为了helper再次observe，否则原生现场引用表会被新capture替换。
- `CheckTarget` 仅对宿主保管的最新profile使用，不接受模型自报的profile/time/actor。目标必须是本轮真实可见的对应类型引用；原生执行还会再验证射线、可见、距离、工具和冷却。
- 本地模型可能超过3秒才返回。此时需要宿主真正再observe、确认同一opaque目标引用仍然可见，再按新证据执行；不能只改CapturedAt延长旧证据。看不见就返回新观察/重规划，不靠实体ID从全图继续追。
- `act_MoveTo` 在启动时把目标解析成一个坐标，不是动态跟踪。建议短时接近后重新观察；连续追击不是此profile暗中补上的脚本。
- `ClassifyAction` 接收原生 `action_status` data，而非整个 `{ok,data}` envelope。只有native成功且affectedCount>0才叫hit；死亡始终unknown。miss、冷却等失败、取消均不叫击杀。
- `NewlyObservedDrops` 只表示两次观测的引用差异。重新进入视野可能获得新引用，不能把它叫新生成物品；更不能推断属于曾攻击的动物。
- 收到命中后，模型可自主选择再次观察/攻击/等待/离开；等待完成不代表有尸体或肉，必须看到真实drop后另发一次拾取。武器准备/装备由生产和库存工具完成，不固定挑牛、不固定食物路线。
