# 阶段40真实矩阵驱动

两个新一次性世界，不复用用户世界或旧场景存档。驱动是确定性接口验收，不是模型自主生存；不调用外部模型、不下载本地模型。

## 单角色 matrix_single

宿主 `ToolAgentScenario.CapabilityMatrix40.cs` 使用已有唯一角色网关。准备阶段仅人工平地、站位、原生下骑和2个橡木注入（日志明确）；正式队列每步都重新快照、显式选工具、经当前新菜单 `ToolDecisionPrompt.ParseResponse` 校验，再调原生网关。

已编写：6种查询，蹲/站、转向、跳、等待、移动，选槽、库存转移，配方查询和原生组合制作，挖/放，以及有限计划成功/取消。实际位移、库存守恒、制作增量、地形变化、任务标识与取消后0.5秒无后续推进分别留证。未在本模块中实跑，不能预称通过。装备穿脱、设置、家具、战斗未被这个场景覆盖，另需夹具。

失败先取消并等待7秒动作/任务排空，超时明确cleanup_uncertain且不恢复地形；随后应关闭一次性世界。Dismount仅实际骑乘时调用；地形修改前等待chunk就绪。

## 双角色 matrix_social

独立 `CapabilitySocialMatrix40` 独占两个external actor。详见 `SOCIAL.md`。通过原生网关，但目前不经过动态菜单适配链，证据单独记native-only，不能并入adapter-validated覆盖。两种路径都不是模型推理。

## 编译、隔离准备、运行

1. 总集成完成且所有编辑停止后，使用现有SDK构建Debug宿主，不restore、不运行旧全量测试：

```sh
./work/dotnet/dotnet build experiments/scapi-tool-agent/ToolAgentGate.csproj --no-restore -m:1 -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false
```

2. 仅准备运行目录，不启动游戏：

```sh
python3 experiments/capability-matrix40/stage.py --label <全新标签> --assets <已有运行资源目录>
```

只复制本次构建输出、现有Content.zip，记录源码和所有安装文件SHA256。拒绝任何已存在目标目录。产物含游戏资产，只留本地work，禁止上传整个运行目录。

3. 由总集成在dot桌面确认无人占窗口后启动生成的run.sh。固定`matrix_single file`或`matrix_social file`，固定`WESTWORLD_CAPABILITY_MENU=1`，不接收任意controller。启动前拒绝已有doc/Mods/日志，验证安装文件hash。最多140秒墙钟，正常内层场景100秒及7秒排空；外部硬截止不等于已完成cleanup。

4. 不保存/复用世界。原始JSONL及构建清单仅留本地；仓库只提交源码、简要结果与必要回归样例。生成摘要：

```sh
python3 experiments/capability-matrix40/summarize.py <run>/tool-agent.jsonl --mode matrix_single
python3 experiments/capability-matrix40/summarize.py <run>/tool-agent.jsonl --mode matrix_social
```

摘要要求内层通过、外层result通过且无断言/cleanup错误；`nativeCalledTools`只说明调用过，不表示均通过。成功覆盖需看逐项效果断言；`adapterValidatedTools`仅单场已通过新菜单验证的项。

## 已执行的本模块验证

- 宿主编译：0错误，39既有警告。
- `python3 -m unittest discover -s experiments/capability-matrix40 -p 'test_*.py' -v`：6项汇总器纯测试通过。只读合成内存日志，不触发游戏/API。
- 已执行的真实矩阵结果见 `docs/development/能力覆盖40/本轮结果.md`；这里的汇总器通过不等于游戏通过。

## 隔离扩展矩阵

阶段41使用 `--suite equipment`、`--suite combat` 或 `--suite furniture`，配合 `--modes matrix_single` 和各自全新label。每组在独立世界运行，只共享基础地形准备及当前菜单执行器，不依赖阶段40前序案例。扩展组失败即排空停止，不在可能污染的状态上继续。默认baseline保留阶段40场景。原始输出始终留本地。
