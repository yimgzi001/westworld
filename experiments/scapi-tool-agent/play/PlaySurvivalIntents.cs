#nullable enable

using System.Text.Json;
using Westworld.Decisions.Local;

internal static class PlaySurvivalIntents
{
    public const string Gather = "gather_available_food";
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

    public static IReadOnlyList<ToolOffer> Offers { get; } =
    [
        new(Gather, "Approach the best nearby edible food seen in the current view or recent sweep, then collect it with native reach checks.", NoArguments),
        new(Eat, "At execution, choose a safe edible item from actual owned inventory and eat one.", NoArguments),
        new(Rest, "Sleep when the real shelter and fatigue permit it. If wet, dry under usable cover before attempting sleep.", NoArguments)
    ];

    public static ToolOffer BuildOffer { get; } = new(BuildShelter,
        "On dry soft earth, dig a temporary three-block-deep shelter and seal the top with owned dirt. Use rest_when_safe next if permitted.", NoArguments);
    public static ToolOffer LeaveOffer { get; } = new(LeaveShelter,
        "Open the temporary dirt roof and use native jumping and owned dirt to return to the surface.", NoArguments);
}
