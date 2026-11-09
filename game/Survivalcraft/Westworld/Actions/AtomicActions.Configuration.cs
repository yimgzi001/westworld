using System.Text.Json;
using System.Xml;
using Engine;

namespace Game.Westworld {
    public sealed partial class AtomicActions {
        FurnitureDesign GetFurnitureDesign(int value) => FurnitureBehavior is { } furniture
            ? FurnitureBlock.GetDesign(furniture, value)
            : null;

        AtomicActionRun RenameFurnitureDesign(AtomicAction action, int value, JsonElement settings) {
            if (!OnlySetting(settings, "name", out JsonElement field) || field.ValueKind != JsonValueKind.String) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            string name;
            try {
                name = XmlConvert.VerifyXmlChars(field.GetString());
            }
            catch (Exception error) when (error is InvalidOperationException or XmlException) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (name.Length > 20 || name.Contains('\r') || name.Contains('\n')
                || (name.Length > 0 && (name[0] == ' ' || name[^1] == ' '))) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            FurnitureDesign design = GetFurnitureDesign(value);
            if (design == null) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (design.Name == name) {
                return Succeed(action);
            }
            // The furniture editor renames the design shared by every reference to this index.
            design.Name = name;
            return Succeed(action, 1);
        }

        static AtomicActionFailure GetConfiguredBlockValue(Block block, int value, JsonElement settings, out int configured) {
            configured = value;
            int data = Terrain.ExtractData(value);
            switch (block) {
                case PistonBlock:
                    if (!ReadPistonSettings(settings, ref data)) {
                        return AtomicActionFailure.InvalidArgument;
                    }
                    break;
                case AdjustableDelayGateBlock:
                    if (!OnlySetting(settings, "delaySteps", out JsonElement delayField) || !ReadIntInRange(delayField, 0, 255, out int delaySteps)) {
                        return AtomicActionFailure.InvalidArgument;
                    }
                    data = AdjustableDelayGateBlock.SetDelay(data, delaySteps);
                    break;
                case BatteryBlock or ButtonBlock or SwitchBlock:
                    if (!OnlySetting(settings, "voltage", out JsonElement voltageField) || !ReadIntInRange(voltageField, 0, 15, out int voltage)) {
                        return AtomicActionFailure.InvalidArgument;
                    }
                    data = block switch {
                        BatteryBlock => BatteryBlock.SetVoltageLevel(data, voltage),
                        ButtonBlock => ButtonBlock.SetVoltageLevel(data, voltage),
                        _ => SwitchBlock.SetVoltageLevel(data, voltage)
                    };
                    break;
                default:
                    return AtomicActionFailure.PreconditionsNotMet;
            }
            configured = Terrain.ReplaceData(value, data);
            return AtomicActionFailure.None;
        }

        static bool ReadPistonSettings(JsonElement settings, ref int data) {
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonProperty property in settings.EnumerateObject()) {
                if (!seen.Add(property.Name)) {
                    return false;
                }
                switch (property.Name) {
                    case "maxExtension" when ReadIntInRange(property.Value, 1, SubsystemPistonBlockBehavior.PistonMaxExtension, out int extension):
                        data = PistonBlock.SetMaxExtension(data, extension - 1);
                        break;
                    case "pullCount" when PistonBlock.GetMode(data) is PistonMode.Pulling or PistonMode.StrictPulling
                        && ReadIntInRange(property.Value, 1, SubsystemPistonBlockBehavior.PistonMaxMovedBlocks, out int pullCount):
                        data = PistonBlock.SetPullCount(data, pullCount - 1);
                        break;
                    case "speed" when ReadIntInRange(property.Value, 0, SubsystemPistonBlockBehavior.PistonMaxSpeedSetting, out int speed):
                        data = PistonBlock.SetSpeed(data, speed);
                        break;
                    default:
                        return false;
                }
            }
            return seen.Count > 0;
        }

