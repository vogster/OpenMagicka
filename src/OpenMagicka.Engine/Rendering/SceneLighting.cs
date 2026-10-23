using Microsoft.Xna.Framework;
using OpenMagicka.Content.Levels;
using OpenMagicka.Content.Magicka;

namespace OpenMagicka.Engine.Rendering;

/// <summary>A directional light (intensity already applied); Direction is normalised and points where the light goes.</summary>
public sealed record DirectionalLighting(Vector3 Direction, Vector3 Diffuse, Vector3 Ambient, float Specular);

/// <summary>A point light (intensity already applied) reaching <see cref="Radius"/> around <see cref="Position"/>.</summary>
public sealed record PointLighting(Vector3 Position, float Radius, Vector3 Diffuse, Vector3 Ambient, float Specular)
{
    /// <summary>Places the unit light sphere (<see cref="LightVolumes.Sphere"/>) over the light's reach.</summary>
    public Matrix World => Matrix.CreateScale(Radius) * Matrix.CreateTranslation(Position);
}

/// <summary>Linear fog as the composition pass takes it.</summary>
public sealed record SceneFog(bool Enabled, float Start, float End, Vector3 Color);

/// <summary>The lighting a scene is drawn with, from its level model and scene settings.</summary>
public static class SceneLighting
{
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
        return sun is null ? Default : new DirectionalLighting(Vector3.Normalize(sun.Direction), sun.DiffuseColor, sun.AmbientColor, sun.SpecularAmount);
    }

    /// <summary>The level's point lights at intensity 1, as a level starts; lights without a finite reach are left out.</summary>
    public static IReadOnlyList<PointLighting> PointLights(IEnumerable<LevelLightContent> lights)
    {
        ArgumentNullException.ThrowIfNull(lights);
        return lights.Where(l => l.Type == LevelLightType.Point && float.IsFinite(l.Range) && l.Range > 0)
            .Select(l => new PointLighting(l.Position, l.Range, l.DiffuseColor, l.AmbientColor, l.SpecularAmount))
            .ToArray();
    }

    /// <summary>The scene's fog when it has a Fog element with a range; off otherwise.</summary>
    public static SceneFog Fog(SceneSettings? settings) => settings?.Fog is { } fog && fog.End > fog.Start
        ? new SceneFog(true, fog.Start, fog.End, new Vector3(fog.Color.X, fog.Color.Y, fog.Color.Z))
        : new SceneFog(false, 0, 0, Vector3.Zero);
}
