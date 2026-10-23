using Microsoft.Xna.Framework;
using OpenMagicka.Content.Magicka;

namespace OpenMagicka.Engine.Cameras;

/// <summary>
/// The box a script locks the camera into (a trigger area's): a corner, the side lengths along the
/// box's own axes and its orientation. The camera's target is kept inside it.
/// </summary>
public sealed record CameraBox(Vector3 Corner, Vector3 SideLengths, Quaternion Orientation)
{
    public static CameraBox From(TriggerAreaContent area)
    {
        ArgumentNullException.ThrowIfNull(area);
        return new CameraBox(area.Corner, area.SideLengths, area.Orientation);
    }

    /// <summary>The point of the box nearest to <paramref name="point"/> (the point itself when inside).</summary>
    public Vector3 ClosestPoint(Vector3 point)
    {
        var orientation = Quaternion.Normalize(Orientation);
        var local = Vector3.Transform(point - Corner, Quaternion.Inverse(orientation));
        var clamped = Vector3.Clamp(local, Vector3.Zero, SideLengths);
        return Corner + Vector3.Transform(clamped, orientation);
    }
}

/// <summary>
/// A level's camera mesh: triangles the camera's target is snapped onto, so the camera stays over
/// the playable area. Immutable.
/// </summary>
public sealed class CameraMesh
{
    private readonly Vector3[] corners;

    public CameraMesh(CollisionMeshContent mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var vertices = mesh.Vertices;
        corners = new Vector3[mesh.Triangles.Count * 3];
        for (var i = 0; i < mesh.Triangles.Count; i++)
        {
            var (a, b, c) = (mesh.Triangles[i].I0, mesh.Triangles[i].I1, mesh.Triangles[i].I2);
            if (!InRange(a, vertices.Count) || !InRange(b, vertices.Count) || !InRange(c, vertices.Count))
            {
                throw new InvalidDataException($"camera mesh triangle {i} refers to a vertex beyond the {vertices.Count} it has");
            }
            (corners[i * 3], corners[i * 3 + 1], corners[i * 3 + 2]) = (vertices[a], vertices[b], vertices[c]);
        }
    }

    public int TriangleCount => corners.Length / 3;

    /// <summary>
    /// The nearest point on the nearest triangle (the first of equally near ones); the point itself
    /// for a mesh without triangles.
    /// </summary>
    public Vector3 ClosestPoint(Vector3 point)
    {
        var best = point;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < corners.Length; i += 3)
        {
            var candidate = ClosestPoint(point, corners[i], corners[i + 1], corners[i + 2]);
            var distance = Vector3.DistanceSquared(point, candidate);
            if (distance < bestDistance)
            {
                (best, bestDistance) = (candidate, distance);
            }
        }
        return best;
    }

    /// <summary>
    /// The point of triangle abc nearest to <paramref name="p"/>, found by the Voronoi region it lies
    /// in. A flat triangle (corners that coincide or lie on a line) is taken as its three edges.
    /// </summary>
    public static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var (ab, ac, ap) = (b - a, c - a, p - a);
        if (Vector3.Cross(ab, ac).LengthSquared() < 1e-20f)
        {
            return Nearest(p, Nearest(p, OnSegment(p, a, b), OnSegment(p, a, c)), OnSegment(p, b, c));
        }
        var (d1, d2) = (Vector3.Dot(ab, ap), Vector3.Dot(ac, ap));
        if (d1 <= 0 && d2 <= 0)
        {
            return a;
        }
        var bp = p - b;
        var (d3, d4) = (Vector3.Dot(ab, bp), Vector3.Dot(ac, bp));
        if (d3 >= 0 && d4 <= d3)
        {
            return b;
        }
        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            return a + ab * (d1 / (d1 - d3));
        }
        var cp = p - c;
        var (d5, d6) = (Vector3.Dot(ab, cp), Vector3.Dot(ac, cp));
        if (d6 >= 0 && d5 <= d6)
        {
            return c;
        }
        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            return a + ac * (d2 / (d2 - d6));
        }
        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            return b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));
        }
        var sum = va + vb + vc;
        return a + ab * (vb / sum) + ac * (vc / sum);
    }

    private static Vector3 OnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var length = ab.LengthSquared();
        return length > 0 ? a + ab * Math.Clamp(Vector3.Dot(p - a, ab) / length, 0, 1) : a;
    }

    private static Vector3 Nearest(Vector3 p, Vector3 x, Vector3 y) => Vector3.DistanceSquared(p, x) <= Vector3.DistanceSquared(p, y) ? x : y;

    private static bool InRange(int index, int count) => index >= 0 && index < count;
}
