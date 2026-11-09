# 单个已知进程资源采样

```sh
python3 experiments/process-metrics/metrics.py --pid 12345 --seconds 90 --interval 0.2
```

PID 必须由宿主启动回执/当前可信状态提供；不要从过期记录猜进程身份。工具只读指定 `/proc/PID/stat` 与 `status`，不枚举进程、不读 environ/cmdline、不发网络、不启动/杀进程，不接用户电脑。stdout 一条 JSON，协调者可保存到本轮证据目录。时长必须 (0,600] 秒、间隔 [0.05,5] 秒。

- 保存第一次采样的 starttimeTicks；后续不一致立即停，不能把复用 PID 的新进程算进去。每次以两次 stat 包住 status 读取，避免读取过程中复用。
- sampledCpuSeconds 为最后与首个有效样本 user+system tick 差 / SC_CLK_TCK。平均核数是该 CPU 秒 / 同一采样区间单调墙钟秒，不是按整机核数归一的百分比，也不包含子进程或线程之外的其他 PID。
- peakSampledRssBytes 只是离散采样的最大 RSS，不是真实瞬时峰值，也不是 GPU/显存。
- 退出、缺权限、计数回退、缺 RSS 明确分类；不足两点 CPU=null。中途停止时保留可证明的前缀统计，但 fullIntervalKnown=false、unsampledTailUnknown=true，不估算尾部或假填 0。进程缺失只能确认“缺失或退出”，不能声称正常退出或推断退出码。
- 它测本地游戏宿主/本地推理进程，不测 OpenAI 等云模型服务器 CPU/RSS。需要多个已知 PID 时由协调者独立调用，不能将任意进程扫描混入。
- 600秒为正常采样调度上限；阻塞内核/proc 读取或系统调度故障不保证硬实时返回。无隐藏后台常驻进程。

纯测试 `python3 -B -m unittest discover -s experiments/process-metrics -v`：10项通过，覆盖stat含空格/括号、RSS、CPU秒与核数、PID复用、退出、权限、缺样本和边界。仅合成数据；真实单场游戏宿主采样见 `experiments/openai-tool-controller/evidence/terra-prefetch12/game-metrics.json`；纯测合成数据不能冒充该实测。
