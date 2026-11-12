namespace Game.Westworld {
    internal sealed record CognitiveFeedbackClassification(bool Technical, bool WorldFailure, int PhysicalEffects);

    internal static class CognitiveFeedbackPolicy {
        internal static CognitiveFeedbackClassification Classify(AtomicActionFailure failure, int affectedCount) {
            bool technical = failure is AtomicActionFailure.ExecutionError or AtomicActionFailure.InvalidArgument
                or AtomicActionFailure.UnsupportedAction or AtomicActionFailure.Busy;
            return new(technical, !technical && failure != AtomicActionFailure.None, Math.Max(0, affectedCount));
        }
    }
}
