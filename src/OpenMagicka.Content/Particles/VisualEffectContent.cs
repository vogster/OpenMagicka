using Microsoft.Xna.Framework;

namespace OpenMagicka.Content.Particles;

public enum VisualEffectType : byte
{
    Single,
    Looping,
    Infinite,
}

public enum EmitterKind
{
    /// <summary>Emits particles per second while the effect runs.</summary>
    Continuous,

    /// <summary>Emits a fixed number of particles once, at its trigger time.</summary>
    Pulse,
}

[Flags]
public enum EmitterFlags
{
    None = 0,
    AlphaBlended = 1 << 0,
    ConeSpread = 1 << 1,
    RotationAligned = 1 << 2,
    Hsv = 1 << 3,
    Colorize = 1 << 4,
    RelativeVelocity = 1 << 5,
    SpawnLights = 1 << 6,
    LightSizeRelative = 1 << 7,
}

/// <summary>Emitter parameters, named as in the XML (docs/engine/ARCHITECTURE.md §5.2).</summary>
public enum EmitterParameter
{
    ParticlesPerSecond, PositionX, PositionY, PositionZ, PositionXOffset, PositionYOffset, PositionZOffset,
    RotationY, Gravity, Drag, ColorR, ColorG, ColorB, ColorAMin, ColorAMax, ColorADistribution,
    HueMin, HueMax, HueDistribution, SatMin, SatMax, SatDistribution, ValueMin, ValueMax, ValueDistribution,
    AlphaMin, AlphaMax, AlphaDistribution, LifeTimeMin, LifeTimeMax, LifeTimeDistribution,
    SizeStartMin, SizeStartMax, SizeStartDistribution, SizeEndMin, SizeEndMax, SizeEndDistribution,
    VelocityMin, VelocityMax, VelocityDistribution, RotationMin, RotationMax, RotationSpeedMin, RotationSpeedMax,
    RotationPCCW, SpreadConeAngle, SpreadConeDistribution, SpreadArcHorizontalAngle, SpreadArcHorizontalDistribution,
    SpreadArcVerticalMin, SpreadArcVerticalMax, SpreadArcVerticalDistribution,
    LightRadius, LightDiffuseR, LightDiffuseG, LightDiffuseB, LightAmbientR, LightAmbientG, LightAmbientB, LightSpecular,

    /// <summary>Pulse emitters only: when the pulse fires, in seconds.</summary>
    TriggerTime,
}

public enum LightVariationType
{
    None,
    Sine,
    Flicker,
    Candle,
    Strobe,
}

public enum LightTransitionType
{
    None,
    Linear,
    Flicker,
}

/// <summary>A value at a time on the effect's timeline (seconds).</summary>
public readonly record struct Keyframe(float Time, float Value);

/// <summary>
/// An animated parameter. Keys stay in file order and are evaluated as the original does: the
/// last key at or before t and the first key after t (scanning in order, stopping at the
/// latter) are blended linearly; with only one of them found its value is used.
/// </summary>
public sealed class ParameterCurve
{
    private readonly Keyframe[] keys;

    public ParameterCurve(IReadOnlyList<Keyframe> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException("a curve needs at least one key", nameof(keys));
        }
        this.keys = [.. keys];
        Keys = Array.AsReadOnly(this.keys);
    }

    public IReadOnlyList<Keyframe> Keys { get; }

    public static ParameterCurve Constant(float value) => new([new Keyframe(0, value)]);

    public float Evaluate(float time)
    {
        if (keys.Length == 1)
        {
            return keys[0].Value;
        }
        Keyframe? before = null;
        Keyframe? after = null;
        foreach (var key in keys)
        {
            if (key.Time <= time)
            {
                before = key;
            }
            else
            {
                after = key;
                break;
            }
        }
        return (before, after) switch
        {
            ({ } a, { } b) => Blend(a, b, time),
            (null, { } b) => b.Value,
            ({ } a, _) => a.Value,
            _ => keys[^1].Value,   // unreachable: every key is either at or before t, or after it
        };
    }

    private static float Blend(Keyframe a, Keyframe b, float time)
    {
        var t = (time - a.Time) / (b.Time - a.Time);
        return a.Value * (1f - t) + b.Value * t;
    }
}

/// <summary>
/// One particle emitter. Parameters missing from the file evaluate to 1 in continuous
/// emitters and to 0 in pulse emitters. Angles are already in radians.
/// </summary>
public sealed record EmitterContent(
    EmitterKind Kind,
    EmitterFlags Flags,
    byte Particle,
    int NrOfParticles,
    IReadOnlyDictionary<EmitterParameter, ParameterCurve> Parameters)
{
    private readonly IReadOnlyDictionary<EmitterParameter, ParameterCurve> parameters = ReadOnly.Dictionary(Parameters);

    public IReadOnlyDictionary<EmitterParameter, ParameterCurve> Parameters { get => parameters; init => parameters = ReadOnly.Dictionary(value); }

    public float Evaluate(EmitterParameter parameter, float time) =>
        Parameters.TryGetValue(parameter, out var curve) ? curve.Evaluate(time)
        : Kind == EmitterKind.Pulse ? 0f
        : 1f;
}

/// <summary>The point light that follows an effect.</summary>
public sealed record EffectLightContent(
    Vector3 Offset,
    Vector3 Diffuse,
    Vector3 Ambient,
    float Specular,
    float Radius,
    LightVariationType VariationType,
    float VariationSpeed,
    float VariationAmount,
    LightTransitionType SpawnTransition,
    float SpawnTransitionTime,
    LightTransitionType DespawnTransition,
    float DespawnTransitionTime)
{
    public static EffectLightContent Off { get; } = new(
        Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, LightVariationType.None, 0, 0,
        LightTransitionType.None, 0, LightTransitionType.None, 0);
}

/// <summary>A visual effect from <c>Effects/**/*.xml</c>; Light is null when the effect has none.</summary>
public sealed record VisualEffectContent(
    VisualEffectType Type,
    float Duration,
    IReadOnlyList<EmitterContent> Emitters,
    EffectLightContent? Light)
{
    private readonly IReadOnlyList<EmitterContent> emitters = ReadOnly.List(Emitters);

    public IReadOnlyList<EmitterContent> Emitters { get => emitters; init => emitters = ReadOnly.List(value); }
}
