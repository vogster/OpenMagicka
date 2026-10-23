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

/// <summary>
/// The point and spot lights' shadow views, as the original builds them. A point light renders a
/// cube map: each face looks out of the light along one axis (+X, -X, +Y, -Y, +Z, -Z, with up
/// +Y, +Y, -Z, +Z, +Y, +Y) through a 90° perspective (near 0.005, far 1) in the light's unit
/// space (its radius scaled to 1), mirrored in X. A spot light renders one map through a
/// perspective as wide as its cone (twice the cutoff angle, near 0.01, far = its range).
/// </summary>
public static class LightShadowViews
{
    private static readonly (Vector3 Direction, Vector3 Up)[] Faces =
    [
        (Vector3.Right, Vector3.Up), (Vector3.Left, Vector3.Up), (Vector3.Up, Vector3.Forward),
        (Vector3.Down, Vector3.Backward), (Vector3.Backward, Vector3.Up), (Vector3.Forward, Vector3.Up),
    ];

    private static readonly Matrix FaceProjection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1, 0.005f, 1);
    private static readonly Matrix MirrorX = Matrix.CreateScale(-1, 1, 1);

    /// <summary>The view-projection of each cube face, in <see cref="Microsoft.Xna.Framework.Graphics.CubeMapFace"/> order.</summary>
    public static IReadOnlyList<Matrix> PointFaces(Vector3 position, float radius)
    {
        var toLight = Matrix.Invert(Matrix.CreateScale(radius) * Matrix.CreateTranslation(position));
        return [.. Faces.Select(f => toLight * Matrix.CreateLookAt(Vector3.Zero, f.Direction, f.Up) * FaceProjection * MirrorX)];
    }

    public static Matrix Spot(SpotLighting light)
    {
        ArgumentNullException.ThrowIfNull(light);
        var up = MathF.Abs(Vector3.Dot(light.Direction, Vector3.Up)) >= 0.9f ? Vector3.Forward : Vector3.Up;
        return Matrix.CreateLookAt(light.Position, light.Position + light.Direction, up)
            * Matrix.CreatePerspectiveFieldOfView(MathF.Min(light.Cutoff * 2, MathF.PI - 0.01f), 1, 0.01f, light.Range);
    }
}
