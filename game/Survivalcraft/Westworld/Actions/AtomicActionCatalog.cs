namespace Game.Westworld {
    public enum AtomicActionCategory {
        Movement,
        Survival,
        Inventory,
        WorldInteraction,
        Combat,
        Production,
        Social
    }

    public sealed record AtomicActionDefinition(string Name, AtomicActionCategory Category, Type RequestType);

    public static class AtomicActionCatalog {
        public static IReadOnlyList<AtomicActionDefinition> Definitions { get; } = Array.AsReadOnly(new[] {
            Define<MoveTo>(AtomicActionCategory.Movement),
            Define<FaceTowards>(AtomicActionCategory.Movement),
            Define<SetPosture>(AtomicActionCategory.Movement),
            Define<Jump>(AtomicActionCategory.Movement),
            Define<StepTo>(AtomicActionCategory.Movement),
            Define<ClimbTo>(AtomicActionCategory.Movement),
            Define<Mount>(AtomicActionCategory.Movement),
            Define<Dismount>(AtomicActionCategory.Movement),
            Define<Steer>(AtomicActionCategory.Movement),

            Define<Wait>(AtomicActionCategory.Survival),
            Define<Eat>(AtomicActionCategory.Survival),
            Define<Sleep>(AtomicActionCategory.Survival),
            Define<WakeUp>(AtomicActionCategory.Survival),

            Define<PickUp>(AtomicActionCategory.Inventory),
            Define<Drop>(AtomicActionCategory.Inventory),
            Define<SelectItem>(AtomicActionCategory.Inventory),
            Define<TransferItems>(AtomicActionCategory.Inventory),
            Define<Equip>(AtomicActionCategory.Inventory),
            Define<Unequip>(AtomicActionCategory.Inventory),

            Define<Dig>(AtomicActionCategory.WorldInteraction),
            Define<Place>(AtomicActionCategory.WorldInteraction),
            Define<Interact>(AtomicActionCategory.WorldInteraction),
            Define<UseItem>(AtomicActionCategory.WorldInteraction),
            Define<ConfigureBlock>(AtomicActionCategory.WorldInteraction),
            Define<ConfigureItem>(AtomicActionCategory.WorldInteraction),

            Define<MeleeAttack>(AtomicActionCategory.Combat),
            Define<UseAimedItem>(AtomicActionCategory.Combat),
            Define<Craft>(AtomicActionCategory.Production),
            Define<BuildFurniture>(AtomicActionCategory.Production),

            Define<Speak>(AtomicActionCategory.Social),
            Define<OfferItem>(AtomicActionCategory.Social),
            Define<RespondToOffer>(AtomicActionCategory.Social)
        });

        static AtomicActionDefinition Define<T>(AtomicActionCategory category) where T : AtomicAction => new(typeof(T).Name, category, typeof(T));
    }
}
