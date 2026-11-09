# 阶段19–23共享观察证据归档

此目录收集失败与成功，不以较新通过覆盖旧失败。`archive.py` 不运行游戏、模型、网络或恢复任务；默认只检查，`--write` 才在本目录写入归档。源码以基线提交 `50e3c1007bdfc0f813195671b737180f47f59ebc` 加各次冻结差异包复现，不重复打包整个源码树。

发布者从准确基线Git tree导出 path→blob SHA1 JSON后执行：

```
python3 experiments/navigation-continuity/evidence/observation21/archive.py --baseline-tree work/base50e3-tree.json
# 检查pending为空、失败正确保留、差异源数量合理后：
python3 experiments/navigation-continuity/evidence/observation21/archive.py --baseline-tree work/base50e3-tree.json --write
```

`manifest.json` 记录各run的exit、checkpoint、日志包、完整源码SHA256索引与差异包、route19严格判定、纯测日志与实际解析计数。`Complete`相关原生日志不被本脚本改写。21功能通过与无效成本采样分开：挖掘后区块无效，基准 known/LOS/read 为0，不可当真实扫描成本。22单人有效微基准与5人夹具失败分别保留；23只在完成checkpoint后纳入。`archiveComplete=false`和pending明确表示部分归档。

压缩包是确定性gzip(mtime=0)+base64分片，每片<=29000字节。主manifest引用各run及每bundle独立manifest，先验其SHA256；按bundle.parts顺序拼接并逐片验SHA256，再base64解码验gzipSHA256，gzip解压验rawSHA256，最后UTF-8 JSON解析。

源码按单文件内容寻址：sourceDelta中的inventoryBase解码得到完整路径SHA256基表，应用inventoryChanges并移除inventoryRemoved。changedOrNewFiles将路径指向source-file包（解压JSON字符串即原UTF-8文本），其余路径从指定Git基线恢复，最终逐文件核对重建inventory的SHA256。同一源码内容不同场次只存一次；不推断source集合外文件删除。原始日志也按文件内容去重，run.logs为原文件名→bundle引用。

`python3 experiments/navigation-continuity/evidence/observation21/verify.py` 可离线验证全部12场次、压缩包、源码差异和原source-hashes索引及秘密形态；不执行任何恢复代码。最终archiveComplete=true，pending=[]。无模型人工夹具，无API请求。

只归档明确白名单文本日志/启动脚本/源码/哈希。禁止游戏资源、依赖DLL、存档、环境变量、凭据；文本遇到key/Bearer/private-key形态即失败关闭，需人工检查而非静默上传。保留原始文本的完整哈希；解压后日志为可审计原始记录，不是成功摘要。工作目录绝对路径仅作为执行出处，不赋予复跑权限。
