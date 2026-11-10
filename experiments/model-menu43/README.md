# 43 当前菜单真实模型食用正控

默认不执行API，stage仅冻结新目录。复用已审阅官方controller和食物stage，不运行旧全量批次。固定gpt-6.1-terra/none、4请求、180秒、每请求45秒，匹配未修改的food_drop宿主预算；输出512。人工食物不能称自然生存。

```sh
python3 experiments/model-menu43/run.py stage --label model43-food-a --build-output experiments/scapi-tool-agent/bin/Debug/net10.0 --content work/integration42-b-social-matrix_social/Content.zip
```

确认实际BUILD目录/当前编译、stage hash和新目录后，在dot图形终端TTY显式启动：

```sh
python3 work/model43-food-a/sources/experiments/model-menu43/run.py run --directory work/model43-food-a --allow-model-api
```

官方controller直接在TTY隐藏读取既有授权key，仅内存保留；wrapper不接收key参数/文件/环境，不打印key。driver ready事件后才启动host。host run.sh显式env WESTWORLD_CAPABILITY_MENU=1。外部195秒截止（包括最多60秒ready等待），失败不重试，终止仅自己拥有的进程组。进程回收不等于游戏排空；execution.nativeDrainCertain保守false，需审阅宿主原生证据后才能确认。

不接受已有control/doc/Mods/日志目录重跑。发布前根据真实model_prompt/capability_discovery确认新菜单有效，并关联官方API audit与宿主requestId发布/接收，避免旧日志或空目录假通过。无usage为null不记零。检查宿主食物来源、库存/原生食用事件与饱食变化后再报告效果。

离线回归：`python3 -B -m unittest discover -s experiments/model-menu43 -p 'test_*.py' -v`。仅临时文件/模拟数据，不启动进程或网络；当前6项通过。真实模型尚未运行。
