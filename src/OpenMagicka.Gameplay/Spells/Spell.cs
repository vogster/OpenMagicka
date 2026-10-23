namespace OpenMagicka.Gameplay.Spells;

/// <summary>
/// A spell: a magnitude for each of the 11 elements (docs/gameplay/SPELLS.md §3), or a magick.
/// <see cref="Element"/> holds the elements with a positive magnitude; adding two spells cancels
/// and merges elements (§3.1). Magnitudes can turn negative there; they then set no flag.
/// </summary>
public readonly record struct Spell
{
    public Spell(float earth = 0, float water = 0, float cold = 0, float fire = 0, float lightning = 0, float arcane = 0,
        float life = 0, float shield = 0, float ice = 0, float steam = 0, float poison = 0)
    {
        (Earth, Water, Cold, Fire, Lightning, Arcane) = (earth, water, cold, fire, lightning, arcane);
        (Life, Shield, Ice, Steam, Poison) = (life, shield, ice, steam, poison);
    }

    public float Earth { get; init; }

    public float Water { get; init; }

    public float Cold { get; init; }

    public float Fire { get; init; }

    public float Lightning { get; init; }

    public float Arcane { get; init; }

    public float Life { get; init; }

    public float Shield { get; init; }

    public float Ice { get; init; }

    public float Steam { get; init; }

    public float Poison { get; init; }

    /// <summary>The magick this entry stands for; <see cref="MagickType.None"/> for an ordinary spell.</summary>
    public MagickType Magick { get; init; }

    /// <summary>The elements with a positive magnitude, or <see cref="Elements.All"/> for a magick.</summary>
    public Elements Element => Magick != MagickType.None ? Elements.All
        : Flag(Earth, Elements.Earth) | Flag(Water, Elements.Water) | Flag(Cold, Elements.Cold) | Flag(Fire, Elements.Fire)
          | Flag(Lightning, Elements.Lightning) | Flag(Arcane, Elements.Arcane) | Flag(Life, Elements.Life)
          | Flag(Shield, Elements.Shield) | Flag(Ice, Elements.Ice) | Flag(Steam, Elements.Steam) | Flag(Poison, Elements.Poison);

    public float TotalMagnitude => Earth + Water + Cold + Fire + Lightning + Arcane + Life + Shield + Ice + Steam + Poison;

    /// <summary>Fire, Cold, Steam, Water and Poison together: what a spray's range grows with.</summary>
    public float TotalSprayMagnitude => Fire + Cold + Steam + Water + Poison;

    public SpellType Type => SpellTypes.Of(Element);

    /// <summary>The blast size of the spell's own type (<see cref="BlastSizeAs"/>).</summary>
    public float BlastSize => BlastSizeAs(Type);

    /// <summary>
    /// <c>√(m / 5)</c>, m the biggest magnitude that matters to the type: Fire, Cold, Steam, Water or
    /// Poison for a spray, Earth or Ice for a projectile, Arcane or Life for a beam; 0 otherwise.
    /// Radii are usually ten times this.
    /// </summary>
    public float BlastSizeAs(SpellType type)
    {
        var m = type switch
        {
            SpellType.Spray => MathF.Max(Steam, MathF.Max(Fire, MathF.Max(Cold, MathF.Max(Water, Poison)))),
            SpellType.Projectile => MathF.Max(Earth, Ice),
            SpellType.Beam => MathF.Max(Arcane, Life),
            _ => 0,
        };
        return MathF.Sqrt(MathF.Max(m, 0) / 5);
    }

    /// <summary>The magnitudes scaled to a total of 1 (a weapon's queue holds these); unchanged when the total is not positive.</summary>
    public Spell Normalized()
    {
        var total = TotalMagnitude;
        return total <= 0 ? this : new Spell(Earth / total, Water / total, Cold / total, Fire / total, Lightning / total,
            Arcane / total, Life / total, Shield / total, Ice / total, Steam / total, Poison / total) { Magick = Magick };
    }

    /// <summary>The magnitude of one element.</summary>
    public float this[Elements element] => element switch
    {
        Elements.Earth => Earth,
        Elements.Water => Water,
        Elements.Cold => Cold,
        Elements.Fire => Fire,
        Elements.Lightning => Lightning,
        Elements.Arcane => Arcane,
        Elements.Life => Life,
        Elements.Shield => Shield,
        Elements.Ice => Ice,
        Elements.Steam => Steam,
        Elements.Poison => Poison,
        _ => throw new ArgumentOutOfRangeException(nameof(element), element, "not a single element"),
    };

    /// <summary>Magnitude 1 for each element of the mask.</summary>
    public static Spell Of(Elements elements)
    {
        float One(Elements flag) => (elements & flag) == flag ? 1 : 0;
        return new Spell(One(Elements.Earth), One(Elements.Water), One(Elements.Cold), One(Elements.Fire), One(Elements.Lightning),
            One(Elements.Arcane), One(Elements.Life), One(Elements.Shield), One(Elements.Ice), One(Elements.Steam), One(Elements.Poison));
    }

    public static Spell OfMagick(MagickType magick) => new() { Magick = magick };

    /// <summary>
    /// The sum, step by step as in §3.1: opposites subtract, Ice and Steam turn back into Water
    /// against Fire and Cold, then Water and Fire make Steam and Water and Cold make Ice. The later
    /// steps use the plain sums of the inputs, not the values the earlier steps left. A magick is
    /// not carried over: folding a queue returns a leading magick entry itself (§3).
    /// </summary>
    public static Spell operator +(Spell a, Spell b)
    {
        var (e, w, c, f, z) = (a.Earth + b.Earth, a.Water + b.Water, a.Cold + b.Cold, a.Fire + b.Fire, a.Lightning + b.Lightning);
        var (l, p) = (a.Life + b.Life, a.Poison + b.Poison);

        var earth = MathF.Max(e - z, 0);
        var cold = MathF.Max(c - f, 0);
        var fire = MathF.Max(f - c, 0);
        var lightning = MathF.Max(z - e, 0);
        var poison = MathF.Max(p - l, 0);
        var water = 0f;

        var ice = a.Ice + b.Ice;
        if (ice > 0 && f > 0)
        {
            var k = MathF.Min(ice, f);
            (water, fire, ice) = (water + k, fire - k, ice - k);
        }
        var steam = a.Steam + b.Steam;
        if (steam > 0 && c > 0)
        {
            var k = MathF.Min(steam, c);
            (water, cold, steam) = (water + k, cold - k, steam - k);
        }
        water += w;

        var boiled = MathF.Min(w, f);
        (steam, water, fire) = (steam + boiled, water - boiled, fire - boiled);
        var frozen = MathF.Min(w, c);
        (ice, water, cold) = (ice + frozen, water - frozen, cold - frozen);

        return new Spell(earth, water, cold, fire, lightning, a.Arcane + b.Arcane, l, a.Shield + b.Shield, ice, steam, poison);
    }

    private static Elements Flag(float magnitude, Elements flag) => magnitude > 0 ? flag : Elements.None;
}
