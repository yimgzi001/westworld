# 本地Qwen自主技能选择层（准备阶段）
只负责真实紧凑观察→模型技能选择，不拥有游戏文件或桌面控制。游戏gateway负责observe/execute和所有安全检查。允许inspect_food / explore_safe_ground / approach_food / gather_food / eat / stop。提示给总目标、技能语义和边界，不指定下一动作或预设路线。

`with LocalModel(LOG_PATH) as model:`启动并保持本地Qwen3.5-2B Q4_K_M、2CPU线程、4096 context、1slot；默认最多10决策/300秒。`decide(obs)`返回选择及验证结果；无效选择不自动换成脚本正确动作。执行前gateway必须重取真实状态、复核target_index对应原观察、检查健康/空气/陈旧/路径与总预算。返回`needs_fresh_execution_recheck`不是执行授权。

观察契约：observation_id、captured_unix、health、air、food、available_skills；food_inventory[{index,count,nutrition,sickness_probability,provenance}]；resources[{index,kind,distance,nutrition,sickness_probability,reachable,gatherable}]；exploration_available、last_result短脱敏描述。索引是当前数组位置，不是GUID。eat必须natural_collected来源，starter和fixture都拒绝。不要把仅在背包出现的物品自动标成natural_collected，来源必须有真实采集证据。

决策日志独立记录观察、原始选择、拒绝原因、tokens、延迟、模型进程CPU秒/RSS；execution_result另记真实前后状态、确认和结果。无模型选择成功不能写成已执行成功，执行失败不隐藏。资源计量只包含模型进程，不含图形游戏/其他进程。全部仅localhost，不使用云API。

目前12/12离线验证测试通过，尚未运行自主游戏试验。初次集成等待gateway真实schema和游戏恢复；不直接启动GUI或操作游戏IPC。

## 首轮真实自主试验：未完成目标，重复检查循环
运行 `run_gateway.py`，通过既有 Gateway 观察/执行，没有直接发送游戏IPC。run=qwen-autonomous-20261107-140050。提示仅给自然采食总目标、技能语义及来源/安全边界，没有指示下一动作。

结果：10次模型选择全部为inspect_food；虽然真实观察持续提供resources=[]、safe_exploration_available=true且available_actions包含explore_safe_ground，模型仍未选择探索。保留失败，没有脚本替换动作。0次探索/采集/进食；位置始终(-136.90239,66,71.20757)，Food .3325728→.32357475，health/air1。所有执行最终stop及收尾stop确认。

实际总47.27秒，模型加载约1.51秒。单次请求3.46–6.05秒，中位3.97秒。10次推理CPU累计82.23秒，模型进程全过程CPU83.92秒，峰值RSS2,005,348KiB（约1.91GiB）。输入3347 tokens、输出454 tokens（包含缓存语义时不能视作全部重新计算token），没有云API费用。模型服务正常退出。

日志：qwen-autonomous-20261107-140050.jsonl与.summary.json；游戏执行证据survivalcraft-lab/qwen-autonomous-20261107-140050/。重复inspect返回的完整观察在gateway日志中嵌套增长，模型实际收到的是短status摘要，不把原始历史全部塞入上下文。

下一轮合理改进是补充真实任务记忆：observe已经完成新scan、连续inspect次数、位置是否变化、资源是否变化、上次技能效果。不能直接规定“下一步explore”后再称模型自主。可降低reason输出长度以减少非动作token，但须记录提示/schema变化，独立报告首轮失败与后续结果。

## 第二轮真实自主试验：加入事实反馈仍循环
独立版本decision_v2.py/run_gateway_v2.py，保留首轮文件。仅新增真实字段及其定义：observe已匹配新scan、连续inspect次数、位置未变次数/持续时间、资源集合未变次数、上一动作真实位移/采得/吃成功。没有删掉技能强迫探索，没有指示下一动作。

run=qwen-autonomous-v2-20261107-140307。仍10/10选择inspect_food，无探索、位置变化、采集或进食。第10次输入明确连续inspect=9、位置资源不变各9次、53.37秒无位置变化、当前扫描已更新；模型仍重复，理由多次关注不可吃的starter面包。反馈确实进入模型观察日志，不是适配字段丢失。

总60.05秒；单次4.48–7.64秒、中位5.31秒；推理CPU累计106.83秒；峰值RSS2,051,832KiB（1.96GiB）；输入4889/output512 tokens。Food .3007823→.28919324，health/air1，最终stop确认，模型退出。

结论：这两轮真实测试未证明2B可以自主完成采食目标；相反暴露无效信息重复选择循环。后续宜先在同一真实快照上离线比较表示与提示，明确隔离参数变化，不继续用真实世界无效检查造成饥饿。即使离线选择改善，也必须另跑真实执行闭环后才能宣称改善有效。

## 六次纯离线A/B诊断（旧真实快照，无游戏动作）
脚本offline_ab.py只读取v2第10次真实观察（显式snapshot/stale），不导入Gateway、不访问游戏。A原中文提示/原表示，B简洁英文提示/同表示，C同英文提示/极简等价表示；可用技能和总目标不变，没有强制探索。按A/B/C/C/B/A顺序各2次、temperature0、同schema；每个提示全文、状态和原始响应保存在offline-ab-results.json。

- A：2/2选inspect_food，target_index=-1，合同合法但仍无任务进展。
- B：2/2选inspect_food，target_index=0，违反无目标技能必须-1的约定，拒绝。
- C：2/2选explore_safe_ground，target_index=1，同样违规，拒绝；没有偷偷归一化索引或执行。

观察：更简状态把starter物品从可选列表改成排除数量，模型动作意图改变，说明循环不完全是无法选择探索。但仍有参数绑定错误，不能称为可执行自主策略。每版2次确定性重复，样本小且缓存不同，不能当稳定成功率估计。

6请求累计CPU53.84秒，峰值RSS2,205,360KiB（2.10GiB）；延迟2.35–8.15秒，重复调用大量缓存，不宜用此做提示版本速度排名。模型已退出。runtime helper日志中的decisions=0是因为离线直接HTTP请求绕过只接新鲜观察的decide入口；实际六次完整记录以offline-ab-results.json的calls为准，不是零调用。

后续若获准继续，可采用按动作分型schema：inspect/explore/stop没有target_index字段，approach/gather/eat才带索引。这样是不让模型产生无意义参数，不是事后修正错误选择；仍需保留所有动作并进行新的离线/真实验收。此次六次预算已结束，未自行加测。

## C2两次离线补充：分型schema得到合法探索选择
保持C的同一旧真实快照、极简状态、英文提示、temperature0、全部动作选择，仅将schema明确为anyOf两个分支：inspect/explore/stop的target_index只能-1；approach/gather/eat的index为0..15。新schema全文和原始响应见offline-c2-results.json，脚本offline_c2.py。没有事后修正输出。

两次均原生生成explore_safe_ground/-1，通过原验证。第1次无缓存6.32秒，第2次缓存308tokens为2.92秒，输出各52tokens；CPU合计18.34秒，峰值RSS2,036,112KiB（1.94GiB）。模型正常退出，无Gateway或游戏动作。

这是在同一快照上形成合法探索选择的证据，不是实时采食闭环成功，亦不代表多样任务可靠性。提示/状态表示与schema都是系统设计的一部分，应完整披露，不将离线改善覆盖前两轮真实循环失败。
