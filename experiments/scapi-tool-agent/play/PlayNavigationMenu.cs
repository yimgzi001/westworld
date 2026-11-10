#nullable enable

using System.Text.Json;
using System.Text.Json.Nodes;
using Westworld.Decisions.Local;

internal static class PlayNavigationMenu
{
    public const string ScoutTool = "scout_local";

    public static ToolOffer ScoutOffer { get; } = MakeScoutOffer(["dry_land", "food", "shelter", "survey"]);
    public static ToolOffer ShoreOffer { get; } = MakeScoutOffer(["dry_land"]);
    public static ToolOffer ShelterOffer { get; } = MakeScoutOffer(["shelter", "survey"]);

    private static ToolOffer MakeScoutOffer(string[] purposes) => new(ScoutTool,
        "Explore with real turning, steering, dismounting near observed shore, and short movements. "
        + "Bearing is relative to current facing, positive right. Stops early at the chosen need, injury, or a blocked route.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                bearing = new { type = "number", minimum = -180, maximum = 180 },
                distance = new { type = "number", minimum = 2, maximum = 12 },
                seek = new { type = "string", @enum = purposes }
            },
            required = new[] { "bearing", "distance", "seek" },
            additionalProperties = false
        }));

    public static ToolOffer LimitRelativeMove(ToolOffer offer)
    {
        if (offer.Name != "act_MoveTo") return offer;

        // 相对探索只走前方的短段。侧后方先转身获取观察，再用新菜单决定。
        var parameters = JsonNode.Parse(offer.Parameters.GetRawText())!.AsObject();
        var fields = parameters["properties"]!.AsObject();
        fields["bearing"]!["minimum"] = -60;
        fields["bearing"]!["maximum"] = 60;
        fields["distance"]!["maximum"] = 3;
        return offer with
        {
            Description = offer.Description + " Relative exploration is a short forward step. Turn to observe before going sideways or back.",
            Parameters = JsonSerializer.SerializeToElement(parameters)
        };
    }
}
