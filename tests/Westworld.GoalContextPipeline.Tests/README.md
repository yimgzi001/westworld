# GoalContextPipeline

纯管线验收，非原生目标成功。运行：

```
./work/dotnet/dotnet build tests/Westworld.GoalContextPipeline.Tests/GoalContextPipeline.Tests.csproj -c Release -m:1 -nodeReuse:false
./work/dotnet/dotnet tests/Westworld.GoalContextPipeline.Tests/bin/Release/net10.0/GoalContextPipeline.Tests.dll
```

默认13条合成语义检查+1条夹具SHA检查+16份历史公开request context预算回放，共30条。fixtures/stage34-contexts.json 为34两场16份真实公开context的gzip/base64，解压后SHA256校验；不含API key、原始请求网络数据或host-only来源审计。运行时注入的goal进度是代表性合成对象，不是回填历史事实，也不是35实际成功证据。可选命令行传request.json路径替代内置夹具。

初轮发现完整GoalExecutionContext重复goal/budget/每条reason/观察时间导致上下文超限；第二轮发现BuildContext满14000后compactor元数据超限。保留work失败日志。当前GoalContextPresentation省去重复文本与每criterion重复时间，保留所有item/criterion状态与证据ID；宿主BuildContext使用13600chars/15600bytes，给后续metadata保留400余量，最终仍按14000/16000硬限检查。不增大模型预算，不裁掉目标事实，不暗删有效观察。

真实35宿主若更密集观察仍可能显式facts_too_large，必须如实失败/重新设计，不能把此次16个历史样本通过说成任意上下文容量保证。
