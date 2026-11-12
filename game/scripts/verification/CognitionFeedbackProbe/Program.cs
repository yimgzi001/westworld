using System.Reflection;
using Game.Westworld;

var type = typeof(AtomicActionFailure).Assembly.GetType("Game.Westworld.CognitiveFeedbackPolicy");
if (type == null) throw new Exception("Production feedback policy is missing.");
var classify = type.GetMethod("Classify", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
var technical = new[] { AtomicActionFailure.ExecutionError, AtomicActionFailure.InvalidArgument,
    AtomicActionFailure.UnsupportedAction, AtomicActionFailure.Busy };
int checks = 0;
foreach (var failure in Enum.GetValues<AtomicActionFailure>()) {
    foreach (int count in new[] { 0, 2 }) {
        object result = classify.Invoke(null, [failure, count])!;
        bool isTechnical = (bool)result.GetType().GetProperty("Technical")!.GetValue(result)!;
        bool worldFailure = (bool)result.GetType().GetProperty("WorldFailure")!.GetValue(result)!;
        int effects = (int)result.GetType().GetProperty("PhysicalEffects")!.GetValue(result)!;
        if (isTechnical != technical.Contains(failure)) throw new Exception($"Wrong source classification: {failure}");
        if (worldFailure != (!isTechnical && failure != AtomicActionFailure.None)) throw new Exception($"Technical diagnostic became world evidence: {failure}");
        if (effects != count) throw new Exception($"Actual partial effects were erased: {failure}");
        checks++;
    }
}
Console.WriteLine($"passed: {checks} feedback classifications and partial-effect preservation checks");
