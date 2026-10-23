using Microsoft.Xna.Framework;
using OpenMagicka.Content.Magicka;
using OpenMagicka.Engine.Cameras;

namespace OpenMagicka.Tests.Engine;

public class GameCameraTests
{
    private static readonly GameCamera Start = GameCamera.Initial;

    private static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-3f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

    private static CameraInputs PlayersAt(Vector3 centre) => CameraInputs.None with { Players = centre };

    [Fact]
    public void TheEye_IsAboveAndBehindTheGroundPoint_LookingDownAt40Degrees()
    {
        var camera = Start.At(new Vector3(10, 0, 20));
        var eye = camera.Update(0.1f, PlayersAt(new Vector3(10, 0, 20))).Eye;

        Near(new Vector3(10, 144, 191), eye);
        Near(new Vector3(0, -MathF.Sin(MathHelper.ToRadians(40)), -MathF.Cos(MathHelper.ToRadians(40))), GameCamera.Direction);
        // The view ray meets the ground ≈224 units away, just in front of the ground point.
        var distance = 144 / -GameCamera.Direction.Y;
        Assert.InRange(distance, 223, 225);
        Assert.InRange((eye + GameCamera.Direction * distance).Z, 19, 20);
        Near(eye + GameCamera.Direction * 200, camera.Update(0.1f, PlayersAt(new Vector3(10, 0, 20))).EyeOfTheBeholder);
    }