        static bool ReadIntInRange(JsonElement field, int min, int max, out int value) {
            value = 0;
            return field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out value) && value >= min && value <= max;
        }

        AtomicActionRun ConfigureCircuit(ConfigureBlock action, int value, Block block, Point3 cell) {
            if (block is TruthTableCircuitBlock table) {
                return ReadTruthTable(action.Settings) is TruthTableData data
                    ? SetCircuitBlockData(action, cell, table.GetFace(value), GetEditableBehavior<TruthTableData>(value), data)
                    : Reject(action, AtomicActionFailure.InvalidArgument);
            }
            return ReadMemoryBank(action.Settings) is MemoryBankData memory
                ? SetCircuitBlockData(action, cell, ((MemoryBankBlock)block).GetFace(value), GetEditableBehavior<MemoryBankData>(value), memory)
                : Reject(action, AtomicActionFailure.InvalidArgument);
        }

        AtomicActionRun ConfigureCircuitItem(ConfigureItem action, int value, Block block) {
            if (block is TruthTableCircuitBlock) {
                return ReadTruthTable(action.Settings) is TruthTableData data
                    ? SetCircuitItemData(action, value, GetEditableBehavior<TruthTableData>(value), data)
                    : Reject(action, AtomicActionFailure.InvalidArgument);
            }
            return ReadMemoryBank(action.Settings) is MemoryBankData memory
                ? SetCircuitItemData(action, value, GetEditableBehavior<MemoryBankData>(value), memory)
                : Reject(action, AtomicActionFailure.InvalidArgument);
        }

        SubsystemEditableItemBehavior<T> GetEditableBehavior<T>(int value) where T : IEditableItemData, new() =>
            m_miner.m_subsystemBlockBehaviors.GetBlockBehaviors(Terrain.ExtractContents(value))
                .OfType<SubsystemEditableItemBehavior<T>>().FirstOrDefault();

        static TruthTableData ReadTruthTable(JsonElement settings) {
            if (!OnlySetting(settings, "outputs", out JsonElement field) || field.ValueKind != JsonValueKind.String
                || field.GetString() is not string text || text.Length > 16 || text.Any(c => c is not ('0' or '1'))) {
                return null;
            }
            TruthTableData data = new();
            data.LoadBinaryString(text);
            return data;
        }

        static MemoryBankData ReadMemoryBank(JsonElement settings) {
            if (!OnlySetting(settings, "data", out JsonElement field) || field.ValueKind != JsonValueKind.String
                || field.GetString() is not string text || text.Length > 256 || text.Any(c => !Uri.IsHexDigit(c))) {
                return null;
            }
            MemoryBankData data = new();
            data.LoadString(text);
            return data;
        }

        static bool CircuitDataChanged<T>(T current, T configured) where T : IEditableItemData, new() {
            if (configured is MemoryBankData memory && current is MemoryBankData previous) {
                memory.LastOutput = previous.LastOutput;
            }
            return (current ?? new T()).SaveString() != configured.SaveString();
        }

        AtomicActionRun SetCircuitBlockData<T>(ConfigureBlock action, Point3 cell, int face, SubsystemEditableItemBehavior<T> behavior, T data)
            where T : IEditableItemData, new() {
            if (behavior == null) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (!CircuitDataChanged(behavior.GetBlockData(cell), data)) {
                return Succeed(action);
            }
            behavior.SetBlockData(cell, data);
            QueueConfiguredCircuit(cell, face);
            return Succeed(action, 1);
        }

        AtomicActionRun SetCircuitItemData<T>(ConfigureItem action, int value, SubsystemEditableItemBehavior<T> behavior, T data)
            where T : IEditableItemData, new() {
            if (behavior == null) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (!CircuitDataChanged(behavior.GetItemData(Terrain.ExtractData(value)), data)) {
                return Succeed(action);
            }
            if (behavior.FindFreeItemId() == 0) {
                return Reject(action, AtomicActionFailure.InventoryFull);
            }
            int id = behavior.StoreItemDataAtUniqueId(data);
            int configured = Terrain.ReplaceData(value, id);
            try {
                return ReplaceConfiguredItem(action, configured, false);
            }
            finally {
                if (HeldInventory.GetSlotValue(action.Item.Slot) != configured) {
                    behavior.m_itemsData.Remove(id);
                }
            }
        }

        void QueueConfiguredCircuit(Point3 cell, int face, bool connections = false) {
            SubsystemElectricity electricity = m_actor.Project.FindSubsystem<SubsystemElectricity>();
            ElectricElement element = electricity?.GetElectricElement(cell.X, cell.Y, cell.Z, face);
            if (element != null) {
                if (connections) {
                    electricity.QueueElectricElementConnectionsForSimulation(element, electricity.CircuitStep + 1);
                }
                else {
                    electricity.QueueElectricElementForSimulation(element, electricity.CircuitStep + 1);
                }
            }
        }

        AtomicActionRun WriteSign(ConfigureBlock action, Point3 cell) {
            if (m_miner.m_subsystemGameInfo.WorldSettings.GameMode == GameMode.Adventure) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            SubsystemSignBlockBehavior signs = m_actor.Project.FindSubsystem<SubsystemSignBlockBehavior>(true);
            SignData current = signs.GetSignData(cell);
            string[] lines = current?.Lines ?? [string.Empty, string.Empty, string.Empty, string.Empty];
            Color[] colors = current?.Colors ?? [Color.Black, Color.Black, Color.Black, Color.Black];
            string url = current?.Url ?? string.Empty;
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonProperty property in action.Settings.EnumerateObject()) {
                if (!seen.Add(property.Name)) {
                    return Reject(action, AtomicActionFailure.InvalidArgument);
                }
                switch (property.Name) {
                    case "lines" when ReadSignLines(property.Value) is string[] newLines:
                        lines = newLines;
                        break;
                    case "colors" when ReadSignColors(property.Value) is Color[] newColors:
                        colors = newColors;
                        break;
                    case "url" when property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is string newUrl
                        && ValidSignUrl(newUrl):
                        url = newUrl;
                        break;
                    default:
                        return Reject(action, AtomicActionFailure.InvalidArgument);
                }
            }
            if (seen.Count == 0) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (current != null && current.Lines.SequenceEqual(lines) && current.Colors.SequenceEqual(colors) && current.Url == url) {
                return Succeed(action);
            }
            signs.SetSignData(cell, lines, colors, url);
            return Succeed(action, 1);
        }

        static string[] ReadSignLines(JsonElement field) {
            if (field.ValueKind != JsonValueKind.Array || field.GetArrayLength() > 4) {
                return null;
            }
            string[] lines = [string.Empty, string.Empty, string.Empty, string.Empty];
            int index = 0;
            foreach (JsonElement line in field.EnumerateArray()) {
                if (line.ValueKind != JsonValueKind.String || line.GetString() is not string text || text.Length > 16
                    || text.Contains('\r') || text.Contains('\n')) {
                    return null;
                }
                lines[index++] = text;
            }
            return lines;
        }

        static Color[] ReadSignColors(JsonElement field) {
            if (field.ValueKind != JsonValueKind.Array || field.GetArrayLength() != 4) {
                return null;
            }
            Color[] palette = EditSignDialog.DefaultLineColors;
            Color[] colors = new Color[4];
            int index = 0;
            foreach (JsonElement color in field.EnumerateArray()) {
                if (!ReadIntInRange(color, 0, palette.Length - 1, out int paletteIndex)) {
                    return null;
                }
                colors[index++] = palette[paletteIndex];
            }
            return colors;
        }

        static bool ValidSignUrl(string url) => url.Length == 0 || (url.Length <= 256 && Uri.TryCreate(url, UriKind.Absolute, out Uri parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps) && parsed.IsWellFormedOriginalString());
    }
}
