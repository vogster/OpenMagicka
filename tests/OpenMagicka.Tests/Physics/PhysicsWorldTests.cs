using System.Numerics;
using OpenMagicka.Physics;

namespace OpenMagicka.Tests.Physics;

public class PhysicsWorldTests
{
    private const float Tick = 1f / 60;

    private static readonly Vector3[] FloorCorners = [new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20), new(-20, 0, 20)];

    /// <summary>Two triangles whose right-hand normal (B − A) × (C − A) points up.</summary>
    private static readonly MeshTriangle[] UpFacing = [new(0, 2, 1), new(0, 3, 2)];

    /// <summary>A 40 × 40 floor at y = 0, solid from above.</summary>
    private static void AddFloor(PhysicsWorld world, int material = 3) => world.AddStaticMesh(material, FloorCorners, UpFacing);

    private static void Run(PhysicsWorld world, float seconds)
    {
        for (var t = 0f; t < seconds; t += Tick)
        {
            world.Step(Tick);
        }
    }

    [Fact]
    public void TheDefaultWorld_HasTheGamesDoubleGravity()
    {
        Assert.Equal(new Vector3(0, -19.64f, 0), PhysicsSettings.Default.Gravity);
        using var world = new PhysicsWorld();
        Assert.Equal(PhysicsSettings.Default, world.Settings);
    }

    [Fact]
    public void ABall_FallsOntoTheFloor_AndRestsOnIt()
    {
        using var world = new PhysicsWorld();
        AddFloor(world);
        var ball = world.AddBody(new SphereShape(0.5f), new Vector3(0, 3, 0), mass: 10);

        Run(world, 3);

        Assert.Equal(0.5f, ball.Position.Y, 1);
        Assert.True(ball.Velocity.Length() < 0.1f, $"still moving at {ball.Velocity}");
    }

    [Fact]
    public void ATriangle_IsSolidOnlyFromItsFront()
    {
        using var world = new PhysicsWorld();
        world.AddStaticMesh(0, FloorCorners, [.. UpFacing.Select(t => new MeshTriangle(t.A, t.C, t.B))]);
        var ball = world.AddBody(new SphereShape(0.5f), new Vector3(0, 3, 0), mass: 10);

        Run(world, 1);

        Assert.True(ball.Position.Y < -1, $"a downward-facing floor should not hold the ball: {ball.Position}");
        Assert.Null(world.CastSegment(new Vector3(1, 4, 2), new Vector3(1, -4, 2)));
        Assert.NotNull(world.CastSegment(new Vector3(1, -4, 2), new Vector3(1, 4, 2)));
    }

    [Fact]
    public void ASegment_HitsTheFloor_WithItsMaterial()
    {
        using var world = new PhysicsWorld();
        AddFloor(world, material: 7);

        var hit = world.CastSegment(new Vector3(1, 4, 2), new Vector3(1, -4, 2));

        Assert.NotNull(hit);
        Assert.Equal(0.5f, hit.Value.Fraction, 4);
        Assert.Equal(new Vector3(1, 0, 2), hit.Value.Position);
        Assert.True(hit.Value.Normal.Y > 0.99f, $"normal {hit.Value.Normal}");
        Assert.Equal(7, hit.Value.Material);
        Assert.Null(hit.Value.Body);
        Assert.Null(world.CastSegment(new Vector3(1, 4, 2), new Vector3(1, 1, 2)));   // stops short of the floor
    }

    [Theory]
    [InlineData(0f, 0f)]            // the diagonal both triangles share
    [InlineData(7f, 7.000001f)]
    [InlineData(20f, 3f)]           // the outer edge
    [InlineData(-20f, -20f)]        // a corner
    public void ASegment_DoesNotSlipThroughAnEdge(float x, float z)
    {
        using var world = new PhysicsWorld();
        AddFloor(world);

        var hit = world.CastSegment(new Vector3(x, 4, z), new Vector3(x, -4, z));

        Assert.Equal(0f, hit?.Position.Y);
    }

    [Fact]
    public void ASegment_HitsTheNearestBody_UnlessItIsIgnored()
    {
        using var world = new PhysicsWorld();
        AddFloor(world);
        var crate = world.AddBody(new BoxShape(new Vector3(2, 2, 2)), new Vector3(0, 1, 0), mass: 50);

        var hit = world.CastSegment(new Vector3(0, 5, 0), new Vector3(0, -1, 0));
        var through = world.CastSegment(new Vector3(0, 5, 0), new Vector3(0, -1, 0), ignore: crate);

        Assert.Same(crate, hit?.Body);
        Assert.Null(hit?.Material);
        Assert.Equal(2f, hit!.Value.Position.Y, 3);
        Assert.Equal(0f, through!.Value.Position.Y, 3);
    }

    [Fact]
    public void ACharacterCapsule_StaysUpright_WhenShoved()
    {
        using var world = new PhysicsWorld();
        AddFloor(world);
        var character = world.AddBody(new CapsuleShape(Radius: 0.5f, Length: 1), new Vector3(0, 1, 0), mass: 75, lockRotation: true);
        var crate = world.AddBody(new BoxShape(new Vector3(1, 1, 1)), new Vector3(-3, 0.5f, 0), mass: 20);

        crate.Velocity = new Vector3(15, 0, 0);
        Run(world, 2);

        Assert.Equal(Quaternion.Identity, character.Orientation);
        Assert.Equal(1f, character.Position.Y, 1);       // half the length plus the radius
        Assert.True(character.Position.X > 0, "the crate should have pushed the character");
    }

    [Fact]
    public void AFastProjectile_DoesNotTunnelThroughAThinWall()
    {
        using var world = new PhysicsWorld(PhysicsSettings.Default with { Gravity = Vector3.Zero });
        Vector3[] wall = [new(5, -5, -5), new(5, 5, -5), new(5, 5, 5), new(5, -5, 5)];
        world.AddStaticMesh(0, wall, [new MeshTriangle(0, 2, 1), new MeshTriangle(0, 3, 2)]);   // facing −X
        var boulder = world.AddBody(new SphereShape(0.225f), Vector3.Zero, mass: 101, fast: true);

        boulder.Velocity = new Vector3(110, 0, 0);       // 1.8 m per tick
        Run(world, 0.5f);

        Assert.True(boulder.Position.X < 5, $"tunnelled to {boulder.Position}");
    }

    [Fact]
    public void ASegmentStartingInsideABody_HitsItAtOnce_FacingBack()
    {
        using var world = new PhysicsWorld();
        var ball = world.AddBody(new SphereShape(1), new Vector3(0, 5, 0), mass: 1);

        var hit = world.CastSegment(new Vector3(0, 5, 0), new Vector3(0, -5, 0));

        Assert.Equal((0f, ball, Vector3.UnitY), (hit!.Value.Fraction, hit.Value.Body, hit.Value.Normal));
    }

    [Fact]
    public void ASegment_ExcludesItsEnd_AndOneOfNoLengthHitsNothing()
    {
        using var world = new PhysicsWorld();
        AddFloor(world);
        world.AddBody(new SphereShape(1), new Vector3(0, 5, 0), mass: 1);

        Assert.Null(world.CastSegment(new Vector3(1, 4, 1), new Vector3(1, 0, 1)));      // ends exactly on the floor
        Assert.Null(world.CastSegment(new Vector3(0, 5, 0), new Vector3(0, 5, 0)));      // inside the ball, but no length
    }

    [Fact]
    public void AStaticMesh_KeepsItsMaterial_AndDropsDegenerateTriangles()
    {
        using var world = new PhysicsWorld();
        Vector3[] vertices = [.. FloorCorners, new(0, 0, 0)];

        var mesh = world.AddStaticMesh(4, vertices, [.. UpFacing, new MeshTriangle(0, 0, 1)]);
        var flat = world.AddStaticMesh(5, vertices, [new MeshTriangle(0, 4, 2)]);   // three points on one line

        Assert.Equal((4, 2), (mesh.Material, mesh.TriangleCount));
        Assert.Equal(0, flat.TriangleCount);
    }

    [Fact]
    public void ARemovedBody_LeavesTheWorld_AndCanNoLongerBeUsed()
    {
        using var world = new PhysicsWorld();
        AddFloor(world);
        var ball = world.AddBody(new SphereShape(1), new Vector3(0, 1, 0), mass: 1);

        world.Remove(ball);

        Assert.True(ball.IsRemoved);
        Assert.Null(world.CastSegment(new Vector3(0, 5, 0), new Vector3(0, 0.5f, 0)));
        Assert.Throws<InvalidOperationException>(() => world.Remove(ball));
        Assert.Throws<ObjectDisposedException>(() => ball.Position);
        Assert.Throws<ObjectDisposedException>(() => ball.Velocity = Vector3.One);
        Assert.Throws<ObjectDisposedException>(() => ball.Mass);
        Assert.IsType<SphereShape>(ball.Shape);
    }

    [Fact]
    public void ADisposedWorld_AndItsBodies_CanNoLongerBeUsed()
    {
        var world = new PhysicsWorld();
        var ball = world.AddBody(new SphereShape(1), Vector3.Zero, mass: 1);
        using var other = new PhysicsWorld();

        world.Dispose();
        world.Dispose();

        Assert.Throws<ObjectDisposedException>(() => ball.Position);
        Assert.Throws<ObjectDisposedException>(() => world.CastSegment(Vector3.Zero, Vector3.One));
        Assert.Throws<ObjectDisposedException>(() => world.Step(Tick));
        Assert.Throws<InvalidOperationException>(() => other.Remove(ball));
    }

    [Fact]
    public void BadInput_IsRejected()
    {
        using var world = new PhysicsWorld();

        var nan = new Vector3(float.NaN, 0, 0);
        var ball = world.AddBody(new SphereShape(1), Vector3.Zero, mass: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddStaticMesh(0, [Vector3.Zero], [new MeshTriangle(0, 0, 1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddStaticMesh(0, [Vector3.Zero, Vector3.UnitX, nan], [new MeshTriangle(0, 1, 2)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddBody(new SphereShape(1), Vector3.Zero, mass: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddBody(new SphereShape(1), nan, mass: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddBody(new SphereShape(-1), Vector3.Zero, mass: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddBody(new BoxShape(new Vector3(1, 0, 1)), Vector3.Zero, mass: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.AddBody(new CapsuleShape(1, -1), Vector3.Zero, mass: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ball.Position = nan);
        Assert.Throws<ArgumentOutOfRangeException>(() => ball.Velocity = nan);
        Assert.Throws<ArgumentOutOfRangeException>(() => world.CastSegment(nan, Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Step(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Step(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsWorld(PhysicsSettings.Default with { SubSteps = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsWorld(PhysicsSettings.Default with { SubSteps = PhysicsSettings.MaxIterations + 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsWorld(PhysicsSettings.Default with { Gravity = nan }));
    }
}
