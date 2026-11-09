using System.Text.Json;
using Engine;

namespace Game.Westworld {
    public sealed partial class ActorTools {
        static Field Target(bool optional = false) => Ref("target", "A currently observed target reference.", optional);
        static Field Slot() => Int("slot", "Own inventory slot number.");
        static Field Value() => Int("value", "Expected value from the latest slot snapshot. Zero represents an empty slot.");
        static Field Count() => Int("count", "Requested number of items.");
        static Field Settings() => Object("settings", "Native settings for this item or block. Read current settings before editing.");
        static Field Timeout(float seconds) => Number("timeout", $"Timeout in game seconds. Default {seconds}.", true);
        static Field Choice(string name, string description, bool optional, params string[] choices) =>
            new(name, "string", description, optional, choices);

        static readonly Field[] FurnitureFields = [
            Text("name", "New design name.", true),
            Int("resolution", "Furniture design resolution.", true),
            Int("mirror_axis", "Optional mirror axis from 0 to 2.", true),
            Int("rotation_axis", "Rotation axis from 0 to 2.", true),
            Int("quarter_turns", "Rotation in quarter turns.", true),
            Object("shift", "Integer x, y and z offsets within the furniture design.", true)
        ];

        static readonly Field[] ShiftFields = [
            Int("x", "Design x offset."), Int("y", "Design y offset."), Int("z", "Design z offset.")
        ];

