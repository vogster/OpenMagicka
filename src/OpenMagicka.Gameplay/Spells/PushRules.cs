namespace OpenMagicka.Gameplay.Spells;

/// <summary>An empty queue's push (docs/gameplay/SPELLS.md §5.1): a cone forward or a ring around the caster.</summary>
/// <param name="HalfAngle">How far either side of the facing it reaches (as all arc angles here); 2π covers everything.</param>
public sealed record PushWave(float Strength, float HalfAngle, float Radius, float Duration)
{
    /// <summary>The magnitude of the push damage each target takes once.</summary>
    public const float Magnitude = 1.5f;

    /// <summary>The radius covered so far: from a quarter of the full radius to all of it over the duration.</summary>
    public float CoveredRadius(float elapsed) => Radius * (0.25f + 0.75f * Math.Clamp(elapsed / Duration, 0, 1));
}

public static class PushRules
{
    /// <summary>The push for a cast; none for Self and Weapon.</summary>
    public static PushWave? For(CastType cast, float spellPower, float additionalRadius) => cast switch
    {
        CastType.Force => new PushWave(Strength(spellPower), MathF.PI / 6, 10, 0.25f),
        CastType.Area => new PushWave(Strength(spellPower), 2 * MathF.PI, 3.5f + additionalRadius, 0.25f),
        _ => null,
    };

    /// <summary>Things without health are thrown at this elevation (radians).</summary>
    public static float LaunchElevation(float spellPower) => 10 * MathF.PI / 180 + spellPower * 0.1f * MathF.PI / 8;

    private static float Strength(float spellPower) => 50 + 500 * spellPower;
}
