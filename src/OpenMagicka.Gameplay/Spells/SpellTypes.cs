namespace OpenMagicka.Gameplay.Spells;

/// <summary>What a spell does when cast (docs/gameplay/SPELLS.md §3.2, §4), in the original order.</summary>
public enum SpellType
{
    Push,
    Spray,
    Projectile,
    Shield,
    Beam,
    Lightning,
    Magick,
}

/// <summary>How a spell is cast (§4).</summary>
public enum CastType
{
    /// <summary>No cast; a blast uses the same value.</summary>
    None = 0,
    Force = 1,
    Area = 2,
    Self = 3,
    Weapon = 4,
    Magick = 5,
}

public static class SpellTypes
{
    /// <summary>The type, by priority: Magick, Push (nothing), Shield, Projectile (Earth, Ice), Beam (Arcane, Life), Lightning (without Steam), Spray.</summary>
    public static SpellType Of(Elements elements) =>
        elements == Elements.All ? SpellType.Magick
        : elements == Elements.None ? SpellType.Push
        : Any(elements, Elements.Shield) ? SpellType.Shield
        : Any(elements, ElementSets.Physical) ? SpellType.Projectile
        : Any(elements, ElementSets.Beams) ? SpellType.Beam
        : Any(elements, Elements.Lightning) && !Any(elements, Elements.Steam) ? SpellType.Lightning
        : SpellType.Spray;

    private static bool Any(Elements set, Elements flags) => (set & flags) != 0;
}