        static readonly ToolSpec[] ActionTools = [
            Action<MoveTo>("Move to an observed or remembered location, or explore with bearing and distance.", (a, p) => a.MoveCommand(p),
                Ref("destination", "An observed or remembered location reference.", true),
                Number("bearing", "Relative bearing from -180 to 180 degrees. Positive turns right.", true),
                Number("distance", "Exploration distance between 1 and 4 blocks.", true), Number("range", "Arrival radius.", true), Timeout(15)),
            Action<FaceTowards>("Aim at a currently observed location, or turn by relative bearing with optional head pitch.", (a, p) => a.TurnCommand(p),
                Ref("destination", "A currently observed location reference.", true),
                Number("bearing", "Relative bearing from -180 to 180 degrees. Positive turns right.", true),
                Number("pitch", "Optional absolute head pitch in degrees from -82 to 82; negative looks down. Only with bearing. Without pitch, bearing keeps existing head pitch.", true), Timeout(5)),
            Action<SetPosture>("Stand or crouch.", (_, p) => new SetPosture(Enum.Parse<BodyPosture>(p.String("posture"), true)),
                Choice("posture", "Desired posture.", false, "standing", "crouching")),
            Action<Jump>("Jump once using native locomotion.", (_, _) => new Jump()),
            Action<ClimbTo>("Climb the observed ladder to an absolute feet height.", (a, p) =>
                new ClimbTo(a.BlockReference(p.String("target")), p.Number("height"), p.Number("timeout", 15f)),
                Target(), Number("height", "Feet height in world coordinates."), Timeout(15)),
            Action<Mount>("Board a visible boat or ride a visible mount.", (a, p) => new Mount(a.Person(p.String("target"))), Target()),
            Action<Dismount>("Get off the current mount.", (_, _) => new Dismount()),
            Action<Steer>("Steer the current mount or boat.", (_, p) =>
                new Steer(p.Number("turn"), p.Number("throttle"), p.Number("duration")),
                Number("turn", "Turn from -1 to 1."), Number("throttle", "Throttle from -1 to 1."),
                Number("duration", "Duration in game seconds.")),

            Action<Wait>("Wait for game time to pass.", (_, p) => new Wait(p.Number("duration")), Number("duration", "Game seconds.")),
            Action<Eat>("Consume food from an owned inventory slot.", (_, p) => new Eat(p.Int("slot"), p.Int("value"), p.Int("count")),
                Slot(), Value(), Count()),
            Action<Sleep>("Try to fall asleep at the current place.", (_, _) => new Sleep()),
            Action<WakeUp>("Wake up.", (_, _) => new WakeUp()),

            Action<PickUp>("Pick up a visible dropped stack or food within reach.", (a, p) => a.Pickup(p),
                Ref("target", "Reference to a visible dropped stack.", true),
                Int("food_value", "Exact value of nearby edible food, used in place of target.", true), Count()),
            Action<Drop>("Drop items from an owned slot.", (_, p) => new Drop(p.OwnItem(), p.Int("count")), Slot(), Value(), Count()),
            Action<SelectItem>("Select a held inventory slot.", (_, p) => new SelectItem(p.Int("slot"), p.Int("value")), Slot(), Value()),
            Action<TransferItems>("Move items between accessible inventory slots.", (a, p) => new TransferItems(
                    new InventorySlot(a.Storage(p.String("source")), p.Int("source_slot"), p.Int("source_value")),
                    new InventorySlot(a.Storage(p.String("destination")), p.Int("destination_slot"), p.Int("destination_value")), p.Int("count")),
                Ref("source", "Observed or own source inventory reference."),
                Int("source_slot", "Source slot."), Int("source_value", "Expected source value."),
                Ref("destination", "Observed or own destination inventory reference."),
                Int("destination_slot", "Destination slot."), Int("destination_value", "Expected destination value, or zero if empty."), Count()),
            Action<Equip>("Wear an item from an owned slot.", (_, p) => new Equip(p.OwnItem()), Slot(), Value()),
            Action<Unequip>("Remove an equipped item.", (_, p) =>
                new Unequip(ClothingSlot.ClothingSlots.GetValueOrDefault(p.String("equipment_slot"))
                    ?? throw new InvalidToolCall("invalid_arguments"), p.Int("value")),
                Text("equipment_slot", "Named equipment slot from read_equipment."), Value()),

            Action<Dig>("Dig the observed block.", (a, p) =>
                new Dig(a.BlockReference(p.String("target")), p.Number("timeout", 15f)), Target(), Timeout(15)),
            Action<Place>("Place the held block against an observed face.", (a, p) =>
                new Place(a.BlockReference(p.String("target")), p.OwnItem()), Target(), Slot(), Value()),
            Action<Interact>("Interact with an observed world target.", (a, p) => new Interact(a.WorldTarget(p.String("target"))), Target()),
            Action<UseItem>("Use a held tool on an observed target, or use it in the current facing direction.", (a, p) =>
                new UseItem(p.OwnItem(), p.String("target") is string target ? a.WorldTarget(target) : null), Slot(), Value(), Target(true)),
            Action<ConfigureBlock>("Edit a reachable block, sign, circuit or shared furniture design.", (a, p) =>
                new ConfigureBlock(a.BlockReference(p.String("target")), p.Json("settings").Clone()), Target(), Settings()),
            Action<ConfigureItem>("Edit an owned item, load a weapon or rename its shared furniture design.", (_, p) =>
                new ConfigureItem(p.OwnItem(), p.Json("settings").Clone()), Slot(), Value(), Settings()),

            Action<MeleeAttack>("Attempt a melee attack against a visible entity.", (a, p) =>
                new MeleeAttack(a.Person(p.String("target"))), Target()),
            Action<UseAimedItem>("Aim and release a held projectile item at an observed target.", (a, p) =>
                new UseAimedItem(p.OwnItem(), a.WorldTarget(p.String("target")), p.Number("duration")),
                Slot(), Value(), Target(), Number("duration", "Aim duration in game seconds.")),

            Action<Craft>("Collect the actual result from an accessible crafting grid.", (a, p) =>
                new Craft(a.Storage(p.String("workspace")), p.Int("result_value"), p.Int("count")),
                Ref("workspace", "Own crafting grid or observed workbench reference."), Int("result_value", "Expected crafted item."), Count()),
            Action<BuildFurniture>("Turn a visible connected set of materials into furniture using a hammer.", (a, p) =>
                new BuildFurniture(a.BlockReference(p.String("target")), p.OwnItem(), ReadFurnitureOptions(p.Json("options"))),
                Target(), Slot(), Value(),
                Object("options", "Optional name, resolution, mirror_axis, rotation_axis, quarter_turns and shift {x,y,z}.", true)),

            Action<Speak>("Speak so nearby listeners can actually hear.", (a, p) =>
                new Speak(p.String("text"), Enum.Parse<SpeechMode>(p.String("mode") ?? "normal", true),
                    p.String("target") is string target ? a.Person(target) : null),
                Text("text", "Words to speak."), Choice("mode", "Speech volume.", true, "whisper", "normal", "shout"), Target(true)),
            Action<OfferItem>("Offer an owned item to a visible nearby person.", (a, p) =>
                new OfferItem(a.Person(p.String("target")), p.OwnItem(), p.Int("count")), Target(), Slot(), Value(), Count()),
            Action<RespondToOffer>("Accept or decline a received handoff offer.", (a, p) =>
                new RespondToOffer(a.ResolveOffer(p.String("offer")), p.Bool("accept")),
                Ref("offer", "Offer reference received from read_social."), Bool("accept", "True accepts the offer."))
        ];

        static FurnitureBuildOptions ReadFurnitureOptions(JsonElement value) {
            if (value.ValueKind == JsonValueKind.Undefined) {
                return null;
            }
            Args fields = new(value, FurnitureFields);
            Point3 shift = default;
            JsonElement offset = fields.Json("shift");
            if (offset.ValueKind != JsonValueKind.Undefined) {
                Args xyz = new(offset, ShiftFields);
                shift = new Point3(xyz.Int("x"), xyz.Int("y"), xyz.Int("z"));
            }
            return new FurnitureBuildOptions(fields.String("name"), fields.OptionalInt("resolution"), fields.OptionalInt("mirror_axis"),
                fields.Int("rotation_axis", 1), fields.Int("quarter_turns"), shift);
        }

        static Field Offset() => Int("offset", "Number of earlier results to skip. Default 0.", true);
        static Field Limit() => Int("limit", "Maximum results between 1 and 100. Default 32.", true);

        static readonly Field[] FallbackFields = [
            Int("step", "Zero-based index of a later step."),
            Text("failure", "Use blocked, timed_out or movement_interrupted for MoveTo; target_changed, out_of_reach, blocked or inventory_full for PickUp; target_changed, insufficient_items or inventory_full for Eat.")
        ];

