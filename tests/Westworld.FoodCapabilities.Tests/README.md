# 食物能力目录的离线边界

运行：`work/dotnet/dotnet run --project tests/Westworld.FoodCapabilities.Tests/FoodCapabilities.Tests.csproj -c Release`。

目录位于 `runtime/Westworld.Scapi.Adapter/FoodCapabilities.cs`，没有接管现有 runner、模型 prompt 或执行器。测试不初始化游戏，不进行真实挖掘、拾取、进食。`Eligible` 是宿主观察的分类结果，不是执行授权；调用者仍须绑定 session/actor/observation/full value，执行时重验并使用原生接口。

## 可扩展的原生路径

- 成熟南瓜131：`BasePumpkinBlock.GetNutritionalValue/GetDropValues` 与 `PumpkinBlock.GetDropValues`。成熟尺寸7可食；掉落会改为dead标记，保留损伤，可能附带经验。不能比较世界full value与掉落full value完全相同。
- 海胆226：`SeaUrchinBlock : BottomSuckerBlock : WaterBlock`；使用原生默认掉落规则，BlocksData中营养0.05、疾病概率0.25、工具级别0。`SubsystemBottomSuckerBlockBehavior.OnCollide`会刺伤，且水下路线有窒息/入水风险。它是额外可食用地形块，但不是默认安全陆地觅食目标。分类必须显式声明水域危险处理能力，不意味着当前执行器已实现。
- 鸟自然产蛋118：`ComponentLayEggBehavior`通过AddPickable产生isLaid标记的蛋。营养取决于EggType，非所有蛋可食；生蛋默认疾病概率0.1，熟蛋概率0。目录要求自然产蛋标记和原生实际营养，不能仅认ID118。
- 熟鸟78、熟肉89、熟鱼162、面包177：可扩展已观测库存/掉落的原生食用入口，不需要进食工具。但这些不是可Dig获得的自然地形食物。烹饪/制面包属于其他流程。
- 生鸟77、生肉88、生鱼161：原生可食、默认疾病概率0.25，必须显式接受风险；自然来源是动物死亡后的ComponentLoot，掉落受尸体存留时间、概率、火焰与Mod hook影响。没有验证狩猎链。
- 牛奶桶110、南瓜汤桶251：可食，但需容器归还/容量/部分效果记账。本批目录收录说明，默认不提供Eligible。
- 黑麦RyeBlock掉种子173，成熟/野生与概率决定数量，种子不是直接可食物；不能把采种当吃到食物。腐烂食品不在启用目录中。

## 判定与风险

`AssessObservedItem`要求观察时同一个完整value对应的真实`GetNutritionalValue`和`GetSicknessProbability`。不依赖旧`SlotObservation.CanEat`的南瓜白名单，亦不伪造营养/疾病概率。默认拒绝任何正疾病概率，显式`AllowReportedSickness`才能放行目录中的生食。零报告疾病概率不是绝对安全：饱食度、疾病状态、同食物过量等仍由原生VitalStats/Clothing处理。

`AssessObservedHarvest`只接受known有效坐标并分类成熟南瓜/海胆，不扫描地形、不保证工具可达、支持物不变、掉落成功或已经拾取。需要视线/加载状态/距离/动作ID幂等、原生Dig、自然拾取和库存增量的真实闭环验收。

建议下一真实增量先在当前观察与库存入口支持上述熟食，而不是绕过路由安全去水下挖海胆。若追求第二种自然食物链，优先设计可见自然蛋拾取与原生食用，并单独处理生蛋疾病风险；不投放食品冒充自然发现。
