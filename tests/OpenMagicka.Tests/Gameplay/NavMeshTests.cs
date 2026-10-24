using System.Numerics;
using OpenMagicka.Gameplay.Navigation;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The nav mesh: nearest points, A* and the funnel (ENTITIES_AI.md §11).</summary>
public class NavMeshTests
{
    /// <summary>
    /// A grid of 1 m cells on the ground, two triangles each, with the cells in <paramref name="holes"/>
    /// left out; neighbours by shared edges, costs between edge midpoints.
    /// </summary>
    internal static NavSurface Grid(int width, int depth, Func<int, int, NavMovement>? movement = null, Vector3 origin = default, params (int X, int Z)[] holes)
    {
        var vertices = new List<Vector3>();
        for (var z = 0; z <= depth; z++)
        {
            for (var x = 0; x <= width; x++)
            {
                vertices.Add(origin + new Vector3(x, 0, z));
            }
        }
        int V(int x, int z) => z * (width + 1) + x;
        var corners = new List<(ushort A, ushort B, ushort C, NavMovement M)>();
        for (var z = 0; z < depth; z++)
        {
            for (var x = 0; x < width; x++)
            {
                if (holes.Contains((x, z)))
                {
                    continue;
                }
                var m = movement?.Invoke(x, z) ?? NavMovement.Default;
                corners.Add(((ushort)V(x, z), (ushort)V(x + 1, z), (ushort)V(x + 1, z + 1), m));   // wound as the game's: facing down by the right hand
                corners.Add(((ushort)V(x, z), (ushort)V(x + 1, z + 1), (ushort)V(x, z + 1), m));
            }
        }
        var edges = new Dictionary<(ushort, ushort), int>();
        for (var i = 0; i < corners.Count; i++)
        {
            var (a, b, c, _) = corners[i];
            foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
            {
                edges[(p, q)] = i;
            }
        }
        ushort Across(ushort p, ushort q) => edges.TryGetValue((q, p), out var n) ? (ushort)n : NavTriangle.None;
        Vector3 Mid(ushort p, ushort q) => (vertices[p] + vertices[q]) / 2;
        var triangles = corners.Select(t => new NavTriangle(
            t.A, t.B, t.C,
            Across(t.A, t.B), Across(t.B, t.C), Across(t.C, t.A),
            Vector3.Distance(Mid(t.A, t.B), Mid(t.B, t.C)), Vector3.Distance(Mid(t.B, t.C), Mid(t.C, t.A)), Vector3.Distance(Mid(t.C, t.A), Mid(t.A, t.B)),
            t.M)).ToList();
        return new NavSurface(vertices, triangles);
    }

    private static List<PathNode> Path(NavMesh mesh, Vector3 from, Vector3 to, out bool reached, NavMovement abilities = NavMovement.Default)
    {
        var path = new List<PathNode>();
        reached = mesh.FindPath(from, to, abilities, path);
        return path;
    }

    /// <summary>The corners, a repeat dropped (the original may end with the end twice).</summary>
    private static List<Vector3> Corners(List<PathNode> path) =>
        [.. path.Select(n => n.Position).Where((p, i) => i == 0 || Vector3.DistanceSquared(p, path[i - 1].Position) > 1e-6f)];

    private static float Length(List<PathNode> path) => path.Zip(path.Skip(1), (a, b) => Vector3.Distance(a.Position, b.Position)).Sum();

    [Fact]
    public void TheNearestPoint_IsOnTheMesh_AndSkipsTrianglesThatAskForMore()
    {
        var mesh = new NavMesh(Grid(4, 4, (x, _) => x == 0 ? NavMovement.Jump : NavMovement.Default));

        var (above, _, d) = mesh.Nearest(new Vector3(2.3f, 5, 1.6f))!.Value;
        Assert.Equal(new Vector3(2.3f, 0, 1.6f), above);
        Assert.Equal(25, d, 3);

        var plain = mesh.Nearest(new Vector3(0.5f, 0, 2))!.Value;
        Assert.Equal(1, plain.Point.X, 4);                                          // the Jump column is not for a plain walker
        var jumper = mesh.Nearest(new Vector3(0.5f, 0, 2), NavMovement.Jump)!.Value;
        Assert.Equal(0.5f, jumper.Point.X, 4);
        Assert.Equal(NavMovement.Jump, mesh.Triangle(jumper.At).Movement);
    }

    [Fact]
    public void AnOpenFloor_GivesAStraightLine()
    {
        var mesh = new NavMesh(Grid(10, 10));

        var path = Path(mesh, new Vector3(0.5f, 0, 0.5f), new Vector3(9.5f, 0, 8.5f), out var reached);

        Assert.True(reached);
        Assert.Equal(2, Corners(path).Count);
        Assert.Equal(new Vector3(0.5f, 0, 0.5f), path[0].Position);
        Assert.Equal(new Vector3(9.5f, 0, 8.5f), path[^1].Position);
    }

