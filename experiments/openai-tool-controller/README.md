# 官方 OpenAI 工具决策文件驱动

标准库独立传输，不执行游戏动作。官方 GPT-6.1 Terra/none 已完成一次历史回放与12轮真实文件控制，结果在 evidence/；随后新增先等待host和源码hash审计，仅24项纯测试，尚未重新原生复验。实际发送游戏观察/背包/工具说明/目标需已有明确用户授权，不能仅因具有 API key 就传输。

固定 HTTPS api.openai.com/v1/responses，无自定义域名或重定向；使用标准urllib支持的环境HTTPS路径和代理处理，不读取、打印或改写代理配置，默认验证TLS证书。模型与reasoning effort均必须显式传入，没有默认或fallback。当前白名单仅允许用户指定、官方文档核实的gpt-6.1-terra/none组合，不允许Luna回退。请求store=false、max_output_tokens=512，原prompt/schema不改。text.format 为 json_schema、strict=false；这是发送配置，不保证该模型/账号接受此 schema 子集。若官方拒绝直接失败，不换模型、放宽schema或改接口重试。官方接口参考 https://platform.openai.com/docs/api-reference/responses/create 。

密钥仅通过 getpass 隐藏终端读取并保留内存；无TTY时禁止退化为明文输入。不要把key放命令参数、环境变量、文件或shell历史。本驱动不读取环境中的key，也不打印HTTP错误正文、请求头或异常traceback。

replay 模式（一次请求，输出文件必须不存在）：

    python3 controller.py replay --prompt ../local-context-replay/evidence/prompt.txt --schema ../local-context-replay/evidence/response-schema.json --output /fresh/work/replay-result.json --model gpt-6.1-terra --reasoning-effort none

file 模式：

    python3 controller.py file --control /fresh/work/tool-agent-terra07-craft/control --audit /fresh/work/tool-agent-terra07-craft/openai-audit.jsonl --model gpt-6.1-terra --reasoning-effort none

