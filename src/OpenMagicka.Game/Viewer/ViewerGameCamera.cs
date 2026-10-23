using Microsoft.Xna.Framework;
using OpenMagicka.Engine;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Game.Viewer;

/// <summary>
/// The viewer's game camera (G): the game's camera following a stand-in player that WASD walks over
/// the ground, kept on the level's camera mesh and at the scene's zoom, so a level is seen as in the
/// game. Immutable; logic thread.
/// </summary>
public sealed record ViewerGameCamera(Vector3 Player, GameCamera Camera)
{
    /// <summary>
    /// The player where the free camera looks at the ground (on the ground below that point when it
    /// looks above the horizon); the camera over it at once, as a scene starts. A scene's zoom that is
    /// not positive is ignored.
    /// </summary>
    public static ViewerGameCamera Start(FreeCamera free, float groundHeight, CameraMesh? mesh, float? magnify)
    {
        ArgumentNullException.ThrowIfNull(free);
        var player = SunShadowView.Focus(free.Position, free.Forward, groundHeight) with { Y = groundHeight };
        var zoom = magnify is > 0 and var m && float.IsFinite(m) ? m : 1;
        var camera = (GameCamera.Initial with { Mesh = mesh }).WithDefaultMagnification(zoom).Release(0).At(player);
        return new ViewerGameCamera(player, camera.Update(0, Following(player)));
    }

    /// <summary>A free camera at the eye, looking the same way, for leaving the game camera.</summary>
    public FreeCamera Free => new(Camera.Eye, 0, MathF.Asin(GameCamera.Direction.Y));

    /// <summary>Walks the player (-1..1 each: right is +X, forward is −Z, as on the screen) and steps the camera.</summary>
    public ViewerGameCamera Move(float right, float forward, float dt, float speed)
    {
        var player = Player + new Vector3(right, 0, -forward) * speed * dt;
        return new ViewerGameCamera(player, Camera.Update(dt, Following(player)));
    }

    private static CameraInputs Following(Vector3 player) => CameraInputs.None with { Players = player, LocalPlayers = [player] };
}