    [Fact]
    public void AWall_IsWalkedAround_ByItsEnd_OnTheMesh()
    {
        var wall = Enumerable.Range(0, 8).Select(z => (4, z)).ToArray();          // column 4 is closed from z 0 to 7
        var mesh = new NavMesh(Grid(9, 10, holes: wall));

        var path = Path(mesh, new Vector3(1.3f, 0, 1.6f), new Vector3(7.6f, 0, 1.3f), out var reached);

        Assert.True(reached);
        var corners = Corners(path);
        Assert.Contains(new Vector3(4, 0, 8), corners);                             // round the wall's end, corner by corner
        Assert.Contains(new Vector3(5, 0, 8), corners);
        Assert.InRange(Length(path), 15, 22);
        for (var i = 1; i < corners.Count; i++)
        {
            for (var t = 0f; t <= 1; t += 0.05f)
            {
                var on = Vector3.Lerp(corners[i - 1], corners[i], t);
                Assert.True(mesh.Nearest(on)!.Value.DistanceSquared < 1e-6f, $"{on} leaves the mesh: {string.Join(" ", corners)}");
            }
        }
    }

    [Fact]
    public void AnUnreachableEnd_GivesAPathToTheLeastTotal_OnItsOwnSide()
    {
        var gap = Enumerable.Range(0, 6).Select(z => (3, z)).ToArray();
        var mesh = new NavMesh(Grid(7, 6, holes: gap));

        var path = Path(mesh, new Vector3(0.5f, 0, 3), new Vector3(6.5f, 0, 3), out var reached);

        Assert.False(reached);
        Assert.NotEmpty(path);
        Assert.All(path, n => Assert.True(n.Position.X <= 3 + 1e-3f, string.Join(" ", Corners(path))));
        Assert.Equal(new Vector3(0.5f, 0, 3), path[0].Position);                  // on an even floor no node totals less than the start
    }

    [Fact]
    public void Water_IsCrossedOnlyOverIce()
    {
        var mesh = new NavMesh(Grid(7, 3, (x, _) => x == 3 ? NavMovement.Water : NavMovement.Default));
        var from = new Vector3(0.5f, 0, 1.5f);
        var to = new Vector3(6.5f, 0, 1.5f);

        Path(mesh, from, to, out var dry);
        Assert.False(dry);

        mesh.IceAlong = (_, _) => true;
        var path = Path(mesh, from, to, out var frozen);
        Assert.True(frozen);
        Assert.Equal(2, Corners(path).Count);
    }

    [Fact]
    public void AJumpPatch_IsWalkedAround_UnlessTheWalkerJumps()
    {
        var mesh = new NavMesh(Grid(7, 7, (x, z) => x == 3 && z < 6 ? NavMovement.Jump : NavMovement.Default));
        var from = new Vector3(0.5f, 0, 1.5f);
        var to = new Vector3(6.5f, 0, 1.5f);

        var around = Path(mesh, from, to, out var walked);
        var over = Path(mesh, from, to, out var jumped, NavMovement.Jump);

        Assert.True(walked && jumped);
        Assert.True(Length(around) > Length(over) + 4);
        Assert.Equal(2, Corners(over).Count);
    }

    [Fact]
    public void APartsMesh_JoinsTheLevelByDynamicTriangles_AndOnlyWhileNear()
    {
        var level = Grid(4, 3, (x, _) => x == 3 ? NavMovement.Dynamic : NavMovement.Default);
        var platform = Grid(4, 3, (x, _) => x == 0 ? NavMovement.Dynamic : NavMovement.Default);
        var mesh = new NavMesh(level);
        mesh.AddPart(platform);
        platform.Place(Matrix4x4.CreateTranslation(4.1f, 0, 0));

        var path = Path(mesh, new Vector3(0.5f, 0, 1.5f), new Vector3(7.5f, 0, 1.5f), out var reached);
        Assert.True(reached);
        Assert.Equal(7.5f, path[^1].Position.X, 3);
        Assert.Contains(path, n => n.Movement.HasFlag(NavMovement.Dynamic));

        platform.Place(Matrix4x4.CreateTranslation(6, 0, 0));
        Path(mesh, new Vector3(0.5f, 0, 1.5f), new Vector3(9.5f, 0, 1.5f), out var parted);
        Assert.False(parted);
        Assert.Equal(new Vector3(9.5f, 0, 1.5f), mesh.Nearest(new Vector3(9.5f, 3, 1.5f))!.Value.Point);   // the part's nearest point, placed
    }

    [Fact]
    public void ABrokenStart_GivesNoPath()
    {
        var mesh = new NavMesh(Grid(3, 3));

        var path = Path(mesh, new Vector3(float.NaN, 0, 0), Vector3.One, out var reached);

        Assert.False(reached);
        Assert.Empty(path);
    }

