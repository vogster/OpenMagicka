namespace OpenMagicka.Physics.Characters;

/// <summary>The body part of a character template (docs/engine/ARCHITECTURE.md §7.4).</summary>
/// <param name="Length">The capsule's cylinder between its end caps, at least <see cref="MinLength"/>.</param>
/// <param name="Speed">The maximum walking speed.</param>
/// <param name="Flies">Flying characters ignore gravity and hover above the nav mesh.</param>
public sealed record CharacterShape(float Radius, float Length, float Mass, float Speed, bool Flies = false)
{
    public const float MinLength = 0.01f;

    internal void Validate()
    {
        Check(Radius > 0, Radius, nameof(Radius));
        Check(Length >= MinLength, Length, nameof(Length));
        Check(Mass > 0, Mass, nameof(Mass));
        Check(Speed >= 0, Speed, nameof(Speed));
    }

    private static void Check(bool valid, float value, string name)
    {
        if (!valid || !float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "out of range");
        }
    }
}

/// <summary>What gameplay tells the body about its character before each step.</summary>
/// <param name="IsPlayer">Players are never walked through by heavier characters.</param>
/// <param name="IgnoresImpacts">No impact damage, as during scripted falls.</param>
/// <param name="IsIntangible">Passes through everything but the level, and nothing but the level stops it (ethereal).</param>
/// <param name="IsOnGrease">Grease lasts only for the tick it is reported.</param>
/// <param name="TurnSpeed">The template's turn speed; the share of the way turned per second.</param>
/// <param name="FlyGround">For a flying character, the nav-mesh height below it.</param>
public sealed record CharacterConditions(
    bool IsAlive = true,
    bool IsPlayer = false,
    bool IsEntangled = false,
    bool IsKnockedDown = false,
    bool CanBeKnockedDown = true,
    bool IsLevitating = false,
    bool IsOnGrease = false,
    bool IgnoresImpacts = false,
    bool IsIntangible = false,
    float TurnSpeed = 10,
    float? FlyGround = null)
{
    public static CharacterConditions Default { get; } = new();
}

/// <summary>What happened to a character during one step; gameplay applies the consequences.</summary>
/// <param name="ImpactSpeed">The hardest hit on the level while in the air, if any (damage: <c>ImpactDamage</c> in Gameplay).</param>
/// <param name="KnockedDownSelf">The character was pushed into a wall or a heavier character.</param>
/// <param name="KnockedDown">Characters this one knocked down.</param>
/// <param name="Touched">Characters this one touched (an invisible character touching an enemy is seen).</param>
public sealed record CharacterReport(
    float? ImpactSpeed,
    bool KnockedDownSelf,
    IReadOnlyList<CharacterBody> KnockedDown,
    IReadOnlyList<CharacterBody> Touched)
{
    public static CharacterReport Quiet { get; } = new(null, false, [], []);
}
