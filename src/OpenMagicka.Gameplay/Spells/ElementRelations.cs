namespace OpenMagicka.Gameplay.Spells;

/// <summary>
/// How elements meet (docs/gameplay/SPELLS.md §1.3, §2.1). The relations look at flags, so an
/// entry holding several elements relates through any of them. For the opposite pairs either flag
/// may sit on either side: one entry holding both Arcane and Life is opposite to anything, and a
/// magick entry (<see cref="Elements.All"/>) is opposite to every element.
/// </summary>
public static class ElementRelations
{
    /// <summary>Cancel each other in a queue: Arcane–Life, Fire–Cold, Earth–Lightning, Water–Lightning, Shield–Shield.</summary>
    public static bool Opposites(Elements a, Elements b) =>
        Pair(a, b, Elements.Arcane, Elements.Life)
        || Pair(a, b, Elements.Fire, Elements.Cold)
        || Pair(a, b, Elements.Earth, Elements.Lightning)
        || Pair(a, b, Elements.Water, Elements.Lightning)
        || (Has(a, Elements.Shield) && Has(b, Elements.Shield));

    /// <summary>The opposites plus Fire–Water: when beams meet and against elemental barriers, not in a queue.</summary>
    public static bool InclusiveOpposites(Elements a, Elements b) => Opposites(a, b) || Pair(a, b, Elements.Fire, Elements.Water);

    /// <summary>Merge back into Water: Cold–Steam, Ice–Fire.</summary>
    public static bool Revertable(Elements a, Elements b) =>
        Either(a, b, Elements.Cold, Elements.Steam) || Either(a, b, Elements.Ice, Elements.Fire);

    /// <summary>Merge into Ice or Steam: Water–Cold, Water–Fire.</summary>
    public static bool Combinable(Elements a, Elements b) =>
        Either(a, b, Elements.Water, Elements.Cold) || Either(a, b, Elements.Water, Elements.Fire);

    /// <summary>
    /// Conjuring Lightning while wet (unless an item allows it) or Fire while greased hurts the
    /// caster instead of adding the element (call with named arguments). The damage itself, the
    /// element's self-cast damage at half magnitude, belongs to the damage rules (OM-36).
    /// </summary>
    public static bool Backfires(Elements element, bool wet, bool greased, bool wetLightningAllowed) =>
        (Has(element, Elements.Lightning) && wet && !wetLightningAllowed) || (Has(element, Elements.Fire) && greased);

    private static bool Has(Elements set, Elements flag) => (set & flag) == flag;

    /// <summary>One of the flags on either side and the other on either side.</summary>
    private static bool Pair(Elements a, Elements b, Elements x, Elements y) =>
        (Has(a, x) || Has(b, x)) && (Has(a, y) || Has(b, y));

    /// <summary>One flag on one side and the other on the other side.</summary>
    private static bool Either(Elements a, Elements b, Elements x, Elements y) =>
        (Has(a, x) && Has(b, y)) || (Has(b, x) && Has(a, y));
}
