using System.Diagnostics;
using System.Text.Json;
using Westworld.Decisions.Local;

var session = Guid.NewGuid();
var observation = Guid.NewGuid();
var a = Guid.NewGuid();
var b = Guid.NewGuid();
var c = Guid.NewGuid();
var longWindow = TimeSpan.FromSeconds(5);
var shortWindow = TimeSpan.FromMilliseconds(60);
int checks = 0;

ToolDecisionPlan Plan(Guid actor) => new(session, actor, observation, "测试目标", []);
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
}

async Task<PlayDecisionResult> Take(PlayDecisionQueue queue, Guid actor)
{
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed < longWindow)
    {
        if (queue.Take(actor) is { } reply) return reply;
        await Task.Delay(5);
    }
    throw new TimeoutException("测试未收到完成信号。");
}

var slow = new TaskCompletionSource<ToolDecisionPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
var shared = new PlayDecisionQueue(2, 3);
Check(shared.TryStart(session, a, observation, longWindow, _ => slow.Task), "启动 A");
Check(!shared.TryStart(session, a, observation, longWindow, _ => Task.FromResult(Plan(a))), "同角色请求不能重叠");
Check(shared.TryStart(session, b, observation, longWindow, _ => Task.FromResult(Plan(b))), "启动 B");
Check(!shared.TryStart(session, c, observation, longWindow, _ => Task.FromResult(Plan(c))), "全局并发上限");
Check((await Take(shared, b)).Plan?.ActorId == b && shared.IsPending(a), "A 等待时 B 独立完成");
Check(shared.TryStart(session, c, observation, longWindow, _ => Task.FromResult(Plan(b))), "B 释放后可启动 C");
Check((await Take(shared, c)).Error == "model_scope_mismatch", "拒绝错角色回复");
shared.Cancel(a);
slow.SetResult(Plan(a));
Check((await Take(shared, a)).Error == "model_canceled", "取消后迟到结果作废");
Check(shared.Issued == 3 && !shared.HasSlot && shared.Stop(), "共享总预算");

var survival = new PlayDecisionQueue(1, null);
for (int n = 0; n < 15; n++)
{
    if (!survival.TryStart(session, a, observation, longWindow, _ => Task.FromResult(Plan(a)))
        || (await Take(survival, a)).Plan?.ActorId != a)
        throw new InvalidOperationException("不限总量的生存队列应继续调度。");
}
Check(survival.Issued == 15 && !survival.BudgetReached && survival.HasSlot && survival.Stop(), "生存队列跨过试玩预算");

var expires = new PlayDecisionQueue(1, 3);
Check(expires.TryStart(session, a, observation, shortWindow, async token =>
{
    await Task.Delay(Timeout.Infinite, token);
    return Plan(a);
}), "启动会超时的请求");
Check((await Take(expires, a)).Error == "model_timeout", "超时释放名额");

var late = new TaskCompletionSource<ToolDecisionPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
Check(expires.TryStart(session, a, observation, shortWindow, _ => late.Task), "启动不及时响应取消的请求");
await Task.Delay(120);
Check(!expires.HasSlot, "未排空的请求继续占用并发名额");
late.SetResult(Plan(a));
Check((await Take(expires, a)).Error == "model_timeout", "超时后成功返回也不能执行");
Check(expires.TryStart(session, b, observation, longWindow, async token =>
{
    await Task.Delay(Timeout.Infinite, token);
    return Plan(b);
}), "启动退出排空检查");
Check(expires.Stop() && !expires.IsPending(b) && !expires.HasSlot, "退出取消在途请求并排空");

var baseMove = new ToolOffer("act_MoveTo", "短程移动", JsonSerializer.SerializeToElement(new
{
    type = "object",
    properties = new
    {
        bearing = new { type = "number", minimum = -180, maximum = 180 },
        distance = new { type = "number", minimum = 1, maximum = 4 },
        destination = new { type = "string" }
    },
    required = Array.Empty<string>(),
    additionalProperties = false
}), new Dictionary<string, IReadOnlyList<string>> { ["destination"] = new[] { "observed" } },
    ParameterBranches: [
        new(new[] { "bearing", "distance" }, new[] { "destination" }),
        new(new[] { "destination" }, new[] { "bearing", "distance" })
    ]);
