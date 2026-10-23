using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>
/// Damage from hitting the level while in the air, by the closing speed the body reports
/// (docs/engine/ARCHITECTURE.md §7.4): <c>100 · (v / 10)^2.7</c>, dealt as Earth with magnitude 1
/// above <see cref="Threshold"/>, knocking down above <see cref="KnockdownThreshold"/>.
/// </summary>
public static class ImpactDamage
{
    public const float Threshold = 50;

    public const float KnockdownThreshold = 100;

    public const float Exponent = 2.7f;

    public const Elements Element = Elements.Earth;

    public const float Magnitude = 1;

    /// <summary>The damage of an impact; zero for a speed that is not positive.</summary>
    public static float Amount(float speed) => speed > 0 && float.IsFinite(speed) ? 100 * MathF.Pow(speed / 10, Exponent) : 0;

    /// <summary>The impact's damage and whether it knocks down, or null when it is too soft to hurt.</summary>
    public static (float Amount, bool KnocksDown)? For(float speed)
    {
        var amount = Amount(speed);
        return amount > Threshold ? (amount, amount > KnockdownThreshold) : null;
    }
}
