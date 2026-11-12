#nullable enable

using System.Text.Json;
using Engine;
using Game.Westworld;
using Westworld.Decisions.Local;

internal static class PlaySurvivalGoals
{
    public const string Tool = "pursue_survival_goal";
    public const string Guide =
        "你是普通生存角色的决策者，根据观察和攻略决定做什么、做多少、顺序与换目标时机。goal 简短保留长期意图和本段目的；steps 从 goalOptions 选一至四个编号，按顺序连续执行。" +
        "readyNow=false 的制作可排在取得材料之后；前提失效会停止后续并反馈原因。编号绑定已观察目标，不假设远处资源、路线或工作站可用。游戏只执行所选动作与紧急保命。" +
        "当前目标默认继续。新攻击、着火、落水先避险，做完任务前可能饿死或冻伤则先补救；普通机会先记住。比较收益、耗时、天色、已有投入和返程成本，切换理由写进 goal 并保留已完成前置。" +
        "foodReserve 的饱食和可靠库存应覆盖 dailyTarget。烤肉主食，南瓜救急并留种；有生肉时比较建立炉具与继续追猎。生肉、幼苗、致病食物不能算可靠储粮，同类食物过量与腐败会降低可吃库存。" +
        "结合 resourcePlanning 的原木、木板、木棒和已见资源选备料量：collect_three_wood 连续取三块，stock_eight_wood 把原木库存补到八块。资源丰富且有余裕可批量备料；饥饿迫近、天色晚或资源稀疏时比较短采、就地加工与迁移。" +
        "木材可制木板和木棒，工作台制木矛；鸭、乌鸦、海鸥掉食用肉，麻雀鸽子不产肉。prey 绑定已见猎物；hunt_route 持续定向巡猎并取肉，旧鸟蛋不代表完成。击杀后先取回掉肉，有安全出口才涉水；危险可中断，没入包不算猎获。普通 route 只观察，不擅自狩猎。recover_spear 回收指定已见投矛。" +
        "collect_one_cobblestone 可先取一块圆石，配两根木棒在工作台制石斧，加快采集；八圆石在工作台制熔炉，生肉加非爆炸燃料烤熟后必须取回。已有材料跳过准备，留足燃料。" +
        "没有露天石料可选 quarry 浅探，最深六格，逐层观察、核对回跳后取样返地面，不保证有石。return_quarry 返回本人发现过石层的入口。是否取样、制斧、采八块与造炉由你权衡，禁止直挖脚下。" +
        "worksites 是本人旧工作站。制作会先接近四十格内已知工位；更远可明确选 return_to_own_workbench 或 return_to_own_furnace，返营返站支持一百二十八格，沿途重看地形。returnJourney 提供距离与估计时间，记住位置不代表抵达或可交互。" +
        "生肉优先烤制，饱食不超过4%、未患病且无安全食物时才作有风险应急；生蛋只是补充，煮蛋需水桶。成熟南瓜一只换三粒种子，先留口粮；温暖向阳、近水耕地适合种植，幼苗可能夭折，成熟前继续供食。" +
        "日落前留返程、烤制、封门时间。营地靠木石水源与安全出口，避开雪地、深水、悬崖及反复遇敌处。已有住处尽量复用炉具与储备；临时坑用于赶不上建屋或避险。门外有狼就评估出口，睡不着不能反复睡。" +
        "湿冷优先离水、遮雨、干燥与回暖，防寒不足不主动进雪地；声音仅提供危险的大概方向。营火避开身体与木墙，自身着火先离火；回暖掉血不算恢复。露天等待不产生屋顶，无进展就重新评估。" +
        "strategyNotes 是当前环境的攻略取舍，结合实际回执和收获。食物、保暖和住处稳定后再发展深矿，备工具、照明、返程口粮与退路。";
    internal enum Kind { Collect, Eat, Explore, Supply, Build, Rest, Leave, Shore, Wait, Evade, Resume }

    internal sealed record Choice(string Id, Kind Activity, object Details, PlaySurvivalFood.Food? Food = null,
        PlaySurvivalExploration.Course? Course = null, string? Supply = null, int Seconds = 0,
        PlaySurvivalProduction.CampSite? Camp = null, int? Bird = null, PlayEarthShelter.Site? Shelter = null,
        PlaySurvivalProduction.QuarrySite? Quarry = null, NearbyPickableView? Pickup = null)
    {
        public object Snapshot() => new { id = Id, activity = Activity.ToString().ToLowerInvariant(), details = Details };
    }

    public static ToolOffer Offer(IReadOnlyList<Choice> choices) => new(Tool,
        "Choose a goalOptions id for this step. Plan one to four consecutive steps. Native actions report actual results.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { target = new { type = "string" } },
            required = new[] { "target" },
            additionalProperties = false
        }), new Dictionary<string, IReadOnlyList<string>> { ["target"] = choices.Select(choice => choice.Id).ToArray() });
}
