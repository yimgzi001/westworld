# 批测检查点与恢复审计

仅在原 dot 云工作根核实记录，不启动游戏、HTTP、子进程，不访问用户电脑。不用旧 PID 推断运行中，也不自动重放任何动作或付费请求。

```sh
python3 experiments/batch-recovery/recovery.py --root "$PWD" begin --case nav-flat-01 --stage navigation --environment dot-cloud --source experiments/scapi-bounded-navigation-gate/Program.cs --result work/nav-flat-01/result.json
# 测试由原有宿主另行执行，检查实际结果后明确登记 passed 或 failed：
python3 experiments/batch-recovery/recovery.py --root "$PWD" finish --case nav-flat-01 --outcome passed
python3 experiments/batch-recovery/recovery.py --root "$PWD" inspect --case nav-flat-01 --environment dot-cloud
```

每个用例保存 `.batch-checkpoints/<case>.json`，固定 v1 schema，仅环境/阶段/用例标签、UTC标签、源码与结果相对路径及 SHA256。不要传 key、敏感内容或私密信息作为标签/文件名。文件内容只流式散列，不写入检查点。标签不是机器身份认证；优先在原环境执行并明确检查 environment。

- begin：源码必须存在；记录当前源码 hash、预期结果路径，原子写 running。相同 case 不覆盖，不重复运行。
- finish：仅 running 可完成；所有源码必须与 begin 一致，所有结果必须实际存在，记录结果 hash，然后原子落盘。passed/failed 是调用者核实后的声明，不解析游戏语义，因此 verified_record 仅表示记录与文件一致，不代表工具证明游戏测试通过。
- inspect：残留 running 一律 interrupted_unknown，即使结果文件存在也只是 uncommitted_result；不判断原进程是否仍活着。完成记录若文件丢失/篡改/环境不符则 integrity_mismatch；不会改写结果或自动恢复。允许先读取错误/缺失信息，再由人或协调者决定如何核实。
- 旧已完成 gate 可以事后登记，但 stage 应标 retrospective，记录的是登记时源码 hash，不是运行当时源码的证明；不要补造旧执行时间或把后来代码 hash 当旧版本。正式新用例先 begin 再执行。

路径只接受工作根内相对路径，拒绝绝对路径、`.`/`..`、反斜线、符号链接（含根及所有父级）；最终文件用 O_NOFOLLOW 打开。每组最多64文件、单文件256MiB、元数据64KiB。要求受信任且没有恶意并发替换父目录的工作根，不宣称是对同权限攻击者的文件系统沙箱。普通并行工具不可同时修改这些文件。

独占 case 锁防同用例并发；崩溃残留锁需先人工核实、再手工处理，工具不按锁里的旧 PID 强拆。临时文件同目录、fsync后replace，再fsync目录；replace前失败保留旧检查点。磁盘/目录fsync失败需 inspect 核实真实落盘，不盲目重跑。

纯测试：`python3 -B -m unittest discover -s experiments/batch-recovery -v`，18项通过，覆盖失败结果、结果缺失/变更、源码变更、原子replace失败、重复、路径/符号链接、残留锁与未知schema。没有游戏/API运行。