    [Fact]
    public void ViewAndProjection_UseTheNarrowFovDividedByTheMagnification()
    {
        var camera = Start with { Magnification = 2 };

        Assert.Equal(Matrix.CreateLookAt(camera.Eye, camera.Eye + GameCamera.Direction, Vector3.Up), camera.View);
        Assert.Equal(Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(2.5f), 16 / 9f, 105, 500), camera.Projection(16 / 9f));
    }

    [Fact]
    public void Interpolated_IsASpringTowardsThePlayers()
    {
        var first = Start.Update(0.1f, PlayersAt(new Vector3(10, 0, 0)));
        var second = first.Update(0.1f, PlayersAt(new Vector3(10, 0, 0)));

        Near(new Vector3(2, 0, 0), first.Ground);     // (0 + 2 × 10 / 1 − 0) × 0.1
        Near(new Vector3(4.6f, 0, 0), second.Ground); // (20 + 2 × 8 − 10) × 0.1
        Near(first.Ground + GameCamera.Offset, first.Eye);
        Near(Vector3.Zero, Start.Ground);             // immutable
    }

    [Fact]
    public void Interpolated_LandsOnTheTargetInsteadOfOvershooting_AndAZeroFrameDoesNotMove()
    {
        var quick = Start with { Time = 0.01f };

        Near(new Vector3(10, 0, 0), quick.Update(0.1f, PlayersAt(new Vector3(10, 0, 0))).Ground);
        Near(Vector3.Zero, Start.Update(0, PlayersAt(new Vector3(10, 0, 0))).Ground);
    }

    [Fact]
    public void Linear_StepsAHundredUnitsPerSecondDividedByTheTime()
    {
        var linear = Start with { Interpolation = CameraInterpolation.Linear };

        Near(new Vector3(10, 0, 0), linear.Update(0.1f, PlayersAt(new Vector3(100, 0, 0))).Ground);
        Near(new Vector3(5, 0, 0), linear.Update(0.1f, PlayersAt(new Vector3(5, 0, 0))).Ground);
    }

    [Fact]
    public void WithoutPlayers_TheCameraStaysWhereItIs()
    {
        var camera = Start.At(new Vector3(3, 0, 3)).Update(0.1f, CameraInputs.None);

        Near(new Vector3(3, 0, 3), camera.Ground);
        Near(new Vector3(3, 0, 3), camera.Target);
    }

    [Fact]
    public void FollowingThePlayers_TheTimeCreepsBackTowardsOne()
    {
        var released = Start.Release(0);
        var camera = released.Update(0.1f, PlayersAt(new Vector3(50, 0, 0)));

        Assert.Equal(0, released.Time);
        Assert.Equal(0.1f, camera.Time, 5);
        Near(new Vector3(50, 0, 0), camera.Ground);   // a short time: the step reaches the target
    }

    [Fact]
    public void TheBias_IsAddedToThePlayers_AndEasesIn()
    {
        var camera = Start.WithBias(new Vector3(10, 0, 0), time: 1);

        var first = camera.Update(1, PlayersAt(Vector3.Zero));
        var second = first.Update(1, PlayersAt(Vector3.Zero));

        Near(Vector3.Zero, first.Target);              // the bias of before this frame
        Near(new Vector3(9.8f, 0, 0), first.Bias);     // lerp(10, 0, 0.02^(1/1))
        Near(new Vector3(9.8f, 0, 0), second.Target);
    }

    [Fact]
    public void AnInstantBias_IsTakenAtOnce_EvenInAZeroFrame()
    {
        var camera = Start.WithBias(new Vector3(10, 0, 0), time: 0).Update(0, PlayersAt(Vector3.Zero)).Update(0.1f, PlayersAt(Vector3.Zero));

        Assert.Equal(new Vector3(10, 0, 0), camera.Bias);
        Near(new Vector3(10, 0, 0), camera.Target);
    }

    [Fact]
    public void BadFramesAndZooms_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Start.Update(-0.1f, CameraInputs.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => Start.Update(float.NaN, CameraInputs.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => Start.WithDefaultMagnification(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Start.WithDynamicMagnification(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Start.WithDynamicMagnification(4, 0.5f));
    }

    [Fact]
    public void FollowingAnEntity_SnapsIt_ButMovingToATargetDoesNot()
    {
        var box = new CameraBox(new Vector3(-5, -1, -5), new Vector3(2, 2, 2), Quaternion.Identity);
        var locked = Start.LockOn(box, time: 1);

        Near(new Vector3(-3, 1, -3), locked.Follow().Update(0.1f, CameraInputs.None with { Followed = new Vector3(9, 9, 9) }).Target);
        Near(new Vector3(9, 9, 9), locked.MoveTo(new Vector3(9, 9, 9), 1).Update(0.1f, CameraInputs.None).Target);
    }

    [Fact]
    public void FollowingAnEntity_FallsBackToThePlayersWhenItIsGone()
    {
        var following = Start.Follow();
        var camera = following.Update(0.1f, CameraInputs.None with { Followed = new Vector3(5, 0, 5), Players = Vector3.Zero });
        var lost = camera.Update(0.1f, PlayersAt(new Vector3(-50, 0, 0)));

        Assert.Equal(CameraBehaviour.FollowEntity, camera.Behaviour);
        Near(new Vector3(5, 0, 5), camera.Target);
        Assert.Equal(CameraBehaviour.FollowPlayers, lost.Behaviour);
        Near(new Vector3(5, 0, 5), lost.Target);
    }

    [Fact]
    public void MovingToATarget_IgnoresThePlayers_AndATimeOfZeroJumps()
    {
        var camera = Start with { TargetMagnification = 3 };

        var moved = camera.MoveTo(new Vector3(7, 0, 7), time: 0).Update(0.1f, PlayersAt(new Vector3(-50, 0, 0)));

        Assert.Equal(CameraBehaviour.MoveToTarget, moved.Behaviour);
        Assert.Equal(0, moved.Time);
        Near(new Vector3(7, 0, 7), moved.Ground);
        Assert.Equal(3, moved.Magnification);
    }

    [Fact]
    public void TheTarget_IsSnappedToTheCameraMesh_OrToTheLockBoxFirst()
    {
        var mesh = new CameraMesh(new CollisionMeshContent(
            [new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(0, 0, 10), new Vector3(10, 0, 10)],
            [new CollisionTriangle(0, 1, 2), new CollisionTriangle(1, 3, 2)]));
        var meshed = Start with { Mesh = mesh };
        var box = new CameraBox(new Vector3(-5, -1, -5), new Vector3(2, 2, 2), Quaternion.Identity);

        Near(new Vector3(10, 0, 5), meshed.Update(0.1f, PlayersAt(new Vector3(20, 3, 5))).Target);
        Near(new Vector3(-3, 1, -3), meshed.LockOn(box, time: 1).Update(0.1f, PlayersAt(new Vector3(20, 3, 5))).Target);
    }

    [Fact]
    public void TheMagnification_EasesTowardsItsTarget_TimesThePlayerMagnificationWhileItLasts()
    {
        var camera = (Start with { TargetMagnification = 2 }).Update(1, PlayersAt(Vector3.Zero));
        var player = Start.WithPlayerMagnification(2, time: 1).Update(1, PlayersAt(Vector3.Zero));
        var after = player.Update(1, PlayersAt(Vector3.Zero));

        Assert.Equal(1.98f, camera.Magnification, 4);            // lerp(2, 1, 0.02^(1/1))
        Assert.Equal(MathHelper.ToRadians(5) / 1.98f, camera.Fov, 6);
        Assert.Equal(1.98f, player.Magnification, 4);
        Assert.Equal(1 + 0.98f * 0.02f, after.Magnification, 4); // the player magnification is over
    }

    [Fact]
    public void DynamicMagnification_FitsTheFarthestPlayerInHalfTheFov()
    {
        // A player 2° off the view axis as seen from the eye: 0.5 × 5° / 2° = 1.25.
        var off = Vector3.TransformNormal(GameCamera.Direction, Matrix.CreateFromAxisAngle(Vector3.UnitX, MathHelper.ToRadians(2)));
        var player = GameCamera.Offset + off * 224;
        // The ground jumps far away this frame; the angle is still measured from the last frame's eye.
        var camera = (Start with { Time = 0 }).At(new Vector3(500, 0, 0)).WithDynamicMagnification(0.5f, 4);

        var fitted = camera.Update(0.1f, PlayersAt(new Vector3(500, 0, 0)) with { LocalPlayers = [player, GameCamera.Offset + GameCamera.Direction * 224] });
        var none = camera.Update(0.1f, PlayersAt(new Vector3(500, 0, 0)));

        Assert.Equal(new Vector3(500, 144, 171), fitted.Eye);

        Assert.Equal(1.25f, fitted.TargetMagnification, 2);
        Assert.Equal(4, none.TargetMagnification);              // no player: the widest angle is 0
        Assert.Equal(1, Start.Update(0.1f, PlayersAt(Vector3.Zero) with { LocalPlayers = [player] }).TargetMagnification);
    }

    [Fact]
    public void AShake_MovesTheGroundPointAfterTheEyeIsPlaced_AndFades()
    {
        var shaking = Start.Shake(magnitude: 10, time: 1).Update(0.1f, PlayersAt(Vector3.Zero));

        var expected = MathF.Sin(MathHelper.WrapAngle(60)) * 10 * 0.0333f;
        Assert.Equal(expected, shaking.Ground.Y, 4);
        Assert.Equal(144, shaking.Eye.Y, 4);
        Assert.Equal(0.9f, shaking.ShakeTime, 5);
        Assert.Equal(0, Start.Shake(10, 1).Update(0, PlayersAt(Vector3.Zero)).Ground.Y);
        // Half-way through, at half the strength, with the time from before this frame.
        var later = (Start.Shake(10, 1) with { ShakeTime = 0.5f }).Update(0.1f, PlayersAt(Vector3.Zero));
        Assert.Equal(MathF.Sin(MathHelper.WrapAngle(30)) * 10 * 0.5f * 0.0333f, later.Ground.Y, 4);
    }

    [Fact]
    public void Release_ResetsTheBehaviour_AndOptionallyKeepsTheBoxAndTheMagnification()
    {
        var box = new CameraBox(Vector3.Zero, Vector3.One, Quaternion.Identity);
        var locked = (Start with { TargetMagnification = 3, Interpolation = CameraInterpolation.Linear }).LockOn(box, 2).Follow();

        var released = locked.Release(1);
        var keptBox = locked.Release(1, releaseBox: false);
        var keptAll = locked.Release(1, releaseBox: false, resetMagnification: false);

        Assert.Equal((CameraBehaviour.FollowPlayers, CameraInterpolation.Interpolated, 1f, 1f), (released.Behaviour, released.Interpolation, released.TargetMagnification, released.Time));
        Assert.Null(released.Box);
        Assert.Same(box, keptBox.Box);
        Assert.Equal(1, keptBox.TargetMagnification);
        Assert.Same(box, keptAll.Box);
        Assert.Equal(3, keptAll.TargetMagnification);
    }

    [Fact]
    public void TheDefaultMagnification_MovesTheTargetOnlyWhenItWasAtTheOldDefault()
    {
        Assert.Equal(1.5f, Start.WithDefaultMagnification(1.5f).TargetMagnification);
        Assert.Equal(2, (Start with { TargetMagnification = 2 }).WithDefaultMagnification(1.5f).TargetMagnification);
        Assert.Equal(1.5f, (Start with { TargetMagnification = 2 }).WithDefaultMagnification(1.5f).DefaultMagnification);
    }

    [Fact]
    public void Placing_SetsTheGroundPoint_AndAtAlsoTheTarget()
    {
        var mesh = new CameraMesh(new CollisionMeshContent([new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(0, 0, 10)], [new CollisionTriangle(0, 1, 2)]));
        var camera = (Start with { Mesh = mesh }).At(new Vector3(1, 2, 3));

        Near(new Vector3(1, 2, 3), camera.Target);
        Near(new Vector3(1, 0, 3), camera.Place(new Vector3(1, 2, 3), snap: true).Ground);
        Near(new Vector3(1, 2, 3), camera.Place(new Vector3(1, 2, 3), snap: true).Target);
        Near(new Vector3(1, 2, 3), camera.Place(new Vector3(1, 2, 3), snap: false).Ground);
    }
}
