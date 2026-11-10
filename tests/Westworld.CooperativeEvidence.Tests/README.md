# 双角色协作审计契约

运行 `./work/dotnet/dotnet run --project tests/Westworld.CooperativeEvidence.Tests/CooperativeEvidence.Tests.csproj`。

链接生产辅助 `CooperativeEvidence45.cs`，30 条纯断言；不启动游戏或调用模型。

宿主使用步骤：
1. 在实际菜单验证派发时生成唯一 Request，保存 World/Session/Actor，并调用 ValidateIdentity 核对收据归属；不得用它替代原生 runId 匹配。
2. 同一物品完整 value、同一对角色、前后实际计数输入 ValidateDelivery / ValidateEating；收据 affectedCount 还需与原生收据身份匹配。此处检查库存，不自动证明世界掉落无变化，也不代表食物值确有增长。
3. ValidateOverlap 输入两动作的实际开始/结束时刻（同一单调时钟）与 B 食用完成后实查 A 同 runId 仍 running 的时刻。采样来源与状态必须由宿主真实查询，不是预期时间。

此辅助只接收审计数据并判定，不创建动作、引用、菜单或模型事实。其他角色的库存可以用于独立验收，不得泄漏到模型菜单。协作成功还应由真实收到的通信、offer引用和健康/食物效果另外核查。
