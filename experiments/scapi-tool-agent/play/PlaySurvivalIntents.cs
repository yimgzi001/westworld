#nullable enable

using System.Text.Json;
using Westworld.Decisions.Local;

internal static class PlaySurvivalIntents
{
    public const string Gather = "gather_available_food";
    public const string CollectTarget = "collect_observed_food";
    public const string Eat = "eat_owned_food";
    public const string Rest = "rest_when_safe";
    public const string BuildShelter = "build_earth_shelter";
    public const string LeaveShelter = "leave_earth_shelter";

    private static readonly JsonElement NoArguments = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { },
        required = Array.Empty<string>(),
        additionalProperties = false
    });

    public static ToolOffer GatherOffer { get; } = new(Gather,
        "Approach nearby observed food and collect it using native reach. Fresh safe food has priority. Uncooked food can be saved.", NoArguments);
    public static ToolOffer CollectTargetOffer(PlaySurvivalFood.Food[] targets) => new(CollectTarget,
        "Commit to one ID from foodTargets. Follow safe ground to the same target and collect it, including after a short detour. Use this for visible food.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { target = new { type = "string" } },
            required = new[] { "target" },
            additionalProperties = false
        }), new Dictionary<string, IReadOnlyList<string>> { ["target"] = targets.Select(food => food.Reference).ToArray() });
    public static ToolOffer EatOffer { get; } = new(Eat,
        "Eat one owned item after checking its native sickness risk and how recently the actor ate that food type.", NoArguments);
    public static ToolOffer RestOffer { get; } = new(Rest,
        "Sleep under usable cover at night or while ill and injured. Dry under cover before attempting sleep if necessary.", NoArguments);
    public static ToolOffer RestAfterBuildOffer { get; } = RestOffer with
    {
        Description = "Use only after build_earth_shelter in the same plan. Sleep or dry under the completed shelter."
    };

    public static ToolOffer BuildOffer { get; } = new(BuildShelter,
        "On dry soft earth, dig a temporary three-block-deep shelter and seal the top with owned dirt. Use rest_when_safe next if permitted.", NoArguments);
    public static ToolOffer LeaveOffer { get; } = new(LeaveShelter,
        "Open the temporary dirt roof and use native jumping and owned dirt to return to the surface.", NoArguments);
}
