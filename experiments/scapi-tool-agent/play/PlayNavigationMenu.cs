#nullable enable

using System.Text.Json;
using System.Text.Json.Nodes;
using Westworld.Decisions.Local;

internal static class PlayNavigationMenu
{
    public const string ScoutTool = "scout_local";

    public static ToolOffer ScoutOffer { get; } = MakeScoutOffer(["food", "survey"], 24);
    public static ToolOffer ShoreOffer { get; } = MakeScoutOffer(["dry_land"], 12);
    public static ToolOffer ShelterOffer { get; } = MakeScoutOffer(["food", "shelter", "survey"], 24);

    private static ToolOffer MakeScoutOffer(string[] purposes, int maxDistance) => new(ScoutTool,
        "Follow one local navigation goal. The game keeps moving while verifying the next route segment and jumping single-block steps. "
        + "Bearing is relative to current facing, positive right. Stops early at the chosen need, danger, injury, or a blocked route.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                bearing = new { type = "number", minimum = -180, maximum = 180 },
                distance = new { type = "number", minimum = 2, maximum = maxDistance },
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
