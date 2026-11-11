#nullable enable

using System.Text.Json;
using Engine;
using Westworld.Decisions.Local;

internal static class PlaySurvivalGoals
{
    public const string Tool = "pursue_survival_goal";
    public const string Guide =
        "你是普通生存角色的决策者。根据观察和攻略制定自己的计划，决定做什么、做多少、先后顺序和换目标的时机。" +
        "在 goal 写当前长期意图和这段计划的目的；steps 从 goalOptions 选择一至四个编号，按你排的顺序连续执行。可重复选采集动作。" +
        "readyNow=false 的制作可以排在取得材料之后；材料不足、猎物消失或路线受阻会停止后续步骤，把实际原因交回你。不要无证据安排远处目标。" +
        "游戏只执行你安排的动作、核对结果和紧急保命，不替你制定采集、建家或发展路线。" +
        "每天先检查 foodReserve：胃里饱食加可靠库存应覆盖 dailyTarget；不能等饿到见底才开始找食。烤肉是主食，南瓜用来救急和留种续种。" +
        "白天先取附近可靠食物；没有现成食物时尽早准备木矛，猎取真正掉肉的鸭、乌鸦、海鸥。麻雀和鸽子不产肉，不追逐。优先已见、够得着的猎物，远程投矛可减少惊飞。" +
        "prey 编号绑定已经看到的某只猎物；hunt_route 是你可选的定向巡猎动作，会持续搜索并在食用鸟进入投矛范围后攻击、拾肉。选择普通 route 只观察，不会自动改为狩猎。" +
        "烤肉的前置关系是木材→木板/木棒→工作台和木矛→狩猎得生肉；八块圆石加工作台做熔炉，再放生肉和燃料烤熟取出。你自行安排先后，已有材料就跳过准备。" +
        "平地裸露的岩石也能从侧边采，禁止挖本人脚下。先取一块圆石，用两根木棒在工作台做石斧，能加快后续批量采石和砍树；是否先做由你比较饱食、余粮和天色。记得留燃料。" +
        "原料不足时沿一条新路线寻找，记住已经确认的营地和资源，日落前回安全掩体。生肉留给熔炉；生蛋只是紧急补充，煮蛋需要水桶，前期不为它专门发展金属。" +
        "低温先干燥回暖，没有热源时继续找食和走向已见的温暖干地，别反复等待。猎食者声音只表示大概方向，结合可见危险和已排除路线选择。" +
        "储粮按能吃下的营养判断：生肉、未成熟作物和高致病食物不能算可靠口粮；同类食物吃太多会生病，留意实际可吃种类。一次少做够近期消耗的熟食，避免腐败浪费。" +
        "做事前比较收益和耗时：拿附近成熟南瓜通常比临饿造炉更快；有生肉时炉火优先于新一轮追猎。collect_three_wood、collect_eight_cobblestone 是你可选的连续采集动作，并非必须依次执行。" +
        "南瓜一只可换三粒种子，先留救命口粮再留种。在营地附近向阳、温暖且安全的土面种植，自带木耙可先耕土；近水耕地更适合长期种植。幼苗可能夭折，低温生长慢，成熟前继续靠狩猎供食。" +
        "营地宜靠近木材、石头和可利用水源，并留安全出口；远离雪地、悬崖、深水和反复发现的猎食者区域。临时坑只用于赶不上建屋的夜晚或紧急避险。" +
        "一旦有可靠住处，应把熔炉、燃料和应急食物集中使用，白天外出、天黑前回营；门外有狼时检查别的出口与可见撤离路线，睡不着不能反复下睡眠指令。" +
        "临时庇护可提前选择已见的 cover 地点；水边和窄岸不适合挖坑。露天等待不会变出屋顶，几次等待还没改善就选有证据的新方案。已经在夜间选择的路线会继续执行。" +
        "worksites 是自己曾放置的工作站。需要原工作台或熔炉时，你可以先选择 return_to_own_workbench 或 return_to_own_furnace，再制作或烤肉；只记得位置不代表已经在交互范围内。" +
        "食物、体温和过夜都稳定后再考虑挖矿；先有石制工具、照明、返程口粮和已知安全退路。开局的裸露石料比盲目下洞代价低，不直挖脚下，也不为远期材料放弃当天口粮。" +
        "strategyNotes 补充当前情境的攻略取舍。结合上次结束原因和实际收获；自身着火先离开火源，回暖掉血不算恢复。受阻就找有新证据的路线或换任务。goal 简短写这次要达到的结果。";

    internal enum Kind { Collect, Eat, Explore, Supply, Build, Rest, Leave, Shore, Wait, Evade, Resume }

    internal sealed record Choice(string Id, Kind Activity, object Details, PlaySurvivalFood.Food? Food = null,
        PlaySurvivalExploration.Course? Course = null, string? Supply = null, int Seconds = 0,
        PlaySurvivalProduction.CampSite? Camp = null, int? Bird = null, PlayEarthShelter.Site? Shelter = null)
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
