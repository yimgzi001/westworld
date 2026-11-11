#if WINDOWS
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    internal static class WorldPanel {
        const int MaxActors = 64;
        const int MaxEvents = 200;
        const int MaxWindowBytes = 128 * 1024;
        static readonly int m_port = int.TryParse(Environment.GetEnvironmentVariable("WENMING_PANEL_PORT"), out int port)
            && port is > 0 and <= 65535 ? port : 0;
        static readonly JsonSerializerOptions m_json = new(CognitionRunLedger.Json) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        static readonly ConditionalWeakTable<Entity, Binding> m_bindings = new();
        static readonly EventWindow m_events = new();
        static readonly EventWindow m_speech = new();
        static readonly Dictionary<int, (int Health, int? Food)> m_previousBody = new();
        static WorldPanelServer m_server;
        static Project m_project;
        static string m_session;
        static long m_lastSample, m_sequence, m_version;
        static bool m_startFailed, m_captureFailed;
        static SaveStamp m_nativeSave;

        sealed record Binding(ActorTools Tools, ActorCognition Cognition);
        sealed record SaveStamp(string Directory, DateTimeOffset At);
        sealed record Item(int Slot, int Value, string Name, int Count, bool Food, bool Selected);

        sealed class EventWindow {
            readonly Queue<(JsonElement Event, int Bytes)> m_entries = new();
            int m_bytes;

            internal void Add(JsonElement entry) {
                int bytes = System.Text.Encoding.UTF8.GetByteCount(entry.GetRawText());
                if (bytes > MaxWindowBytes) return;
                while (m_entries.Count >= MaxEvents || m_bytes + bytes > MaxWindowBytes) {
                    m_bytes -= m_entries.Dequeue().Bytes;
                }
                m_entries.Enqueue((entry, bytes));
                m_bytes += bytes;
            }

            internal JsonElement[] Snapshot() => m_entries.Select(entry => entry.Event).ToArray();

            internal void Clear() {
                m_entries.Clear();
                m_bytes = 0;
            }
        }

        internal static void OpenWorld(Project project) {
            if (m_port == 0 || m_startFailed || project == null) return;
            try {
                m_server ??= new WorldPanelServer(m_port);
                m_project = project;
                m_session = Guid.NewGuid().ToString("N");
                m_server.Publish("{\"state\":\"loading\"}"u8.ToArray());
            }
            catch (Exception error) {
                m_startFailed = true;
                Log.Warning($"World panel could not start on 127.0.0.1:{m_port}: {error.Message}");
            }
        }

        internal static void CloseWorld(Project project) {
            if (project != m_project) return;
            m_project = null;
            m_events.Clear();
            m_speech.Clear();
            m_previousBody.Clear();
            m_lastSample = m_sequence = m_version = 0;
            m_server.Publish("{\"state\":\"noWorld\"}"u8.ToArray());
        }

        internal static void Register(Entity actor, ActorTools tools, ActorCognition cognition = null) {
            if (m_port == 0) return;
            m_bindings.AddOrUpdate(actor, new Binding(tools, cognition));
        }

        internal static void NativeSaveCompleted(string directory) {
            if (m_port != 0) Volatile.Write(ref m_nativeSave, new SaveStamp(directory, DateTimeOffset.UtcNow));
        }

        internal static void Capture(Project project, bool canAdvance) {
            if (project != m_project) return;
            long now = Stopwatch.GetTimestamp();
            if (m_lastSample != 0 && Stopwatch.GetElapsedTime(m_lastSample, now).TotalMilliseconds < 500) return;
            m_lastSample = now;
            try {
                m_server.Publish(JsonSerializer.SerializeToUtf8Bytes(ReadWorld(project, canAdvance), m_json));
                m_captureFailed = false;
            }
            catch (Exception error) {
                if (!m_captureFailed) Log.Warning($"World panel snapshot unavailable: {error.GetType().Name}");
                m_captureFailed = true;
                m_server.Publish("{\"state\":\"snapshotUnavailable\"}"u8.ToArray());
            }
        }

        static object ReadWorld(Project project, bool canAdvance) {
            SubsystemTime clock = project.FindSubsystem<SubsystemTime>(true);
            SubsystemGameInfo info = project.FindSubsystem<SubsystemGameInfo>(true);
            SubsystemTimeOfDay day = project.FindSubsystem<SubsystemTimeOfDay>();
            SubsystemWeather weather = project.FindSubsystem<SubsystemWeather>();
            WorldCognitionScheduler scheduler = WorldCognitionScheduler.ForPanel(project);
            Dictionary<int, Binding> scheduled = scheduler?.PanelActors.ToDictionary(entry => entry.Tools.PanelActor.Id,
                entry => new Binding(entry.Tools, entry.Cognition)) ?? [];

            List<object> actors = [];
            int actorCount = 0, alive = 0, creatures = 0;
            long foodInBags = 0;
            foreach (Entity entity in project.Entities) {
                Binding binding = scheduled.GetValueOrDefault(entity.Id);
                if (binding == null) m_bindings.TryGetValue(entity, out binding);
                ComponentPlayer player = entity.FindComponent<ComponentPlayer>();
                ComponentActorNutrition nutrition = entity.FindComponent<ComponentActorNutrition>();
                ComponentHealth health = entity.FindComponent<ComponentHealth>();
                if (binding == null && player == null && nutrition == null) {
                    if (entity.FindComponent<ComponentCreature>() != null && health is { Health: > 0 }) creatures++;
                    continue;
                }
                ComponentBody body = entity.FindComponent<ComponentBody>();
                if (body == null || health == null) continue;
                actorCount++;
                if (health.Health > 0) alive++;
                Item[] inventory = ReadInventory(entity.FindComponent<ComponentMiner>()?.Inventory);
                if (inventory != null) foodInBags += inventory.Where(item => item.Food).Sum(item => (long)item.Count);
                if (actors.Count >= MaxActors) continue;

                ComponentVitalStats vital = entity.FindComponent<ComponentVitalStats>();
                int healthPercent = Percent(health.Health);
                float? reserve = vital?.Food ?? nutrition?.Food;
                int? foodPercent = reserve is float food ? Percent(food) : null;
                ObserveBody(entity, clock.GameTime, healthPercent, foodPercent);
                actors.Add(new {
                    entity.Id, name = Name(entity), position = Position(body.Position), facing = Position(body.Matrix.Forward),
                    alive = health.Health > 0, health = healthPercent, food = foodPercent,
                    hunger = reserve.HasValue ? CognitiveBody.HungerBand(reserve) : null,
                    stamina = vital?.Stamina, sleep = vital?.Sleep, temperature = vital?.Temperature, wetness = vital?.Wetness,
                    sleeping = entity.FindComponent<ComponentSleep>()?.IsSleeping, inventory = inventory?.Take(96).ToArray(),
                    inventoryCount = inventory?.Length, task = binding?.Tools.CognitiveTask,
                    action = ActionView(binding?.Tools.PanelAction), request = scheduler?.PanelRequestState(entity.Id),
                    cognition = binding?.Cognition?.ReadPanelState()
                });
            }
            foreach (int id in m_previousBody.Keys.ToArray()) {
                if (project.FindEntity(id) == null) m_previousBody.Remove(id);
            }

            long droppedFood = 0;
            int dropStacks = 0;
            if (project.FindSubsystem<SubsystemPickables>() is { } pickables) {
                foreach (Pickable item in pickables.Pickables) {
                    if (item.ToRemove || item.Count <= 0) continue;
                    dropStacks++;
                    if (BlockFor(item.Value)?.GetNutritionalValue(item.Value) > 0) droppedFood += item.Count;
                }
            }
            SaveStamp native = Volatile.Read(ref m_nativeSave);
            DateTimeOffset? savedAt = native?.Directory == info.DirectoryName ? native.At :
                GameManager.WorldInfo is { LastSaveTime: var saved } && saved != default ? new DateTimeOffset(saved) : null;
            return new {
                state = "live", session = m_session, version = ++m_version, sampledAt = DateTimeOffset.UtcNow,
                world = new {
                    name = info.WorldSettings.Name, gameMode = info.WorldSettings.GameMode.ToString(), time = clock.GameTime,
                    elapsed = info.TotalElapsedGameTime, day = day?.Day, timeOfDay = day?.TimeOfDay,
                    precipitation = weather?.PrecipitationIntensity, timeFactor = clock.GameTimeFactor,
                    advancing = canAdvance && clock.GameTimeDelta > 0, savedAt,
                    chunks = project.FindSubsystem<SubsystemTerrain>()?.Terrain?.AllocatedChunks.Length,
                    alive, actorCount, creatures, dropStacks, droppedFood, foodInBags
                },
                actors, model = scheduler?.ReadPanelState(), sequence = m_sequence,
                events = m_events.Snapshot(), speech = m_speech.Snapshot()
            };
        }

        static Item[] ReadInventory(IInventory inventory) {
            if (inventory == null) return null;
            List<Item> items = [];
            for (int slot = 0; slot < inventory.SlotsCount; slot++) {
                int count = inventory.GetSlotCount(slot);
                if (count <= 0) continue;
                int value = inventory.GetSlotValue(slot);
                items.Add(new Item(slot, value, ItemName(value), count, BlockFor(value)?.GetNutritionalValue(value) > 0,
                    inventory.ActiveSlotIndex == slot));
            }
            return items.ToArray();
        }

        static Block BlockFor(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)];
        static string ItemName(int value) => BlockFor(value)?.GetDisplayName(null, value) ?? $"#{Terrain.ExtractContents(value)}";
        static string Name(Entity actor) => actor.FindComponent<ComponentPlayer>()?.PlayerData?.Name
            ?? actor.FindComponent<ComponentCreature>()?.DisplayName ?? $"#{actor.Id}";
        static object Position(Vector3 point) => new { point.X, point.Y, point.Z };
        static int Percent(float value) => (int)MathF.Round(value * 100f);

        static object ActionView(AtomicAction action) => action switch {
            null => null,
            MoveTo move => new { name = nameof(MoveTo), target = Position(move.Destination) },
            Dig { Target: not null } dig => new { name = nameof(Dig), cell = new { dig.Target.Cell.X, dig.Target.Cell.Y, dig.Target.Cell.Z },
                value = dig.Target.ExpectedValue },
            PickUp { Target: not null } pick => new { name = nameof(PickUp), target = Position(pick.Target.Position), pick.Count, item = ItemName(pick.Target.Value) },
            Eat eat => new { name = nameof(Eat), eat.Slot, eat.Count, item = ItemName(eat.ExpectedValue) },
            _ => new { name = action.GetType().Name }
        };

        static void ObserveBody(Entity actor, double time, int health, int? food) {
            if (m_previousBody.TryGetValue(actor.Id, out (int Health, int? Food) previous)
                && (previous.Health != health || previous.Food != food)) {
                Add(actor, "body", time, () => new { health, food, beforeHealth = previous.Health, beforeFood = previous.Food });
            }
            m_previousBody[actor.Id] = (health, food);
        }

        internal static void RecordAction(Entity actor, AtomicActionRun run) {
            if (m_project == null || actor.Project != m_project) return;
            Add(actor, "action", GameTime(actor), () => new {
                action = ActionView(run.Action), run.Result.Status, run.Result.Failure, run.Result.AffectedCount, run.Result.OfferId,
                taskId = m_bindings.TryGetValue(actor, out Binding binding) ? binding.Tools.CognitiveActiveTaskId : null,
                items = run.Result.ReceivedItems?.Take(16).Select(item => new { item.Slot, item.Count, item.Value, name = ItemName(item.Value) }).ToArray()
            });
        }

        internal static void RecordSpeech(Entity actor, Guid speechId, int chunk, SpeechMode mode, string text,
            IEnumerable<Entity> listeners, string status) {
            if (m_project == null || actor.Project != m_project) return;
            Add(actor, "speech", GameTime(actor), () => new {
                speechId, chunk, mode, text, status,
                listeners = listeners.OrderBy(listener => listener.Id).Select(listener => new { listener.Id, name = Name(listener) }).ToArray()
            }, m_speech);
        }

        internal static void RecordTransfer(Entity actor, Entity recipient, Guid offerId, string status, int value, int count) {
            if (m_project == null || actor.Project != m_project) return;
            Add(actor, "transfer", GameTime(actor), () => new {
                recipient = new { recipient.Id, name = Name(recipient) }, offerId, status, item = ItemName(value), value, count
            });
        }

        static double GameTime(Entity actor) => actor.Project.FindSubsystem<SubsystemTime>(true).GameTime;

        static void Add(Entity actor, string kind, double time, Func<object> payload, EventWindow window = null) {
            long sequence = ++m_sequence;
            try {
                JsonElement entry = JsonSerializer.SerializeToElement(new { sequence, kind, time, actorId = actor.Id,
                    actorName = Name(actor), data = payload() }, m_json);
                (window ?? m_events).Add(entry);
            }
            catch (Exception error) {
                // A missing observation is visible as a sequence gap and must not affect action execution.
                Log.Warning($"World panel skipped an event: {error.GetType().Name}");
            }
        }
    }
}
#endif
