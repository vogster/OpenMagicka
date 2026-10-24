using System.Numerics;

namespace OpenMagicka.Gameplay.Navigation;

/// <summary>A triangle of one of the level's nav meshes: the level's own (<see cref="Level"/>), or an animated part's by index.</summary>
public readonly record struct NavRef(ushort Mesh, ushort Triangle)
{
    public const ushort Level = ushort.MaxValue;

    public uint Key => (uint)(Mesh << 16 | Triangle);

    public static NavRef Of(uint key) => new((ushort)(key >> 16), (ushort)key);
}

/// <summary>A corner of a path, and what the triangle it lies on asks (a jump, a moving part).</summary>
public readonly record struct PathNode(Vector3 Position, NavMovement Movement);

/// <summary>
/// A level's navigation (ENTITIES_AI.md §11): its own nav mesh and those of its animated parts,
/// nearest walkable points, and paths by A* over triangles, pulled straight.
/// </summary>
public sealed partial class NavMesh
{
    private readonly List<NavSurface> parts = [];

    public NavMesh(NavSurface level)
    {
        Level = level ?? throw new ArgumentNullException(nameof(level));
        if (level.Transform != Matrix4x4.Identity)
        {
            throw new ArgumentException("a level's own nav mesh stands where it is", nameof(level));
        }
        level.IsFixed = true;
    }

    /// <summary>The level's own mesh, which stays put.</summary>
    public NavSurface Level { get; }

    /// <summary>The animated parts' meshes, by index.</summary>
    public IReadOnlyList<NavSurface> Parts => parts;

    /// <summary>
    /// Whether there is ice along a segment (world space); a Water triangle is only walkable over
    /// ice. The level's liquids answer it; without them, there is none.
    /// </summary>
    public Func<Vector3, Vector3, bool>? IceAlong { get; set; }

    /// <summary>Adds an animated part's mesh; its index names it.</summary>
    public int AddPart(NavSurface part)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (part.IsFixed || parts.Contains(part))
        {
            throw new ArgumentException("a mesh is the level's or a part's, once", nameof(part));
        }
        if (parts.Count >= NavRef.Level - 1)
        {
            throw new InvalidOperationException("too many animated nav meshes");
        }
        parts.Add(part);
        return parts.Count - 1;
    }

    public NavSurface Surface(ushort mesh) => mesh == NavRef.Level ? Level : parts[mesh];

    public NavTriangle Triangle(NavRef at) => Surface(at.Mesh).Triangles[at.Triangle];

    /// <summary>
    /// The nearest point (world space) on a triangle the abilities admit, of all the meshes, and the
    /// squared distance to it; null when no triangle admits them. Each part is searched in its own
    /// space, as the original (so a scaled part's distances are its own).
    /// </summary>
    public (Vector3 Point, NavRef At, float DistanceSquared)? Nearest(Vector3 point, NavMovement abilities = NavMovement.Default)
    {
        var best = Level.Nearest(point, abilities, out var on, out var triangle);
        var found = triangle >= 0 ? (on, new NavRef(NavRef.Level, (ushort)triangle)) : ((Vector3, NavRef)?)null;
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var d = part.Nearest(part.ToLocal(point), abilities, out var local, out var t, best);
            if (t >= 0 && d < best)
            {
                best = d;
                found = (part.ToWorld(local), new NavRef((ushort)i, (ushort)t));
            }
        }
        return found is var (p, at) ? (p, at, best) : null;
    }

    /// <summary>The nearest walkable point, or the point itself when there is none.</summary>
    public Vector3 NearestOr(Vector3 point, NavMovement abilities = NavMovement.Default) => Nearest(point, abilities)?.Point ?? point;

    /// <summary>
    /// A path from start to end for a walker with these abilities (it may always cross Dynamic
    /// triangles, and Water over ice), corner by corner from start to end. True when it reaches
    /// the end's triangle; else it goes as near as the search got.
    /// </summary>
    public bool FindPath(Vector3 start, Vector3 end, NavMovement abilities, List<PathNode> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        path.Clear();
        var triangles = new List<NavRef>();
        var reached = Search(start, end, abilities, triangles);
        Straighten(start, end, triangles, path);
        return reached;
    }
}
