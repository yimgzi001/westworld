# 阶段35：明文验证证据

本包使用可直接阅读的UTF-8日志/JSON，没有gzip、base64或内容寻址分片。只收已经发生的两场和必要纯测试记录，不含游戏本体、资产、世界、存档、Key或环境变量。

## 结果

- **39套纯回归1591项通过**：逐个测试日志读取最后计数，不把聚合日志再加一次。新增stage脚本14项另列。纯测试不是模型或游戏自然生存通过。
- **readiness原生夹具通过，6.6456986秒**：host、driver及协调退出文件均0，固定离线validator独立重算pass。原生Jump脉冲已报告成功，但身体仍在空中；紧接的导航尝试以body_not_ready拒绝，没有启动路径段、没有隐藏重试。落地后真实稳定采样恢复Ready。人工非模型夹具，不是自然模型成功，也不是跳跃动画结束等于已可导航。
- **food-0请求结果未知**：官方driver只记录1个API request_attempt，无request_result/回复/用量。宿主超时后发布第2个request，但该请求没有API attempt。主audit为空、0原生动作。轮询受审核阻止后协调者Ctrl-C停止宿主并确认无残留进程，后来会话49135查询返回Unknown。保留原interruption说明，不把这些信息编成正常退出码。
- food-0没有host exit.txt、最终result或driver ended记录；费用及请求是否完成均未知，不能写0费用、模型失败或自然食物失败。没有重放。food1/2没有运行，不在本包中给出结果。

## 来源与保留层次

readiness冻结150份来源，food-0冻结156份来源，逐stage全部文件哈希已在归档时核对。宿主共同SHA256：`250cbcbd99415ca981d77daee00ffb88ba7df401aeaa2610d7d2f7bde6c93f14`。

完整源码随本次仓库提交保存，本包只保存source-hashes、installed-hashes、构建前来源清单，不再次复制整份源码。两stage的stage.py版本不同，分别保留4份必要的小源码文件中的两份；另外两份为冻结readiness验收器和驱动常量/纯函数来源。不能拿当前stage脚本冒充旧运行版本。build清单与readiness/food-0分别有121/123个同路径哈希关联，全部相符。哈希只能证明对应字节，不能替代独立重建完整游戏。

pipeline真实旧上下文fixture由同次源码提交提供，这里仅记录文件哈希，不重复携带压缩体。fixture回放是离线测试，不是阶段35新模型调用。

## 文件与校验

- `readiness/`：原生日志、控制请求/响应、退出、driver结果和独立验收。
- `food-0/`：中断前原始记录、两份公开游戏请求、1次API尝试、缺失结果说明。
- `pure/`：39份独立测试日志、聚合日志、最终构建、来源哈希及14项stage测试。
- `frozen-minimal/`：4份必要小脚本；其余源码不重复归档。
- `manifest.json`：每个文件的原始SHA256、脱敏后SHA256、字节数与来源。

```sh
python3 -B experiments/navigation-readiness-evidence/evidence/integration35/verify.py
python3 -B experiments/navigation-readiness-evidence/evidence/integration35/verify.py --check-sources
```

第二条还校验当前仓库源码与冻结清单；历史源码以后变化时应在对应提交运行。原始work输入仍在时，可用 `archive.py --write` 重建。脚本不会启动游戏、driver或API。verify只抽取固定哈希验收器的纯validate及必要纯函数，不导入driver入口。

仓库、用户家目录和系统字体绝对路径脱敏为占位符；原始哈希仍保留，未知或失败不改写。角色health/inventory等是模拟游戏状态，不是用户个人健康/资产。凭据模式和私有路径扫描无命中不是绝对安全保证；所有文件都可直接审阅。原始证据不变，归档校验不是不可篡改证明。
