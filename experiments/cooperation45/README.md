# 阶段45同世界双角色运行器

该运行器仅冻结已构建宿主，固定 `matrix_coop file`，不构建、不启动游戏、不调用模型。复用阶段40的新目录隔离、文件哈希校验、超时和资源采样流程；不修改共享stage。

准备：

```bash
python3 experiments/cooperation45/stage.py --label integration45-a --assets work/已有资源目录
```

必须提供已有 `Content.zip`，以及已编译的 `experiments/scapi-tool-agent/bin/Debug/net10.0/ToolAgentGate.dll`。标签目录已存在、构建输出含doc/Mods/日志或符号链接则拒绝。生成脚本固定140秒总进程限制，使用当前dot图形会话运行；实际运行由主协调者在审阅后执行。不要把准备成功当作游戏通过。

安装文件、源文件和汇总器均冻结；启动前核验安装哈希。含启动的wall/CPU/peakRSS资源记录单列，不当作单AI稳态成本。失败和缺失证据必须明确失败；不会启动第二个世界来替代同世界两个角色。

纯回归：

```bash
python3 -m unittest discover -s experiments/cooperation45 -p 'test_*.py' -v
```

只归档源码、驱动、纯回归与精简结果；不归档游戏本体、存档、原始大日志或凭据。此驱动不涉及模型费用，也不证明模型自主协作。