只读 control/*/request.json 的公开请求，不读游戏审计来替模型决策。返回相同sessionId/actorId/observationId/requestId与decision，response.tmp写完原子发布response.json。每requestId最多一次，失败不重试；最多12次API、总650秒，每次至多45秒请求截止预算。control父目录exit.txt仅作为可信host生命周期信号，不送模型；host结束不再发布晚回复。

只接受status=completed中assistant output_text。拒绝/refusal/incomplete、JSON结构错误或请求失败不发布response，但保留实际usage与安全错误分类。模型schema约束不替代游戏宿主的严格ToolDecisionPrompt解析、引用重验证与动作前提检查。本驱动不执行生成代码。审计记录模型、耗时、实际usage、提示/schema哈希及原始决策JSON，不记录key。

单测（无真实网络/无需key）：

    python3 -B -m unittest discover -s experiments/openai-tool-controller -p 'test_*.py' -v

测试使用假的传输和假的HTTP连接覆盖固定目的地、拒绝重定向与错误正文、预算、信封ID、usage、超时、不完整/拒绝/非assistant、原子发布、无重试、host退出及getpass拒绝明文回退。

用户已明确允许游戏观察/背包/目标/工具说明规则发往官方OpenAI，但不涵盖全仓库或私人数据。当前要求GPT-6.1 Terra；协调者已核实官方模型页 https://developers.openai.com/api/docs/models/gpt-6.1-terra 支持API ID gpt-6.1-terra、none、Responses与Structured Outputs。实际调用结果独立归档，试验后的流程改进仅纯测；绝不以Luna回退。

## 先准备驱动，再启动游戏

先创建本轮独立工作父目录（由协调者完成），不要创建control目录。启动file驱动并在TTY隐藏输入key；看到“Controller ready; waiting for host control directory”后才启动游戏host。驱动等待host创建control，最多60秒，等待时间计入总650秒；超时明确退出，不自动创建目录、不重试API。父目录exit.txt存在则结束等待；控制目录及祖先symlink会被拒绝，host自身仍负责fresh世界/目录约束。已存在的response不会覆盖，audit仍要求新文件。

单测会使用TemporaryDirectory和原子文件发布，因此需要系统允许的可写临时目录。只读执行环境出现临时文件权限错误不是产品回归失败证据；应按支持的权限流程授权后重跑，或使用已获准可写的TMPDIR。不要通过绕过环境限制执行。当前纯测试23项通过，无真实API/key。

真实API性能与目标进展由单独实测报告记录。本驱动修改只消除启动准备空窗，不代表解决模型制作策略或游戏技能问题。

## 驱动版本可复现性

导入模块时读取一次自身源码SHA256并固定为DRIVER_SOURCE_SHA256，每次infer审计包含driverSourceSha256及maxOutputTokens=512。运行期间即使磁盘文件变化，也不把新文件hash当成已载入代码版本。该字段从本次修改后开始记录，不倒填旧Terra07日志；旧运行没有记录驱动源码hash，不能以当前hash证明旧运行版本。先就绪等待与此版本字段目前仅纯测试，尚未原生复验。

## 每请求独立进程截止预算（本批仅离线验证）

原 urllib socket timeout 不是总墙钟截止。现在默认 infer 为每次 HTTP 启动固定的本文件 Python `-I …/controller.py --http-worker`，不使用 shell、不执行模型生成代码。仅通过匿名 stdin pipe 传 payload/key/timeout，不把 key 放 argv、环境或文件。子进程只可向固定官方 HTTPS 地址发 POST，保持 TLS 验证、拒重定向及响应 262144 字节上限；普通环境 HTTPS 路由保持原样。

父进程用 monotonic 截止与 communicate 的剩余预算（最多 45 秒）；超时杀死并回收该请求进程，不重试。完成 kill/wait 与管道清理仍需要操作系统调度，因此 45 秒是请求执行截止，不声称整个清理函数在任何 OS 故障下严格 45 秒返回。worker 不启动后代进程。stdout 以独立线程有界读取，最多保留 262144 字节，超限杀死；stderr 丢弃，错误仅允许固定安全分类。输入 envelope 也受 262144 字节上限约束。子进程 API 输出若包含本次完整 key，会拒绝并且不保存该输出。

超时、非零 worker 退出、响应超限、协议失败没有可靠 usage 时记录 null，绝不记作 0 或估算消耗；服务器可能已经接收/计费，进程被杀不代表服务端取消。file 模式仍将失败 requestId 记为已尝试，禁止自动重放。

新增离线测试覆盖 timeout→kill→回收、固定 argv/匿名 stdin、超大 stdout、worker 非零退出、错误/结果 credential 回显过滤，以及真实固定 worker 接收非法 envelope 后无 HTTP 退出。当前总 31 项通过；无真实 key、无真实 API。本批没有原生游戏或付费请求验证。直接 `http_post` 仅供固定 worker 和离线 HTTP mock 测试使用，正常 infer 默认必须走 isolated_http_post。


## Restart protection

File mode reserves each canonical request UUID in `.request-claims/` before any provider call. The exclusive, fsynced marker contains only identities, prompt/schema hashes, model/effort and `reserved_or_submitted_unknown`, never key or prompt text. Existing or partially written claims block automatic retries, even after driver restart. Outcomes remain in the audit and response files; a claim does not prove a request reached the provider. Reconcile an interrupted run manually; do not delete claims and blindly replay. Keep the control directory and claims together. This protects this local run, not copies across independent machines or provider-side transactions. Replay CLI remains an explicit new one-off request, not automatic recovery.


## Host deadline and cancellation

New file requests may include `deadlineUtc`; the transport caps its remaining request timeout against that advisory deadline and checks `canceled.json` before the call and before publishing. Expired/canceled requests do not start a new HTTP call. An already submitted server request may still incur usage even if its result is withheld; known usage remains in the audit. Game-side monotonic deadline and identity validation remain authoritative. UTC clock changes and the check/rename race mean this is not a distributed cancellation transaction. Legacy requests retain the existing total/HTTP bounds.

For small measured batches, file CLI exposes existing bounded parameters `--max-requests 4 --total-seconds 180 --wait-seconds 60`; upper limits remain 12/650/60. Limits include host startup wait and do not grant permission to retry a claimed request. Request count is a cap, not a target.
