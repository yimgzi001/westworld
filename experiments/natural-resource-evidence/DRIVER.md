# 自然资源链的固定文件驱动

这是独立、可审查的**无模型回归策略**，不是产品策略、模型自主表现或自主食物链。宿主负责自然生成世界、原生动作与来源审计；driver不会启动GUI/API、写世界、注入方块/物品、改库存/健康/位置、传送或自动下船。

## 运行与依赖

先由协调者完成冻结构建和fresh staging，再在dot云电脑按既有GUI流程运行：

```sh
python3 -B <stage>/deterministic_driver.py <stage>
# 由协调者在同一fresh stage串行启动原生宿主：
# <dotnet> ToolAgentGate.dll natural file <seed>
```

也支持 `python3 -B experiments/natural-resource-evidence/deterministic_driver.py <stage>`，但正式证据应执行stage中冻结副本。顶层stage入口只导入本stage `sources/experiments/food-acquisition-evidence/deterministic_driver.py`，不回到工作仓库读后来修改的代码。直接在源码目录或sources目录运行时使用同级food目录。stage必须保存两份源码与SHA。

每场最多 **8个文件请求、180秒**，和natural宿主8请求/180秒/45秒每请求/7秒排空一致。启动标记拒重放；每请求只处理一次，拒重复request/observation或actor/session变化；请求/响应分别有256KiB/64KiB边界，检查取消与真实UTC期限，原子硬链接提交，不覆盖其他响应，拒符号链接。运行中异常写 `natural-driver-result.json` 的明确error并退出非零，不伪造宿主结果。driver结束不等于游戏目标成功。

## 决策能读取什么

只从每次公开request的最终 `prompt` 中唯一 `OBSERVATION:` JSON读取事实，不使用request.context备用入口。共享food驱动的safe、read_request、facts_from_prompt、valid、counts，使用公开offers的工具schema、引用枚举与分支约束。只读控制目录request/取消/退出元数据，不读取tool-agent日志、native证据、来源审计、全知地图或游戏存档。代码没有网络调用或执行模型代码的入口。

## 策略与边界

1. 选择当前visibleBlocks中的近处泥土/草/木，公开基础游戏contents白名单为2/8/9/10/11。土草优先。必须有有限cell/position/value/distance、实际ref并在当次Dig菜单中；面点必须属于该格。目标距离≤2.75m，高度只取脚下一级到身旁一级。
2. 排除actor水平身体范围向外扩.65m覆盖的格，因此脚下或踩在边界的支撑格也不挖。排除液体18/92、未知/空气/岩石/沙与白名单外目标，以及与**已可见**液体相邻的格。看不到的流体/地形不被当成安全事实。
3. 每次只回一个 `act_Dig(target,timeout=4)`，然后要求新observationId与真实Dig终态。不重复尝试同引用或同一格，即使该格换了新引用；失败可选另一合法格。动作结果未知则停止追加动作，拒盲目重放。
4. Dig实际成功后才考虑当前 `sense=vision` 的掉落。只选同类资源、在目标附近的新可见ref，排除挖掘前已可见的ref；这种筛选**不证明因果来源**。近身≤1.25m水平距离尝试 `act_PickUp(target,count=1)`；否则仅在≤3m、相对高度≤1m且当前菜单有权限时尝试 `act_MoveTo(destination=实际掉落ref,range=.75,timeout=4)`。不把方块位置假装掉落，不凭预取预测库存，不在没有真实掉落时先走一段。
5. MoveTo/PickUp同工具同ref不重试。没有合法掉落时最多等两次新的公开观察，再考虑另一个未尝试的安全候选格。没有安全目标时使用下述有限主动观察，不能在无证据时走向未知处凑成功。
6. 已尝试挖掘且当前资源库存比初始增加时停发动作，仅说“库存增加已观察”；来源唯一性、Dig→掉落→真实转移入包、同run成功与原生健康/异常状态仍必须由独立宿主证据验证。

