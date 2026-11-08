namespace RecoveryVision;

/// <summary>New reconstruction, not recovered original implementation.
/// Pure geometry only: caller must separately require loaded terrain and an unobstructed ray.
/// Right-handed camera basis uses positive local Z forward, X right, Y up.
/// FOV is rectangular/perspective (vertical uses local Z, not horizontal distance).</summary>
public static class VisionCore
{
    public const double Radius = 8;
    public const float MaxYawMagnitude = 0.1f;
    public static readonly double VerticalSlope = System.Math.Tan(35 * System.Math.PI / 180);
    public readonly record struct Vec(double X, double Y, double Z)
    {
        public bool Finite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
        public double Dot(Vec b) => X*b.X + Y*b.Y + Z*b.Z;
        public static Vec operator -(Vec a, Vec b) => new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    }

    /// <summary>Inclusive range and FOV boundaries. Invalid or zero direction fails closed.</summary>
    public static bool ContainsLocal(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) || z <= 0) return false;
        double squared = x*x+y*y+z*z;
        if (!double.IsFinite(squared) || squared > Radius*Radius) return false;
        // tan(45 degrees) is exactly 1; do not approximate through trig.
        return System.Math.Abs(x) <= z && System.Math.Abs(y) <= z*VerticalSlope;
    }

    /// <summary>Caller supplies camera basis from the real eye rotation.
    /// Tolerance here validates floating-point orthonormal basis, never yaw or FOV bounds.</summary>
    public static bool ContainsWorld(Vec eye, Vec point, Vec right, Vec up, Vec forward)
    {
        if (!eye.Finite || !point.Finite || !right.Finite || !up.Finite || !forward.Finite) return false;
        const double basisTolerance=1e-5;
        foreach (var axis in new[]{right,up,forward})
            if (System.Math.Abs(axis.Dot(axis)-1)>basisTolerance) return false;
        if (System.Math.Abs(right.Dot(up))>basisTolerance ||
            System.Math.Abs(right.Dot(forward))>basisTolerance ||
            System.Math.Abs(up.Dot(forward))>basisTolerance) return false;
        Vec d=point-eye;
        if (!double.IsFinite(d.Dot(d)) || d.Dot(d)>Radius*Radius) return false;
        return ContainsLocal(d.Dot(right),d.Dot(up),d.Dot(forward));
    }

    /// <summary>Compare float to float. Never promote the bound to double 0.1.
    /// No epsilon: MathF.BitIncrement(0.1f) must be rejected.</summary>
    public static bool ValidYaw(float yaw) =>
        float.IsFinite(yaw) && System.MathF.Abs(yaw) <= MaxYawMagnitude;
}
