using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Westworld {
    // Host calls TryStart and Poll on the game thread and pauses world advancement between them.
    public sealed class ActorModelTurn : IDisposable {
        static readonly JsonSerializerOptions m_json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        readonly ActorCognition m_cognition;
        readonly ActorTools m_tools;
        readonly IActorDecisionSource m_source;
        readonly CancellationTokenSource m_cancel = new();
        Task<ActorModelReply> m_request;
        string m_turnId;
        double m_gameTime;
        bool m_disposed;

        public string TurnId => m_turnId;
        public string LastResultJson { get; private set; }

        public ActorModelTurn(ActorCognition cognition, ActorTools tools, IActorDecisionSource source) {
            m_cognition = cognition ?? throw new ArgumentNullException(nameof(cognition));
            m_tools = tools ?? throw new ArgumentNullException(nameof(tools));
            m_source = source ?? throw new ArgumentNullException(nameof(source));
            if (!cognition.IsBoundTo(tools)) {
                throw new ArgumentException("The tool session belongs to a different actor.", nameof(tools));
            }
        }

        public bool TryStart() {
            if (m_disposed || m_request != null || !m_tools.CognitiveWorldIsCurrent || m_cognition.PrepareTurn() is not string input) {
                return false;
            }
            using JsonDocument context = JsonDocument.Parse(input);
            string id = context.RootElement.GetProperty("id").GetString();
            if (!m_cognition.TryClaimModelTurn(id)) {
                return false;
            }
            m_turnId = id;
            m_gameTime = m_tools.CognitiveTime;
            string schemas = m_tools.DescribeDecisionTools();
            CancellationToken cancellation = m_cancel.Token;
            m_request = Task.Run(() => m_source.RequestAsync(input, schemas, cancellation), cancellation);
            return true;
        }

        public string Poll() {
            if (m_request is not { IsCompleted: true }) {
                return null;
            }
            Task<ActorModelReply> request = m_request;
            m_request = null;
            ActorModelReply reply;
            try {
                reply = request.GetAwaiter().GetResult() ?? new ActorModelReply(null, Error: "decision_source_failed");
            }
            catch (Exception) {
                reply = new ActorModelReply(null, Error: "decision_source_failed");
            }
            string error = reply.Error;
            if (!m_tools.CognitiveWorldIsCurrent || m_gameTime != m_tools.CognitiveTime) {
                m_cognition.AbandonTurn(m_turnId);
                error ??= "world_advanced";
            }
            else if (error == null) {
                using JsonDocument result = JsonDocument.Parse(m_cognition.CommitTurn(m_turnId, reply.DecisionJson));
                if (!result.RootElement.GetProperty("ok").GetBoolean()) {
                    error = result.RootElement.GetProperty("error").GetString();
                }
            }
            LastResultJson = JsonSerializer.Serialize(new {
                ok = error == null, turnId = m_turnId, error, reply.Model, reply.RequestId,
                reply.InputTokens, reply.OutputTokens, reply.Status, reply.RetryAfterSeconds
            }, m_json);
            return LastResultJson;
        }

        public void Dispose() {
            if (!m_disposed) {
                m_disposed = true;
                m_cancel.Cancel();
                m_cancel.Dispose();
                m_request = null;
            }
        }
    }
}
