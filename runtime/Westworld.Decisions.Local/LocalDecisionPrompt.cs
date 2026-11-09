using System.Text.Json;
using Westworld.Runtime.Core;
namespace Westworld.Decisions.Local;

// Capability filter is supplied by the trusted host, never by model output.
// This mapper contains no native game references, process execution or network calls.
public sealed class LocalDecisionPrompt {
    sealed record Candidate(string Skill, int Slot, int Value, CellTarget? Target, float Nutrition, double Distance);
    readonly Guid session, actor, observation, decision = Guid.NewGuid();
    readonly Candidate[] candidates;
    readonly object gate = new();
    HighLevelDecision? accepted;
    public string Prompt { get; }
    public int CandidateCount => candidates.Length;
    LocalDecisionPrompt(ActorObservation source, Candidate[] candidates) {
        session = source.SessionId; actor = source.ActorId; observation = source.ObservationId;
        this.candidates = candidates;
        var compact = new {
            health = source.Health, food = source.Food,
            candidates = candidates.Select((c, i) => (object)(c.Skill == "EatOne"
                ? new { candidate_index = i, skill = c.Skill, resource = "edible_inventory_item", nutrition = Math.Round(c.Nutrition, 3) }
                : (object)new { candidate_index = i, skill = c.Skill, resource = "supported_visible_harvest", distance = Math.Round(c.Distance, 2) })).ToArray()
        };
        Prompt = "Choose one survival action using only the listed candidates. Food is fullness (0 hungry, 1 full). " +
            "EatOne consumes one supported inventory item. ForageOne approaches, harvests, collects and eats one supported visible resource. " +
            "Wait does nothing. Prefer a useful achievable action when hungry; wait when no action is needed or available. " +
            "Return exactly one JSON object with exactly two keys, for example {\"skill\":\"Wait\",\"candidate_index\":-1}. " +
            "The skill must be EatOne, ForageOne, or Wait; candidate_index must be an integer. " +
            "For EatOne/ForageOne copy a matching listed index and skill. For Wait use candidate_index=-1. " +
            "No explanation, markdown, extra fields, commands or code.\n" + JsonSerializer.Serialize(compact);
    }
    public static LocalDecisionPrompt Create(ActorObservation source, Func<int, bool> isSupportedHarvestValue, int maxCandidates = 24) {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(isSupportedHarvestValue);
        if (maxCandidates < 1 || maxCandidates > 64) throw new ArgumentOutOfRangeException(nameof(maxCandidates));
        if (source.SessionId == Guid.Empty || source.ActorId == Guid.Empty || source.ObservationId == Guid.Empty ||
            !double.IsFinite(source.Time) || !double.IsFinite(source.Position.X) || !double.IsFinite(source.Position.Y) || !double.IsFinite(source.Position.Z) ||
            !float.IsFinite(source.Health) || !float.IsFinite(source.Food) || source.Health < 0 || source.Health > 1 || source.Food < 0 || source.Food > 1 ||
            source.Inventory == null || source.Cells == null || source.Inventory.Length > 4096 || source.Cells.Length > 4096)
            throw new ArgumentException("Invalid bounded actor observation");
        var slots = source.Inventory.ToArray(); var cells = source.Cells.ToArray();
        if (slots.Any(s => s == null) || cells.Any(c => c == null) || slots.Select(s => s.Slot).Distinct().Count() != slots.Length ||
            cells.Select(c => (c.X, c.Y, c.Z)).Distinct().Count() != cells.Length) throw new ArgumentException("Ambiguous observation entries");
        var choices = new List<Candidate>();
        foreach (var s in slots.OrderBy(s => s.Slot)) {
            if (!s.CanEat || s.Count <= 0) continue;
            if (s.Slot < 0 || s.Slot > 4095 || !float.IsFinite(s.Nutrition) || s.Nutrition <= 0) throw new ArgumentException("Invalid edible slot");
            choices.Add(new("EatOne", s.Slot, s.Value, null, s.Nutrition, 0));
        }
        foreach (var c in cells) {
            if (c.Visibility != "known" || !c.Value.HasValue || !isSupportedHarvestValue(c.Value.Value)) continue;
            var target = new CellTarget(c.X, c.Y, c.Z, c.Value.Value); target.Validate();
            double dx = c.X + 0.5 - source.Position.X, dy = c.Y + 0.5 - source.Position.Y, dz = c.Z + 0.5 - source.Position.Z;
            choices.Add(new("ForageOne", -1, 0, target, 0, Math.Sqrt(dx * dx + dy * dy + dz * dz)));
        }
        var bounded = choices.OrderBy(c => c.Skill == "EatOne" ? 0 : 1).ThenBy(c => c.Distance)
            .ThenBy(c => c.Slot).ThenBy(c => c.Target?.X).ThenBy(c => c.Target?.Y).ThenBy(c => c.Target?.Z).Take(maxCandidates).ToArray();
        return new LocalDecisionPrompt(source, bounded);
    }
    // One prompt is one decision identity. Retried identical choices cannot become new actions.
    public HighLevelDecision ParseResponse(string response) {
        if (response == null || response.Length == 0 || response.Length > 4096) throw new FormatException("Response size invalid");
        using var document = JsonDocument.Parse(response, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 3 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Response must be an object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!names.Add(property.Name) || property.Name is not ("skill" or "candidate_index")) throw new FormatException("Unexpected or duplicate response field");
        if (names.Count != 2 || root.GetProperty("skill").ValueKind != JsonValueKind.String) throw new FormatException("Missing or invalid fields");
        string? skill = root.GetProperty("skill").GetString(); var number = root.GetProperty("candidate_index");
        if (number.ValueKind != JsonValueKind.Number || number.GetRawText().IndexOfAny(['.', 'e', 'E']) >= 0 || !number.TryGetInt32(out int index)) throw new FormatException("Candidate index must be an integer");
        HighLevelDecision result;
        if (skill == "Wait" && index == -1) result = new(session, actor, observation, decision, "Observe", "Wait", -1, 0);
        else {
            if (skill is not ("EatOne" or "ForageOne") || index < 0 || index >= candidates.Length || candidates[index].Skill != skill) throw new FormatException("Unknown or mismatched candidate");
            var c = candidates[index];
            result = new(session, actor, observation, decision, "SatisfyHunger", c.Skill, c.Slot, c.Value, c.Target);
        }
        lock (gate) {
            if (accepted != null && accepted != result) throw new InvalidOperationException("Prompt already resolved to another decision");
            return accepted ??= result;
        }
    }
}
