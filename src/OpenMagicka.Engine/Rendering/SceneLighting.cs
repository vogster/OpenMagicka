using Microsoft.Xna.Framework;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;

namespace OpenMagicka.Engine.Rendering;

/// <summary>
/// A directional light (intensity already applied); Direction is normalised and points where the
/// light goes. <see cref="ShadowMapSize"/> is the side of its shadow map, or 0 when it casts none.
/// </summary>
public sealed record DirectionalLighting(Vector3 Direction, Vector3 Diffuse, Vector3 Ambient, float Specular, int ShadowMapSize = 0);

/// <summary>A point light (intensity already applied) reaching <see cref="Radius"/> around <see cref="Position"/>.</summary>
public sealed record PointLighting(Vector3 Position, float Radius, Vector3 Diffuse, Vector3 Ambient, float Specular)
{
    /// <summary>Places the unit light sphere (<see cref="LightVolumes.Sphere"/>) over the light's reach.</summary>
    public Matrix World => Matrix.CreateScale(Radius) * Matrix.CreateTranslation(Position);
}

/// <summary>
/// A spot light (intensity already applied) at <see cref="Position"/> shining along <see cref="Direction"/>
/// (normalised) up to <see cref="Range"/>, within <see cref="Cutoff"/> radians of its axis.
/// </summary>
public sealed record SpotLighting(
    Vector3 Position, Vector3 Direction, float Range, float Cutoff, float Sharpness, bool Attenuation, Vector3 Diffuse, Vector3 Ambient, float Specular)
{
    /// <summary>
    /// Turns the cone's axis (-Z, see <see cref="LightVolumes.Cone"/>) along the light and moves it to
    /// the light; the shader applies the range itself. Up is the world's, or world forward (-Z) when the light
    /// points nearly straight up or down, as in the original.
    /// </summary>
    public Matrix World => Matrix.CreateWorld(Position, Direction, MathF.Abs(Vector3.Dot(Direction, Vector3.Up)) >= 0.9f ? Vector3.Forward : Vector3.Up);
}

/// <summary>Linear fog as the composition pass takes it.</summary>
public sealed record SceneFog(bool Enabled, float Start, float End, Vector3 Color);

/// <summary>The lighting a scene is drawn with, from its level model and scene settings.</summary>
public static class SceneLighting
{
    /// <summary>The largest shadow map used (the shipped levels ask for up to 2048).</summary>
    public const int MaxShadowMapSize = 4096;

    /// <summary>A soft light from above and in front, for models and levels without a directional light.</summary>
    public static DirectionalLighting Default { get; } =
        new(Vector3.Normalize(new Vector3(-0.3f, -1, -0.5f)), new Vector3(0.9f), new Vector3(0.35f), 0.3f);

    /// <summary>
    /// The level's first directional light (every shipped level has exactly one) at intensity 1, as
    /// a level starts; <see cref="Default"/> when there is none.
    /// </summary>
    public static DirectionalLighting Sun(IEnumerable<LevelLightContent> lights)
    {
        ArgumentNullException.ThrowIfNull(lights);
        var sun = lights.FirstOrDefault(l => l.Type == LevelLightType.Directional && l.Direction.LengthSquared() > 0);
        return sun is null ? Default : new DirectionalLighting(Vector3.Normalize(sun.Direction), sun.DiffuseColor, sun.AmbientColor, sun.SpecularAmount,
            sun.CastShadows && sun.ShadowMapSize > 0 ? Math.Min(sun.ShadowMapSize, MaxShadowMapSize) : 0);
    }

    /// <summary>The level's point lights at intensity 1, as a level starts; lights without a finite reach are left out.</summary>
    public static IReadOnlyList<PointLighting> PointLights(IEnumerable<LevelLightContent> lights)
    {
        ArgumentNullException.ThrowIfNull(lights);
        return lights.Where(l => l.Type == LevelLightType.Point && float.IsFinite(l.Range) && l.Range > 0)
            .Select(l => new PointLighting(l.Position, l.Range, l.DiffuseColor, l.AmbientColor, l.SpecularAmount))
            .ToArray();
    }

    /// <summary>The level's spot lights at intensity 1, as a level starts; lights without a finite reach, a direction or an angle are left out.</summary>
    public static IReadOnlyList<SpotLighting> SpotLights(IEnumerable<LevelLightContent> lights)
    {
        ArgumentNullException.ThrowIfNull(lights);
        return lights.Where(l => l.Type == LevelLightType.Spot && float.IsFinite(l.Range) && l.Range > 0
                && l.Direction.LengthSquared() > 0 && float.IsFinite(l.Direction.LengthSquared()) && l.CutoffAngle > 0 && l.CutoffAngle <= MathHelper.PiOver2)
            .Select(l => new SpotLighting(l.Position, Vector3.Normalize(l.Direction), l.Range, l.CutoffAngle, l.Sharpness, l.UseAttenuation,
                l.DiffuseColor, l.AmbientColor, l.SpecularAmount))
            .ToArray();
    }

    /// <summary>The scene's fog when it has a Fog element with a range; off otherwise.</summary>
    public static SceneFog Fog(SceneSettings? settings) => settings?.Fog is { } fog && fog.End > fog.Start
        ? new SceneFog(true, fog.Start, fog.End, new Vector3(fog.Color.X, fog.Color.Y, fog.Color.Z))
        : new SceneFog(false, 0, 0, Vector3.Zero);
}
