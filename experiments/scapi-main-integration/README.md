# main 动作与完整生存角色整合验收

只在隔离云端 `work/` 子目录运行，禁止指向用户存档。

```sh
dotnet build experiments/scapi-main-integration/IntegrationGate.csproj -c Release -m:1 -o work/integration
cd work/integration
dotnet IntegrationGate.dll fixture
dotnet IntegrationGate.dll reload
```

需要正常桌面/OpenGL 和本地模型 `work/local-model/Qwen3.5-4B-Q4_K_M.gguf`、`work/local-model/llama-b12211/llama-completion`，版本哈希见已有本地模型清单。模型单次请求，不执行模型生成代码。

`natural` 必须使用另一个干净输出目录，不含 doc/Mods/旧世界；不布置食物但清空起始物品，有限探索耗尽以非零退出，不能当成功。fixture 可重复验证时也应新建输出目录，不能覆盖旧世界。

主线程检查点在完整更新后捕获 waiting 边界；多子步可能错过边界，不能强存执行中动作。reload 在第一次世界更新前恢复，验证同任务、库存只减1、生理增益与禁止同Project重复恢复。

详细架构、来源、局限和最终结果见 `docs/development/main能力整合验收-2026-11-09.md`。

## 局部探索批测

`bash experiments/scapi-main-integration/run-search-batch.sh` 在正常云桌面会话中一次构建，运行六种子自然地图、封闭墙、动态阻挡以及原采集/存读档回归。可用 DOTNET 指定已安装SDK绝对路径。每次创建新work目录并保留各自结果，不覆盖旧世界。自然场景找不到食物时退出1是失败证据，不以脚本最后返回码代替逐场景结果。

自然地图不摆放食物或改地形，但清空起始背包。真实4B现在可显式选择Survey/FindFood高层目标，本地有限技能负责观察与短步，不能宣称模型逐格设计路线。模型选择Wait则停止，不会被宿主搜索覆盖。局部记忆只记实际已访问位置与失败边，未知格永远不当空气。沙/砾只在显式ObservedFullCube策略下作为当前静态支撑，不推断地下稳定性。

动态障碍模式刻意在观测后加入石墙，用来检验真实碰撞失败和替代路线，不是自然生成。宿主使用实际BoundingBox核对原空气证据、有限步长/超时与高差/入水反应停止；这些是采样式反应，不保证未来世界绝对安全，也未覆盖采集链所有移动。

## 短时多视角窗口

当前自然探索最多64次短路线尝试，整体150秒截止。LocalObservationMemory只保存同角色3秒内、当前5m范围、最多1024格实际已见证据，每格保留来源ObservationId与观察时间；历史明确标记historical。每次发起移动先新Observe/Project，执行中若原证据超过3秒立即取消并记Interrupted，不把过期当真实碰壁。

模型输出使用本次实际候选的(skill,index)组合语法约束，parser和scheduler仍独立重验，不把无效EatOne偷偷改成Survey。推理失败保留有界诊断与已消耗资源指标。main 45614f异步账本模块已复用，但真实采集主链没有启用有现场引用兼容风险的lookahead。

## 混合高度导航（当前）

平地与一格上下台阶来自同一角色的真实观察窗口，共用LocalExploration排序、动作ID、失败退避和3秒证据时效。台阶不直接改写MoveTo，而选用显式StepTo；逐帧由ObservedStepGuard检查空气包络及有限已知支撑接触。扫描交替俯视/仰视，不能用未知上方净空。

乘船出生是单独导航模式：父体为Boat时当前陆地Survey明确失败退出，不消耗模型进行无效转向，也不无条件下船。后续需已观察岸边、原生下船及稳定性验收。

取消表示停止动作输入，惯性与重力仍可继续，尚未证明跳跃中断后的落地恢复。自然多种子短测不等于成功自然生存；若没有实际选到台阶，应标未覆盖，不能拿人工台阶通过替代。

## 连续技能与环境食物目标

`terraced` 是明确人工转角台阶资源正控，完整串联对齐、稳定、补观察、跨阶、真实采食与存读档；和 `fixture` 一样需新目录，成功后可 `reload`。不是天然觅食成功。

失败动作/对齐后使用 NavigationSettling 确认真实驻留，再允许规划。路径输出累计实际水平段及净位移，剔除准备摆位，不把命令次数当距离。缺格观察和执行前证据刷新都有限，不能偷延长TTL。

当前食物执行有已有库存优先与可见植物采集；环境日志增加真实可见动物和掉落，用于后续环境驱动策略。武器制作/狩猎/熔炉烹饪尚未接入此runner，不因原子动作存在而宣称已支持。详见连续导航整链实测与环境驱动食物策略设计。
