using System.Diagnostics;
using System.Numerics;
using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Navigation;
using OpenMagicka.Gameplay.Navigation;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Engine;

/// <summary>The game's levels' navigation (ENTITIES_AI.md §11), on every scene file (skipped without MAGICKA_DIR).</summary>
public class LevelNavigationTests(ITestOutputHelper output)
{
    private const int PathsPerLevel = 40;

    private static IEnumerable<(string Path, LevelModelContent Level)> Levels()
    {
        var loader = new ContentLoader(new ContentFileSystem(GameContent.ContentDirectory!), XnbReaderRegistry.Magicka);
        foreach (var path in loader.Files.ListFiles("Levels", recursive: true).Where(p => p.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase)))
        {
            if (loader.LoadUncached(path[..^4]).Root is LevelModelContent level)
            {
                yield return (path, level);
            }
        }
    }

    /// <summary>Every triangle of every mesh, on the ground plane (x, z), in the world.</summary>
    private static List<(Vector2 A, Vector2 B, Vector2 C)> Flat(NavMesh mesh) =>
        [.. new[] { mesh.Level }.Concat(mesh.Parts).SelectMany(s => s.Triangles.Select(t =>
            (Xz(s.ToWorld(s.Vertex(t.A))), Xz(s.ToWorld(s.Vertex(t.B))), Xz(s.ToWorld(s.Vertex(t.C))))))];

    private static Vector2 Xz(Vector3 v) => new(v.X, v.Z);

    /// <summary>The game's nav triangles face down by the right hand; a few face up.</summary>
    private static bool FacesUp(NavSurface surface, OpenMagicka.Gameplay.Navigation.NavTriangle t) =>
        Vector3.Cross(surface.Vertex(t.B) - surface.Vertex(t.A), surface.Vertex(t.C) - surface.Vertex(t.A)).Y > 1e-6f;

    /// <summary>Whether a point is over (or within 5 cm of) some triangle, seen from above.</summary>
    private static bool Above(List<(Vector2 A, Vector2 B, Vector2 C)> ground, Vector3 point)
    {
        var p = Xz(point);
        foreach (var (a, b, c) in ground)
        {
            if (Distance(p, a, b, c) < 0.05f)
            {
                return true;
            }
        }
        return false;
    }

    private static float Distance(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;
        var (d1, d2, d3) = (Cross(b - a, p - a), Cross(c - b, p - b), Cross(a - c, p - c));
        if ((d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0))
        {
            return 0;
        }
        static float ToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = ab.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1) : 0;
            return Vector2.Distance(p, a + ab * t);
        }
        return MathF.Min(ToSegment(p, a, b), MathF.Min(ToSegment(p, b, c), ToSegment(p, c, a)));
    }

    [GameContentFact]
    public void EveryLevel_FindsPathsThatStayOnItsMesh()
    {
        var (levels, parts, searches, reached, corners, off, reversed) = (0, 0, 0, 0, 0, 0, 0);
        var clock = new Stopwatch();
        var slowest = TimeSpan.Zero;
        var walker = NavMovement.Default | NavMovement.Dynamic | NavMovement.Water;
        foreach (var (file, level) in Levels())
        {
            var mesh = LevelNavigation.From(level);
            levels++;
            parts += mesh.Parts.Count;
            var surface = mesh.Level;
            var count = surface.Triangles.Count;
            if (count == 0)
            {
                continue;
            }
            var random = new Random(file.Length);
            var ground = Flat(mesh);
            reversed += mesh.Level.Triangles.Count(t => FacesUp(mesh.Level, t));
            var path = new List<PathNode>();
            for (var i = 0; i < PathsPerLevel; i++)
            {
                var from = surface.Centre(random.Next(count));
                var to = surface.Centre(random.Next(count));
                var started = clock.Elapsed;
                clock.Start();
                var found = mesh.FindPath(from, to, NavMovement.Default, path);
                clock.Stop();
                slowest = TimeSpan.FromTicks(Math.Max(slowest.Ticks, (clock.Elapsed - started).Ticks));
                searches++;
                reached += found ? 1 : 0;
                corners += path.Count;
                Assert.True(path.Count >= 2, $"{file}: {from} → {to} gave {path.Count} corners");
                Assert.All(path, n => Assert.True(float.IsFinite(n.Position.X + n.Position.Y + n.Position.Z), file));
                if (found)
                {
                    Assert.True(Vector3.Distance(path[^1].Position, mesh.Nearest(to, walker)!.Value.Point) < 1e-3f, $"{file}: ends off the end");
                }
                for (var k = 1; k < path.Count; k++)
                {
                    foreach (var t in new[] { 0.25f, 0.5f, 0.75f })
                    {
                        var on = Vector3.Lerp(path[k - 1].Position, path[k].Position, t);
                        if (!Above(ground, on))
                        {
                            off++;
                            output.WriteLine($"{file}: a leg {path[k - 1].Position} → {path[k].Position} leaves the mesh at {on}");
                            break;
                        }
                    }
                }
            }
        }
        output.WriteLine($"{off} legs off the mesh; {reversed} level nav triangles wound the other way");
        output.WriteLine($"{levels} scenes, {parts} animated nav meshes; {searches} searches, {reached} reached, {corners / (float)searches:F1} corners each; " +
            $"{clock.Elapsed.TotalMilliseconds / searches:F3} ms each, slowest {slowest.TotalMilliseconds:F1} ms");
        Assert.True(levels > 100);
        Assert.True(reached > searches * 0.5, $"{reached} of {searches} reached");
        Assert.True(off <= searches / 1000, $"{off} legs left the mesh");             // as the original: across a triangle wound the other way the funnel opens
    }
}
