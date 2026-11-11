using System.Globalization;
using System.Text.Json;
using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    public sealed partial class ActorTools {
        const string CarriedStorage = "carried";
        const string PersonalStorage = "personal";
        const string LogPrefix = "wood:";

        static readonly ToolSpec m_personalCraftTool = Action<Craft>(
            "Craft one native batch in your personal 2x2 grid. Prefer prepare=true as the only task step to automatically arrange carried ingredients and craft. The recipe determines the output quantity.",
            (actor, args) => actor.PersonalCraft(args), Choice("workspace", "Use the personal grid.", false, PersonalStorage),
            Int("result_value", "Exact resultValue from observation.production.recipes or readyCraft."),
            Int("count", "Optional. Omit this field to craft one batch.", true),
            new Field("prepare", "boolean", "When true, use this as the only task step with zero retries and no branching. The game arranges carried ingredients and crafts the selected recipe.", true));

        internal bool HasProduction => m_actions.BoundActor.ValuesDictionary.DatabaseObject.Name == FoundingWorld.ProducerTemplate;

        bool IsProductionTool(string tool) => HasProduction
            && tool is "act_SelectItem" or "act_Dig" or "act_TransferItems" or "act_Craft" or "act_OfferItem" or "act_RespondToOffer";

        static string PersonReference(Entity entity) => entity == null ? null : FormattableString.Invariant($"person:{entity.Id}");

        static bool TryPersonId(string reference, out int id) {
            id = 0;
            return reference?.StartsWith("person:", StringComparison.Ordinal) == true
                && int.TryParse(reference.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
        }

        object ProductionObservation(ActorObservation observation, SelfView self) {
            var logs = m_actions.Queries.FindVisibleLogs().Select(log => {
                var direction = RelativeDirection(new Vector3(log.Target.Cell) + new Vector3(.5f), self);
                return new {
                    Target = LogReference(log.Target), Bearing = direction.Bearing, HorizontalDistance = direction.Distance,
                    log.InReach
                };
            }).ToArray();
            var materials = observation.Pickables.Where(item => IsWood(item.Value)).Select(item => {
                var direction = RelativeDirection(item.Position, self);
                return new { item.Value, item.Count, item.InReach, Bearing = direction.Bearing, HorizontalDistance = direction.Distance };
            }).ToArray();
            var recipes = ProductionRecipes().Select(recipe => new {
                recipe.ResultValue, recipe.ResultCount,
                Name = GameKnowledge.ReadItem(recipe.ResultValue).Type,
                Ingredients = recipe.Ingredients.Select(item => new { Slot = item.Column + 2 * item.Row, item.CraftingId }).ToArray()
            }).ToArray();
            var carried = CognitiveInventory()?.Slots.Where(item => item.Count > 0).Select(item => {
                ItemGuideEntry guide = GameKnowledge.ReadItem(item.Value);
                return new { item.Slot, item.Value, item.Count, Item = guide?.Type, guide?.CraftingId };
            }).ToArray();
            StorageView workspace = m_actions.Queries.ReadStorage(m_actions.Queries.OwnCrafting);
            CraftingPreview ready = workspace?.Crafting;
            return new {
                Logs = logs, Materials = materials, Recipes = recipes, Carried = carried,
                PersonalGrid = workspace?.Inventory.Slots.Where(item => item.Slot < 4 && item.Count > 0).ToArray(),
                ReadyCraft = ready is { Recipe: { } recipe, MaterialBatches: > 0 } && IsKnownProductionRecipe(recipe.ResultValue)
                    ? new { recipe.ResultValue, recipe.ResultCount } : null,
                OutgoingOffers = m_actions.ReadOutgoingItemOffers().Select(offer => new {
                    OfferReference = offer.Id.ToString("N"), Recipient = PersonReference(offer.Recipient),
                    ItemValue = offer.Item.ExpectedValue, offer.Count,
                    SecondsRemaining = Math.Max(0, offer.ExpiresAt - m_actions.Queries.GameTime)
                }).ToArray(),
                Hints = "Select the stone axe before digging a visible log. If no log is visible, look in another direction or explore a short distance. "
                    + "For dropped wood beyond reach, you can plan a move followed by a pickup. Use its observed bearing and horizontal distance, clamped to 0.2 to 4 blocks, with an arrival radius of 0.15. "
                    + "To make a known recipe from materials you already own, submit act_Craft with workspace=personal, the recipe's resultValue, and prepare=true as the only task step. "
                    + "The game arranges ingredients and crafts, preserving each completed step across saves. If arranging slots manually, transfer from carried to personal, then craft without prepare. "
                    + "Omit count. One log yields four planks, exactly enough for one workbench. "
                    + "For the workbench goal, collect a ready batch of planks or a ready workbench before looking for more wood. "
                    + "You can record your own agreed role or promised delivery in commitment, citing the received message ID as source. "
                    + "Consider your commitments when choosing the next task; resolve a commitment when fulfilled or revised. "
                    + "To approach a visible person for a handoff, use their observed bearing and horizontalDistance, stopping within two blocks. "
                    + "Your outgoingOffers list contains your own pending proposals. Consider them before using the offered materials for another task. "
                    + "You can wait for the recipient's independent response. An identical live offer retry reuses the same offer."
            };
        }

        static (float Bearing, float Distance) RelativeDirection(Vector3 point, SelfView self) {
            Vector2 forward = self.Facing.XZ;
            Vector2 right = new(-forward.Y, forward.X);
            Vector2 offset = (point - self.Position).XZ;
            return (MathUtils.RadToDeg(MathF.Atan2(Vector2.Dot(right, offset), Vector2.Dot(forward, offset))), offset.Length());
        }

        static IEnumerable<RecipeGuideEntry> ProductionRecipes() =>
            new[] { PlanksBlock.Index, StickBlock.Index, CraftingTableBlock.Index }.SelectMany(value => GameKnowledge.FindRecipes(value))
                .Where(recipe => recipe.Station == RecipeStation.PersonalOrWorkbench && recipe.RequiredLevel <= 1f
                    && recipe.RemainsCount == 0 && recipe.Ingredients.All(item => item.CraftingId is "wood" or "planks"));

        static bool IsWood(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)] is WoodBlock;

        static string LogReference(BlockTarget block) =>
            FormattableString.Invariant($"{LogPrefix}{block.Cell.X}:{block.Cell.Y}:{block.Cell.Z}:{block.ExpectedValue}");

        Dig DigCommand(Args args) {
            string reference = args.String("target");
            if (HasProduction && reference?.StartsWith(LogPrefix, StringComparison.Ordinal) == true) {
                string[] parts = reference.Split(':');
                if (parts.Length != 5 || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out int x)
                    || !int.TryParse(parts[2], CultureInfo.InvariantCulture, out int y)
                    || !int.TryParse(parts[3], CultureInfo.InvariantCulture, out int z)
                    || !int.TryParse(parts[4], CultureInfo.InvariantCulture, out int value) || !IsWood(value)) {
                    throw new InvalidToolCall("invalid_arguments");
                }
                return new Dig(new BlockTarget(new Point3(x, y, z), value), args.Number("timeout", 15f));
            }
            return new Dig(BlockReference(reference), args.Number("timeout", 15f));
        }

        internal bool ProductionTargetsWereObserved(JsonElement steps, JsonElement observation, CognitiveMessage[] messages,
            ActorCognition.RecentProcess[] recent) {
            if (!HasProduction) return true;
            if (!observation.TryGetProperty("production", out JsonElement production)) return false;
            HashSet<string> logs = production.GetProperty("logs").EnumerateArray()
                .Select(item => item.GetProperty("target").GetString()).ToHashSet(StringComparer.Ordinal);
            HashSet<string> people = observation.GetProperty("entities").EnumerateArray()
                .Select(item => item.GetProperty("reference").GetString()).ToHashSet(StringComparer.Ordinal);
            people.UnionWith(messages.Select(message => message.Value.Source));
            HashSet<string> offers = messages.Where(message => message.Value.Kind == AtomicSocialEventKind.ItemOffered)
                .Select(message => message.Value.OfferReference).ToHashSet(StringComparer.Ordinal);
            foreach (ActorCognition.RecentProcess entry in recent ?? []) {
                if (entry.Kind != "received" || !entry.Details.TryGetProperty("value", out JsonElement received)) continue;
                if (received.TryGetProperty("source", out JsonElement person) && person.ValueKind == JsonValueKind.String) people.Add(person.GetString());
                if (received.TryGetProperty("kind", out JsonElement kind) && kind.GetString() == "itemOffered"
                    && received.TryGetProperty("offerReference", out JsonElement offered) && offered.ValueKind == JsonValueKind.String) offers.Add(offered.GetString());
            }
            foreach (JsonElement step in steps.EnumerateArray()) {
                if (step.ValueKind != JsonValueKind.Object || !step.TryGetProperty("tool", out JsonElement tool)
                    || tool.ValueKind != JsonValueKind.String) return false;
                string name = tool.GetString();
                if (name is not ("act_Dig" or "act_OfferItem" or "act_RespondToOffer")) continue;
                string field = name == "act_RespondToOffer" ? "offer" : "target";
                if (!step.TryGetProperty("arguments", out JsonElement args) || args.ValueKind != JsonValueKind.Object
                    || !args.TryGetProperty(field, out JsonElement target) || target.ValueKind != JsonValueKind.String) return false;
                string reference = target.GetString();
                if (name == "act_Dig" && !logs.Contains(reference)
                    || name == "act_OfferItem" && !people.Contains(reference)
                    || name == "act_RespondToOffer" && !offers.Contains(reference)) return false;
            }
            return true;
        }

        static string StringArgument(ActorPlanStep step, string name) =>
            step.Arguments.TryGetProperty(name, out JsonElement argument) && argument.ValueKind == JsonValueKind.String ? argument.GetString() : null;

        static bool IsPersonalTransfer(ActorPlanStep step) =>
            (StringArgument(step, "source"), StringArgument(step, "destination")) is (CarriedStorage, PersonalStorage) or (PersonalStorage, CarriedStorage);

        static bool IsPersonalCraft(ActorPlanStep step) => StringArgument(step, "workspace") == PersonalStorage
            && step.Arguments.TryGetProperty("result_value", out JsonElement value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int result) && IsKnownProductionRecipe(result);

        static bool IsKnownProductionRecipe(int value) => ProductionRecipes().Any(recipe => recipe.ResultValue == value);

        ActorTaskStep[] PrepareProductionCraft(IReadOnlyList<ActorPlanStep> plan) {
            ActorPlanStep request = plan[0];
            if (!HasProduction || plan.Count != 1 || request.Tool != "act_Craft" || request.Retries != 0
                || request.Next != null || request.Fallback != null || request.ItemFromStep != null) {
                throw new InvalidToolCall("craft_requires_separate_task");
            }
            Args args = new(request.Arguments, m_personalCraftTool.Fields);
            if (args.String("workspace") != PersonalStorage) {
                throw new InvalidToolCall("invalid_arguments");
            }
            Craft craft = PersonalCraft(args);
            if (request.MinimumAffected < 0 || request.MinimumAffected > craft.Count) {
                throw new InvalidToolCall("invalid_arguments");
            }
            RecipeGuideEntry recipe = ProductionRecipes().First(candidate => candidate.ResultValue == craft.ExpectedResultValue);
            IReadOnlyList<ActorTaskStep> composed = RecipeTaskComposer.Compose(m_actions, m_actions.Queries.OwnInventory,
                m_actions.Queries.OwnCrafting as ComponentCraftingTable, recipe, craft.Count);
            if (composed == null || composed.Count > 12) {
                throw new InvalidToolCall("plan_unavailable");
            }
            // Save primitive steps and their cursor so a reload never recomposes already settled transfers.
            return composed.Select((step, index) => {
                string tool;
                JsonElement arguments;
                if (index == composed.Count - 1) {
                    tool = "act_Craft";
                    arguments = ToJson(new { workspace = PersonalStorage, result_value = craft.ExpectedResultValue });
                }
                else {
                    TransferItems transfer = (TransferItems)step.CreateAction();
                    tool = "act_TransferItems";
                    arguments = ToJson(new {
                        source = CarriedStorage, source_slot = transfer.Source.Slot, source_value = transfer.Source.ExpectedValue,
                        destination = PersonalStorage, destination_slot = transfer.Destination.Slot,
                        destination_value = transfer.Destination.ExpectedValue, count = transfer.Count
                    });
                }
                return step with { Plan = new ActorPlanStep(tool, arguments, 0, step.MinimumAffectedCount, null, null, null) };
            }).ToArray();
        }

        Craft PersonalCraft(Args args) {
            int value = args.Int("result_value");
            RecipeGuideEntry recipe = ProductionRecipes().FirstOrDefault(candidate => candidate.ResultValue == value);
            if (recipe == null || (args.OptionalInt("count") is int requested && requested != 1 && requested != recipe.ResultCount)) {
                throw new InvalidToolCall("invalid_arguments");
            }
            return new Craft(m_actions.Queries.OwnCrafting, value, recipe.ResultCount);
        }
    }
}
