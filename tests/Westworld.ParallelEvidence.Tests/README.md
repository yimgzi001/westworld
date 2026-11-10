# 阶段46持续动作并发证据

链接生产 `ParallelEvidence46.cs`，运行 `dotnet run --project tests/Westworld.ParallelEvidence.Tests/ParallelEvidence.Tests.csproj`。25条纯契约断言，不跑游戏/API。

- 每个 `ParallelSample46` 绑定原始派发 world/session/actor/request 与原生runId。不能使用状态查询request代替原派发request。
- `ValidateRunningWindow` 要求至少3个共同游戏tick的双方running样本、跨度≥1游戏秒、采样缺口≤配置（最大1秒）、双方真实位移。返回最大采样缺口，`ContinuousCoverageProven=false`。不能据此声称每一刻都被采样或已经证明长期并发。
- `ValidateCancellationIsolation` 的 before 是双方仍running；after 是A取消后已settle而B仍running的同tick样本。final是B同run成功时双方同tick样本。A从after至final漂移默认≤.03；取消瞬间至settle的惯性另行报告，不把惯性与稳定漂移混在一起。
- 宿主必须核对receipt真实session/actor/runId；构造样本不能用计划状态、预期时间或人工位置替代原生实测。最初fixture允许明示摆位，但证据窗口内不得改位置/速度。
- 位移函数为3D欧氏距离；移动场还应单独验证水平位移/路径，不把坠落当行走成功。