var move = PlayNavigationMenu.LimitRelativeMove(baseMove);
var movementPrompt = new ToolDecisionPrompt(session, a, observation, JsonSerializer.SerializeToElement(new { }), [move]);
bool CanPlan(ToolDecisionPrompt prompt, string tool, object arguments)
{
    try
    {
        prompt.ParseResponse(JsonSerializer.Serialize(new
        {
            goal = "验证移动菜单",
            steps = new[] { new { tool, arguments } }
        }));
        return true;
    }
    catch (FormatException) { return false; }
}
bool CanMove(object arguments) => CanPlan(movementPrompt, move.Name, arguments);
Check(CanMove(new { bearing = -60, distance = 3 }), "允许前向短步边界");
Check(CanMove(new { bearing = 60, distance = 2 }), "允许另一侧前向短步边界");
Check(!CanMove(new { bearing = 90, distance = 3 }), "侧方先转身");
Check(!CanMove(new { bearing = 180, distance = 3 }), "禁止向背后盲走");
Check(!CanMove(new { bearing = 0, distance = 4 }), "相对探索最多三格");
Check(CanMove(new { destination = "observed" }), "保留观察过的目标");
Check(!CanMove(new { destination = "unknown" }), "未观察目标仍拒绝");
Check(baseMove.Parameters.GetProperty("properties").GetProperty("bearing").GetProperty("maximum").GetInt32() == 180,
    "试玩约束不修改原生菜单");

var scout = PlayNavigationMenu.ScoutOffer;
var scoutPrompt = new ToolDecisionPrompt(session, a, observation, JsonSerializer.SerializeToElement(new { }), [scout]);
var shorePrompt = new ToolDecisionPrompt(session, a, observation, JsonSerializer.SerializeToElement(new { }), [PlayNavigationMenu.ShoreOffer]);
var shelterPrompt = new ToolDecisionPrompt(session, a, observation, JsonSerializer.SerializeToElement(new { }), [PlayNavigationMenu.ShelterOffer]);
Check(CanPlan(shorePrompt, scout.Name, new { bearing = 180, distance = 12, seek = "dry_land" }), "水上允许用身后的岸为目标");
Check(!CanPlan(shorePrompt, scout.Name, new { bearing = 180, distance = 13, seek = "dry_land" }), "限制一次水上导航距离");
Check(CanPlan(scoutPrompt, scout.Name, new { bearing = 0, distance = 40, seek = "food" }), "旧导航协议允许四十格边界");
Check(!CanPlan(scoutPrompt, scout.Name, new { bearing = 0, distance = 41, seek = "food" }), "旧导航协议继续拒绝越界");
Check(!CanPlan(scoutPrompt, scout.Name, new { bearing = 0, distance = 5, seek = "dry_land" }), "陆地菜单不重复找岸");
Check(!CanPlan(scoutPrompt, scout.Name, new { bearing = 0, distance = 5, seek = "shelter" }), "白天普通探索不重复原地寻找庇护");
Check(CanPlan(shelterPrompt, scout.Name, new { bearing = 0, distance = 12, seek = "shelter" }), "需要寻找庇护时保留导航选项");
Check(!CanPlan(scoutPrompt, scout.Name, new { bearing = 0, distance = 5, seek = "attack" }), "仅允许已有探索目的");
Check(!CanPlan(shelterPrompt, scout.Name, new { bearing = 0, distance = 5, seek = "shelter", x = 100 }), "拒绝未观察坐标");

var inbox = new PlaySocialInbox();
JsonElement Offer(string reference, double gameTime) =>
    JsonSerializer.SerializeToElement(new { kind = "itemOffered", gameTime, offerReference = reference });
inbox.Add(Offer("first", 1));
for (int i = 0; i < 12; i++)
    inbox.Add(JsonSerializer.SerializeToElement(new { kind = "heardSpeech", gameTime = 3, text = $"消息 {i}" }));
var beforeExpiry = inbox.Snapshot(15.99);
Check(beforeExpiry.GetArrayLength() == 8 && beforeExpiry.EnumerateArray().Any(message =>
    message.TryGetProperty("offerReference", out var reference) && reference.GetString() == "first"),
    "聊天刷屏不会挤出仍有效的交接");