“近处/非足下/已见液体排除”是有限目标筛选，不是完整地形安全证明。原生射线、距离、工具条件与路径/碰撞仍可能拒绝或失败，完整结果必须保留。自然出生可能在水面或船上，driver不会隐式准备平地或下船。木头裸手挖掘若4秒不足可失败，不扩大生产菜单或替换原生前置条件。没有自然资源候选、掉落被遮挡或未知时，此固定策略可能耗尽预算；不得称作自主采集能力或生存闭环通过。

## 阶段32：有限主动观察

31版不作转向，真实a/b两场没有合法土/草/木，8次均空计划。公开a视野是水/沙砾，后见海胆；b是沙子/水，位置、朝向和可见格集合8次不变。两场扫描完成且无输出裁剪，但只覆盖当前5m有界视角，不能断言其他方向没有资源。31冻结源码与失败证据不改写；32改动只是新的确定性测试策略，不是生产模型的决策规则。

- 在所有合法采集候选都用尽时，要求当前公开 `observation.self.isMounted === false` **且** `hasParentBody === false`，以及有限、非垂直的 `self.facing` 和当前 `act_FaceTowards` offer。缺字段、null、0、字符串false都不当成明确false；未知或骑乘只给阻碍说明。原生未知字段可能因JSON忽略null而省略。
- 每次仅发 `act_FaceTowards(bearing=90, timeout=4)`，整个driver最多3次尝试，仍在8请求/180秒总预算内，不因失败、换观察或其他动作重置。下一步必须是新observationId与实际FaceTowards终态，不能用预取的预测成功。失败/取消/拒绝/跳过锁定不再尝试扫描。
- **native bearing是相对角度**，不是 `host_navigate_local` 的绝对方位。冻结 `ActorTools.TurnCommand → RelativePoint` 使用当前Facing绕+Y轴旋转负bearing，因此native +90令+Z朝向变为-X。诊断角采用 `atan2(facing.x, facing.z)`，+Z=0、+X=90；预计新角为当前角减90，按360环绕。每次都用最新真实self.facing推导，不能按最初方向硬算。
- 已观察方向被记录。成功终态后还要求实际新朝向与预计方向差≤10°；不变、反转、缺朝向等都阻断后续扫描，不把陈旧成功状态当成转向完成。下一次目标若距已观察方向≤10°则不重复。此容差是驱动一致性检查，不替代原生动作成功，也不宣称3次转向构成完整360°视野覆盖。
- 新视角若有合法候选，立即回到单Dig→重观察的既有路径。没有目标仍明确空计划/预算结束，不自动Dismount，不朝水底、沙砾、海胆或未知方向发探索MoveTo。可见表面reference不是安全步行路径证据，wetness/漂移也不是骑乘证明。

启动标记和结果中 `policyVersion=bounded_turn_scan_v2` 区分版本，结果另保留scanAttempts、scanHeadings和scanBlocked。扫描尝试或实际朝向改变均不等于自然资源获取成功。

## 纯测试与冻结

```sh
python3 -B -m unittest discover -s experiments/natural-resource-evidence -v
```

31版driver纯测试36项，覆盖支撑/邻水/未知/岩石排除、单Dig→重观察、实际终态、可见掉落、菜单和引用分支、同格/同ref不重试、库存不是来源证明、预取不预测、身份、限额/期限/取消、原子不覆盖、符号链接、异常结果，以及顶层stage冻结依赖导入。

32版另增15项转向纯测，driver合计51项：三次上限、明确false门槛、无自动下船、四轴/独立Quaternion公式/±180环绕、实际朝向推导、重复方向与陈旧成功拒绝、真实终态等待、失败锁定、缺失/越界菜单、采集优先、预取/健康/未知结果阻断。当前同目录stage与validator合计61项通过。纯测试及既有公开request的离线选择检查不算32自然世界运行证据；本次未启动GUI/API。
