# 生产与配置接口纯测试

本项目只构造公开事实 DTO 与 schema，并调用本地参数验证器；不启动游戏、不调用模型/API、不产生游戏效果。

阶段40：165条断言通过，构建0警告0错误。日志：`work/capability40/settings/{build,test}.log`。

```sh
./work/dotnet/dotnet build tests/Westworld.ProductionFamily.Tests/ProductionFamily.Tests.csproj --no-restore -m:1 -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false
./work/dotnet/dotnet tests/Westworld.ProductionFamily.Tests/bin/Debug/net10.0/ProductionFamily.Tests.dll
```

## 复用的原生契约

来源是 `game/Survivalcraft/Westworld/Actions/AtomicActions.Configuration.cs`、`AtomicActions.World.cs`、`AtomicActions.Furniture.cs`、`Upper/ActorTools.Catalog.cs` 与 `Knowledge/ActorQueries.Configuration.cs`。

- 电池/按钮/开关：voltage 0..15；延时器：delaySteps 0..255。
- 活塞：maxExtension 1..8、speed 0..3；仅本次 settings.mode 为 Pulling/StrictPulling 时开放 pullCount 1..8。不写 mode/extended。
- 真值表 outputs 最长16，存储器 data 最长256，家具 name 最长20。
- 告示牌由成功的 read_sign 证明，Synthetic Type=SignBlock；lines最多4行/每行16字符，colors恰4个0..7索引，url最长256。只支持方块，不把手持告示牌当已存在目标。
- 弓/弩/枪：sourceSlot、expectedSourceValue枚举关联到当前另一个有物品的槽；弩额外支持 cock=true。弹药兼容、武器状态、消耗由原生校验，不根据库存值猜成功。超过32种当前装填选项明确计入省略数量。
- 家具构建 options：name、resolution 2..256、mirror_axis/rotation_axis 0..2、quarter_turns 0..3、shift x/y/z -256..256。quarter_turns使用等价旋转规范值；几何可容纳范围由真实设计检查。

`ProductionSettings.Values` 是成功当前read_settings的settings对象，可选以兼容旧调用。告示牌则由中央mapper从read_sign映射，不能单凭历史记忆或名字伪造当前可编辑事实。

## 验证边界

模板验证闭合字段、类型、长度、数值范围、目标与物品关联，不是所有原生语义的替代。当前结构schema不支持正则；真值表0/1、存储器十六进制、家具XML字符/空格、告示牌URL格式与换行等继续交原生拒绝。生成合法参数及实际状态改变仍需真实矩阵。本项目不把纯测断言数当作原生能力通过数。