Check(!inbox.Snapshot(16).EnumerateArray().Any(message => message.TryGetProperty("offerReference", out _)),
    "交接到达十五秒时移除");
inbox.Add(Offer("accepted", 17));
inbox.Add(Offer("still_pending", 17));
inbox.RetireOffer("accepted");
Check(inbox.Snapshot(18).EnumerateArray()
    .Where(message => message.TryGetProperty("offerReference", out _))
    .Select(message => message.GetProperty("offerReference").GetString()).SequenceEqual(new[] { "still_pending" }),
    "回应后只移除这一条交接");

var goalOffer = new ToolOffer("pursue_survival_goal", "选择一件已经观察过的工作",
    JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { target = new { type = "string" } },
        required = new[] { "target" }, additionalProperties = false
    }), new Dictionary<string, IReadOnlyList<string>> { ["target"] = new[] { "food_1", "route_1" } });
const string goalGuide = "从这次快照的目标编号里选一件事，执行端继续行动。";
var goalPrompt = new ToolDecisionPrompt(session, a, observation, JsonSerializer.SerializeToElement(new { }),
    [goalOffer], maxSteps: 1, minSteps: 1, decisionGuide: goalGuide);
Check(CanPlan(goalPrompt, goalOffer.Name, new { target = "food_1" }), "单目标允许已绑定的食物");
Check(CanPlan(goalPrompt, goalOffer.Name, new { target = "route_1" }), "单目标允许已验证的路线");
Check(!CanPlan(goalPrompt, goalOffer.Name, new { target = "food_2" }), "拒绝未绑定的目标编号");
Check(!CanPlan(goalPrompt, goalOffer.Name, new { target = "food_1", bearing = 180 }), "单目标拒绝夹带角度");
Check(goalPrompt.Prompt.Contains(goalGuide) && !goalPrompt.Prompt.Contains("MoveTo:"), "生存提示不混入底层移动协议");
Check(RejectShape("""{"goal":"原地等","steps":[]}"""), "单目标拒绝无条件空等");
Check(RejectShape("""{"goal":"预测未来","steps":[{"tool":"pursue_survival_goal","arguments":{"target":"food_1"}},{"tool":"pursue_survival_goal","arguments":{"target":"route_1"}}]}"""),
    "单目标拒绝提前排未知结果的下一步");
bool RejectShape(string raw)
{
    try { goalPrompt.ParseResponse(raw); return false; }
    catch (FormatException) { return true; }
}
bool InvalidSetup(int minimum, IReadOnlyList<ToolOffer> offered)
{
    try { new ToolDecisionPrompt(session, a, observation, JsonSerializer.SerializeToElement(new { }), offered, maxSteps: 1, minSteps: minimum); return false; }
    catch (ArgumentException) { return true; }
}
Check(InvalidSetup(2, [goalOffer]), "拒绝超过最大步数的下界");
Check(InvalidSetup(-1, [goalOffer]), "拒绝负的步数下界");
Check(InvalidSetup(1, []), "空菜单不能要求一个不存在的动作");
var sequencePrompt = new ToolDecisionPrompt(session, a, observation, JsonSerializer.SerializeToElement(new { }),
    [goalOffer], maxSteps: 4, minSteps: 1, decisionGuide: goalGuide);
bool SequenceAccepted(params string[] targets)
{
    try
    {
        sequencePrompt.ParseResponse(JsonSerializer.Serialize(new { goal = "按模型安排连续执行",
            steps = targets.Select(target => new { tool = goalOffer.Name, arguments = new { target } }).ToArray() }));
        return true;
    }
    catch (FormatException) { return false; }
}
Check(SequenceAccepted("food_1", "route_1"), "多步计划接纳两个已绑定的目标");
Check(!SequenceAccepted("food_1", "unknown"), "后续步骤也必须使用本人已绑定的编号");
Check(!SequenceAccepted("route_1", "route_1", "route_1", "route_1", "route_1"), "拒绝超过四步的计划");
Check(!SequenceAccepted(), "有目标菜单时空计划不能代替明确选择");
Console.WriteLine($"试玩决策、单目标、移动和收件箱 {checks} 项通过。");
