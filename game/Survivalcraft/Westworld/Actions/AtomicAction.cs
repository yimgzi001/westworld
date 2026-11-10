using System.Text.Json;
using System.Text.Json.Serialization;
using Engine;
using GameEntitySystem;

namespace Game.Westworld {
    public abstract record AtomicAction;

    public sealed record MoveTo(Vector3 Destination, float Range = 0.75f, float Timeout = 15f) : AtomicAction;
    // Trusted host supplies already observed flat-route evidence; never exposed in model catalog.
    public sealed record MoveAlongObservedRoute(IReadOnlyList<Vector3> Points, float FinalRange = 0.25f,
        float Timeout = 4f, bool LevelGaze = false) : AtomicAction {
        // 只允许同一游戏线程的宿主接续已经验证的路线，接纳后才替换它持有的观察证据。
        [JsonIgnore]
        public Func<double, (MoveAlongObservedRoute Route, Action Accepted)?> Continue { get; init; }
    }
    // Native body yaw plus head pitch, advanced through locomotion orders. Negative pitch looks down.
    // HorizontalOnly preserves legacy bearing-only tool calls without changing the existing pitch.
    public sealed record FaceTowards(Vector3 Point, float Timeout = 5f, float? PitchDegrees = null, bool HorizontalOnly = false) : AtomicAction;
    public sealed record SetPosture(BodyPosture Posture) : AtomicAction;
    public sealed record Jump : AtomicAction;
    // Local one-cube transition. Caller supplies observation proof; no hidden path search.
    public sealed record StepTo(Vector3 Destination, float Timeout = 3f) : AtomicAction;
    public sealed record ClimbTo(BlockTarget Ladder, float Height, float Timeout = 15f) : AtomicAction;
    public sealed record Mount(Entity Target) : AtomicAction;
    public sealed record Dismount : AtomicAction;
    public sealed record Steer(float Turn, float Throttle, float Duration) : AtomicAction;

    public sealed record Wait(float Duration) : AtomicAction;
    public sealed record Eat(int Slot, int ExpectedValue, int Count) : AtomicAction;
    public sealed record Sleep : AtomicAction;
    public sealed record WakeUp : AtomicAction;

    public sealed record PickUp(Pickable Target, int Count) : AtomicAction {
        public int? FoodValue { get; init; }
    }
    public sealed record Drop(OwnItemSlot Item, int Count) : AtomicAction;
    public sealed record SelectItem(int Slot, int ExpectedValue) : AtomicAction;
    public sealed record TransferItems(InventorySlot Source, InventorySlot Destination, int Count) : AtomicAction;
    public sealed record Equip(OwnItemSlot Item) : AtomicAction;
    public sealed record Unequip(ClothingSlot Slot, int ExpectedValue) : AtomicAction;

    public sealed record Dig(BlockTarget Target, float Timeout = 15f) : AtomicAction;
    public sealed record Place(BlockTarget Against, OwnItemSlot Item) : AtomicAction;
    public sealed record Interact(ActionTarget Target) : AtomicAction;
    public sealed record UseItem(OwnItemSlot Item, ActionTarget Target = null) : AtomicAction;
    public sealed record ConfigureBlock(BlockTarget Target, JsonElement Settings) : AtomicAction;
    public sealed record ConfigureItem(OwnItemSlot Item, JsonElement Settings) : AtomicAction;

    public sealed record MeleeAttack(Entity Target) : AtomicAction;
    public sealed record UseAimedItem(OwnItemSlot Item, ActionTarget Target, float HoldDuration) : AtomicAction;
    public sealed record Craft(IInventory Workspace, int ExpectedResultValue, int Count) : AtomicAction;
    public sealed record BuildFurniture(BlockTarget Target, OwnItemSlot Hammer, FurnitureBuildOptions Options = null) : AtomicAction;

    public sealed record FurnitureBuildOptions(string Name = null, int? Resolution = null, int? MirrorAxis = null,
        int RotationAxis = 1, int QuarterTurns = 0, Point3 Shift = default);

    public sealed record Speak(string Text, SpeechMode Mode = SpeechMode.Normal, Entity Recipient = null) : AtomicAction;
    public sealed record OfferItem(Entity Recipient, OwnItemSlot Item, int Count) : AtomicAction;
    public sealed record RespondToOffer(Guid OfferId, bool Accept) : AtomicAction;

    public enum BodyPosture { Standing, Crouching }
    public enum SpeechMode { Whisper, Normal, Shout }

    // Trusted game code resolves targets and inventories before building a command.
    public readonly record struct OwnItemSlot(int Slot, int ExpectedValue);
    public readonly record struct InventorySlot(IInventory Inventory, int Slot, int ExpectedValue);

    public abstract record ActionTarget;
    public sealed record BlockTarget(Point3 Cell, int ExpectedValue, int? Face = null) : ActionTarget;
    public sealed record MovingBlockTarget(IMovingBlockSet Set, Point3 Offset, int ExpectedValue, int? Face = null) : ActionTarget;
    public sealed record EntityTarget(Entity Entity) : ActionTarget;
    public sealed record PointTarget(Vector3 Point) : ActionTarget;

    public enum AtomicActionStatus {
        Running,
        Succeeded,
        Failed,
        Cancelled
    }

    public enum AtomicActionFailure {
        None,
        UnsupportedAction,
        InvalidArgument,
        ActorUnavailable,
        Busy,
        MovementInterrupted,
        Blocked,
        TimedOut,
        TargetChanged,
        OutOfReach,
        InsufficientItems,
        InventoryFull,
        PreconditionsNotMet,
        ExecutionError
    }

    public readonly record struct AtomicActionResult(AtomicActionStatus Status, AtomicActionFailure Failure = AtomicActionFailure.None,
        int AffectedCount = 0, Guid? OfferId = null) {
        public IReadOnlyList<InventorySlotView> ReceivedItems { get; internal init; }
    }

    public sealed class AtomicActionRun {
        public AtomicAction Action { get; }

        public AtomicActionResult Result { get; internal set; }

        internal AtomicActionRun(AtomicAction action, AtomicActionResult result) {
            Action = action;
            Result = result;
        }
    }
}
