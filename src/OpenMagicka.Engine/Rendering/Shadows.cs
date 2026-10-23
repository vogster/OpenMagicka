using Microsoft.Xna.Framework;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// The sun's shadow map view, as the original builds it: an orthographic box looking along the
/// light, 60 units wide around a focus point, reaching 200 units towards the light and 100 beyond
/// the focus. The focus is snapped to the map's texels in light space, so the shadows do not
/// shimmer as the camera moves. The shadow pass draws the casters with <see cref="LightViewProjection"/>
/// as the view-projection; the sun's pass reads the map with it.
/// </summary>
public sealed record SunShadowView(Matrix LightView, Matrix LightProjection, Vector3 SnappedGround)
{
    public const float HalfWidth = 30;
    public const float TowardsLight = 200;
    public const float BeyondFocus = 100;

    /// <summary>How far ahead of the camera the original puts the focus.</summary>
    public const float FocalDistance = 224;

    public Matrix LightViewProjection => LightView * LightProjection;

    public static SunShadowView Around(Vector3 direction, Vector3 focus, int mapSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapSize);
        var up = MathF.Abs(Vector3.Dot(Vector3.Normalize(direction), Vector3.Up)) > 0.999f ? Vector3.Forward : Vector3.Up;
        var view = Matrix.CreateLookAt(Vector3.Zero, direction, up);
        var inLight = Vector3.Transform(focus, view);
        var texelsPerUnit = mapSize / (2 * HalfWidth);
        var snapped = new Vector3(
            MathF.Floor(inLight.X * texelsPerUnit) / texelsPerUnit,
            MathF.Floor(inLight.Y * texelsPerUnit) / texelsPerUnit,
            MathF.Floor(inLight.Z * texelsPerUnit) / texelsPerUnit);
        var projection = Matrix.CreateOrthographicOffCenter(
            snapped.X - HalfWidth, snapped.X + HalfWidth, snapped.Y - HalfWidth, snapped.Y + HalfWidth,
            -(snapped.Z + TowardsLight), -(snapped.Z - BeyondFocus));
        return new SunShadowView(view, projection, Vector3.Transform(snapped, Matrix.Invert(view)));
    }

    /// <summary>
    /// Where the shadows are wanted: the original's camera always looks at the ground from
    /// <see cref="FocalDistance"/> away; a free camera's view meets the ground (within the far
    /// plane) or, when it does not, the same distance ahead.
    /// </summary>
    public static Vector3 Focus(Vector3 camera, Vector3 forward, float groundHeight)
    {
        if (forward.Y < -1e-3f)
        {
            var distance = (camera.Y - groundHeight) / -forward.Y;
            if (distance > 0 && distance <= DeferredRenderer.FarPlane)
            {
                return camera + forward * distance;
            }
        }
        return camera + forward * FocalDistance;
    }
}
