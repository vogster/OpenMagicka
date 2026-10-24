using System.Numerics;
using OpenMagicka.Physics;

namespace OpenMagicka.Tests.Physics;

/// <summary>Static meshes that are not the level: shields that stand, let things through and go.</summary>
public class PhysicsMeshTests
{
    private const float Tick = 1f / 60;

    /// <summary>A 4 × 4 wall in the x = 0 plane, solid from both sides.</summary>
    private static readonly Vector3[] WallCorners = [new(0, 0, -2), new(0, 0, 2), new(0, 4, 2), new(0, 4, -2)];

    private static readonly MeshTriangle[] BothSides = [new(0, 1, 2), new(0, 2, 3), new(0, 2, 1), new(0, 3, 2)];

    private static StaticMesh Wall(PhysicsWorld world) => world.AddStaticMesh(0, WallCorners, BothSides, isLevel: false);

    private static PhysicsBody Ball(PhysicsWorld world)
    {
        var ball = world.AddBody(new SphereShape(0.3f), new Vector3(3, 1, 0), mass: 1);
        ball.AffectedByGravity = false;
        ball.Velocity = new Vector3(-6, 0, 0);
        return ball;
    }

    private static void Run(PhysicsWorld world, float seconds)
    {
        for (var t = 0f; t < seconds; t += Tick)
        {
            world.Step(Tick);
        }
    }

    [Fact]
    public void AWall_StopsBodiesAndSegments_ButNotLevelOnlyOnes()
    {
        using var world = new PhysicsWorld();
        var wall = Wall(world);
        var ball = Ball(world);

        Run(world, 1);

        Assert.False(wall.IsLevel);
        Assert.True(ball.Position.X > 0, $"went through, at {ball.Position}");
        Assert.NotNull(world.CastSegment(new Vector3(3, 1, 0), new Vector3(-3, 1, 0)));
        Assert.Null(world.CastSegment(new Vector3(3, 1, 0), new Vector3(-3, 1, 0), levelOnly: true));
    }

    [Fact]
    public void APassableWall_LetsThroughOnlyBodiesThatPassSuchMeshes()
    {
        using var world = new PhysicsWorld();
        var wall = Wall(world);
        wall.Passable = true;
        var ball = Ball(world);
        ball.PassesPassableMeshes = true;
        var rock = Ball(world);
        rock.Position = new Vector3(3, 1, 1.5f);

        Run(world, 1);

        Assert.True(ball.Position.X < 0, $"held at {ball.Position}");
        Assert.True(rock.Position.X > 0, $"went through, at {rock.Position}");
    }

    [Fact]
    public void ARemovedWall_IsGone_AndTheLevelCannotBeRemoved()
    {
        using var world = new PhysicsWorld();
        var wall = Wall(world);
        var level = world.AddStaticMesh(0, WallCorners, BothSides);

        world.RemoveStaticMesh(wall);
        world.RemoveStaticMesh(wall);                                                // twice is harmless

        Assert.True(wall.IsRemoved);
        Assert.Throws<InvalidOperationException>(() => world.RemoveStaticMesh(level));
    }
}
