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

`bash experiments/scapi-main-integration/run-search-batch.sh` 在正常云桌面会话中一次构建，运行三种子自然地图、封闭墙、动态阻挡以及原采集/存读档回归。可用 DOTNET 指定已安装SDK绝对路径。每次创建新work目录并保留各自结果，不覆盖旧世界。自然场景找不到食物时退出1是失败证据，不以脚本最后返回码代替逐场景结果。

自然地图不摆放食物或改地形，但清空起始背包。真实4B现在可显式选择Survey/FindFood高层目标，本地有限技能负责观察与短步，不能宣称模型逐格设计路线。模型选择Wait则停止，不会被宿主搜索覆盖。局部记忆只记实际已访问位置与失败边，未知格永远不当空气。沙/砾只在显式ObservedFullCube策略下作为当前静态支撑，不推断地下稳定性。

动态障碍模式刻意在观测后加入石墙，用来检验真实碰撞失败和替代路线，不是自然生成。宿主使用实际BoundingBox核对原空气证据、有限步长/超时与高差/入水反应停止；这些是采样式反应，不保证未来世界绝对安全，也未覆盖采集链所有移动。

## 短时多视角窗口

当前自然探索最多32次短路线尝试，整体150秒截止。LocalObservationMemory只保存同角色3秒内、当前5m范围、最多1024格实际已见证据，每格保留来源ObservationId与观察时间；历史明确标记historical。每次发起移动先新Observe/Project，执行中若原证据超过3秒立即取消并记Interrupted，不把过期当真实碰壁。

模型输出使用本次实际候选的(skill,index)组合语法约束，parser和scheduler仍独立重验，不把无效EatOne偷偷改成Survey。推理失败保留有界诊断与已消耗资源指标。main 45614f异步账本模块已复用，但真实采集主链没有启用有现场引用兼容风险的lookahead。
