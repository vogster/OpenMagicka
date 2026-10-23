using Microsoft.Xna.Framework;
using OpenMagicka.Engine;

namespace OpenMagicka.Tests.Engine;

public class FreeCameraTests
{
    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"expected {expected}, got {actual}");

    [Fact]
    public void ANewCamera_LooksAlongMinusZ()
    {
        var camera = new FreeCamera(new Vector3(1, 2, 3), Yaw: 0, Pitch: 0);

        Near(-Vector3.UnitZ, camera.Forward);
        Assert.Equal(Matrix.CreateLookAt(new Vector3(1, 2, 3), new Vector3(1, 2, 2), Vector3.Up), camera.View);
    }

    [Fact]
    public void Moving_FollowsTheViewDirectionAndWorldUp()
    {
        var camera = new FreeCamera(Vector3.Zero, Yaw: MathHelper.PiOver2, Pitch: 0);   // turned left: looking along -X

        var moved = camera.Move(forward: 2, right: 0, up: 1, dt: 0.5f, speed: 10);

        Near(new Vector3(-10, 5, 0), moved.Position);
        Near(Vector3.Zero, camera.Position);                                             // immutable
    }

    [Fact]
    public void Turning_ClampsThePitchShortOfStraightUpOrDown()
    {
        var camera = new FreeCamera(Vector3.Zero, 0, 0).Turn(yaw: 1, pitch: 10);

        Assert.Equal(1, camera.Yaw, 5);
        Assert.Equal(FreeCamera.MaxPitch, camera.Pitch, 5);
        Assert.Equal(-FreeCamera.MaxPitch, camera.Turn(0, -100).Pitch, 5);
    }

    [Fact]
    public void Lerp_InterpolatesPositionAndAngles()
    {
        var a = new FreeCamera(Vector3.Zero, 0, 0);
        var b = new FreeCamera(new Vector3(10, 0, 0), 1, 0.5f);

        var half = FreeCamera.Lerp(a, b, 0.5f);

        Near(new Vector3(5, 0, 0), half.Position);
        Assert.Equal((0.5f, 0.25f), (half.Yaw, half.Pitch));
    }

    [Fact]
    public void Framing_PlacesTheCameraToSeeABox()
    {
        var box = new BoundingBox(new Vector3(-50, 0, -50), new Vector3(50, 10, 50));

        var camera = FreeCamera.Framing(box);

        Assert.True(camera.Position.Y > 10, "above the box");
        var toCenter = Vector3.Normalize((box.Min + box.Max) / 2 - camera.Position);
        Assert.True(Vector3.Dot(toCenter, camera.Forward) > 0.99f, "looking at its centre");
    }
}
