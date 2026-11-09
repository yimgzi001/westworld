# 连续局部导航：验收工具与当前证据

## 当前状态

- 工具纯测试 **30项通过**。它们验证分析、严格验收、固定延迟driver和stage，不等价于原生游戏成功。
- Stage18固定平地3m→1m的1/3/8秒回复矩阵 **9/9真实完成，冻结严格验收9/9 pass，全部updateFailure=0**。逐项见 `matrix.json` 与 `stage18-summary.json`。每项仅在真实exit及日志存在后登记，不能将未运行负控算通过。
- 本目录工具不自行启动模型或GUI。真实游戏由协调者另行运行；这些工具负责准备固定文件驱动及离线核实。固定回复不是模型自主策略，不能声称自然地形、生存或多AI规模已验收。

```sh
python3 -B -m unittest discover -s experiments/navigation-continuity -v
python3 experiments/navigation-continuity/analyze.py work/<case>/tool-agent.jsonl
python3 experiments/navigation-continuity/validate_route.py work/<case>/tool-agent.jsonl
```

## 严格验收而不是沿用旧PASS

`validate_route.py` 输出pass/fail/unknown及逐项检查：首命令3m；唯一bounded收据/终态、单segment Arrived与实际进展；唯一完整route和匹配local起终点；完整授权bearing的3m终点；同owner实际持租Moving样本，相邻间隔≤0.1s；每个内部waypoint半径0.2m的两侧都有样本且无<0.1m/s；第二1m也实际Arrived/进展；整场passed且updateFailure=0。

已知反例fail，缺身份/证据unknown，不靠选择第一个事件猜成功。CLI退出0只表示分析完成，**验收结果必须读取verdict**。序列关联采用唯一串行单角色区间及实际bounded/local IDs；身体样本缺routeId仍是已知限制，不宣称加密认证或任意并行日志关联。

## 统计边界

遥测 `navigation_sample` 包含宿主monotonicSeconds、bounded requestId、ownerId、state、实际horizontalSpeed、position/velocity、grounded、segmentCount/reason及busy/ownsLease。ownerId是对象标识，不代表持租。只累加同owner/request、时间递增且≤0.1s相邻样本；缺样/变更/回退不插值。阶段耗时按左样本状态离散统计，不能证明两帧间每瞬都同状态。

真实Arrived/Failed/Cancelled且busy=false的样本会保留；没有终态后样本的旧日志仍unknown，不能把缺样补成静止。内部waypoint附近速度属于采样证据，不能保证采样间完全无停顿。route事件无单调时标、身体样本无routeId，因此选中route统计明确为串行日志关联；segmentCount不冒充内部waypointIndex。

原生terminal→下一dispatch、宿主观察terminal→dispatch分别列出，不把轮询时间归模型。旧dispatch缺candidate requestId，所以时间关联标temporal_only_unbound。UTC/游戏Time不代替宿主单调时间。缺少query/projection CPU独立计时就保持null。

## 固定driver与安全staging

冻结新host构建后：

```sh
python3 experiments/navigation-continuity/stage.py --label continuity-new --delay 1 --repeat 1 --content work/nav-handoff02-late/Content.zip
python3 experiments/navigation-continuity/stage.py --label continuity-new --delay 3 --repeat 1 --case route_wall --content work/nav-handoff02-late/Content.zip
```

stage仅新建work目录。默认early正控；负控白名单route_cancel_before/route_cancel_after/route_wall/route_support。输出目录含label/delay/repeat和负控case，拒覆盖旧目录。stage.json标staged_not_run，不能当执行结果。

复制当前编译DLL/native库、deps/runtimeconfig、固定init.js与既有Content.zip；不复制key或模型。native库白名单包含.so及纯数字版本链（.so.3/.so.3.4）、dll/dylib，不接受.so.secret/.so.3.key。目标明确dot Linux x64，创建目录前必须有libglfw.so.3及libnfd.so。源码快照覆盖Core/Adapter/host/原生Actions与driver/stage，全文件hash保存；hash证明暂存内容，不自动证明这些源文件构建出了DLL，协调者须先build/freeze。

run.sh校验fresh/hash后调用固定已审driver及已构建ToolAgentGate handoff file指定case，分别记录host/driver退出码。实际run.sh需协调者通过支持的GUI流程串行执行。driver不读游戏隐藏状态、不调用网络/模型，只读最多两个公开request；第一立即给3m东方导航，第二延迟1/3/8秒给1m。总65秒，等待检查exit/canceled，不覆盖响应；同目录再次启动拒重放。取消后未投递必须如实记未投递，不能称验证了迟包攻击。

## 矩阵解释

九个正控为1/3/8秒各三次，同3m目标、权限与预算，保留所有成功和失败；8秒可在终点合理等待，不为掩盖延迟多走。八个额外规划负控的执行状态单列，不能因九场正控通过而补成已运行。route专属机制测试还需证明route实际已启动，并验证取消/墙/失支撑的真实机制；TTL停机不算碰撞机制通过。

每场使用batch-recovery先begin固定源码/结果，再真实执行与finish。旧结果事后登记必须显式retrospective。Terra对照沿用已明确批准的数据范围和模型，超范围才另确认；模型不选择相同任务时不硬改回复凑配对。

## 历史证据保留

| 阶段 | 事实及局限 |
|---|---|
| Terra12/13 | 真实模型旧日志，无逐帧身体数据；状态耗时/物理停车unknown。已有analysis附原日志SHA。 |
| Stage14 | 首3m只有末约1.14m选中两waypoint快路，另8次fallback；内部附近35样本最低1.85687m/s。不是整3m连续。 |
| Stage15 | 四场漏复制版本化GLFW库，未进入游戏。stage白名单缺陷已修，原失败目录未覆盖。 |
| Stage16 | 旧fallback不能通过严格完整route验收，native16-validation保留fail。 |
| Stage17 | 首完整3m/3waypoint单segment2.6501s成立，第二1m证据刷新失败；整链fail，native17-validation保留。 |
| Stage18 | 各实际完成场独立analysis/validation及SHA，不由旧PASS推断；当前汇总见stage18-summary。 |

fixtures仅保留16/17所需脱敏事实，去掉prompt/API/工具原始上下文与格证据；17保留身体时序以可移植回归。合成正分支测试不会修改真实失败记录。

Stage17回归事实以确定性gzip+base64保存（不足29KB），manifest校验压缩字节与解压后原文SHA256和长度，解码仅内存。原187646字节内容与断言不变，不再作为大段JSON源码重复发布；Stage16小型事实仍保留JSON。
