using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Westworld {
    // Host polls and commits on the game update thread. A decision source can keep the frozen input outside the game.
    public sealed partial class ActorCognition {
        const int InboxLimit = 32;
        const int MemoryLimit = 32;
        const int CommitmentLimit = 4;
        const double MinimumThinkInterval = .1;

        sealed record NoteProposal(string Source, string Text);
        sealed record Decision(string Reason, string Task, JsonElement Steps, string Goal, double? ReviewAfter,
            NoteProposal Remember, NoteProposal Commitment, string ResolveCommitment, string[] Handled);
        internal sealed record Pending(CognitiveContext Context, string Input, int DroppedMessages);

        static readonly JsonSerializerOptions m_json = new(JsonSerializerDefaults.Web) {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
        };

        readonly ActorTools m_tools;
        readonly List<CognitiveMessage> m_inbox = [];
        readonly List<CognitiveNote> m_memory = [];
        readonly List<CognitiveNote> m_commitments = [];
        string m_profile;
        string m_goal;
        CognitiveBody m_body;
        Guid m_seenTask;
        ActorTaskView m_restoredOutcome;
        long m_version;
        double m_reviewAt;
        double m_nextTurnAt;
        int m_droppedMessages;
        Pending m_pending;
        string m_dispatchedModelTurn;

        internal bool IsBoundTo(ActorTools tools) => m_tools == tools;

        internal bool TryClaimModelTurn(string id) {
            if (m_pending == null || m_pending.Context.Id != id || m_dispatchedModelTurn == id) {
                return false;
            }
            if (m_pending.Context.GameTime != Now) {
                m_pending = null;
                return false;
            }
            m_dispatchedModelTurn = id;
            return true;
        }

        public string LastDecisionJson { get; private set; }

        public ActorCognition(ActorTools tools, string profile) {
            ArgumentNullException.ThrowIfNull(tools);
            if (string.IsNullOrWhiteSpace(profile) || profile.Length > 320) {
                throw new ArgumentException("Provide a short profile.", nameof(profile));
            }
            m_tools = tools;
            tools.BindCognition(PeekInbox);
            m_profile = profile.Trim();
        }

        public string PrepareTurn() => PrepareTurn(cohort: false);

        internal string PrepareTurn(bool cohort, bool lookAhead = false) {
            CollectInbox();
            if (m_pending != null) {
                return m_pending.Input;
            }
            double now = Now;
            SelfView self = m_tools.CognitiveBody;
            if (!CanThink(self) || now < m_nextTurnAt) {
                return null;
            }
            CognitiveBody body = CognitiveBody.From(self);
            ActorTaskView task = AtCognitiveTime(m_tools.CognitiveTask);
            ActorTaskView outcome = LatestOutcome();
            List<string> reasons = [];
            if (lookAhead) {
                reasons.Add("nextTask");
            }
            if (m_version == 0) {
                reasons.Add("initial");
            }
            else {
                if (body != m_body) {
                    reasons.Add("bodyChanged");
                }
                if (now >= m_reviewAt) {
                    reasons.Add("review");
                }
            }
            if (outcome != null && outcome.TaskId != m_seenTask) {
                reasons.Add("taskEnded");
            }
            if (m_droppedMessages > 0 || m_inbox.Any(message => message.Ready && !message.Considered)) {
                reasons.Add("social");
            }
            if (reasons.Count == 0) {
                return null;
            }

            ActorTaskView earlierOutcome = outcome?.TaskId != task.TaskId && (lookAhead || reasons.Contains("taskEnded")) ? outcome : null;
            CognitiveContext context = ActorCognitiveContext.Assemble(m_tools, m_version, now, reasons.ToArray(), m_profile,
                m_goal, body, task, earlierOutcome, m_inbox, m_commitments, m_memory);
            if (cohort) {
                context = context with { Actions = m_tools.CognitiveActions(cohort: true) };
            }
            string input = JsonSerializer.Serialize(new { context.Id, context.Version, context.GameTime, context.Reasons,
                context.Profile, context.Goal, context.Body, context.Task, context.LastOutcome, context.Commitments, context.Observation,
                context.Inventory, context.Environment, context.Messages, context.Memories, context.Actions,
                nextTaskInstruction = lookAhead
                    ? "Use start for a separate follow-up after the current task succeeds, or keep if there is no useful follow-up. "
                        + "Do not repeat the current task. The game will recheck the follow-up before starting it."
                    : null,
                droppedMessages = m_droppedMessages }, m_json);
            m_pending = new Pending(context, input, m_droppedMessages);
            return input;
        }

        public bool AbandonTurn(string id) {
            if (m_pending == null || m_pending.Context.Id != id) {
                return false;
            }
            m_pending = null;
            m_nextTurnAt = Now + MinimumThinkInterval;
            return true;
        }

        internal Pending ScheduledInput => m_pending;

        internal void RestoreScheduledInput(Pending pending) => m_pending = pending;

        internal string ValidateScheduled(string response) {
            Decision decision = DecodeDecision(response);
            if (m_pending == null) {
                return "unknown_turn";
            }
            string error = ValidateDecision(decision, m_pending.Context);
            return error ?? m_tools.ValidateCognitiveTask(decision.Task, decision.Steps);
        }

        internal string CommitObservation(string id, string response, SelfView observedBody, Guid? precedingTask) {
            if (m_pending?.Context is not { } context || context.Id != id) {
                return Result("unknown_turn");
            }
            SelfView current = m_tools.CognitiveBody;
            if (!m_tools.CognitiveWorldIsCurrent || !CanThink(current) || context.Version != m_version) {
                return Result("stale_turn");
            }
            ActorTaskView task = m_tools.CognitiveTask;
            CognitiveBody body = CognitiveBody.From(current);
            if (precedingTask is Guid taskId) {
                if (context.Task?.TaskId != taskId || task.TaskId != taskId) {
                    return Result("task_changed");
                }
                if (task.Status != ActorTaskStatus.Succeeded) {
                    return Result("preceding_task_unsuccessful");
                }
                if (body.Condition != context.Body.Condition) {
                    return Result("stale_turn");
                }
            }
            else if (body != context.Body || !SameTask(task, context.Task)) {
                return Result("stale_turn");
            }
            Decision decision = DecodeDecision(response);
            string error = ValidateDecision(decision, context);
            if (error == null && precedingTask != null && decision.Task is not ("start" or "keep")) {
                error = "invalid_follow_up";
            }
            error ??= m_tools.ValidateCognitiveTask(decision.Task, decision.Steps);
            if (error == null && decision.Task is "start" or "replace") {
                error = m_tools.RecheckObservationPlan(decision.Steps, observedBody, current);
            }
            return error != null ? Result(error) : InstallTurn(id, response, scheduled: false, continuation: precedingTask != null);
        }

        public string CommitTurn(string id, string response) => InstallTurn(id, response, scheduled: false);

        internal string InstallScheduled(string id, string response) => InstallTurn(id, response, scheduled: true);

        string InstallTurn(string id, string response, bool scheduled, bool continuation = false) {
            if (m_pending == null || id != m_pending.Context.Id) {
                return Result("unknown_turn");
            }
            CognitiveContext context = m_pending.Context;
            SelfView self = m_tools.CognitiveBody;
            ActorTaskView task = m_tools.CognitiveTask;
            if (!CanThink(self) || context.Version != m_version
                || (!scheduled && !continuation && (CognitiveBody.From(self) != context.Body || !SameTask(task, context.Task)))) {
                m_pending = null;
                return Result("stale_turn");
            }
            Decision decision = DecodeDecision(response);
            string invalid = ValidateDecision(decision, context);
            if (invalid != null) {
                return Result(invalid);
            }
            CognitiveNote commitment = decision.Commitment == null ? null : MakeNote(decision.Commitment, context);
            CognitiveNote memory = decision.Remember == null ? null : MakeNote(decision.Remember, context);
            string rejected = scheduled && decision.Task != "keep" && !SameTask(task, context.Task)
                ? "task_changed" : m_tools.ApplyCognitiveTask(decision.Task, decision.Steps);
            if (rejected != null && !scheduled) {
                return Result(rejected);
            }

            double now = Now;
            m_version++;
            m_body = context.Body;
            m_reviewAt = now + decision.ReviewAfter.Value;
            m_nextTurnAt = now + MinimumThinkInterval;
            m_goal = decision.Goal ?? m_goal;
            if (context.LastOutcome != null) {
                m_seenTask = context.LastOutcome.TaskId;
            }
            if (context.Task != null && (continuation || IsTerminal(context.Task)
                || rejected == null && decision.Task is "cancel" or "replace")) {
                m_seenTask = context.Task.TaskId;
            }
            if (decision.ResolveCommitment != null) {
                m_commitments.RemoveAll(note => note.Id == decision.ResolveCommitment);
            }
            if (commitment != null) {
                m_commitments.Add(commitment);
            }
            if (memory != null) {
                if (m_memory.Count == MemoryLimit) {
                    m_memory.RemoveAt(0);
                }
                m_memory.Add(memory);
            }
            HashSet<string> handled = (decision.Handled ?? []).ToHashSet(StringComparer.Ordinal);
            HashSet<string> presented = context.Messages.Select(message => message.Id).ToHashSet(StringComparer.Ordinal);
            m_inbox.RemoveAll(message => handled.Contains(message.Id));
            for (int index = 0; index < m_inbox.Count; index++) {
                CognitiveMessage message = m_inbox[index];
                if (presented.Contains(message.Id)) {
                    m_inbox[index] = message with { Considered = true };
                }
            }
            m_droppedMessages -= m_pending.DroppedMessages;
            LastDecisionJson = JsonSerializer.Serialize(new {
                input = JsonSerializer.Deserialize<JsonElement>(m_pending.Input), decision = JsonSerializer.Deserialize<JsonElement>(response),
                precedingTaskOutcome = continuation ? AtCognitiveTime(task) : null,
                taskId = m_tools.CognitiveTask.TaskId, version = m_version, handled = handled.Count,
                remembered = decision.Remember != null, reviewAt = m_reviewAt, actionError = rejected
            }, m_json);
            m_pending = null;
            return Result(null);
        }

        static Decision DecodeDecision(string response) {
            try {
                return response is { Length: > 0 and <= 32768 } ? JsonSerializer.Deserialize<Decision>(response, m_json) : null;
            }
            catch (JsonException) {
                return null;
            }
        }

        ActorSocialView[] PeekInbox() {
            CollectInbox();
            return m_inbox.Select(message => message.Value).ToArray();
        }

        void CollectInbox() {
            foreach (ActorSocialView entry in m_tools.CognitiveInbox()) {
                ActorSocialView view = entry with { GameTime = entry.GameTime + m_timeOffset };
                int previous = view.SpeechId is Guid speech
                    ? m_inbox.FindLastIndex(message => !message.Ready && message.Value.SpeechId == speech) : -1;
                if (previous >= 0) {
                    CognitiveMessage message = m_inbox[previous];
                    string separator = view.SpeechChunk == message.Value.SpeechChunk + 1 ? "" : " ... ";
                    m_inbox[previous] = message with {
                        Value = view with { Text = LimitSpeech(message.Value.Text + separator + view.Text), Source = view.Source ?? message.Value.Source }
                    };
                    continue;
                }
                if (m_inbox.Count == InboxLimit) {
                    int considered = m_inbox.FindIndex(message => message.Considered
                        && !(m_pending?.Context.Messages.Any(presented => presented.Id == message.Id) ?? false));
                    if (considered < 0) {
                        m_droppedMessages++;
                        continue;
                    }
                    m_inbox.RemoveAt(considered);
                }
                string text = view.SpeechChunk > 0 ? "... " + view.Text : view.Text;
                m_inbox.Add(new CognitiveMessage(Guid.NewGuid().ToString("N"), view with { Text = LimitSpeech(text) }, false));
            }

            // If a speaker stopped or went out of earshot, make the part actually heard available.
            double now = Now;
            for (int index = 0; index < m_inbox.Count; index++) {
                CognitiveMessage message = m_inbox[index];
                if (!message.Ready && now - message.Value.GameTime >= 1.25) {
                    m_inbox[index] = message with { Value = message.Value with { SpeechComplete = null, Text = LimitSpeech(message.Value.Text + " ...") } };
                }
            }
        }

        static string LimitSpeech(string text) => text is { Length: > 320 } ? text[..320] : text;

        static string ValidateDecision(Decision decision, CognitiveContext context) {
            if (decision == null || !ValidText(decision.Reason, 320) || decision.Task is not ("keep" or "cancel" or "start" or "replace")
                || decision.Goal is { Length: > 256 } || decision.ReviewAfter is not double delay
                || !double.IsFinite(delay) || delay is < MinimumThinkInterval or > 120
                || (decision.Task is "start" or "replace" ? decision.Steps.ValueKind != JsonValueKind.Array
                    : decision.Steps.ValueKind != JsonValueKind.Undefined)) {
                return "invalid_decision";
            }
            if (decision.Handled != null && (decision.Handled.Length > context.Messages.Length
                || decision.Handled.Distinct(StringComparer.Ordinal).Count() != decision.Handled.Length
                || decision.Handled.Any(id => !context.Messages.Any(message => message.Id == id)))) {
                return "unknown_message";
            }
            if (decision.ResolveCommitment != null && !context.Commitments.Any(note => note.Id == decision.ResolveCommitment)) {
                return "unknown_commitment";
            }
            if (decision.Commitment != null && context.Commitments.Length == CommitmentLimit && decision.ResolveCommitment == null) {
                return "commitments_full";
            }
            return ValidNote(decision.Remember, context) && ValidNote(decision.Commitment, context) ? null : "invalid_evidence";
        }

        static bool ValidNote(NoteProposal note, CognitiveContext context) =>
            note == null || ValidText(note.Text, 256) && EvidenceKind(note.Source, context) != null;

        static string EvidenceKind(string source, CognitiveContext context) => source switch {
            "observation" => "observation",
            "body" => "body",
            "task" when context.Task != null => "task",
            "taskOutcome" when context.LastOutcome != null => "task",
            _ => context.Messages.Any(message => message.Id == source) ? "social" : null
        };

        static CognitiveNote MakeNote(NoteProposal note, CognitiveContext context) {
            string kind = EvidenceKind(note.Source, context);
            JsonElement evidence = note.Source switch {
                "observation" => context.Observation,
                "body" => JsonSerializer.SerializeToElement(context.Body, m_json),
                "task" => JsonSerializer.SerializeToElement(context.Task, m_json),
                "taskOutcome" => JsonSerializer.SerializeToElement(context.LastOutcome, m_json),
                _ => JsonSerializer.SerializeToElement(context.Messages.First(message => message.Id == note.Source).Value, m_json)
            };
            string source = kind == "social" ? note.Source : context.Id + ":" + note.Source;
            return new CognitiveNote(Guid.NewGuid().ToString("N"), source, kind, note.Text.Trim(), context.GameTime, evidence);
        }

        static bool ValidText(string value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit;
        static bool CanThink(SelfView self) => self is { Health: > 0, IsSleeping: false };
        static bool IsTerminal(ActorTaskView task) =>
            task?.Status is ActorTaskStatus.Succeeded or ActorTaskStatus.Failed or ActorTaskStatus.Cancelled;

        static bool SameTask(ActorTaskView current, ActorTaskView expected) => current.Status == ActorTaskStatus.Idle
            ? expected == null
            : expected != null && current.TaskId == expected.TaskId && current.Version == expected.Version
                && current.Status == expected.Status && current.CurrentStep == expected.CurrentStep
                && current.CompletedSteps == expected.CompletedSteps
                && (current.CurrentStep is not int index || current.Steps[index].Attempts == expected.Steps[index].Attempts);

        static string Result(string error) => JsonSerializer.Serialize(new { ok = error == null, error }, m_json);
    }
}
