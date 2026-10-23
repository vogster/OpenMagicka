namespace OpenMagicka.Gameplay.Spells;

/// <summary>Beams (docs/gameplay/SPELLS.md §5.4).</summary>
public static class BeamRules
{
    public const float GrowthPerSecond = 90;

    public const float MaxLength = 75;

    /// <summary>The deepest a chain of reflected and merged beams goes.</summary>
    public const int MaxChainDepth = 6;

    /// <summary>Beams closer than this meet; they explode this long after meeting.</summary>
    public const float MeetingDistance = 1;

    public const float ExplosionDelay = 0.25f;

    public const float KnockbackStrength = 500;

    public const float KnockbackMagnitude = 2;

    public const float KnockbackRadius = 5;

    /// <summary>How far either side of the facing a weapon cast's arcane blade reaches.</summary>
    public const float WeaponHalfAngle = 9 * MathF.PI / 20;

    /// <summary>Elements a beam carries besides Arcane and Life.</summary>
    public const Elements ExtraElements = Elements.Water | Elements.Cold | Elements.Fire | Elements.Lightning | Elements.Steam | Elements.Poison;

    /// <summary>A forward beam lasts <c>1 + 2·(A + L)</c> seconds.</summary>
    public static float Lifetime(Spell spell) => 1 + 2 * (spell.Arcane + spell.Life);

    public static float Length(float elapsed) => Math.Clamp(elapsed * GrowthPerSecond, 0, MaxLength);

    public static float WeaponRadius(Spell spell) => 1.5f + spell.TotalMagnitude / 2;

    public static float AreaRadius(Spell spell) => spell.BlastSize * 10;

    /// <summary>
    /// Crossing beams explode when they share an owner, when their extra elements oppose (fire
    /// and water included), when one carries Arcane and the other does not, or when they meet
    /// nearly head-on (their mean direction shorter than 0.2); otherwise they merge.
    /// </summary>
    public static bool Explode(bool sameOwner, Elements a, Elements b, float meanDirectionLength) =>
        sameOwner
        || ElementRelations.InclusiveOpposites(a & ExtraElements, b & ExtraElements)
        || ((a & Elements.Arcane) != 0) != ((b & Elements.Arcane) != 0)
        || meanDirectionLength < 0.2f;

    /// <summary>The explosion: each owner's damage over 3 plus the number of owners, and a knockback.</summary>
    public static float ExplosionRadius(int owners) => 3 + owners;
}
