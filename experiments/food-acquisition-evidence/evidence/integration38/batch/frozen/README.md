# 38：Eat关联参数与食物闭环，独立验收准备

非生产策略。准备不调用API、不启动GUI/游戏，不改旧世界/证据。`cases.json`区分离线、4个人工fixture、1次自然模型。宿主明确冻结后才stage，全新目录，所有失败保留。

## 实际接口与已有能力

`offers[].Parameters`仍为基础参数，关联约束由ParameterBranches进入最终`responseSchema.properties.steps.items.oneOf[].properties.arguments.oneOf`。driver读取这一**实际公开responseSchema**，不拿独立slot/value enum交叉积冒充关联。只有同工具唯一分支有效，无responseSchema不回退。

原生StartEat本来先ValidateInventorySource再消耗；新增菜单约束应在模型解析与fresh派发重验时更早拒绝，而不是替代原生终态。现food_drop原生下船、平地、world熟肉1份，没有库存/健康注入；正控来源→自动拾取→Eat与自然场不能混称。

复用现`food-acquisition-evidence/deterministic_driver.py`的公开观察策略和幂等有界文件协议，只截获其“现在已有新熟肉可Eat”的决定。探针只执行一次，之后空计划，不自动纠错/重试或补吃。

- wrong_slot_value：用另一个实际有物品的槽位+本次新食物value，两个字段分别真实存在但组合错误。期望controller_response_rejected且零Eat调用，不把native拒绝等同前置参数过滤通过。
- over_count：新食物实际count+1。期望同上。
- fresh_slot_change：同一初始有效计划先合法act_TransferItems，把新食物从原槽转到**同背包公开空槽**，再Eat原slot/value。第一步实际成功；第二步应fresh重验拒绝，零Eat调用；库存原槽空、新槽物品真实存在。绝不手改库存/发新引用替换旧Eat。
- valid_eat：保留正常有效Eat。必须原生事件、库存下降、FoodItemsEaten、饱食与来源守恒全部真实，不因driver选了Eat就说成功。

两个坏响应被controller拒绝后，既有file请求仍等其45秒deadline。driver不读宿主日志，不对同requestId补发“修正”响应，不绕过验证。目标失败是负控预期结果，不能把host非零退出当作整个参数契约失败，也不能改host结果为成功。缺失probe/未获得新食物/transfer失败为unknown或失败，不能硬写通过。

## 预算与启动

4个fixture各最多4请求/180秒/45秒请求+7秒排空，外部runner每场195秒watchdog；自然模型1场8请求/180秒/45秒+7秒。全批最多8次API，仅自然模型场会调用；确定性场0。8次是上限而非目标，取消仍占额度。严格不因失败重开世界直到通过。

`prepare.py`复用food.stage的显式build_output，与natural.stage新世界路径。自然run.sh只有host，绝不自动启动模型/固定driver。使用:

```
python3 -B work/integration38-cases/prepare.py --build <isolated/debug> --content <Content.zip> --expected-host <SHA>
```

每场manifest列driverCommand、hostOnlyRun、validatorCommand与预算。协调者有界runner同时启动对应fixture driver与host，host退出写exit.txt，等driver退出后独立validator；结束写runner-outcome再finish。只预声明该保证可写outcome；缺原始文件标missing，不伪造结果。失败场继续但owner排空不确定则停止后续。

自然场seed `natural34-b`已有36对照，**不是盲测**。原目标/公开事实/官方模型自由规划，不指定Eat、资源、路线、下船或等待。官方驱动由协调者单独显式启动，8/180参数，不fallback、不改变被拒schema或工具；密钥不写脚本/manifest。`experiments/integration-matrix/validate_food.py`独立核自然来源与消费，另报接口正确性、实际目标、策略选择、已知usage与未知数。模型先脱险或不Eat可以是合法选择，但目标未完成仍记录失败。

## 当前纯测边界

10项Python纯测仅检验准备脚本的参数挑选与有效schema读取，合成oneOf/const、过count、同value分槽、公开empty-slot、fresh槽变化；不是生产菜单的213项，也不是原生通过。发布前需协调者用冻结实际菜单/ToolDecisionPrompt纯测加同版原生结果，不把这10项替代真实接线。
