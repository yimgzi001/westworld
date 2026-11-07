namespace Game {
    public class ClothingSlot {
        /// <summary>
        ///     调用自定义部位（比如手臂）的ClothingSlot，可以用ClothingSlot.ClothingSlots["Arms"]
        /// </summary>
        public static Dictionary<string, ClothingSlot> ClothingSlots = new();

        public static Dictionary<int, ClothingSlot> ClothingSlotsByInt = new();
        public static ClothingSlot Head => ClothingSlotsByInt[0];
        public static ClothingSlot Torso => ClothingSlotsByInt[1];
        public static ClothingSlot Legs => ClothingSlotsByInt[2];
        public static ClothingSlot Feet => ClothingSlotsByInt[3];

        public static void AddClothingSlot(string name) {
            if (ClothingSlots.ContainsKey(name)) {
                return;
            }
            ClothingSlots[name] = new ClothingSlot { Name = name, StableId = ClothingSlots.Count };
            ClothingSlotsByInt[ClothingSlots[name].StableId] = ClothingSlots[name];
        }

        public static void Initialize() {
            //注意不能 Clear 后重建实例：ClothingSlot 未重写 Equals/GetHashCode，按引用相等作
            //ComponentClothing.m_clothes/InsulationBySlots 的键，已加载世界里的实体持着旧实例键，
            //重建会让切语言后（此方法被 LanguageControl.ChangeLanguage 重调）GetClothes 等按
            //ClothingSlotsByInt 新实例查字典直接 KeyNotFound。重入只补缺失部位+刷新文本属性
            AddClothingSlot("Head");
            AddClothingSlot("Torso");
            AddClothingSlot("Legs");
            AddClothingSlot("Feet");
            ClothingSlots["Head"].MessageWhenLeastInsulated = LanguageControl.Get(ComponentVitalStats.fName, 41);
            ClothingSlots["Torso"].MessageWhenLeastInsulated = LanguageControl.Get(ComponentVitalStats.fName, 42);
            ClothingSlots["Legs"].MessageWhenLeastInsulated = LanguageControl.Get(ComponentVitalStats.fName, 43);
            ClothingSlots["Feet"].MessageWhenLeastInsulated = LanguageControl.Get(ComponentVitalStats.fName, 44);
            ClothingSlots["Head"].BasicInsulation = 2f;
            ClothingSlots["Torso"].BasicInsulation = 0.2f;
            ClothingSlots["Legs"].BasicInsulation = 0.4f;
            ClothingSlots["Feet"].BasicInsulation = 2f;
            ModsManager.HookAction(
                "InitializeClothingSlots",
                loader => {
                    loader.InitializeClothingSlots();
                    return false;
                }
            );
        }

        public int StableId;

        public float BasicInsulation = 1e8f;

        public string Name;
        public virtual string MessageWhenLeastInsulated { get; set; } = string.Empty;

        // 显式转换操作符
        public static explicit operator int(ClothingSlot slot) => slot.StableId;
        public static implicit operator ClothingSlot(int id) => ClothingSlotsByInt.TryGetValue(id, out ClothingSlot slot) ? slot : null;
    }
}