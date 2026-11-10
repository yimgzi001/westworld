# 阶段39能力菜单控制器测试

生产源码：`experiments/scapi-tool-agent/CapabilityMenu.cs`。只执行纯菜单/协议测试，不启动游戏、模型或网络。

## 接线

1. 同一 actor/session/capture 内收集目录、旧菜单候选与各族 typed 候选，构建 `CapabilityMenu`。typed 候选按名字显式覆盖旧松散绑定；事实缺失时不得拿旧 offer 回退。
2. `Build()` 返回完整名称索引、状态 capture、当前菜单。初始只含两项发现查询，不靠宿主推断游戏目标。
3. 模型可调用 `read_capabilities({offset:0,limit:8})` 查看所有能力状态；`read_capability_schema` 不带选择时分页查看 schema。
4. 模型调用 `read_capability_schema({select_tool:"act_Mount"})` 或 `{select_family:"body"}`，明确替换下一轮菜单。选项来自实际目录，不是任意字符串。选择不启动任何游戏动作。
5. Program 截获发现工具调用，使用 `HandleDiscovery(session,actor,capture,step.Tool,step.Arguments)`，不能转发原生 gateway。选择成功后记录 `selectedTools`/`selectedFamilies` 名字，结束该轮计划并发布新提示，不能直接继续执行同计划中后面的动作。
6. 下一次实际观察重新构造控制器，在新 capture 上 `Select` 同一名称偏好。名称可保留，旧 offer、库存引用、位置引用和世界事实不能沿用。执行前仍按最新事实重建并走原解析/dispatch 验证。

可执行菜单最多30项，预留2项发现查询。超量选择明确报错，不静默裁掉能力。未知/不可用能力留在索引和状态页，并列出 `RequestedButUnavailable`。如某族超过30个可执行能力，模型应缩小到具体工具，不增加原32工具上限。

## 运行

```sh
work/dotnet/dotnet build tests/Westworld.CapabilityMenu.Tests/Westworld.CapabilityMenu.Tests.csproj -m:1 -nr:false -p:UseSharedCompilation=false
work/dotnet/dotnet tests/Westworld.CapabilityMenu.Tests/bin/Debug/net10.0/Westworld.CapabilityMenu.Tests.dll
```

结果：87项断言通过；范围隔离、55项逐页完整覆盖、显式选择、超预算失败不污染先前菜单、typed覆盖、未知事实、新观察不复用旧offer、实际决策协议解析均覆盖。该结果不代表55项真实游戏动作已通过。
