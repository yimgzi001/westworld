using System.Collections.ObjectModel;
using Engine;

namespace Game.Westworld {
    public sealed record EditableSettingsView(string Type, IReadOnlyDictionary<string, object> Settings);

    public sealed partial class AtomicActions {
        public sealed partial class ActorQueries {
            public EditableSettingsView ReadBlockSettings(BlockTarget target) {
                if (!m_port.IsActorAvailable || !m_port.CanTargetWorld() || m_port.m_sleep?.IsSleeping == true || target == null
                    || m_port.FindWorldRay(target, RaycastMode.Interaction, m_port.InteractionReach, out _, out object hit)
                        != AtomicActionFailure.None
                    || hit is not TerrainRaycastResult block) {
                    return null;
                }
                return ReadSettings(block.Value, target.Cell);
            }

            public EditableSettingsView ReadItemSettings(OwnItemSlot item) {
                if (!m_port.IsActorAvailable || m_port.HeldInventory == null || m_port.m_sleep?.IsSleeping == true
                    || m_port.CheckWorldItem(item, false) != AtomicActionFailure.None) {
                    return null;
                }
                return ReadSettings(item.ExpectedValue, null);
            }

            EditableSettingsView ReadSettings(int value, Point3? cell) {
                Block block = WorldBlock(value);
                if (block is not FurnitureBlock && !block.IsEditable_(value)) {
                    return null;
                }
                int data = Terrain.ExtractData(value);
                Dictionary<string, object> settings = block switch {
                    FurnitureBlock when m_port.GetFurnitureDesign(value) is FurnitureDesign design => new() { ["name"] = design.Name },
                    PistonBlock => ReadPistonSettings(data),
                    AdjustableDelayGateBlock => new() { ["delaySteps"] = AdjustableDelayGateBlock.GetDelay(data) },
                    BatteryBlock => new() { ["voltage"] = BatteryBlock.GetVoltageLevel(data) },
                    ButtonBlock => new() { ["voltage"] = ButtonBlock.GetVoltageLevel(data) },
                    SwitchBlock => new() { ["voltage"] = SwitchBlock.GetVoltageLevel(data), ["isOn"] = SwitchBlock.GetLeverState(value) },
                    TruthTableCircuitBlock => ReadTruthTableSettings(value, cell),
                    MemoryBankBlock => ReadMemoryBankSettings(value, cell),
                    BowBlock when cell == null => new() {
                        ["draw"] = BowBlock.GetDraw(data), ["arrowType"] = BowBlock.GetArrowType(data)?.ToString()
                    },
                    CrossbowBlock when cell == null => new() {
                        ["draw"] = CrossbowBlock.GetDraw(data), ["boltType"] = CrossbowBlock.GetArrowType(data)?.ToString()
                    },
                    MusketBlock when cell == null => new() {
                        ["loadState"] = MusketBlock.GetLoadState(data).ToString(),
                        ["hammerCocked"] = MusketBlock.GetHammerState(data),
                        ["bulletType"] = MusketBlock.GetBulletType(data)?.ToString()
                    },
                    _ => null
                };
                return settings != null ? new EditableSettingsView(block.GetType().Name, new ReadOnlyDictionary<string, object>(settings)) : null;
            }

            static Dictionary<string, object> ReadPistonSettings(int data) {
                PistonMode mode = PistonBlock.GetMode(data);
                Dictionary<string, object> settings = new() {
                    ["mode"] = mode.ToString(),
                    ["maxExtension"] = PistonBlock.GetMaxExtension(data) + 1,
                    ["speed"] = PistonBlock.GetSpeed(data),
                    ["extended"] = PistonBlock.GetIsExtended(data)
                };
                if (mode is PistonMode.Pulling or PistonMode.StrictPulling) {
                    settings["pullCount"] = PistonBlock.GetPullCount(data) + 1;
                }
                return settings;
            }

            Dictionary<string, object> ReadTruthTableSettings(int value, Point3? cell) {
                SubsystemEditableItemBehavior<TruthTableData> behavior = m_port.GetEditableBehavior<TruthTableData>(value);
                if (behavior == null) {
                    return null;
                }
                TruthTableData data = cell.HasValue ? behavior.GetBlockData(cell.Value) : behavior.GetItemData(Terrain.ExtractData(value));
                return new() { ["outputs"] = data?.SaveBinaryString() ?? string.Empty, ["voltageLevels"] = data?.SaveString() ?? string.Empty };
            }

            Dictionary<string, object> ReadMemoryBankSettings(int value, Point3? cell) {
                SubsystemEditableItemBehavior<MemoryBankData> behavior = m_port.GetEditableBehavior<MemoryBankData>(value);
                if (behavior == null) {
                    return null;
                }
                MemoryBankData data = cell.HasValue ? behavior.GetBlockData(cell.Value) : behavior.GetItemData(Terrain.ExtractData(value));
                return new() { ["data"] = data?.SaveString(false) ?? string.Empty, ["lastOutput"] = (int)(data?.LastOutput ?? 0) };
            }
        }
    }
}
