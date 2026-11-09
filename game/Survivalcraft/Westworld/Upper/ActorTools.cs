using System.Text.Json;
using System.Text.Json.Serialization;
using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    public sealed record ActorToolDefinition(string Name, string Description, JsonElement Parameters);
    public sealed record ActorToolResult(bool Ok, JsonElement Data, string Error = null);
    public sealed record ActorActionView(string Action, AtomicActionStatus Status, AtomicActionFailure Failure, int AffectedCount);
    public sealed record ActorSocialView(AtomicSocialEventKind Kind, double GameTime, string Source, string Text,
        string OfferReference, int ItemValue, int Count) {
        public Guid? SpeechId { get; init; }
        public bool? SpeechComplete { get; init; }
        public int? SpeechChunk { get; init; }
    }

    // One host-owned session per bound actor. All calls are made on the game update thread.
    public sealed partial class ActorTools {
        sealed record Field(string Name, string Type, string Description, bool Optional = false, string[] Choices = null,
            Field[] Children = null);
        sealed record ToolSpec(ActorToolDefinition Definition, Field[] Fields, Func<ActorTools, Args, object> Read,
            string Action, Func<ActorTools, Args, AtomicAction> Create);
        sealed class InvalidToolCall(string code) : Exception {
            public string Code { get; } = code;
        }

        const int MaxPayloadLength = 32768;
        const int MaxOfferReferences = 64;
        readonly AtomicActions m_actions;
        readonly ActorWorldMemory m_memory;
        readonly ActorTaskExecutor m_tasks;
        readonly Dictionary<string, Guid> m_offers = new(StringComparer.Ordinal);
        readonly Queue<string> m_offerOrder = new();
        readonly string m_scope = Guid.NewGuid().ToString("N");
        AtomicActionRun m_direct;
        long m_offerSequence;

        static readonly JsonSerializerOptions m_json = CreateJsonOptions();

        public ActorKnowledge Knowledge { get; }

        public ActorTools(AtomicActions actions, bool publicGuide = false) {
            ArgumentNullException.ThrowIfNull(actions);
            m_actions = actions;
            m_memory = new ActorWorldMemory(actions.BoundActor, actions);
            m_tasks = new ActorTaskExecutor(actions.BoundActor, actions);
            Knowledge = new ActorKnowledge(publicGuide);
        }

        public IReadOnlyList<ActorToolDefinition> GetTools() {
            HashSet<string> supported = m_actions.GetCapabilities().Select(action => action.Name).ToHashSet(StringComparer.Ordinal);
            return QueryTools.Concat(ActionTools.Where(spec => supported.Contains(spec.Action)))
                .Select(spec => spec.Definition)
                .ToArray();
        }

        public string DescribeToolsJson() => JsonSerializer.Serialize(GetTools(), m_json);

        public string InvokeJson(string name, string arguments) {
            if (arguments == null || arguments.Length > MaxPayloadLength) {
                return JsonSerializer.Serialize(Error("invalid_arguments"), m_json);
            }
            try {
                using JsonDocument document = JsonDocument.Parse(arguments);
                return JsonSerializer.Serialize(Invoke(name, document.RootElement), m_json);
            }
            catch (JsonException) {
                return JsonSerializer.Serialize(Error("invalid_arguments"), m_json);
            }
        }

        public ActorToolResult Invoke(string name, JsonElement arguments) {
            ToolSpec tool = FindTool(name);
            if (tool == null) {
                return Error("unknown_tool");
            }
            try {
                if (arguments.ValueKind != JsonValueKind.Object || arguments.GetRawText().Length > MaxPayloadLength) {
                    return Error("invalid_arguments");
                }
                Args args = new(arguments, tool.Fields);
                object result = tool.Create != null ? StartDirect(tool, args) : tool.Read(this, args);
                return new ActorToolResult(true, ToJson(result));
            }
            catch (InvalidToolCall failure) {
                return Error(failure.Code);
            }
            catch (ArgumentException) {
                return Error("invalid_arguments");
            }
            catch (Exception error) {
                Log.Error($"Actor tool {name}: {error}");
                return Error("execution_error");
            }
        }

        static ToolSpec FindTool(string name) => QueryTools.Concat(ActionTools)
            .FirstOrDefault(spec => spec.Definition.Name == name);

        static ToolSpec FindActionTool(string name) => ActionTools.FirstOrDefault(spec => spec.Definition.Name == name);

        static ActorToolResult Error(string code) => new(false, ToJson(null), code);

        static JsonElement ToJson(object value) => JsonSerializer.SerializeToElement(value, m_json);

        bool TaskActive => m_tasks.IsActive;

        bool DirectActive => m_direct?.Result.Status == AtomicActionStatus.Running;

        ActorActionView StartDirect(ToolSpec tool, Args args) {
            if (TaskActive || DirectActive) {
                throw new InvalidToolCall("busy");
            }
            if (!m_actions.GetCapabilities().Any(action => action.Name == tool.Action)) {
                throw new InvalidToolCall("tool_unavailable");
            }
            m_direct = m_actions.Start(tool.Create(this, args));
            return ReadDirect();
        }

        ActorActionView ReadDirect() {
            if (m_direct == null) {
                return null;
            }
            AtomicActionResult result = m_direct.Result;
            return new ActorActionView(m_direct.Action.GetType().Name, result.Status, result.Failure, result.AffectedCount);
        }

        ActorObservation Observe(float lookDistance, float objectDistance, int limit) => m_memory.Capture(lookDistance, objectDistance, limit);

        InventoryView ReadInventory() {
            InventoryView view = m_actions.Queries.ReadInventory();
            RememberItems(view);
            return view;
        }

        StorageView ReadStorage(string reference) {
            StorageView view = m_actions.Queries.ReadStorage(Storage(reference));
            RememberItems(view?.Inventory);
            return view;
        }

        void RememberItems(InventoryView inventory) {
            if (inventory == null) {
                return;
            }
            foreach (InventorySlotView slot in inventory.Slots) {
                if (slot.Count > 0) {
                    Knowledge.TeachItem(slot.Value);
                }
            }
        }

        ActorSocialView[] ReadSocial() => m_peekCognitiveInbox?.Invoke() ?? DrainSocialViews();

        ActorSocialView[] DrainSocialViews() => m_actions.DrainSocialEvents().Select(item => new ActorSocialView(
            item.Kind,
            item.GameTime,
            m_memory.FindObservedEntityReference(item.Source),
            item.Text,
            item.Kind == AtomicSocialEventKind.ItemOffered && item.OfferId.HasValue ? PublishOffer(item.OfferId.Value) : null,
            item.ItemValue,
            item.Count
        ) { SpeechId = item.SpeechId, SpeechComplete = item.SpeechComplete, SpeechChunk = item.SpeechChunk }).ToArray();

        string PublishOffer(Guid offer) {
            if (m_offers.Count == MaxOfferReferences) {
                m_offers.Remove(m_offerOrder.Dequeue());
            }
            string reference = $"offer_{m_scope}_{++m_offerSequence}";
            m_offers.Add(reference, offer);
            m_offerOrder.Enqueue(reference);
            return reference;
        }

        Guid ResolveOffer(string reference) => m_offers.TryGetValue(reference, out Guid offer)
            ? offer : throw new InvalidToolCall("unknown_reference");

        BlockTarget BlockReference(string reference) => Need(m_memory.ResolveBlock(reference));
        ActionTarget WorldTarget(string reference) => Need(m_memory.ResolveTarget(reference));
        Entity Person(string reference) => Need(m_memory.ResolveEntity(reference));
        IInventory Storage(string reference) => Need(m_memory.ResolveStorage(reference));
        Vector3 Position(string reference) => m_memory.ResolvePosition(reference) ?? throw new InvalidToolCall("unknown_reference");
        Vector3 ObservedPosition(string reference) => m_memory.ResolveObservedPosition(reference) ?? throw new InvalidToolCall("unknown_reference");
        PickUp Pickup(Args args) {
            string target = args.String("target");
            int? foodValue = args.OptionalInt("food_value");
            if ((target == null) == (foodValue == null) || foodValue is <= 0) {
                throw new InvalidToolCall("invalid_arguments");
            }
            int count = args.Int("count");
            return foodValue != null ? new PickUp(null, count) { FoodValue = foodValue } :
                Need(m_memory.ResolvePickup(target, count));
        }

        MoveTo MoveCommand(Args args) {
            string reference = args.String("destination");
            bool relative = args.Has("bearing") && args.Has("distance");
            if (reference != null ? args.Has("bearing") || args.Has("distance") : !relative) {
                throw new InvalidToolCall("invalid_arguments");
            }
            Vector3 destination = reference == null ? RelativePoint(args.Number("bearing"), args.Number("distance")) : Position(reference);
            return new MoveTo(destination, args.Number("range", reference == null ? 0.4f : 0.75f), args.Number("timeout", 15f));
        }

        FaceTowards TurnCommand(Args args) {
            string reference = args.String("destination");
            if ((reference != null) == args.Has("bearing")) {
                throw new InvalidToolCall("invalid_arguments");
            }
            Vector3 point = reference == null ? RelativePoint(args.Number("bearing"), 2f) : ObservedPosition(reference);
            if (reference != null && args.Has("pitch")) throw new InvalidToolCall("invalid_arguments");
            float? pitch = args.Has("pitch") ? args.Number("pitch") : null;
            if (pitch is < -82f or > 82f) throw new InvalidToolCall("invalid_arguments");
            return new FaceTowards(point, args.Number("timeout", 5f), pitch,
                HorizontalOnly: reference == null && !pitch.HasValue);
        }

        Vector3 RelativePoint(float bearing, float distance) {
            if (bearing is < -180f or > 180f || distance is < 1f or > 4f) {
                throw new InvalidToolCall("invalid_arguments");
            }
            SelfView self = m_actions.Queries.ReadSelf() ?? throw new InvalidToolCall("actor_unavailable");
            Vector3 facing = new(self.Facing.X, 0f, self.Facing.Z);
            if (facing.LengthSquared() < 0.0001f) {
                throw new InvalidToolCall("actor_unavailable");
            }
            Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathUtils.DegToRad(-bearing));
            return self.Position + distance * Vector3.Transform(Vector3.Normalize(facing), rotation);
        }

        static T Need<T>(T value) where T : class => value ?? throw new InvalidToolCall("unknown_reference");

        ActorTaskView PlanCraft(string workspaceReference, string recipeReference, int count) {
            if (TaskActive || DirectActive) {
                throw new InvalidToolCall("busy");
            }
            IInventory workspace = Storage(workspaceReference);
            RecipeGuideEntry recipe = Need(Knowledge.ResolveRecipe(recipeReference));
            IInventory inventory = m_actions.BoundActor.FindComponent<ComponentMiner>()?.Inventory;
            if (workspace is not ComponentCraftingTable table || m_actions.Queries.ReadStorage(table) == null) {
                throw new InvalidToolCall("workspace_unavailable");
            }
            IReadOnlyList<ActorTaskStep> steps = RecipeTaskComposer.Compose(m_actions, inventory, table, recipe, count);
            if (steps == null) {
                throw new InvalidToolCall("plan_unavailable");
            }
            return SubmitSteps(steps);
        }

        ActorTaskView SubmitSteps(IReadOnlyList<ActorTaskStep> steps) {
            if (!m_tasks.Submit(steps)) {
                throw new InvalidToolCall("plan_unavailable");
            }
            return m_tasks.Status;
        }

        static ToolSpec Query(string name, string description, Func<ActorTools, Args, object> call, params Field[] fields) =>
            new(Describe(name, description, fields), fields, call, null, null);

        static ToolSpec Action<T>(string description, Func<ActorTools, Args, T> create, params Field[] fields) where T : AtomicAction =>
            new(Describe($"act_{typeof(T).Name}", description, fields), fields, null, typeof(T).Name, (tools, args) => create(tools, args));

        static ActorToolDefinition Describe(string name, string description, Field[] fields) =>
            new(name, description, ToJson(DescribeObject(fields)));

        static Dictionary<string, object> DescribeObject(Field[] fields) => new() {
            ["type"] = "object",
            ["properties"] = fields.ToDictionary(field => field.Name, DescribeField),
            ["required"] = fields.Where(field => !field.Optional).Select(field => field.Name).ToArray(),
            ["additionalProperties"] = false
        };

        static Dictionary<string, object> DescribeField(Field field) {
            Dictionary<string, object> property = field.Type == "object" && field.Children != null
                ? DescribeObject(field.Children) : new() { ["type"] = field.Type };
            property["description"] = field.Description;
            if (field.Choices != null) {
                property["enum"] = field.Choices;
            }
            if (field.Type == "array") {
                property["items"] = field.Children == null ? new { type = "object" } : DescribeObject(field.Children);
            }
            return property;
        }

        static Field Ref(string name, string description, bool optional = false) => new(name, "string", description, optional);
        static Field Text(string name, string description, bool optional = false) => new(name, "string", description, optional);
        static Field Int(string name, string description, bool optional = false) => new(name, "integer", description, optional);
        static Field Number(string name, string description, bool optional = false) => new(name, "number", description, optional);
        static Field Object(string name, string description, bool optional = false) => new(name, "object", description, optional);
        static Field Bool(string name, string description) => new(name, "boolean", description);

        sealed class Args {
            readonly JsonElement m_values;

            public Args(JsonElement values, Field[] fields) {
                if (values.ValueKind != JsonValueKind.Object) {
                    throw new InvalidToolCall("invalid_arguments");
                }
                m_values = values;
                HashSet<string> seen = new(StringComparer.Ordinal);
                foreach (JsonProperty property in values.EnumerateObject()) {
                    Field field = fields.FirstOrDefault(field => field.Name == property.Name);
                    if (field == null || !seen.Add(property.Name) || !Valid(property.Value, field)) {
                        throw new InvalidToolCall("invalid_arguments");
                    }
                }
                if (fields.Any(field => !field.Optional && !seen.Contains(field.Name))) {
                    throw new InvalidToolCall("invalid_arguments");
                }
            }

            static bool Valid(JsonElement value, Field field) => field.Type switch {
                "string" => value.ValueKind == JsonValueKind.String
                    && (field.Choices == null || field.Choices.Contains(value.GetString(), StringComparer.Ordinal)),
                "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
                "number" => value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out float number) && float.IsFinite(number),
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                _ => false
            };

            public JsonElement Json(string name) => m_values.TryGetProperty(name, out JsonElement value) ? value : default;
            public bool Has(string name) => m_values.TryGetProperty(name, out _);
            public string String(string name) => Json(name).ValueKind == JsonValueKind.String ? Json(name).GetString() : null;
            public int Int(string name, int fallback = 0) => OptionalInt(name) ?? fallback;
            public int? OptionalInt(string name) => Json(name).ValueKind == JsonValueKind.Number ? Json(name).GetInt32() : null;
            public float Number(string name, float fallback = 0f) => Json(name).ValueKind == JsonValueKind.Number ? Json(name).GetSingle() : fallback;
            public bool Bool(string name) => Json(name).GetBoolean();
            public OwnItemSlot OwnItem() => new(Int("slot"), Int("value"));
        }

        static JsonSerializerOptions CreateJsonOptions() {
            JsonSerializerOptions options = new() {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            options.Converters.Add(new VectorJson());
            options.Converters.Add(new PointJson());
            options.Converters.Add(new ColorJson());
            return options;
        }

        sealed class VectorJson : JsonConverter<Vector3> {
            public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
            public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options) {
                writer.WriteStartObject();
                writer.WriteNumber("x", value.X);
                writer.WriteNumber("y", value.Y);
                writer.WriteNumber("z", value.Z);
                writer.WriteEndObject();
            }
        }

        sealed class PointJson : JsonConverter<Point3> {
            public override Point3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
            public override void Write(Utf8JsonWriter writer, Point3 value, JsonSerializerOptions options) {
                writer.WriteStartObject();
                writer.WriteNumber("x", value.X);
                writer.WriteNumber("y", value.Y);
                writer.WriteNumber("z", value.Z);
                writer.WriteEndObject();
            }
        }

        sealed class ColorJson : JsonConverter<Color> {
            public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
            public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) {
                writer.WriteStartArray();
                writer.WriteNumberValue(value.R);
                writer.WriteNumberValue(value.G);
                writer.WriteNumberValue(value.B);
                writer.WriteNumberValue(value.A);
                writer.WriteEndArray();
            }
        }
    }
}
