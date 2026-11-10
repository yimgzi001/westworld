# 集中联测 P5（准备与离线纯测，不代表已运行）

只使用一次协调者验证过的冻结构建。默认 `prepare` 不启动任何进程；`run`
必须显式选择场次。所有阶段使用新目录、新世界，拒绝已有运行标记和自动重放。

9场矩阵分组：
- 3 个不同 seed 的 `natural_food file` 官方模型场，最多每场8请求/180秒。
- `direct_late/failure/cancel` 人工确定性生命周期场。这些是可控慢回复/失败/取消，
  **不是慢真实模型，也不是自然食物或自主能力证明**。成熟原生夹具与验证器复用不改。
- `tail_early/tail_late` 复用host direct_early/direct_late，但独立driver首计划为
  MoveTo+FaceTowards两个原生run，后继Wait。各步都先按公开offer/schema校验，
  独立validator核对原计划指纹、两个receipt/run成功、tail_settled后唯一handoff与Wait。
  响应早/晚必须由实际时间证明；缺事件、提前接续、尾失败、run替换均不算正控成功。
  不使用旧单导航validator，因为它强制仅两个动作。
- natural33 仅作历史自然资源获取正控，不得记成食物成功。

统一构建完成后准备（替换明确路径/hash，不使用旧构建）：

```sh
python3 experiments/integration-matrix/batch.py prepare --root "$PWD" \
  --label integration34 --build /ABS/FROZEN/BUILD --content /ABS/Content.zip \
  --expected-host-sha256 EXACT_SHA256 --seed seed-a --seed seed-b --seed seed-c
```

每场二进制/Content/deps hash必须相同，源文件快照共同部分及准备结束的源码hash必须一致。
冻结 `batch.py` 和食物验证器到 batch/frozen，既有驱动/生命周期验证器用各场冻结源码。
源码与构建对应关系仍需协调者的编译记录证明，不能由stage本身推导。

只在允许的云桌面终端显式执行，**本开发批未执行**：

```sh
python3 work/integration34-integration/frozen/batch.py run \
  --manifest work/integration34-integration/matrix.json --case direct_late
# 真模型需要独立明确的模型、effort与发送授权；脚本不保存/传递key参数。
python3 work/integration34-integration/frozen/batch.py run \
  --manifest work/integration34-integration/matrix.json --case food-0 \
  --allow-model-api --model gpt-6.1-terra --reasoning-effort none
```

`--case deterministic`只运行6场无API生命周期测试；`--case models`只运行3场真实模型。
`--case all`严格串行；任一拥有进程组排空不确定则停止，不启动下一GUI。
普通场次失败可继续其他fresh场次，组结果保留每场失败，任一失败/unknown使组命令exit1。真实驱动继承TTY，自己getpass隐藏读取key；
若系统不能提供安全TTY，驱动拒绝，禁止改成明文、环境或文件key。
等待其持久ready事件之后才启动host。准备就绪最多60秒，模型场外层总195秒
（含key/ready等待），确定性场80秒；外层可能比host180秒预算更早停止，必须报告。
只终止本runner创建的独立进程组。启动/退出/排空记录写入execution文件，不补造native exit。
终端/getpass及GUI进程链仅假process测试，尚未原生验证。

离线汇总：

```sh
python3 work/integration34-integration/frozen/batch.py summarize \
  --manifest work/integration34-integration/matrix.json --output /FRESH/summary.json
```

## 食物验收

`validate_food.py`独立读取原始scope、来源、Dig实际run/receipt、transfer、Eat实际
run/receipt、原生FoodEaten事件及meal terminal、final inventory。按时间重放库存与
来源数量，核对FoodItemsEaten和Food变化、零unknown/注入/监听丢失、安全排空。
不采用 `foodGoalVerified` 或 `verified` 布尔值作为成功证据。允许初始鱼被吃，但它
永远不满足新食物目标。缺事件、错run、数量不守恒、饱食无增加均fail。
同一request/value含多个meal但当前日志不能唯一分配事件时保守拒绝，不伪造绑定。
仅自然方块来源；动物、烹饪转换来源尚未支持。

这是日志一致性验收，不是密码学防篡改证明，也不证明模型自主性。真正模型响应必须
另有官方审计与host request/response同ID对应。人工夹具即使存在API样式记录也不计入模型组。

## 指标边界

usage只从旧每请求api-audit读取，绝不把生命周期sidecar的副本再相加。缺失结果、
缺失usage、悬而未决attempt保留totalTokens=null；已知分项仅作known subtotal，不估费用。
请求到动作采用host诊断字段，candidateRequestId缺失时不能宣称逐请求唯一绑定。
运动重叠要求同机monotonic区间、同session/actor/receipt/run、官方attempt/result同request，
连续moving样本间隔≤0.25秒且净段路径>0.01米；缺边界或采样则unknown。
它证明本地官方请求尚未结束时身体真实移动，不证明服务器计算、候选接续成功或零停顿。

测试全部synthetic/fake transport或fake process，无GUI、API、密钥：
`python3 -B -m unittest discover -s experiments/integration-matrix -p 'test_*.py' -v`

负尾 tail_failure 映射host direct_tail_failure，首计划MoveTo+FaceTowards(180度、timeout=.1)，候选Wait。
必须由真实尾run失败/取消、原Move成功、candidate失效及零后继派发、原生排空事件证明，
不读取tailFailure汇总来替代这些断言。若尾实际成功则本负控不成立，不能把总超时算通过。
物理停稳仍由host guard保证，当前没有独立末帧速度证据，验收范围明确标注。
候选旧引用失效与原计划尾引用失效是不同覆盖点，本9场仍不覆盖两者，不能冒称已覆盖。

## 失败快速停止与取消审计（本改动仅离线验证）

模型场启动后每次轮询（目标间隔50ms）读取完整的每请求audit行。首个
`network_error`、不可恢复HTTP认证/请求错误、worker/响应/决策协议错误会立即
结束本场拥有的host/driver进程组，并设置`stopRemainingModels`。组执行停止，
不等待宿主再超时申请下一请求，也不自动重放已claim的请求。未知错误正文不写入
组审计；只保存固定错误分类和合法request UUID。部分末行等待下次读取，不误判为
协议失败。该观察不是和发送端的分布式事务：已有并发在途调用或调度竞态仍可能
发生，停止本地worker也不能证明服务器未计费。timeout/canceled不虚构usage=0。

KeyboardInterrupt现在记录`error=KeyboardInterrupt`、`interrupted=true`以及实际
host/driver退出码和拥有进程排空结果；不再出现Ctrl-C后error仍为null。
组开始/逐场结束/组结束有fsync事件日志，组最终报告用finally保留已有结果和未启动
场次。排空不确定或中断时停止其余场次。重复执行同组事件/结果文件会拒绝。
进程被SIGKILL、再次强制打断清理或存储故障仍可能缺少结束记录，必须保持unknown。

桌面GUI所在执行上下文与获授权的模型网络上下文可能不同；一个上下文的无key
官方HTTP可达性不证明另一个可执行带key的模型POST。不能静默切换代理、通道、
执行环境或认证路径。被审核拒绝的请求不得换路径重试；若未来确需GUI与模型网络
分离，须由协调者明确安排受支持、获授权且身份绑定的桥接方案，本runner没有自动
回退能力。此次修复不修改integration34已冻结stage、旧中断结果或原始API审计，
不重试已失败/被拒绝的模型场，也不声称food-0具有真实模型响应。
