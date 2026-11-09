using System.Text.Json;

namespace Game.Westworld {
    internal sealed record CognitiveMessage(string Id, ActorSocialView Value, bool Considered) {
        internal bool Ready => Value.SpeechComplete != false;
    }
    internal sealed record CognitiveNote(string Id, string Source, string Kind, string Text, double At, JsonElement Evidence) {
        internal CognitiveNoteView View() => new(Id, Source, Kind, Text, At);
    }
    internal sealed record CognitiveNoteView(string Id, string Source, string Kind, string Text, double At);
    internal sealed record CognitiveBody(string Hunger, string Energy, string Condition) {
        internal static CognitiveBody From(SelfView view) => new(
            Band(view.Food, "hungry", "peckish", "fed"),
            Band(view.Stamina, "exhausted", "tired", "rested"),
            Band(view.Health, "critical", "hurt", "well"));

        static string Band(float? level, string low, string mid, string high) =>
            !level.HasValue ? "unknown" : level < .3f ? low : level < .65f ? mid : high;
    }

    internal sealed record CognitiveContext(string Id, long Version, double GameTime, string[] Reasons,
        string Profile, string Goal, CognitiveBody Body, ActorTaskView Task, ActorTaskView LastOutcome, CognitiveNoteView[] Commitments,
        JsonElement Observation, InventoryView Inventory, EnvironmentView Environment,
        CognitiveMessage[] Messages, CognitiveNoteView[] Memories, string[] Actions);

    internal static class ActorCognitiveContext {
        // Sources are captured in one game-thread call. Only the requested optional sections are read.
        internal static CognitiveContext Assemble(ActorTools tools, long version, double now, string[] reasons, string profile,
            string goal, CognitiveBody body, ActorTaskView task, ActorTaskView lastOutcome, IEnumerable<CognitiveMessage> inbox,
            IReadOnlyList<CognitiveNote> commitments, IReadOnlyList<CognitiveNote> memory) {
            CognitiveMessage[] messages = inbox.Where(message => message.Ready).OrderBy(message => message.Considered).Take(6).ToArray();
            bool social = reasons.Contains("social");
            bool review = reasons.Contains("review");
            bool needsInventory = tools.HasActorNutrition || social || reasons.Contains("taskEnded") || review;
            HashSet<string> related = commitments.Select(note => note.Source).Concat(messages.Select(message => message.Id))
                .ToHashSet(StringComparer.Ordinal);
            CognitiveNoteView[] recalled = memory.Where(note => related.Contains(note.Source)
                    || social && note.Kind == "social" || reasons.Contains("taskEnded") && note.Kind == "task"
                    || review && note.Kind == "observation" || reasons.Contains("bodyChanged") && note.Kind == "body")
                .OrderByDescending(note => related.Contains(note.Source)).ThenByDescending(note => note.At).Take(4).Select(note => note.View()).ToArray();
            EnvironmentView environment = review ? tools.CognitiveEnvironment() : null;
            return new CognitiveContext(Guid.NewGuid().ToString("N"), version, now, reasons, profile, goal, body,
                task.Status == ActorTaskStatus.Idle ? null : task, lastOutcome, commitments.Select(note => note.View()).ToArray(), tools.CognitiveObservation(),
                needsInventory ? tools.CognitiveInventory() : null, environment == null ? null : environment with { GameTime = now },
                messages, recalled, tools.CognitiveActions());
        }
    }
}
