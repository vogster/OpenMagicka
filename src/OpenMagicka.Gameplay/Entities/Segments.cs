using System.Numerics;

namespace OpenMagicka.Gameplay.Entities;

/// <summary>A line segment from <see cref="Origin"/> along <see cref="Delta"/>.</summary>
public readonly record struct Segment(Vector3 Origin, Vector3 Delta)
{
    public static Segment Between(Vector3 from, Vector3 to) => new(from, to - from);

    public Vector3 At(float t) => Origin + t * Delta;

    /// <summary>
    /// The least squared distance between two segments, with where it falls on each (0 to 1);
    /// a segment of no length is its origin.
    /// </summary>
    public static float DistanceSquared(Segment a, Segment b, out float s, out float t)
    {
        var r = a.Origin - b.Origin;
        var (aa, bb, ab) = (a.Delta.LengthSquared(), b.Delta.LengthSquared(), Vector3.Dot(a.Delta, b.Delta));
        var (ar, br) = (Vector3.Dot(a.Delta, r), Vector3.Dot(b.Delta, r));
        const float Tiny = 1e-12f;
        if (aa <= Tiny && bb <= Tiny)
        {
            (s, t) = (0, 0);
        }
        else if (aa <= Tiny)
        {
            (s, t) = (0, Math.Clamp(br / bb, 0, 1));
        }
        else if (bb <= Tiny)
        {
            (s, t) = (Math.Clamp(-ar / aa, 0, 1), 0);
        }
        else
        {
            var denominator = aa * bb - ab * ab;
            s = denominator > Tiny ? Math.Clamp((ab * br - ar * bb) / denominator, 0, 1) : 0;
            t = (ab * s + br) / bb;
            if (t < 0)
            {
                (s, t) = (Math.Clamp(-ar / aa, 0, 1), 0);
            }
            else if (t > 1)
            {
                (s, t) = (Math.Clamp((ab - ar) / aa, 0, 1), 1);
            }
        }
        return Vector3.DistanceSquared(a.At(s), b.At(t));
    }
}