        static readonly Field[] PlanFields = [
            Text("tool", "An act_ tool name from the current tool list."),
            Object("arguments", "Arguments matching the named action tool."),
            Int("retries", "Maximum retries on safe pre-effect failures, from 0 to 2.", true),
            Int("minimum_affected", "Minimum actual affected count for this step.", true),
            Int("item_from_step", "For Eat, bind the item from an earlier PickUp step and pass only count in arguments.", true),
            Int("next", "On success, jump to a later zero-based step; step count ends the plan.", true),
            new Field("fallback", "object", "On a matching safe failure, jump to the specified later step.", true, Children: FallbackFields)
        ];

        static readonly ToolSpec[] QueryTools = [
            Query("observe", "Read one current snapshot: self, visible terrain blocks, forward focus, visible objects, inventory summary and coverage.", (a, p) =>
                a.Observe(p.Number("look_distance", 12f), p.Number("object_distance", 8f), p.Int("limit", 24), p.Int("block_limit", 32)),
                Number("look_distance", "Forward ray length up to 16 blocks.", true),
                Number("object_distance", "Visible object radius up to 16 blocks.", true), Int("limit", "Object limit per group. Default 24.", true), Int("block_limit", "Visible non-air block output limit 1..128, default 32; terrain scan radius is 5. Omission is reported.", true)),
            Query("read_self", "Read your own body and location.", (a, _) => a.m_actions.Queries.ReadSelf()),
            Query("read_environment", "Read local light, weather and time.", (a, _) => a.m_actions.Queries.ReadEnvironment()),
            Query("read_inventory", "Read your own inventory and selected slot.", (a, _) => a.ReadInventory()),
            Query("read_equipment", "Read your equipped clothing and armor.", (a, _) => a.m_actions.Queries.ReadEquipment()),
            Query("read_storage", "Read an accessible inventory, crafting preview or furnace.", (a, p) => a.ReadStorage(p.String("storage")),
                Ref("storage", "Reference issued by observe.")),
            Query("read_sign", "Read a reachable sign.", (a, p) => a.m_actions.Queries.ReadSign(a.BlockReference(p.String("target"))), Target()),
            Query("read_block_settings", "Read settings for an observed editable block.", (a, p) =>
                a.m_actions.Queries.ReadBlockSettings(a.BlockReference(p.String("target"))), Target()),
            Query("read_item_settings", "Read settings for an owned editable item.", (a, p) => a.m_actions.Queries.ReadItemSettings(p.OwnItem()),
                Slot(), Value()),
            Query("read_map", "Read historical places. With offset/limit returns a live-LRU page, not a stable snapshot; no arguments preserves the legacy list.",
                (a, p) => p.Has("offset") || p.Has("limit") ? (object)a.m_memory.ReadMap(p.Int("offset"), p.Int("limit", 4)) : a.m_memory.ReadMap(),
                Offset(), Limit()),
            Query("read_social", "Read received speech and handoff events. With a cognitive runtime bound, reading leaves its inbox intact.", (a, _) => a.ReadSocial()),
            Query("read_item", "Read the entry for a known item.", (a, p) => a.Knowledge.ReadItem(p.Int("value")), Value()),
            Query("find_items", "Search items you know about.", (a, p) =>
                a.Knowledge.FindItems(p.String("search"), p.Int("offset"), p.Int("limit", 32)), Text("search", "Name or type.", true), Offset(), Limit()),
            Query("find_creatures", "Search creatures you know about.", (a, p) =>
                a.Knowledge.FindCreatures(p.String("search"), p.Int("offset"), p.Int("limit", 32)), Text("search", "Name or type.", true), Offset(), Limit()),
            Query("find_recipes", "Search recipes you know by result or ingredient.", (a, p) =>
                a.Knowledge.FindRecipes(p.OptionalInt("result_value"), p.OptionalInt("ingredient_value"), p.Int("offset"), p.Int("limit", 32)),
                Int("result_value", "Known output value.", true), Int("ingredient_value", "Known input value.", true), Offset(), Limit()),
            Query("action_status", "Check the last direct action.", (a, _) => a.ReadDirect()),
            Query("cancel_action", "Cancel the running direct action.", (a, _) => a.m_direct != null && a.m_actions.Cancel(a.m_direct)),
            Query("task_status", "Read progress and actual results of the current or last task.", (a, _) => a.m_tasks.Status),
            Query("cancel_task", "Cancel the current task without undoing completed effects.", (a, _) => a.m_tasks.Cancel()),
            Query("submit_plan", "Schedule up to twelve declared action steps.", (a, p) => a.SubmitPlan(p.Json("steps")),
                new Field("steps", "array", "One to twelve steps with zero-based indices.", Children: PlanFields)),
            Query("plan_craft", "Schedule inventory transfers and crafting for a known recipe.", (a, p) =>
                a.PlanCraft(p.String("workspace"), p.String("recipe"), p.Int("count")),
                Ref("workspace", "Own crafting grid or observed workbench reference."),
                Ref("recipe", "Recipe reference issued by find_recipes."), Count())
        ];
    }
}
