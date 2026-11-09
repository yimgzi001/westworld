# food26/27证据归档（已归档）

此目录使用基线a943fb29完整Git提交加内容寻址差异源码，不重复打包整棵源码树。archive.py只读取已完成checkpoint的运行，默认dry-run，--write才写此目录；不执行游戏、模型或恢复代码。

待发布器提供准确基线完整SHA及tree：

```
python3 experiments/food-acquisition-evidence/evidence/food27/archive.py --baseline-tree work/basea943-tree.json --baseline-commit <完整40位SHA>
```

最终manifest已pending为空、archiveComplete=true，三场次都已收尾。保留food26序列化失败，food27确定性正控与Terra模型场次分别记录，不互相替代。

各run manifest引用原始安全日志、installed/source hashes、run脚本、独立食物validator结果和checkpoint。源包只存每个唯一差异文件一次；inventoryBase加inventoryChanges/inventoryRemoved重建该场次完整源码SHA256索引，changedOrNewFiles引用内容包，其余从准确Git基线恢复。不能以当前工作树重建旧失败的源码。

每bundle用确定性gzip+base64分片，每片最大29000字节。先验bundle manifest SHA256，再验每part SHA256、gzip SHA256、解压raw SHA256。verify.py离线验证全部包、源码差异及原source索引，不运行恢复代码。source原始字节以UTF-8原样保存。

API审计只在该场次checkpoint完成后读取，扫描凭据形态并提取身份、模型名、源码/提示词/Schema哈希、usage、时间、结果分类等白名单字段；不归档原始API响应、rawDecision、prompt或任何凭据来源。未知字段丢弃但原审计SHA256保留。游戏本体、DLL、资源包、存档、环境变量和Key均不归档。

纯测按来源分开：food26冻结聚合23套888项；后续真实Engine向量序列化19项；控制器43项；驱动20/部署8项；离线validator10个测试方法。后几组不能合成“最终代码全套重跑”的声明。构建manifest和日志保留各自源/DLL哈希。

模型场次host退出1而driver退出0，来源唯一性为false且没有Eat；归档并不将其改写为成功。新增postRunValidatorSources明确为运行后验证器修正，不能混同冻结游戏源码。
