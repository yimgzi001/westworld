# 原生位置记忆短验收

独立dot云桌面实验，人工平地及单木地标、完整无视口生存角色。没有模型/API，没有背包注入。初始fixture设置位置，随后等待落稳并用原生FaceTowards朝向地标，真实射线获取观察。

最终map-memory06：7.54秒、33断言、退出0、游戏updateFailureCount=0、cleanupVerified=true。

验证：真实看见木地标及站立位置自动记忆；原生无参read_map仍数组；分页total/offset/hasMore/nextOffset及末页真实；越界拒绝。真实转开后历史时间保持，历史引用不能用于Dig/MeleeAttack/PickUp/FaceTowards。每个拒绝写入后都按既有ledger读取真实动作/库存并对账，不跳过保护。随后fixture把木地标换成花岗岩，旧实时引用也不能挖，方块仍花岗岩。记忆站立位置可通过原生MoveTo解析成功。

**范围限制：**最终场景只转开，没有长距离离开回访。总水平位移0.55米含出生落稳等过程，不当成导航行程成绩。map-memory04尝试原生旧MoveTo离开时blocked，故长期回访仍待连续导航整合验收。历史地标位置不是实时资源存在证明。

## 全部试验记录

- 01：日志直接序列化Engine.Vector3触发递归错误；改成数值数组。
- 02：出生更新覆盖初始朝向，首条射线未见木。
- 03：过早启动朝向仍未通过；追加落稳等待与动作结果检查。
- 04：真实观察及地图分页通过，旧MoveTo离开blocked，未通过整链。
- 05：转开与历史时间、Dig拒绝通过；下一写入因未对账被ledger拒绝，修正测试流程而非放松保护。
- 06：上述权限与站立点解析33项通过。

证据位于evidence/runs.json及六份完整JSONL gzip/base64文本，包含失败，不复制游戏资产、世界存档或二进制。每轮记录实际部署DLL哈希，未用最终源码hash倒填早期版本。解码可用Python标准库base64.b64decode和gzip.decompress。

构建MapMemoryGate.csproj后复制输出到新的work目录，在正常云桌面终端执行dotnet MapMemoryGate.dll。目录须不存在doc/Mods，单场墙钟上限60秒。通过本门不表示模型自主探索或制作已成功。
