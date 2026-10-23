using Microsoft.Xna.Framework;
using OpenMagicka.Engine.Cameras;
using OpenMagicka.Engine.Rendering;

namespace OpenMagicka.Game.Viewer;

/// <summary>The viewer's clip planes for a scene of a given size (the diagonal of its bounds).</summary>
public static class ViewerProjection
{
    /// <summary>
    /// With the original pipeline the far plane is the original's (<see cref="DeferredRenderer.FarPlane"/>)
    /// and the near plane grows with the scene up to 10, so the depth buffer is precise enough for
    /// the depth the G-buffer pass restores (the original camera's near plane is 105). The simple
    /// view reaches the whole level.
    /// </summary>
    public static (float Near, float Far) ClipPlanes(float sceneSize, bool originalPipeline) => originalPipeline
        ? (Math.Clamp(sceneSize / 20, 0.1f, 10f), DeferredRenderer.FarPlane)
        : (Math.Max(sceneSize / 7000, 0.5f), Math.Max(sceneSize * 3, 1000));

    /// <summary>The game camera between two ticks: its eye and field of view interpolated, with its own clip planes.</summary>
    public static (Matrix View, Matrix Projection, Vector3 Eye) Game(GameCamera previous, GameCamera current, float amount, float aspectRatio)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var eye = Vector3.Lerp(previous.Eye, current.Eye, amount);
        var fov = MathHelper.Lerp(previous.Fov, current.Fov, amount);
        return (Matrix.CreateLookAt(eye, eye + GameCamera.Direction, Vector3.Up),
            Matrix.CreatePerspectiveFieldOfView(fov, aspectRatio, GameCamera.NearPlane, GameCamera.FarPlane), eye);
    }
}
