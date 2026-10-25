using System.Numerics;
using OpenMagicka.Physics;

namespace OpenMagicka.Tests.Physics;

/// <summary>A level's moving part as a mesh placed anew each frame, and what touches it (OM-74).</summary>
public class PhysicsMovingMeshTests
{
    private const float Tick = 1f / 60;

    /// <summary>A 4 × 4 platform at its own origin, up-facing.</summary>
    private static StaticMesh Platform(PhysicsWorld world) =>
        world.AddStaticMesh(3, [new(-2, 0, -2), new(2, 0, -2), new(2, 0, 2), new(-2, 0, 2)], [new(0, 2, 1), new(0, 3, 2)]);

    [Fact]
    public void APlacedMesh_IsHitWhereItNowIs_AndBearsWhatFallsOnIt()
    {
        using var world = new PhysicsWorld();
        var platform = Platform(world);

        world.PlaceStaticMesh(platform, Matrix4x4.CreateTranslation(10, 2, 0));
        var box = world.AddBody(new BoxShape(Vector3.One), new Vector3(10, 4, 0), mass: 1);
        for (var t = 0f; t < 2; t += Tick)
        {
            world.Step(Tick);
        }

        Assert.Equal(2.5f, box.Position.Y, 1);                                                    // resting on the moved platform
        Assert.Equal(2, world.CastSegment(new Vector3(10, 5, 0), new Vector3(10, -5, 0), levelOnly: true)!.Value.Position.Y, 3);
        Assert.Null(world.CastSegment(new Vector3(0, 5, 0), new Vector3(0, -5, 0), levelOnly: true));   // nothing left where it was
    }

    [Fact]
    public void AMeshTold_HearsWhatTouchesIt_SeenFromTheBody()
    {
        using var world = new PhysicsWorld();
        var platform = Platform(world);
        var touches = new List<(PhysicsBody Body, Contact Contact)>();
        platform.Touched = (body, contact) => touches.Add((body, contact));
        var box = world.AddBody(new BoxShape(Vector3.One), new Vector3(0, 1, 0), mass: 1);

        for (var t = 0f; t < 1; t += Tick)
        {
            world.Step(Tick);
        }

        Assert.Contains(touches, t => ReferenceEquals(t.Body, box) && t.Contact.Normal.Y > 0.9f && ReferenceEquals(t.Contact.OtherMesh, platform));
    }

    [Fact]
    public void ATransformWithScale_IsPlacedWithoutIt_AndOneNotFinite_IsRefused()
    {
        using var world = new PhysicsWorld();
        var platform = Platform(world);

        world.PlaceStaticMesh(platform, Matrix4x4.CreateScale(3) * Matrix4x4.CreateTranslation(0, 1, 0));

        Assert.Null(world.CastSegment(new Vector3(5, 5, 0), new Vector3(5, -5, 0), levelOnly: true));   // not grown to 6 wide
        Assert.Throws<ArgumentOutOfRangeException>(() => world.PlaceStaticMesh(platform, Matrix4x4.CreateTranslation(float.NaN, 0, 0)));
    }
}
