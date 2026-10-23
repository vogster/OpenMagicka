using System.Diagnostics;
using System.Numerics;
using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Content.Xnb;
using OpenMagicka.Engine.Physics;
using OpenMagicka.Physics;
using OpenMagicka.Tests.Content;
using Xunit.Abstractions;

namespace OpenMagicka.Tests.Physics;

/// <summary>The physics spike of docs/adr/0003-physics.md on the game's levels (skipped without MAGICKA_DIR).</summary>
public class PhysicsSpikeTests(ITestOutputHelper output)
{
    private const float Tick = 1f / 60;
    private static readonly Vector3 Drop = new(0, 2, 0);
    private static readonly Vector3 Reach = new(0, 12, 0);

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

    private static Vector3 Numeric(Microsoft.Xna.Framework.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Nav triangle centres with level ground under them, and that ground's height.</summary>
    private static List<(Vector3 Spawn, float Ground)> SpawnPoints(PhysicsWorld world, NavMeshContent nav, int count)
    {
        var points = new List<(Vector3, float)>();
        var stride = Math.Max(1, nav.Triangles.Count / count);
        for (var i = 0; i < nav.Triangles.Count && points.Count < count; i += stride)
        {
            var t = nav.Triangles[i];
            var centre = (Numeric(nav.Vertices[t.A]) + Numeric(nav.Vertices[t.B]) + Numeric(nav.Vertices[t.C])) / 3;
            if (world.CastSegment(centre + Drop, centre + Drop - Reach) is { Material: not null } ground)
            {
                points.Add((centre + Drop, ground.Position.Y));
            }
        }
        return points;
    }

    private static BodyShape ShapeFor(int i) => (i % 4) switch
    {
        0 or 1 => new CapsuleShape(Radius: 0.4f, Length: 1),
        2 => new SphereShape(0.5f),
        _ => new BoxShape(new Vector3(1, 1, 1)),
    };

    private static List<(PhysicsBody Body, Vector3 Spawn, float Ground)> Populate(PhysicsWorld world, List<(Vector3 Spawn, float Ground)> points) =>
        [.. points.Select((p, i) => (world.AddBody(ShapeFor(i), p.Spawn, mass: 75, lockRotation: ShapeFor(i) is CapsuleShape), p.Spawn, p.Ground))];

    [GameContentFact]
    public void EveryLevel_HoldsTheBodiesDroppedOnIt()
    {
        int levels = 0, bodies = 0, rolledOff = 0;
        var through = new List<string>();
        foreach (var (path, level) in Levels())
        {
            using var world = new PhysicsWorld();
            if (LevelCollision.AddTo(world, level) == 0)
            {
                continue;
            }
            levels++;
            var dropped = Populate(world, SpawnPoints(world, level.NavMesh, count: 24));
            for (var tick = 0; tick < 180; tick++)
            {
                world.Step(Tick);
            }
            foreach (var (body, spawn, ground) in dropped)
            {
                bodies++;
                var moved = new Vector2(body.Position.X - spawn.X, body.Position.Z - spawn.Z).Length();
                if (body.Position.Y < ground - 1)
                {
                    if (moved < 0.5f)
                    {
                        through.Add($"{path}: {body.Shape} at {spawn} fell to {body.Position}");
                    }
                    else
                    {
                        rolledOff++;
                    }
                }
            }
        }
        output.WriteLine($"{levels} levels, {bodies} bodies dropped; {rolledOff} rolled off an edge, {through.Count} fell straight through");
        Assert.True(levels > 100, $"only {levels} levels with collision");
        Assert.Empty(through);
    }

    [GameContentFact]
    public void TheLargestLevel_StepsWithBodiesAndSegmentQueries_WithinTheTickBudget()
    {
        var (path, level) = Levels().MaxBy(l => l.Level.CollisionMeshes.Sum(m => m?.Triangles.Count ?? 0));
        var build = Stopwatch.StartNew();
        using var world = new PhysicsWorld();
        var triangles = LevelCollision.AddTo(world, level);
        build.Stop();
        var bodies = Populate(world, SpawnPoints(world, level.NavMesh, count: 64));
        var random = new Random(1);
        var nav = level.NavMesh.Vertices;
        var lift = Vector3.UnitY;

        var steps = new List<double>();
        var casts = new List<double>();
        var hits = 0;
        for (var tick = 0; tick < 660; tick++)
        {
            var clock = Stopwatch.StartNew();
            world.Step(Tick);
            var stepped = clock.Elapsed.TotalMilliseconds;
            for (var i = 0; i < 1000; i++)
            {
                var from = Numeric(nav[random.Next(nav.Count)]) + lift;
                var to = Numeric(nav[random.Next(nav.Count)]) + lift;
                hits += world.CastSegment(from, to) is null ? 0 : 1;
            }
            if (tick >= 60)
            {
                steps.Add(stepped);
                casts.Add(clock.Elapsed.TotalMilliseconds - stepped);
            }
        }

        steps.Sort();
        casts.Sort();
        var total = steps.Average() + casts.Average();
        output.WriteLine($"{path}: {triangles} triangles built in {build.Elapsed.TotalMilliseconds:F0} ms; {bodies.Count} bodies, {bodies.Count(b => b.Body.IsAwake)} awake at the end");
        output.WriteLine($"step: mean {steps.Average():F3} ms, p99 {steps[steps.Count * 99 / 100]:F3} ms, max {steps[^1]:F3} ms");
        output.WriteLine($"1000 segments: mean {casts.Average():F3} ms, p99 {casts[casts.Count * 99 / 100]:F3} ms; {hits * 100.0 / (660 * 1000):F1} % blocked");
        Assert.True(bodies.Count >= 32, $"only {bodies.Count} bodies found ground");
        Assert.True(total < 8, $"a tick of physics took {total:F2} ms on average");
    }
}
