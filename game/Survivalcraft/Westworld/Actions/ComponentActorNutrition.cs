using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game.Westworld {
    // Lightweight food reserve for non-player bodies, independent of player UI and sickness.
    public sealed class ComponentActorNutrition : Component, IUpdateable {
        float m_food;

        public float Food {
            get => m_food;
            set => m_food = MathUtils.Saturate(value);
        }

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public void Update(float dt) => Food -= dt / 2880f;

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) =>
            Food = valuesDictionary.GetValue("Food", 0.9f);

        public override void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap) =>
            valuesDictionary.SetValue("Food", Food);
    }
}
