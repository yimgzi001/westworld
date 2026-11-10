# 阶段30：模型候选中断与确定性食物回归

本包仅封存已结束的food30-terra与food30-deterministic。前者是基础设施中断、模型结果未知，后者是人工食物来源的确定性原生闭环通过。两者不是同一构建，不能混成模型正反对照，也不能宣称自然觅食或长期自主生存。

## food30-terra：未知，不是模型失败

使用阶段29冻结build04，120份源码。驱动会话已丢失，宿主在180.0440092秒以wall_budget_exhausted退出，exit1、updateFailures=0。4次请求发布，0次响应、0次动作启动，路径0米。原始openai-audit.jsonl为0字节。

没有API回执，因此API是否执行、模型实际输出、token消耗和计费均未知。空审计文件不证明零API调用或零账单；宿主日志中的model_failure不能据此归因模型能力。analysis保留infrastructure_interrupted_model_outcome_unknown和model_quality_verdict=not evaluated。没有重启或重放。

food-driver-started、food-driver-result、driver-exit及api-driver-exit均不存在，归档明确列出缺失，不补造退出码或成功结果。食物链acquisition/eat均为null；目标来源后来发生value变化，账本unknown也原样保留，不作为模型失败证据。

## food30-deterministic：原生闭环通过

使用新build05，121份源码。宿主8.090572秒通过，exit0、updateFailures=0，离线analysis验证pass。

人工放置的目标新食物value=89，与出生鱼value=162不同；没有库存注入。原生自动拾取目标来源，世界数量1→0、本角色库存0→1；之后真实食用，库存1→0、FoodEaten统计0→1、food约0.5487882→1，来源账本守恒。收尾idle且lateDispatches=0。

这是原生自动拾取，不是模型主动PickUp。确定性驱动收到并回复3次请求，结果标注deterministic_fixture_not_model；独立driver-exit.txt没有保存，归档不推测其退出码。宿主prefetchTaken=1，路径约2.697米；确定性预取不能当成真实模型推理重叠。

build05宿主DLL SHA256：7ea3a2489f08b43b91f331faef6e109900e4d7f5c8f645260e57fc95725c7fbe。build日志0错误、29警告。changed-sources中5份路径与本场冻结源码匹配，其余未入stage的测试/脚本/文档路径只保留原哈希清单，不从活动工作树补录。构建对应关系不等于完整游戏重建证明。

## 范围、隐私与复核

只读每场sources、source-hashes、安装哈希、checkpoint和已完成日志，以及work/direct30-model-tests、work/direct30-model-build-logs。未归档当前正在修改的驱动审计代码。

保留28套1094项纯测试、13项stage测试记录；10项endpoint检查已经包含在1094中，不重复累加。这些是既有日志，本次归档不重跑；纯测试不能替代真实模型或自然生存验证。

逐内容SHA256寻址、确定性gzip、Base64分片每片最多29000字节。源码按冻结字节保留；日志和配置将云仓库绝对路径替换为<CLOUD_REPO>，分别记录原始与归档SHA256。只收录空API审计的空文本及其哈希；若该文件变为非空，归档脚本立即拒绝，不能自动纳入原始API账本。

不包含密钥、环境变量、私人文件、游戏二进制、内容资产、世界存档或第三方依赖。凭据特征扫描无命中不等于对所有潜在秘密的绝对保证。

- `python3 archive.py`：只读检查输入和生成计划。
- `python3 archive.py --write`：仅重建本目录。
- `python3 verify.py`：离线解码、哈希核对、来源守恒和未知边界检查，不执行归档源码，不启动游戏/API或网络。

验证器同时检查每个压缩包被实际证据传递引用，不以注册表存在代替引用完整性。归档完成不等于模型测试通过。
