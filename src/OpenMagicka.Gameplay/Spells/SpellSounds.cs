namespace OpenMagicka.Gameplay.Spells;

/// <summary>
/// A spell's sounds as the original plays them when it is cast (docs/gameplay/SPELLS.md §4.3): one cue of the Spells
/// bank for each element in it, at the element's magnitude (the cue's "Magnitude"), from a table the spell's type and
/// the cast choose:
/// <list type="bullet">
/// <item>a forward cast, and every lightning cast: <c>spell_&lt;element&gt;_spray</c>;</item>
/// <item>an area cast or a self-cast: <c>spell_&lt;element&gt;_area</c> (a beam's self-cast: <c>_self</c>, none for
/// earth, ice or poison; a beam's weapon cast: <c>_area</c>);</item>
/// <item>a blast (no cast type): <c>spell_&lt;element&gt;_blast</c>.</item>
/// </list>
/// </summary>
public static class SpellSounds
{
    /// <summary>The elements in the original's order (its tables' rows).</summary>
    private static readonly Elements[] Order =
    [
        Elements.Earth, Elements.Water, Elements.Cold, Elements.Fire, Elements.Lightning, Elements.Arcane, Elements.Life, Elements.Shield,
        Elements.Ice, Elements.Steam, Elements.Poison,
    ];

    /// <summary>The cues of a spell cast so, with their magnitudes; none for a type or a cast the original has none for.</summary>
    public static IReadOnlyList<(string Cue, float Magnitude)> For(Spell spell, SpellType type, CastType cast)
    {
        var sounds = new List<(string, float)>();
        foreach (var element in Order)
        {
            var magnitude = spell[element];
            if (magnitude > 0 && Kind(type, cast, element) is { } kind)
            {
                sounds.Add(($"spell_{element.ToString().ToLowerInvariant()}_{kind}", magnitude));
            }
        }
        return sounds;
    }

    private static string? Kind(SpellType type, CastType cast, Elements element) => (type, cast) switch
    {
        (SpellType.Lightning, _) => "spray",
        (SpellType.Beam, CastType.Self) => element is Elements.Earth or Elements.Ice or Elements.Poison ? null : "self",
        (SpellType.Beam, CastType.Weapon) => "area",
        (SpellType.Spray or SpellType.Projectile or SpellType.Shield or SpellType.Beam or SpellType.Magick, CastType.Area or CastType.Self) => "area",
        (SpellType.Spray or SpellType.Projectile or SpellType.Shield or SpellType.Beam or SpellType.Magick, CastType.None) => "blast",
        (SpellType.Spray or SpellType.Projectile or SpellType.Shield or SpellType.Beam or SpellType.Magick, CastType.Force) => "spray",
        _ => null,
    };
}
