using System.Numerics;

namespace OpenMagicka.Gameplay.Navigation;

/// <summary>What a nav triangle asks of a walker (the original's movement properties).</summary>
[Flags]
public enum NavMovement : byte
{
    Default = 0,
    Water = 1,
    Jump = 2,
    Fly = 4,

    /// <summary>A boundary triangle that may join an animated mesh.</summary>
    Dynamic = 0x80,

    All = 0xFF,
}

/// <summary>
/// One nav triangle: its corners, the triangles across its edges AB, BC and CA (<see cref="None"/>
/// for none), the editor's costs of crossing it between two edges, and what it asks.
/// </summary>
public readonly record struct NavTriangle(
    ushort A, ushort B, ushort C,
    ushort NeighbourA, ushort NeighbourB, ushort NeighbourC,
    float CostAB, float CostBC, float CostCA,
    NavMovement Movement)
{
    public const ushort None = ushort.MaxValue;

    /// <summary>Whether a walker with these abilities may stand on it: a plain triangle, or one asking what it has.</summary>
    public bool Admits(NavMovement abilities) => Movement == NavMovement.Default || (Movement & abilities) != 0;

    public ushort Corner(int i) => i switch { 0 => A, 1 => B, _ => C };

    public ushort Neighbour(int edge) => edge switch { 0 => NeighbourA, 1 => NeighbourB, _ => NeighbourC };

    /// <summary>
    /// The cost of crossing from the edge it was entered by (the one toward <paramref name="from"/>) to
    /// <paramref name="edge"/>; null when <paramref name="from"/> is across no other edge.
    /// </summary>
    public float? CostFrom(ushort from, int edge) => edge switch
    {
        0 => from == NeighbourB ? CostAB : from == NeighbourC ? CostCA : null,
        1 => from == NeighbourA ? CostAB : from == NeighbourC ? CostBC : null,
        _ => from == NeighbourA ? CostCA : from == NeighbourB ? CostBC : null,
    };
}

/// <summary>
/// A nav mesh's triangles in its own space, placed in the world by a transform (the level's mesh
/// stays put; an animated part's moves). Nearest points are exact; a tree of boxes only speeds them.
/// </summary>
public sealed class NavSurface
{
    private readonly Vector3[] vertices;
    private readonly NavTriangle[] triangles;
    private readonly TriangleTree tree;
    private readonly int[] dynamic;
    private Matrix4x4 transform = Matrix4x4.Identity, inverse = Matrix4x4.Identity;

    public NavSurface(IReadOnlyList<Vector3> vertices, IReadOnlyList<NavTriangle> triangles)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(triangles);
        if (vertices.Count > ushort.MaxValue || triangles.Count >= ushort.MaxValue)
        {
            throw new ArgumentException("a nav mesh holds at most 65535 vertices and 65534 triangles");
        }
        this.vertices = [.. vertices];
        this.triangles = [.. triangles];
        foreach (var t in this.triangles)
        {
            if (t.A >= this.vertices.Length || t.B >= this.vertices.Length || t.C >= this.vertices.Length)
            {
                throw new ArgumentException("a nav triangle names a vertex the mesh lacks");
            }
        }
        tree = new TriangleTree(this.vertices, this.triangles);
        dynamic = [.. Enumerable.Range(0, this.triangles.Length).Where(i => (this.triangles[i].Movement & NavMovement.Dynamic) != 0)];
    }

    public IReadOnlyList<Vector3> Vertices => vertices;

    public IReadOnlyList<NavTriangle> Triangles => triangles;

    /// <summary>Local to world.</summary>
    public Matrix4x4 Transform => transform;

    /// <summary>World to local.</summary>
    public Matrix4x4 Inverse => inverse;

    /// <summary>Bumped by each move; the nav mesh rejoins its meshes when one has moved.</summary>
    public int Version { get; private set; }

    /// <summary>A level's own mesh stays put (the nav mesh takes its points as they are).</summary>
    internal bool IsFixed { get; set; }

    /// <summary>The Dynamic triangles, which may join other meshes.</summary>
    public IReadOnlyList<int> Dynamic => dynamic;

    /// <summary>Moves the mesh (an animated part's, each frame); false when the transform cannot be inverted.</summary>
    public bool Place(Matrix4x4 world)
    {
        if (IsFixed)
        {
            throw new InvalidOperationException("a level's own nav mesh does not move");
        }
        if (!Matrix4x4.Invert(world, out var inverted))
        {
            return false;
        }
        (transform, inverse) = (world, inverted);
        Version++;
        return true;
    }

    public Vector3 ToWorld(Vector3 local) => Vector3.Transform(local, transform);

    public Vector3 ToLocal(Vector3 world) => Vector3.Transform(world, inverse);

    public Vector3 Vertex(int index) => vertices[index];

    /// <summary>The centre of a triangle, in local space (a third of the corners, as the original).</summary>
    public Vector3 Centre(int triangle)
    {
        var t = triangles[triangle];
        return (vertices[t.A] + vertices[t.B] + vertices[t.C]) * 0.333333f;
    }

    /// <summary>
    /// The nearest point (local space) on a triangle the abilities admit, nearer (squared) than
    /// <paramref name="within"/>; the triangle is -1 when there is none.
    /// </summary>
    public float Nearest(Vector3 local, NavMovement abilities, out Vector3 point, out int triangle, float within = float.MaxValue) =>
        tree.Nearest(local, abilities, within, out point, out triangle);

    /// <summary>The nearest point on one triangle (local space).</summary>
    public Vector3 Nearest(int triangle, Vector3 local)
    {
        var t = triangles[triangle];
        return Geometry.ClosestOnTriangle(local, vertices[t.A], vertices[t.B], vertices[t.C]);
    }

    /// <summary>Adds the Dynamic triangles whose boxes meet the box (local space) to the list, for joining meshes.</summary>
    public void DynamicIn(Vector3 min, Vector3 max, List<int> into) => tree.DynamicIn(min, max, into);
}
