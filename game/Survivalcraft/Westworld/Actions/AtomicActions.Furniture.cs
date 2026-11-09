using Engine;

namespace Game.Westworld {
    public sealed partial class AtomicActions {
        SubsystemFurnitureBlockBehavior FurnitureBehavior => m_actor.Project.FindSubsystem<SubsystemFurnitureBlockBehavior>();

        void RegisterFurniture() => Register<BuildFurniture>(StartBuildFurniture, () => CanHoldWorldItem() && FurnitureBehavior != null);

        AtomicActionRun StartBuildFurniture(BuildFurniture action) {
            if (action.Target == null) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (m_miner.AnyIsPending || m_miner.DigCellFace.HasValue) {
                return Reject(action, AtomicActionFailure.Busy);
            }
            AtomicActionFailure failure = CheckWorldItem(action.Hammer, true);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }
            if (WorldBlock(action.Hammer.ExpectedValue) is not HammerBlock || !m_miner.IsLevelSufficientForTool(action.Hammer.ExpectedValue)) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            failure = FindWorldRay(action.Target, RaycastMode.Digging, InteractionReach, out Ray3 ray, out object hit);
            if (failure != AtomicActionFailure.None) {
                return Reject(action, failure);
            }

            SubsystemFurnitureBlockBehavior furniture = FurnitureBehavior;
            SubsystemFurnitureBlockBehavior.FurnitureScan scan = furniture.ScanFurnitureDesign(((TerrainRaycastResult)hit).CellFace,
                ray.Direction, out _);
            if (scan == null) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (action.Options != null && !ApplyFurnitureOptions(scan.Design, action.Options)) {
                return Reject(action, AtomicActionFailure.InvalidArgument);
            }
            if (SubsystemFurnitureBlockBehavior.CountDesignTriangles(scan.Design) > FurnitureDesign.MaxTriangles) {
                return Reject(action, AtomicActionFailure.PreconditionsNotMet);
            }
            if (!SelectWorldItem(action.Hammer)) {
                return Reject(action, AtomicActionFailure.Blocked);
            }
            Pickable output = furniture.CompleteFurnitureDesign(scan, m_miner, action.Hammer.ExpectedValue,
                out SubsystemFurnitureBlockBehavior.FurnitureFailure buildFailure);
            if (output is { Count: > 0 }) {
                return Succeed(action, output.Count);
            }
            return Reject(action, buildFailure switch {
                SubsystemFurnitureBlockBehavior.FurnitureFailure.InsufficientDurability => AtomicActionFailure.PreconditionsNotMet,
                SubsystemFurnitureBlockBehavior.FurnitureFailure.SourceChanged => AtomicActionFailure.TargetChanged,
                _ => AtomicActionFailure.Blocked
            });
        }

        static bool ApplyFurnitureOptions(FurnitureDesign design, FurnitureBuildOptions options) {
            if (options.Name is { Length: > 20 } || options.Name is { Length: > 0 } name && (name[0] == ' ' || name[^1] == ' ')
                || options.RotationAxis is < 0 or > 2 || options.MirrorAxis is < 0 or > 2
                || options.Resolution is < FurnitureDesign.MinResolution or > FurnitureDesign.MaxResolution) {
                return false;
            }
            if (options.Name != null) {
                design.Name = options.Name;
            }
            if (options.Resolution is int resolution && resolution != design.Resolution) {
                Box box = design.Box;
                if (box.Width > resolution || box.Height > resolution || box.Depth > resolution) {
                    return false;
                }
                // Follow the dialog's resize behavior by shifting contents inward before shrinking.
                design.Shift(new Point3(Math.Min(0, resolution - box.Right), Math.Min(0, resolution - box.Bottom),
                    Math.Min(0, resolution - box.Far)));
                design.Resize(resolution);
            }
            if (options.MirrorAxis is int mirrorAxis) {
                design.Mirror(mirrorAxis);
            }
            design.Rotate(options.RotationAxis, options.QuarterTurns);

            Box bounds = design.Box;
            Point3 shift = options.Shift;
            if (shift.X < -bounds.Left || shift.X > design.Resolution - bounds.Right
                || shift.Y < -bounds.Top || shift.Y > design.Resolution - bounds.Bottom
                || shift.Z < -bounds.Near || shift.Z > design.Resolution - bounds.Far) {
                return false;
            }
            design.Shift(shift);
            return true;
        }
    }
}
