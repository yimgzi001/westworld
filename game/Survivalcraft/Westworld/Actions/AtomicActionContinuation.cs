using System.Text.Json.Serialization;
using Engine;

namespace Game.Westworld {
    [JsonDerivedType(typeof(MotionContinuation), "motion")]
    [JsonDerivedType(typeof(WaitContinuation), "wait")]
    [JsonDerivedType(typeof(SpeechContinuation), "speech")]
    [JsonDerivedType(typeof(DigContinuation), "dig")]
    internal abstract record AtomicActionContinuation {
        internal abstract bool Matches(string tool);
    }

    [method: JsonConstructor]
    internal sealed record MotionContinuation(float X, float Y, float Z, float Seconds, float? Range) : AtomicActionContinuation {
        internal MotionContinuation(Vector3 point, float seconds, float? range = null)
            : this(point.X, point.Y, point.Z, seconds, range) { }

        internal AtomicAction Action => Range is float range
            ? new MoveTo(new Vector3(X, Y, Z), range, Seconds)
            : new FaceTowards(new Vector3(X, Y, Z), Seconds);

        internal override bool Matches(string tool) =>
            tool == (Range.HasValue ? "act_MoveTo" : "act_FaceTowards")
            && float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z)
            && float.IsFinite(Seconds) && Seconds > 0f
            && (!Range.HasValue || Range > 0f && float.IsFinite(Range.Value * Range.Value));
    }

    internal sealed record WaitContinuation(float Seconds) : AtomicActionContinuation {
        internal override bool Matches(string tool) => tool == "act_Wait" && float.IsFinite(Seconds) && Seconds > 0f;
    }

    internal sealed record SpeechContinuation(string Text, SpeechMode Mode, Guid Id, int Chunk, int Delivered,
        double Seconds, int[] Listeners) : AtomicActionContinuation {
        internal override bool Matches(string tool) => tool == "act_Speak" && Text is { Length: > 0 and <= 512 }
            && Enum.IsDefined(Mode) && Id != Guid.Empty && Chunk >= 0 && Delivered >= 0
            && double.IsFinite(Seconds) && Seconds is >= 0 and <= 1 && Listeners != null;
    }

    internal sealed record DigContinuation(int X, int Y, int Z, int Value, int? Face, float Seconds, double Elapsed,
        int ToolValue, int? ToolSlot) : AtomicActionContinuation {
        internal Dig Action => new(new BlockTarget(new Point3(X, Y, Z), Value, Face), Seconds);

        internal override bool Matches(string tool) => tool == "act_Dig" && Y is >= 0 and < 256 && Face is null or (>= 0 and <= 5)
            && Value > 0 && float.IsFinite(Seconds) && Seconds > 0f && double.IsFinite(Elapsed) && Elapsed >= 0;
    }
}
