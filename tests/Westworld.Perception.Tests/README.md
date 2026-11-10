# P3：不可变地形感知frame与变化契约

这是 **Runtime.Core纯契约**，尚未接入真实宿主Capture/Prompt/Offers，不是新增视觉能力、模型探索策略或原生运行证据。没有读Terrain、生成动作ref、给工具授权、选择方向、自动找岸或调用模型。现有固定driver未改。

## 最小接口

```csharp
var scope = new PerceptionScope(worldId, sessionId, actorId, bindingGeneration);
var frame = PerceptionFrame.FromObservation(scope, captureSequence,
    actualActorObservation, sameCaptureLocalMemoryWindow);
var journal = new PerceptionChangeJournal(scope, capacity: 64);
journal.Observe(frame);
PerceptionChangeBatch changes = journal.TakePending();
```

- 复用现有`ActorObservation`、`CellObservation`、`Position`和`LocalObservationWindow`，不再存一份长期地形记忆。frame为最小**地形**合同，不含完整自身状态、库存、实体、eye pose或覆盖统计；缺项没有伪造默认值。完整统一frame的原生接线由宿主另外集成。
- `WorldId`和`BindingRevision`由可信宿主提供。后者是原生绑定代次，**不是ActorRegistry每次Update都递增的Revision**。纯库不能验证native实体/世界真实存在，不能由模型自行构造入参。
- frame完全验证后冻结数组/字典；当前观察和可选memory必须同session/actor/observationId/Time/Position/Health。memory的current格要逐项匹配本帧known格，历史不能覆盖current。跨世界/会话/actor/绑定由journal严格拒绝，换绑定要新建journal。
- 所有时间采用`ActorObservation.Time`的单调观察时钟；不能混入gameTime/UTC。Sequence严格递增，允许相同时间的新capture；回退/重复snapshot拒绝且不改变已有状态。向前跳帧记录`SkippedFrames`并要求resync。
- 最多4096个合并后的格证据、3秒证据寿命。`ReadAt(cell, now)`区分Current（本frame确实观察）、Historical、Unknown、Absent、Expired。过期保留原值/原来源作历史解释，**不具实时权限**。`Evidence`是捕获时的不可变记录，不是live查询；任何过时frame都不能用于绕过宿主fresh复验。
- unknown没有值；本帧unknown与memory历史值分开。没有mem时不虚构历史。frame不生成动作ref，即使Current且未过期也不能单独授权交互。
- `ValueSemantics`标记生产者的值解释，journal不允许中途变更。纯helper不猜mod位编码、不自动剥光照。若要忽略光照，生产者应在**当前格和同次memory**一致规范化并声明相同语义；不要只改diff的一侧。

## 事件和合并

|类型|精确含义|
|---|---|
|FirstObserved|journal首次得到此格current事实，不是世界新生成资源|
|ObservedAgain|历史格重新当前可见且值相同|
|ObservedValueChanged|两次真实观察的值不同；新known零值也是该事件，不归因于某个矿工|
|NotCurrentlyObserved|遮挡、视角、未加载、输出缺项等均可能，只说当前不再有证据，不叫删除/拾取|
|KnowledgeExpired / KnowledgeEvicted|历史证据TTL过期/提前消失，不是世界资源消失|
|HistoricalMemoryUpdated|输入memory带来了不同旧来源，不宣称本帧发现新物|

每格合并为一条，保留首次Previous、末次Current、首末sequence、来源snapshot、发生次数及按首次出现顺序去重的Kinds。中间变化不是完整轨迹，但不会因最终值相同抹成“没有变化”。Current描述该格**最后一次变化事件的来源frame**，不保证等于整个batch最新frame；用ObservedAt、batch.ToCapturedAt/EvidenceLifetimeSeconds判断年龄，不能当成新ref来源。

待处理最多64个subject（可配1–256）。容量满后仍合并已有subject，新subject的变化计入OmittedTransitions；不悄悄丢失并声称完整。首次无基线、跳帧、容量遗漏均设置ResyncRequired。保留最近一个完整frame供调用者发当前快照，初版禁止delta-only。TakePending仅取出本地批次，不表示模型已收到或确认；Prompt最终投影、保留基线/失败重发由宿主负责。

最多4096个snapshot ID的有界重放账，不因取出事件而忘记旧ID。耗尽明确拒绝，需宿主建立新基线；不循环淘汰ID后允许旧快照伪装成新版。journal只许创建线程使用；不可变frame/batch可交只读消费者。

## 验证

```sh
work/dotnet/dotnet build tests/Westworld.Perception.Tests/Westworld.Perception.Tests.csproj \
  -c Release --artifacts-path work/perception-p3-pure
work/dotnet/dotnet work/perception-p3-pure/bin/Westworld.Perception.Tests/release/Westworld.Perception.Tests.dll
```

97条纯断言：不可变/来源数组脱离、同捕获memory绑定、TTL/unknown/known-air、新增与遮挡、重新观察与实际值变化、过期/淘汰、合并中间事件、容量遗漏/跳帧、跨world/session/actor/binding、帧乱序与重放、线程边界、非法输入原子拒绝、历史来源改值/改时间拒绝。

首次测试项目编译有4处params参数的目标类型`new()`语法错误，核心库已编过；修为显式CellObservation构造后通过。原失败保留在`work/perception-p3-build.log`，修后日志另存。无GUI、真实世界、模型/API调用或宿主接入验收；不能把97条当观察准确率或原生覆盖率。
