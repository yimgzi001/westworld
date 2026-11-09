# main 动作与完整生存角色整合验收

只在隔离云端 `work/` 子目录运行，禁止指向用户存档。

```sh
dotnet build experiments/scapi-main-integration/IntegrationGate.csproj -c Release -m:1 -o work/integration
cd work/integration
dotnet IntegrationGate.dll fixture
dotnet IntegrationGate.dll reload
```

需要正常桌面/OpenGL 和本地模型 `work/local-model/Qwen3.5-4B-Q4_K_M.gguf`、`work/local-model/llama-b12211/llama-completion`，版本哈希见已有本地模型清单。模型单次请求，不执行模型生成代码。

`natural` 必须使用另一个干净输出目录，不含 doc/Mods/旧世界；不布置食物但清空起始物品，有限探索耗尽以非零退出，不能当成功。fixture 可重复验证时也应新建输出目录，不能覆盖旧世界。

主线程检查点在完整更新后捕获 waiting 边界；多子步可能错过边界，不能强存执行中动作。reload 在第一次世界更新前恢复，验证同任务、库存只减1、生理增益与禁止同Project重复恢复。

详细架构、来源、局限和最终结果见 `docs/development/main能力整合验收-2026-11-09.md`。
