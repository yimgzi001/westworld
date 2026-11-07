using System.Globalization;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public enum FactorAdditionType {
        Multiply,
        Add
    }

    public class ComponentLevel : ComponentFactors {
        /// <summary>
        ///     这里的Factor类型从struct改为class，是由于模组在修改Factor的时候，通常是需要修改引用的值。
        ///     如果是struct则只能复制并修改值，不能修改引用。
        /// </summary>
        public class Factor {
            /// <summary>
            /// 在玩家信息面板上显示影响因素的名称，如“未患流感”
            /// </summary>
            public string Description;
            /// <summary>
            /// 该影响因素的具体数值
            /// </summary>
            public float Value;
            /// <summary>
            /// 该Factor的索引名称，模组使用Name来在m_xxxFactors列表中查找对应的Factor
            /// </summary>
            public string Name;
            /// <summary>
            /// 该影响因子是乘算还是加算
            /// </summary>
            public FactorAdditionType FactorAdditionType = FactorAdditionType.Multiply;
        }

        public new static string fName = "ComponentLevel";

        public float? m_lastLevelTextValue;

        public ComponentPlayer m_componentPlayer;
        public ComponentVitalStats m_componentVitalStats;
        public string m_cachedPlayerClassName;
        public string m_cachedGameModeString;

        private readonly Func<ModLoader, bool> m_onLevelUpdateAction;
        private float m_cachedLevelDescriptionFloor = float.NaN;
        private string m_cachedLevelDescription = string.Empty;
        private float m_cachedStaminaPercent = float.NaN;
        private string m_cachedStaminaDescription = string.Empty;
        private string m_cachedPlayerClassEnumName = string.Empty;
        private static readonly Dictionary<int, string> s_clothingFactorNames = [];
        /// <summary>
        ///     引擎自有的 Factor 槽位表。Generate 每帧覆写槽位字段后重填进 m_xxxFactors，
        ///     稳态下不分配任何新 Factor。注意引擎槽位对象跨帧复用且身份稳定——模组跨帧持有
        ///     这些引用时读到的是最新的值，而非生成当帧的快照；模组自行 Add 的 Factor 仍在
        ///     每次 Generate 时被清除，行为不变。
        /// </summary>
        private readonly List<Factor> m_strengthSlots = [];
        private readonly List<Factor> m_resilienceSlots = [];
        private readonly List<Factor> m_speedSlots = [];
        private readonly List<Factor> m_hungerSlots = [];
        private readonly List<Factor> m_wetnessSlots = [];

        public ComponentLevel() {
            m_onLevelUpdateAction = modLoader => {
#pragma warning disable CS0618
                modLoader.OnLevelUpdate(this);
#pragma warning restore CS0618
                return false;
            };
        }

        public static float FemaleStrengthFactor = 0.8f;
        public static float FemaleResilienceFactor = 0.8f;
        public static float FemaleSpeedFactor = 1.03f;
        public static float FemaleHungerFactor = 0.7f;

        public virtual void AddExperience(int count, bool playSound) {
            if (playSound) {
                m_subsystemAudio.PlaySound("Audio/ExperienceCollected", 0.2f, m_random.Float(-0.1f, 0.4f), 0f, 0f);
            }
            for (int i = 0; i < count; i++) {
                float num = 0.012f / MathF.Pow(1.08f, MathF.Floor(m_componentPlayer.PlayerData.Level - 1f));
                if (MathF.Floor(m_componentPlayer.PlayerData.Level + num) > MathF.Floor(m_componentPlayer.PlayerData.Level)) {
                    Time.QueueTimeDelayedExecution(
                        Time.FrameStartTime + 0.5 + 0.0,
                        delegate { m_componentPlayer.ComponentGui.DisplaySmallMessage(LanguageControl.Get(fName, 1), Color.White, true, false); }
                    );
                    Time.QueueTimeDelayedExecution(
                        Time.FrameStartTime + 0.5 + 0.0,
                        delegate { m_subsystemAudio.PlaySound("Audio/ExperienceCollected", 1f, -0.2f, 0f, 0f); }
                    );
                    Time.QueueTimeDelayedExecution(
                        Time.FrameStartTime + 0.5 + 0.15000000596046448,
                        delegate { m_subsystemAudio.PlaySound("Audio/ExperienceCollected", 1f, -0.03333333f, 0f, 0f); }
                    );
                    Time.QueueTimeDelayedExecution(
                        Time.FrameStartTime + 0.5 + 0.30000001192092896,
                        delegate { m_subsystemAudio.PlaySound("Audio/ExperienceCollected", 1f, 142f / (339f * (float)Math.PI), 0f, 0f); }
                    );
                    Time.QueueTimeDelayedExecution(
                        Time.FrameStartTime + 0.5 + 0.45000001788139343,
                        delegate { m_subsystemAudio.PlaySound("Audio/ExperienceCollected", 1f, 23f / 60f, 0f, 0f); }
                    );
                    Time.QueueTimeDelayedExecution(
                        Time.FrameStartTime + 0.5 + 0.75,
                        delegate { m_subsystemAudio.PlaySound("Audio/ExperienceCollected", 1f, -0.03333333f, 0f, 0f); }
                    );
                    Time.QueueTimeDelayedExecution(
                        Time.FrameStartTime + 0.5 + 0.90000003576278687,
                        delegate { m_subsystemAudio.PlaySound("Audio/ExperienceCollected", 1f, 23f / 60f, 0f, 0f); }
                    );
                }
                m_componentPlayer.PlayerData.Level += num;
            }
        }

        /// <summary>
        ///     生成玩家的所有关于力量的因素
        /// </summary>
        private static void EnsureSlots(List<Factor> slots, int count) {
            while (slots.Count < count) {
                slots.Add(new Factor());
            }
        }

        private static void SetSlot(Factor slot, string name, float value, string description, FactorAdditionType factorAdditionType = FactorAdditionType.Multiply) {
            slot.Name = name;
            slot.Value = value;
            slot.Description = description;
            slot.FactorAdditionType = factorAdditionType;
        }

        private static void CommitSlots(List<Factor> slots, List<Factor> factors, int count) {
            factors.Clear();
            for (int i = 0; i < count; i++) {
                factors.Add(slots[i]);
            }
        }

        public override void GenerateStrengthFactors() {
            base.GenerateStrengthFactors();
            List<Factor> slots = m_strengthSlots;
            EnsureSlots(slots, 8);
            SetSlot(slots[0], "PlayerClass", m_componentPlayer.PlayerData.PlayerClass == PlayerClass.Female ? FemaleStrengthFactor : 1f, m_cachedPlayerClassName);
            float level = m_componentPlayer.PlayerData.Level;
            float num3 = 1f + 0.05f * MathF.Floor(Math.Clamp(level, 1f, 21f) - 1f);
            SetSlot(slots[1], "Level", num3, GetLevelDescription());
            float stamina = m_componentPlayer.ComponentVitalStats.Stamina;
            float num5 = MathUtils.Lerp(0.5f, 1f, MathUtils.Saturate(4f * stamina)) * MathUtils.Lerp(0.9f, 1f, MathUtils.Saturate(stamina));
            SetSlot(slots[2], "Stamina", num5, GetStaminaDescription(stamina));
            SetSlot(slots[3], "IsSick", m_componentPlayer.ComponentSickness.IsSick ? 0.75f : 1f,
                m_componentPlayer.ComponentSickness.IsSick ? LanguageControl.Get(fName, 4) : LanguageControl.Get(fName, 5));
            SetSlot(slots[4], "IsPuking", !m_componentPlayer.ComponentSickness.IsPuking ? 1 : 0,
                m_componentPlayer.ComponentSickness.IsPuking ? LanguageControl.Get(fName, 6) : LanguageControl.Get(fName, 7));
            SetSlot(slots[5], "HasFlu", m_componentPlayer.ComponentFlu.HasFlu ? 0.75f : 1f,
                m_componentPlayer.ComponentFlu.HasFlu ? LanguageControl.Get(fName, 8) : LanguageControl.Get(fName, 9));
            SetSlot(slots[6], "IsCoughing", !m_componentPlayer.ComponentFlu.IsCoughing ? 1 : 0,
                m_componentPlayer.ComponentFlu.IsCoughing ? LanguageControl.Get(fName, 10) : LanguageControl.Get(fName, 11));
            float num15 = m_subsystemGameInfo.WorldSettings.GameMode == GameMode.Harmless ? 1.25f : 1f;
            SetSlot(slots[7], "GameMode", num15, m_cachedGameModeString);
            CommitSlots(slots, m_strengthFactors, 8);
        }

        /// <summary>
        ///     生成玩家所有关于防御的因素
        /// </summary>
        public override void GenerateResilienceFactors() {
            base.GenerateResilienceFactors();
            List<Factor> slots = m_resilienceSlots;
            EnsureSlots(slots, 5);
            SetSlot(slots[0], "PlayerClass", m_componentPlayer.PlayerData.PlayerClass == PlayerClass.Female ? FemaleResilienceFactor : 1f, m_cachedPlayerClassName);
            float level = m_componentPlayer.PlayerData.Level;
            float num3 = 1f + 0.05f * MathF.Floor(Math.Clamp(level, 1f, 21f) - 1f);
            SetSlot(slots[1], "Level", num3, GetLevelDescription());
            SetSlot(slots[2], "IsSick", m_componentPlayer.ComponentSickness.IsSick ? 0.75f : 1f,
                m_componentPlayer.ComponentSickness.IsSick ? LanguageControl.Get(fName, 4) : LanguageControl.Get(fName, 5));
            SetSlot(slots[3], "HasFlu", m_componentPlayer.ComponentFlu.HasFlu ? 0.75f : 1f,
                m_componentPlayer.ComponentFlu.HasFlu ? LanguageControl.Get(fName, 8) : LanguageControl.Get(fName, 9));
            float num9 = 1f;
            if (m_subsystemGameInfo.WorldSettings.GameMode == GameMode.Harmless) {
                num9 = 1.5f;
            }
            if (m_subsystemGameInfo.WorldSettings.GameMode == GameMode.Survival) {
                num9 = 1.25f;
            }
            if (m_subsystemGameInfo.WorldSettings.GameMode == GameMode.Creative) {
                num9 = float.PositiveInfinity;
            }
            SetSlot(slots[4], "GameMode", num9, m_cachedGameModeString);
            CommitSlots(slots, m_resilienceFactors, 5);
        }

        /// <summary>
        ///     生成玩家所有关于速度的因素
        /// </summary>
        public override void GenerateSpeedFactors() {
            base.GenerateSpeedFactors();
            List<Factor> slots = m_speedSlots;
            EnsureSlots(slots, 7);
            SetSlot(slots[0], "PlayerClass", m_componentPlayer.PlayerData.PlayerClass == PlayerClass.Female ? FemaleSpeedFactor : 1f, m_cachedPlayerClassName);
            float level = m_componentPlayer.PlayerData.Level;
            float num3 = 1f + 0.02f * MathF.Floor(Math.Clamp(level, 1f, 21f) - 1f);
            SetSlot(slots[1], "Level", num3, GetLevelDescription());
            int index = 2;
            foreach (ClothingSlot clothingSlot in ClothingSlot.ClothingSlots.Values) {
                foreach (int clothe in m_componentPlayer.ComponentClothing.GetClothes(clothingSlot)) {
                    index = GenerateClothingSpeedFactorIntoSlot(clothe, slots, index);
                }
            }
            float stamina = m_componentPlayer.ComponentVitalStats.Stamina;
            float num4 = MathUtils.Lerp(0.5f, 1f, MathUtils.Saturate(4f * stamina)) * MathUtils.Lerp(0.9f, 1f, MathUtils.Saturate(stamina));
            EnsureSlots(slots, index + 5);
            SetSlot(slots[index], "Stamina", num4, GetStaminaDescription(stamina));
            SetSlot(slots[index + 1], "IsSick", m_componentPlayer.ComponentSickness.IsSick ? 0.75f : 1f,
                m_componentPlayer.ComponentSickness.IsSick ? LanguageControl.Get(fName, 4) : LanguageControl.Get(fName, 5));
            SetSlot(slots[index + 2], "IsPuking", !m_componentPlayer.ComponentSickness.IsPuking ? 1 : 0,
                m_componentPlayer.ComponentSickness.IsPuking ? LanguageControl.Get(fName, 6) : LanguageControl.Get(fName, 7));
            SetSlot(slots[index + 3], "HasFlu", m_componentPlayer.ComponentFlu.HasFlu ? 0.75f : 1f,
                m_componentPlayer.ComponentFlu.HasFlu ? LanguageControl.Get(fName, 8) : LanguageControl.Get(fName, 9));
            SetSlot(slots[index + 4], "IsCoughing", !m_componentPlayer.ComponentFlu.IsCoughing ? 1 : 0,
                m_componentPlayer.ComponentFlu.IsCoughing ? LanguageControl.Get(fName, 10) : LanguageControl.Get(fName, 11));
            CommitSlots(slots, m_speedFactors, index + 5);
        }

        /// <summary>
        ///     生成玩家所有关于饥饿的因素
        /// </summary>
        public override void GenerateHungerFactors() {
            base.GenerateHungerFactors();
            List<Factor> slots = m_hungerSlots;
            EnsureSlots(slots, 3);
            SetSlot(slots[0], "PlayerClass", m_componentPlayer.PlayerData.PlayerClass == PlayerClass.Female ? FemaleHungerFactor : 1f, m_cachedPlayerClassEnumName);
            float level = m_componentPlayer.PlayerData.Level;
            float num3 = 1f - 0.01f * MathF.Floor(Math.Clamp(level, 1f, 21f) - 1f);
            SetSlot(slots[1], "Level", num3, GetLevelDescription());
            float num5 = 1f;
            if (m_subsystemGameInfo.WorldSettings.GameMode == GameMode.Harmless) {
                num5 = 0.66f;
            }
            if (m_subsystemGameInfo.WorldSettings.GameMode == GameMode.Survival) {
                num5 = 0.75f;
            }
            if (m_subsystemGameInfo.WorldSettings.GameMode == GameMode.Creative) {
                num5 = 0f;
            }
            SetSlot(slots[2], "GameMode", num5, m_cachedGameModeString);
            CommitSlots(slots, m_hungerFactors, 3);
        }

        /// <summary>
        ///     生成玩家所有关于潮湿度的因素
        /// </summary>
        public void GenerateWetnessFactors() {
            List<Factor> wetness = OtherFactors["Wetness"];
            List<Factor> slots = m_wetnessSlots;
            EnsureSlots(slots, 4);
            //潮湿度初始值为0
            SetSlot(slots[0], "Initialize", -1f, null, FactorAdditionType.Add);
            int index = 1;
            //在水中会提高潮湿度
            if (m_componentPlayer.ComponentBody.ImmersionFactor > 0.2f
                && m_componentPlayer.ComponentBody.ImmersionFluidBlock is WaterBlock) {
                SetSlot(slots[index++], "InWater", 3f * (2f * m_componentPlayer.ComponentBody.ImmersionFactor - m_componentVitalStats.Wetness), null, FactorAdditionType.Add);
            }
            //在雨中会提高潮湿度
            int x = Terrain.ToCell(m_componentPlayer.ComponentBody.Position.X);
            int num2 = Terrain.ToCell(m_componentPlayer.ComponentBody.Position.Y + 0.1f);
            int z = Terrain.ToCell(m_componentPlayer.ComponentBody.Position.Z);
            PrecipitationShaftInfo precipitationShaftInfo = m_componentVitalStats.m_subsystemWeather.GetPrecipitationShaftInfo(x, z);
            if (num2 >= precipitationShaftInfo.YLimit
                && precipitationShaftInfo.Type == PrecipitationType.Rain) {
                SetSlot(slots[index++], "Precipitation", 0.05f * precipitationShaftInfo.Intensity, null, FactorAdditionType.Add);
            }
            //自动降低潮湿度，气温越高降低越快
            float num3 = 180f;
            if (m_componentVitalStats.m_targetTemperature > 8f) {
                num3 = 120f;
            }
            if (m_componentVitalStats.m_targetTemperature > 16f) {
                num3 = 60f;
            }
            if (m_componentVitalStats.m_targetTemperature > 24f) {
                num3 = 30f;
            }
            SetSlot(slots[index], "Temperature", -1f / num3, null, FactorAdditionType.Add);
            CommitSlots(slots, wetness, index + 1);
        }

        /// <summary>
        ///     等级描述按整数等级缓存，避免 Update 每帧分配字符串。
        ///     键用 float 而非 int：Level 为 NaN 时 float 键恒不等，保持与未缓存时相同的输出。
        /// </summary>
        private string GetLevelDescription() {
            float floorLevel = MathF.Floor(m_componentPlayer.PlayerData.Level);
            if (m_cachedLevelDescriptionFloor != floorLevel) {
                m_cachedLevelDescriptionFloor = floorLevel;
                m_cachedLevelDescription = string.Format(LanguageControl.Get(fName, 2), floorLevel.ToString(CultureInfo.InvariantCulture));
            }
            return m_cachedLevelDescription;
        }

        /// <summary>
        ///     体力描述按取整后的百分比值（float）缓存，键即格式化器的精确输入，无碰撞域问题。
        ///     取整口径必须与 "{stamina * 100f:0}" 一致：自定义数字格式 "0" 对中点值按远离零取整，
        ///     故用 MidpointRounding.AwayFromZero 生成键。NaN 不等于自身，恒重算，与未缓存时输出行为一致。
        /// </summary>
        private string GetStaminaDescription(float stamina) {
            float percent = MathF.Round(stamina * 100f, MidpointRounding.AwayFromZero);
            if (m_cachedStaminaPercent != percent) {
                m_cachedStaminaPercent = percent;
                m_cachedStaminaDescription = string.Format(LanguageControl.Get(fName, 3), $"{stamina * 100f:0}");
            }
            return m_cachedStaminaDescription;
        }

        public override void Update(float dt) {
            if (m_subsystemTime.PeriodicGameTimeEvent(180.0, 179.0)) {
                AddExperience(1, false);
            }
            if (!m_lastLevelTextValue.HasValue
                || m_lastLevelTextValue.Value != MathF.Floor(m_componentPlayer.PlayerData.Level)) {
                m_componentPlayer.ComponentGui.LevelLabelWidget.Text = string.Format(
                    LanguageControl.Get(fName, 2),
                    MathF.Floor(m_componentPlayer.PlayerData.Level).ToString(CultureInfo.InvariantCulture)
                );
                m_lastLevelTextValue = MathF.Floor(m_componentPlayer.PlayerData.Level);
            }
            m_componentPlayer.PlayerStats.HighestLevel = MathUtils.Max(
                m_componentPlayer.PlayerStats.HighestLevel,
                m_componentPlayer.PlayerData.Level
            );
            base.Update(dt);
            GenerateWetnessFactors();
            ModsManager.HookAction("OnLevelUpdate", m_onLevelUpdateAction);
        }

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) {
            base.Load(valuesDictionary, idToEntityMap);
            m_componentPlayer = Entity.FindComponent<ComponentPlayer>(true);
            m_componentVitalStats = Entity.FindComponent<ComponentVitalStats>(true);
            OtherFactors["Wetness"] = new List<Factor>();
            m_cachedPlayerClassEnumName = m_componentPlayer.PlayerData.PlayerClass.ToString();
            m_cachedPlayerClassName = LanguageControl.Get("PlayerClass", m_cachedPlayerClassEnumName);
            m_cachedGameModeString = string.Format(
                LanguageControl.Get(fName, 12),
                LanguageControl.Get("GameMode", m_subsystemGameInfo.WorldSettings.GameMode.ToString())
            );
        }

        public void GenerateClothingSpeedFactors(int clothingValue) {
            Block block = BlocksManager.Blocks[Terrain.ExtractContents(clothingValue)];
            ClothingData clothingData = block.GetClothingData(clothingValue);
            if (clothingData != null
                && clothingData.MovementSpeedFactor != 1f) {
                m_speedFactors.Add(
                    new Factor {
                        Name = GetClothingFactorName(clothingValue), Value = clothingData.MovementSpeedFactor, Description = clothingData.DisplayName
                    }
                );
            }
        }

        private int GenerateClothingSpeedFactorIntoSlot(int clothingValue, List<Factor> slots, int index) {
            Block block = BlocksManager.Blocks[Terrain.ExtractContents(clothingValue)];
            ClothingData clothingData = block.GetClothingData(clothingValue);
            if (clothingData != null
                && clothingData.MovementSpeedFactor != 1f) {
                EnsureSlots(slots, index + 1);
                SetSlot(slots[index], GetClothingFactorName(clothingValue), clothingData.MovementSpeedFactor, clothingData.DisplayName);
                return index + 1;
            }
            return index;
        }

        /// <summary>
        ///     衣物 Factor 的 Name 按 clothingValue 缓存，避免每帧插值分配。
        /// </summary>
        private static string GetClothingFactorName(int clothingValue) {
            if (!s_clothingFactorNames.TryGetValue(clothingValue, out string name)) {
                name = $"Clothing {clothingValue}";
                s_clothingFactorNames[clothingValue] = name;
            }
            return name;
        }

        [Obsolete("Use GenerateClothingSpeedFactors.")]
        public static void AddClothingFactor(int clothingValue, ref float clothingFactor, ICollection<Factor> factors) {
            Block block = BlocksManager.Blocks[Terrain.ExtractContents(clothingValue)];
            ClothingData clothingData = block.GetClothingData(clothingValue);
            if (clothingData != null
                && clothingData.MovementSpeedFactor != 1f) {
                clothingFactor *= clothingData.MovementSpeedFactor;
                factors?.Add(new Factor { Value = clothingData.MovementSpeedFactor, Description = clothingData.DisplayName });
            }
        }
    }
}