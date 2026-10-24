using System.Numerics;

namespace OpenMagicka.Gameplay.Navigation;

/// <summary>Closest points and distances between points, segments and triangles.</summary>
internal static class Geometry
{
    /// <summary>The point of triangle abc nearest p (by its Voronoi regions).</summary>
    public static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0)
        {
            return a;
        }
        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3)
        {
            return b;
        }
        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            return d1 - d3 > 1e-20f ? a + ab * (d1 / (d1 - d3)) : a;          // a repeated corner: no edge to slide along
        }
        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6)
        {
            return c;
        }
        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            return d2 - d6 > 1e-20f ? a + ac * (d2 / (d2 - d6)) : a;
        }
        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            var span = d4 - d3 + (d5 - d6);
            return span > 1e-20f ? b + (c - b) * ((d4 - d3) / span) : b;
        }
        var denominator = va + vb + vc;
        if (MathF.Abs(denominator) < 1e-20f)
        {
            return a;                                                         // a degenerate triangle: its first corner
        }
        var v = vb / denominator;
        var w = vc / denominator;
        return a + ab * v + ac * w;
    }

    /// <summary>The squared distance between segments pq and rs.</summary>
    public static float SegmentSegmentSquared(Vector3 p, Vector3 q, Vector3 r, Vector3 s)
    {
        var d1 = q - p;
        var d2 = s - r;
        var e = p - r;
        var a = Vector3.Dot(d1, d1);
        var f = Vector3.Dot(d2, d2);
        var g = Vector3.Dot(d2, e);
        float t, u;
        if (a <= 1e-12f && f <= 1e-12f)
        {
            return Vector3.DistanceSquared(p, r);
        }
        if (a <= 1e-12f)
        {
            (t, u) = (0, Math.Clamp(g / f, 0, 1));
        }
        else
        {
            var c = Vector3.Dot(d1, e);
            if (f <= 1e-12f)
            {
                (t, u) = (Math.Clamp(-c / a, 0, 1), 0);
            }
            else
            {
                var b = Vector3.Dot(d1, d2);
                var denominator = a * f - b * b;
                t = denominator > 1e-12f ? Math.Clamp((b * g - c * f) / denominator, 0, 1) : 0;
                u = (b * t + g) / f;
                if (u < 0)
                {
                    (u, t) = (0, Math.Clamp(-c / a, 0, 1));
                }
                else if (u > 1)
                {
                    (u, t) = (1, Math.Clamp((b - c) / a, 0, 1));
                }
            }
        }
        return Vector3.DistanceSquared(p + d1 * t, r + d2 * u);
    }

    /// <summary>
    /// The squared distance between two triangles: none when they cross, else the least of the
    /// corner-to-triangle and edge-to-edge distances.
    /// </summary>
    public static float TriangleTriangleSquared(Vector3 a0, Vector3 a1, Vector3 a2, Vector3 b0, Vector3 b1, Vector3 b2)
    {
        ReadOnlySpan<Vector3> a = [a0, a1, a2], b = [b0, b1, b2];
        if (Crosses(a, b) || Crosses(b, a))
        {
            return 0;
        }
        var best = float.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            best = MathF.Min(best, Vector3.DistanceSquared(a[i], ClosestOnTriangle(a[i], b0, b1, b2)));
            best = MathF.Min(best, Vector3.DistanceSquared(b[i], ClosestOnTriangle(b[i], a0, a1, a2)));
            for (var j = 0; j < 3; j++)
            {
                best = MathF.Min(best, SegmentSegmentSquared(a[i], a[(i + 1) % 3], b[j], b[(j + 1) % 3]));
            }
        }
        return best;
    }

    /// <summary>Whether an edge of triangle <paramref name="edges"/> passes through triangle <paramref name="face"/>.</summary>
    private static bool Crosses(ReadOnlySpan<Vector3> edges, ReadOnlySpan<Vector3> face)
    {
        for (var i = 0; i < 3; i++)
        {
            if (SegmentHitsTriangle(edges[i], edges[(i + 1) % 3], face[0], face[1], face[2]))
            {
                return true;
            }
        }
        return false;
    }

    private static bool SegmentHitsTriangle(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var qp = p - q;
        var n = Vector3.Cross(ab, ac);
        var d = Vector3.Dot(qp, n);
        if (MathF.Abs(d) < 1e-12f)
        {
            return false;
        }
        var ap = p - a;
        var t = Vector3.Dot(ap, n) / d;
        if (t < 0 || t > 1)
        {
            return false;
        }
        var e = Vector3.Cross(qp, ap);
        var v = Vector3.Dot(ac, e) / d;
        var w = -Vector3.Dot(ab, e) / d;
        return v >= 0 && w >= 0 && v + w <= 1;
    }

    /// <summary>Half the cross product (c − a) × (b − a) on the ground plane: the signed area of abc, by the original's sign.</summary>
    public static float Area2D(Vector3 a, Vector3 b, Vector3 c) =>
        (b.X * a.Z - a.X * b.Z + (c.X * b.Z - b.X * c.Z) + (a.X * c.Z - c.X * a.Z)) * 0.5f;

    /// <summary>The walker's heuristic: |dx| + 1.75·|dz|.</summary>
    public static float Manhattan(Vector3 a, Vector3 b) => MathF.Abs(a.X - b.X) + MathF.Abs(a.Z - b.Z) * 1.75f;
}
