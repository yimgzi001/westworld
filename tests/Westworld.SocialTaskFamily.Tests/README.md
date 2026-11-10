# 社交与有限计划纯契约测试

阶段40共68项断言，无游戏启动、模型API或网络请求。

- 当前社交动作自动进入原生有限计划：Speak、OfferItem、RespondToOffer；当前不可用条目遮蔽旧候选。
- 参数关联、本人收件、过期、单一owner和原生12步/每步2次重试限制保持。
- `FiniteNativePlan.BuildPages` 将过大的当前动作集合分成协议可接受的页，页内仍为原生`submit_plan`，不另建执行器。
- 一个动作的关联`oneOf`仅在各分支有必填const/enum字段证明互斥时拆页；否则保留原oneOf，不能把原本模糊的输入变成合法。
- 页号通过`SocialTaskFamily.Build(scope,facts,planPage)`显式选择。结果有`PlanPage/PlanPages/PlanRejected`，宿主必须展示并在新事实后重建。选择不执行。
- 每页最多协议允许的32分支，参数不超过8192字符。当前不能容纳的单分支明确出现在Rejected，不能默默替换成任意对象。

运行（在项目根目录，已安装SDK与恢复依赖）：

```sh
./work/dotnet/dotnet build tests/Westworld.SocialTaskFamily.Tests/SocialTaskFamily.Tests.csproj --no-restore -m:1 -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false
./work/dotnet/dotnet tests/Westworld.SocialTaskFamily.Tests/bin/Debug/net10.0/SocialTaskFamily.Tests.dll
```

覆盖的是模板协议，不是原生社交交付或长期生存成功。原生任务启动时与每一步执行仍检查真实前置条件；只使用当前存在的引用，不支持在一个提交内引用未来才产生的offer。
