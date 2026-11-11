#nullable enable

using System.Text.Json;
using Engine;
using Westworld.Decisions.Local;

internal static class PlaySurvivalGoals
{
    public const string Tool = "pursue_survival_goal";
    public const string Guide =
        "你为一个普通生存角色选择接下来要完成的一件事。从 goalOptions 选一个编号。游戏会连续走路、跳台阶、采集或制作，并核对实际结果。" +
        "优先考虑安全、当天的食物和入夜前的庇护。缺粮时优先取能到达且风险低的已见食物。没有食物目标时沿一条新路线寻找，或在条件合适时做熟食。" +
        "背包缺乏可靠食物且看见可猎食用鸟时，优先考虑备熟食。天色充裕而缺工具时可先备木矛。生产目标里的 nextStage 是已核对的下一步。" +
        "生肉留给熔炉。生蛋可以先收好，低饱食时才吃。低温先干燥回暖。猎食者声音只表示大概方向，结合可见危险和已经排除的路线选择。" +
        "结合上次的结束原因和实际收获。受阻就找有新证据的路线或换任务。goal 简短写这次要达到的结果。";

    internal enum Kind { Collect, Eat, Explore, Supply, Build, Rest, Leave, Shore, Wait, Evade }

    internal sealed record Choice(string Id, Kind Activity, object Details, PlaySurvivalFood.Food? Food = null,
        PlaySurvivalExploration.Course? Course = null, string? Supply = null, int Seconds = 0)
    {
        public object Snapshot() => new { id = Id, activity = Activity.ToString().ToLowerInvariant(), details = Details };
    }

    public static ToolOffer Offer(IReadOnlyList<Choice> choices) => new(Tool,
        "Choose one goalOptions id. The actor pursues it through native actions and reports what actually happened.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { target = new { type = "string" } },
            required = new[] { "target" },
            additionalProperties = false
        }), new Dictionary<string, IReadOnlyList<string>> { ["target"] = choices.Select(choice => choice.Id).ToArray() });
}
