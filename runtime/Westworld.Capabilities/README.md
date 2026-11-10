# 能力族公开事实适配

本项目将原生能力转成描述、可用性状态及有界执行菜单，不执行游戏动作、不决定目标、不读取隐藏世界。现有 namespace `Capability39` 为兼容已验证原型保留。

- `BodyFamily`：身体、交通、装备共 11 项。
- `ProductionFamily`：持物、交互、配置、家具、合成共 8 项。
- `SocialTaskFamily`：社交及有限任务共 5 项。
- `CapabilityRegistry`：完整能力目录与分页发现，由中央集成提供。

宿主必须映射同角色同次公开快照及真实组件支持状态；菜单中的引用、物品配对和设置都必须在实际调用前再次重建验证。能力目录不表示当前可执行，更不表示原生效果已经实测。

身体族仍将攀爬目标是否梯子、骑乘对象是否可骑、物品是否可穿交给原生前置条件。生产设置目前有类型模板的仅电池、按钮、开关电压及可调延时器；其他设置明确未适配，不接受任意对象。社交必须保留原生收件事件，不可反复读取排空收件箱；定向说话不是私聊。原生任务必须由既有执行器和所属角色账本管理，不创建第二调度器。

测试为无游戏、无模型调用的公开事实与协议验证。分别运行 `tests/Westworld.BodyFamily.Tests/BodyFamily.Tests.csproj`、`tests/Westworld.ProductionFamily.Tests/ProductionFamily.Tests.csproj`、`tests/Westworld.SocialTaskFamily.Tests/SocialTaskFamily.Tests.csproj`。
