using System.Numerics;
using Jitter2.LinearMath;

namespace OpenMagicka.Physics;

/// <summary>Conversions at the engine boundary: gameplay sees only System.Numerics.</summary>
internal static class Vectors
{
    public static JVector ToJitter(this Vector3 v) => new(v.X, v.Y, v.Z);

    public static Vector3 ToNumerics(this JVector v) => new(v.X, v.Y, v.Z);

    public static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    public static Vector3 ThrowIfNotFinite(Vector3 v, string name) =>
        IsFinite(v) ? v : throw new ArgumentOutOfRangeException(name, v, "must be finite");
}
