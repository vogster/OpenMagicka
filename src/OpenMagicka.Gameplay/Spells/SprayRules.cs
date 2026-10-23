namespace OpenMagicka.Gameplay.Spells;

/// <summary>Sprays (docs/gameplay/SPELLS.md §5.2). Arc angles reach that far either side of the facing.</summary>
public static class SprayRules
{
    public const float HalfAngle = MathF.PI / 10;

    public const float MaxDuration = 4;

    /// <summary>The range builds up at this rate (a fraction per second) after the cast starts.</summary>
    public const float BuildUpPerSecond = 4;

    public const float WeaponRadius = 6;

    public const float WeaponHalfAngle = MathF.PI / 2;

    /// <summary>The base range: 5 plus the spray magnitudes (10 with five elements).</summary>
    public static float Range(Spell spell) => 5 + spell.TotalSprayMagnitude;

    /// <summary>The share of the range built up so far.</summary>
    public static float BuildUp(float elapsed) => Math.Clamp(BuildUpPerSecond * elapsed, 0, 1);

    /// <summary>
    /// How far a spray hits this frame: <c>R · 0.1 · R · f</c>, f the free part of the ray before
    /// the first obstacle (about R²/10 in the open).
    /// </summary>
    public static float Reach(float range, float freeFraction) => range * 0.1f * range * freeFraction;

    public static float AreaRadius(Spell spell) => spell.BlastSize * 10;
}