    [Fact]
    public void AnEmptyMesh_HasNoNearestPoint_AndNoPath()
    {
        var mesh = new NavMesh(new NavSurface([], []));

        Assert.Null(mesh.Nearest(Vector3.Zero));
        Assert.Equal(Vector3.One, mesh.NearestOr(Vector3.One));
        Assert.Empty(Path(mesh, Vector3.Zero, Vector3.One, out _));
    }

    [Fact]
    public void TheNearestPoint_IsTheSameAsLookingAtEveryTriangle()
    {
        var random = new Random(7);
        var vertices = Enumerable.Range(0, 300).Select(_ => new Vector3(random.NextSingle() * 40, random.NextSingle() * 4, random.NextSingle() * 40)).ToList();
        var triangles = Enumerable.Range(0, 400).Select(i => new NavTriangle(
            (ushort)random.Next(300), (ushort)random.Next(300), (ushort)random.Next(300),
            NavTriangle.None, NavTriangle.None, NavTriangle.None, 1, 1, 1,
            i % 5 == 0 ? NavMovement.Jump : NavMovement.Default)).ToList();
        var surface = new NavSurface(vertices, triangles);

        for (var n = 0; n < 500; n++)
        {
            var p = new Vector3(random.NextSingle() * 50 - 5, random.NextSingle() * 10 - 3, random.NextSingle() * 50 - 5);
            var d = surface.Nearest(p, NavMovement.Default, out var on, out var at);
            var best = Enumerable.Range(0, triangles.Count).Where(i => triangles[i].Movement == NavMovement.Default)
                .Min(i => Vector3.DistanceSquared(p, surface.Nearest(i, p)));
            Assert.Equal(best, d, 3);
            Assert.Equal(d, Vector3.DistanceSquared(p, on), 3);
            Assert.Equal(NavMovement.Default, triangles[at].Movement);
        }
    }

    [Fact]
    public void ADegenerateTriangle_GivesItsPoints_NotNaN()
    {
        var surface = new NavSurface([Vector3.Zero, Vector3.Zero, new Vector3(2, 0, 0), new Vector3(4, 0, 0)],
            [new NavTriangle(0, 1, 2, NavTriangle.None, NavTriangle.None, NavTriangle.None, 1, 1, 1, NavMovement.Default),
             new NavTriangle(0, 2, 3, NavTriangle.None, NavTriangle.None, NavTriangle.None, 1, 1, 1, NavMovement.Default)]);

        foreach (var p in new[] { new Vector3(1, 1, 0.5f), new Vector3(-1, 0, 0), new Vector3(3, 2, 0) })
        {
            surface.Nearest(p, NavMovement.Default, out var on, out _);
            Assert.True(float.IsFinite(on.X + on.Y + on.Z), $"{p}: {on}");
            Assert.Equal(0, on.Z, 4);
        }
    }

    [Fact]
    public void TwoParts_JoinEachOther_AndARotatedPartIsWalked()
    {
        var level = Grid(3, 3, (x, _) => x == 2 ? NavMovement.Dynamic : NavMovement.Default);
        var first = Grid(3, 3, (x, _) => x == 0 || x == 2 ? NavMovement.Dynamic : NavMovement.Default);
        var second = Grid(3, 3, (_, z) => z == 0 ? NavMovement.Dynamic : NavMovement.Default);
        var mesh = new NavMesh(level);
        mesh.AddPart(first);
        mesh.AddPart(second);
        first.Place(Matrix4x4.CreateTranslation(3.1f, 0, 0));
        second.Place(Matrix4x4.CreateRotationY(-MathF.PI / 2) * Matrix4x4.CreateTranslation(6.1f, 0, 0));     // its z = 0 edge lies along x = 6.1

        var end = second.ToWorld(new Vector3(1.5f, 0, 2.5f));
        var path = Path(mesh, new Vector3(0.5f, 0, 1.5f), end, out var reached);

        Assert.True(reached);
        Assert.True(Vector3.Distance(path[^1].Position, end) < 1e-3f);
        Assert.Equal(new Vector3(end.X, 0, end.Z), mesh.Nearest(end + Vector3.UnitY)!.Value.Point, new NearVector());
    }

    [Fact]
    public void TheLevelsOwnMesh_DoesNotMove_AndAMeshIsAddedOnce()
    {
        var level = Grid(2, 2);
        var part = Grid(2, 2);
        var mesh = new NavMesh(level);
        mesh.AddPart(part);

        Assert.Throws<InvalidOperationException>(() => level.Place(Matrix4x4.CreateTranslation(1, 0, 0)));
        Assert.Throws<ArgumentException>(() => mesh.AddPart(part));
        Assert.Throws<ArgumentException>(() => mesh.AddPart(level));
    }

    private sealed class NearVector : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.DistanceSquared(a, b) < 1e-6f;

        public int GetHashCode(Vector3 v) => 0;
    }
}
