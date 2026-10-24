using System.Numerics;

namespace OpenMagicka.Gameplay.Navigation;

public sealed partial class NavMesh
{
    /// <summary>Within this distance of each other, two Dynamic triangles of different meshes join.</summary>
    public const float JoinDistance = 0.25f;

    /// <summary>A way across to a triangle of another mesh: what it costs, and where it is reckoned to be (world space).</summary>
    internal readonly record struct Joint(NavRef To, float Cost, Vector3 EdgePos);

    private readonly Dictionary<uint, List<Joint>> joints = [];
    private readonly List<int> candidates = [];
    private int[] joinedVersions = [];

    /// <summary>
    /// The joints between the meshes: each Dynamic triangle of the level with those of every part,
    /// then each part with the later ones. Two join when within 0.25; the cost is between their
    /// centres. The original rebuilds them before every search; we do when a part has moved, which
    /// gives the same joints.
    /// </summary>
    private void JoinParts()
    {
        if (joinedVersions.Length == parts.Count && parts.Select(p => p.Version).SequenceEqual(joinedVersions))
        {
            return;
        }
        joinedVersions = [.. parts.Select(p => p.Version)];
        joints.Clear();
        foreach (var triangle in Level.Dynamic)
        {
            var (a, b, c) = Corners(Level, triangle);
            for (var j = 0; j < parts.Count; j++)
            {
                var part = parts[j];
                Join(new NavRef(NavRef.Level, (ushort)triangle), (part.ToLocal(a), part.ToLocal(b), part.ToLocal(c)), (ushort)j, part);
            }
        }
        for (var k = 0; k < parts.Count; k++)
        {
            var part = parts[k];
            foreach (var triangle in part.Dynamic)
            {
                var (a, b, c) = Corners(part, triangle);
                (a, b, c) = (part.ToWorld(a), part.ToWorld(b), part.ToWorld(c));
                for (var i = k + 1; i < parts.Count; i++)
                {
                    var other = parts[i];
                    Join(new NavRef((ushort)k, (ushort)triangle), (other.ToLocal(a), other.ToLocal(b), other.ToLocal(c)), (ushort)i, part);   // as the original: the other's centre placed by this part
                }
            }
        }
    }

    /// <summary>
    /// Joins one triangle (its corners in the other mesh's space) with the other mesh's Dynamic
    /// triangles near it, both ways; <paramref name="placer"/> puts the other's centre in the world.
    /// </summary>
    private void Join(NavRef from, (Vector3 A, Vector3 B, Vector3 C) corners, ushort mesh, NavSurface placer)
    {
        var other = parts[mesh];
        var margin = new Vector3(JoinDistance);
        var min = Vector3.Min(corners.A, Vector3.Min(corners.B, corners.C)) - margin;
        var max = Vector3.Max(corners.A, Vector3.Max(corners.B, corners.C)) + margin;
        var centre = (corners.A + corners.B + corners.C) * (1f / 3);
        candidates.Clear();
        other.DynamicIn(min, max, candidates);
        foreach (var t in candidates)
        {
            var (a, b, c) = Corners(other, t);
            if (!(Geometry.TriangleTriangleSquared(a, b, c, corners.A, corners.B, corners.C) <= JoinDistance * JoinDistance))
            {
                continue;
            }
            var theirs = (a + b + c) * (1f / 3);
            var cost = Vector3.Distance(centre, theirs);
            var edge = placer.ToWorld(theirs);
            var to = new NavRef(mesh, (ushort)t);
            Across(from).Add(new Joint(to, cost, edge));
            Across(to).Add(new Joint(from, cost, edge));                      // as the original: the same spot both ways
        }
    }

    private List<Joint> Across(NavRef at)
    {
        if (!joints.TryGetValue(at.Key, out var list))
        {
            joints[at.Key] = list = [];
        }
        return list;
    }

    private static (Vector3, Vector3, Vector3) Corners(NavSurface surface, int triangle)
    {
        var t = surface.Triangles[triangle];
        return (surface.Vertex(t.A), surface.Vertex(t.B), surface.Vertex(t.C));
    }
}
