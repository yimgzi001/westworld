# 可选模型组合狩猎夹具

`HuntingFixture` 是宿主专用人工组件正控，不是策略/执行器。它不创建ledger、不发工具调用、不替模型移动/瞄准/攻击/等待/拾取，不调用模型。源文件为global类，可由同项目Program直接使用。

```csharp
using var fixture = HuntingFixture.SetupFixture(player, value => HostLog(value));
// 在实际游戏线程每帧：
fixture.Update();
// Complete / Failed / FailureReason 决定宿主结束验收；AuditSnapshot仅写审计日志。
```

设置内容：明确铺设半径6格平地与3格高围墙，调整初始身体到夹具中心；原生生成一只完整Cow_Brown；在世界放置一件木棒拾取物。**跳过武器制作，仅测模型组合狩猎组件**。不修改动物健康/行为，不给背包添物，不替模型选中武器。既有初始背包原样记录；若已有木棒/生肉或世界生肉，拒绝此正控，而不是清空数据。

模型应只读取正常原生observe、inventory和工具回执。`HuntingFixture`、`AuditSnapshot`与日志包含不可见诊断健康、真实所有者和初始位置，严禁加入模型提示词。只能把普通任务目标交给模型，不能从本类泄漏目标引用/路线/动作顺序。模型可自行观察、选择工具，宿主审计不会帮它追目标或等待。

审计通过条件：原生Injured回调证明由绑定角色实际造成伤害，最后致命伤属于它且没有其他来源伤害；原生DeathTime/CorpseDuration成立；PickableAdded事件捕获所有OwnerEntity==目标的生肉对象和数量，出现其他生肉即拒绝来源归因；掉肉时间不早于原生尸体延迟；目标生肉全离世界且背包净增等于总数。默认300游戏秒、允许10..900秒；死亡/超时/污染会Failed，失败不会直接改游戏或清世界，runner负责取消自身在途动作并收尾。

每帧统计实际水平路径与初始到当前位置净位移；不含夹具初始摆放。`Dispose()`只解绑审计回调，不删世界/物品/动物。

Host成功日志只叫`hunting_fixture_outcome_verified`，它本身不能证明模型发起了动作。必须与runner真实模型请求、解析决定、原生tool receipt关联后，才可报告模型闭环。尤其“当前手持木棒”不能单独证明调用过SelectItem：原生拾取入当前槽也可能直接持有；显式装备请求需要从工具回执核实。

当前验证：独立source-link编译通过，0错误，既有ImageSharp依赖漏洞警告9条。未在此子任务启动GUI/模型或实测本夹具；由统一runner安排模型组合测试。旧固定脚本hunting-gate的成功不能冒充本类模型测试结果。
