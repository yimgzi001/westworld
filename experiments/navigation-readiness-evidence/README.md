# N35-R：Jump 原生脉冲与即时导航 readiness 门

**非模型、人工平地、一次受控能力测试。尚未进行原生运行。** 不是模型规划策略、自然生存结果或自动等待方案。保留原版 Jump 的输入脉冲完成时机，不等落地才报成功。

## 运行合同

需先由协调者统一冻结/编译35宿主，使用全新 stage 与 checkpoint。专属模式是 `ToolAgentGate.dll handoff file direct_readiness`。它复用原 direct fixture 的原生下船与人工平地，明确不属于天然场景。仅此 case 放开 `host_navigation_status` 菜单并采用独立终止，不更改自然模式。

冻结此目录全部 `.py` 与 `experiments/food-acquisition-evidence/deterministic_driver.py` 到 stage 的 `sources/experiments/` 相同布局。driver 可从源码快照执行，或者复制到 stage 顶层执行；顶层导入只找本 stage 的 frozen food dependency，不回退当前仓库。

协调者在同一个有界 GUI runner 内启动：

```sh
python3 -B <stage>/sources/experiments/navigation-readiness-evidence/driver.py <stage>
# 独立游戏进程，同一 fresh stage 的 cwd：
<dotnet> ToolAgentGate.dll handoff file direct_readiness
# 游戏退出后，runner 写 exit.txt，等待 driver 退出，再运行：
python3 -B <stage>/sources/experiments/navigation-readiness-evidence/validate.py <stage> --output <stage>/readiness-validation.json
```

上述命令为使用说明，本目录不自行启动游戏/API。driver 最多4请求/65秒；宿主沿用 handoff 60秒停止、7秒排空，外部 runner 每场最多90秒。退出失败不删除证据，不因 validator 非零退出而丢失 runner outcome。checkpoint 只预声明必定生成的 runner outcome；缺失 driver/native 文件保持 missing，不能造文件冒充执行完成。

## 确定性控制器

只读 file request 的 identity/deadline/final `OBSERVATION` 与 offers，不读 host/native audit、来源账本或世界。第一份公开事实必须明确未骑乘、无 ParentBody，并有当前真实 ready body。

同一 accepted plan 只有：

1. `act_Jump {}`；
2. `host_navigation_status {}`；
3. `host_navigate_local {bearing:90,distance:1,max_seconds:4}`，绝对 +X。

导航放最后，默认即时调用，不附 Wait，不重试。失败即既有 ClearPlan 清旧尾。后续普通请求只查询 status；若因调度错过离地窗口而真正开始导航并产生预取，只回空后继。不会偷偷补 Jump、Wait、移动或取消其他 owner。幂等发布采用 exclusive hard-link，拒绝既有 response，取消/到期不发送，文件读取有界，跨scope与重复request/observation拒绝。

## 只读 fixture 与独立结果

`ToolAgentScenario.ReadinessFixture.cs` 仅在专属 End 分支运行，读取唯一原生 owner 的 action/status/body。导航尝试后暂停新增决策，不发动作；实际 readiness 与 Walk/Turn 静默持续至少0.2秒才 RequestStop，沿原有 owner 排空。它不改 grounded、速度、Jump结果或落地时机。

日志：`n35_readiness_fixture_sample`（约50ms和状态变化，但不承诺帧调度）与 `n35_readiness_fixture_result`，带 session/actor、单调时钟、原生 Jump run、navigation request/readiness、未改写的 start_failure_readiness、实际位置/速度/输入、owner状态。raw receipts、`native_terminal_observed`、退出排空仍需独立匹配。

validator 最低门：同一 Jump run 真成功；Jump成功时仍离地；查询后同计划一次导航；真实 `body_not_ready`+`not_grounded`，segments/path=0、没有本地 native run；后真实稳定；失败样本没有被落地后的ready覆盖；零重试/迟派发；actor/session绑定与正常排空。

- `pass`：上述证据全满足。
- `uncovered`：执行时已不处于离地条件。本次不算覆盖，不自动再跑。
- `unknown`：关键证据或稳定采样不足，不算通过。
- `fail`：证据相互矛盾或确定违约。原始失败保留。

纯测：`python3 -B -m unittest discover -s experiments/navigation-readiness-evidence -v`。13项涵盖真实日志形状的正控合成输入、跨actor、错run、无证据/覆盖不足、窗口未覆盖、重试/迟派发、改写失败快照、无未知骑乘推断、无私有context回退、staged依赖导入。这些是合成纯测试，不是原生覆盖证明。
