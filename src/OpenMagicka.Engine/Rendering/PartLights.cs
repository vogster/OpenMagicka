using Microsoft.Xna.Framework;
using OpenMagicka.Content;
using OpenMagicka.Content.Magicka;

namespace OpenMagicka.Engine.Rendering;

/// <summary>The point and spot lights a frame is lit with, and the sun's way when a part turns it (null: the level's).</summary>
public sealed record FrameLights(IReadOnlyList<PointLighting> Points, IReadOnlyList<SpotLighting> Spots, Vector3? SunDirection = null)
{
    /// <summary>The frame's sun: the level's, turned when a part carries it.</summary>
    public DirectionalLighting Sun(DirectionalLighting level) => SunDirection is { } way ? level with { Direction = way } : level;
}

/// <summary>Which of the level's lights a part carries.</summary>
public enum PartLightKind : byte { Point, Spot, Sun }

/// <summary>
/// A level light that rides on an animated part (one of the part's light attachments): the part by its
/// place in <see cref="LevelParts.Order"/>, the light by its place among the level's point or spot lights
/// (or the level's sun), and where it sits on the part. As the original, each frame the light is put at
/// that place times the part's transform: a point light's position, a spot light's position and facing,
/// the sun's way (the place's forward). Lights are found by name, in any case; a name none of the level's
/// lights has is passed over, and so is a light left out for having no reach, a directional light that
/// is not the sun (the levels have one each), or a light on a part that never takes a place (its rest pose
/// is not finite).
/// </summary>
public sealed record PartLight(int Part, PartLightKind Kind, int Light, Matrix Local)
{
    public static IReadOnlyList<PartLight> Of(LevelModelContent level)
    {
        ArgumentNullException.ThrowIfNull(level);
        var points = level.Lights.Where(SceneLighting.IsPoint).Select(l => LegacyStringHash.Lower(l.Name)).ToList();
        var spots = level.Lights.Where(SceneLighting.IsSpot).Select(l => LegacyStringHash.Lower(l.Name)).ToList();
        var sun = SceneLighting.SunOf(level.Lights) is { } found ? LegacyStringHash.Lower(found.Name) : (int?)null;
        var riding = new List<PartLight>();
        var order = LevelParts.Order(level.AnimatedParts);
        var rests = LevelParts.Rests(level.AnimatedParts);
        for (var part = 0; part < order.Count; part++)
        {
            foreach (var attached in LevelParts.IsFinite(rests[part]) ? order[part].Lights : [])
            {
                var key = LegacyStringHash.Lower(attached.LightName);
                if (points.IndexOf(key) is >= 0 and var point)
                {
                    riding.Add(new PartLight(part, PartLightKind.Point, point, attached.LocalTransform));
                }
                else if (spots.IndexOf(key) is >= 0 and var spot)
                {
                    riding.Add(new PartLight(part, PartLightKind.Spot, spot, attached.LocalTransform));
                }
                else if (key == sun)
                {
                    riding.Add(new PartLight(part, PartLightKind.Sun, 0, attached.LocalTransform));
                }
            }
        }
        return riding;
    }

    /// <summary>
    /// The level's lights with those on its parts where the parts are now (by <see cref="LevelParts.Order"/>);
    /// the level's own when the frame gives no parts or none carries a light. A part the frame lacks, or a
    /// place that is not finite, leaves its lights where they are.
    /// </summary>
    public static FrameLights Place(GpuLevel level, IReadOnlyList<Matrix>? parts)
    {
        ArgumentNullException.ThrowIfNull(level);
        return Place(level.Lights, level.PartLights, parts);
    }

    /// <summary>The lights with those riding on the parts where the parts are now (see the other overload).</summary>
    public static FrameLights Place(FrameLights lights, IReadOnlyList<PartLight> riding, IReadOnlyList<Matrix>? parts)
    {
        ArgumentNullException.ThrowIfNull(lights);
        ArgumentNullException.ThrowIfNull(riding);
        if (parts is null || riding.Count == 0)
        {
            return lights;
        }
        var points = lights.Points.ToArray();
        var spots = lights.Spots.ToArray();
        var sun = lights.SunDirection;
        foreach (var light in riding.Where(l => l.Part < parts.Count))
        {
            var at = light.Local * parts[light.Part];
            if (!LevelParts.IsFinite(at))
            {
                continue;
            }
            switch (light.Kind)
            {
                case PartLightKind.Point when light.Light < points.Length:
                    points[light.Light] = points[light.Light] with { Position = at.Translation };
                    break;
                case PartLightKind.Spot when light.Light < spots.Length:
                    spots[light.Light] = spots[light.Light] with { Position = at.Translation, Direction = Way(at.Forward) ?? spots[light.Light].Direction };
                    break;
                case PartLightKind.Sun:
                    sun = Way(at.Forward) ?? sun;
                    break;
            }
        }
        return new FrameLights(points, spots, sun);
    }

    /// <summary>A facing made a direction; none when it has no length.</summary>
    private static Vector3? Way(Vector3 facing) => facing.LengthSquared() > 1e-12f ? Vector3.Normalize(facing) : null;
}
