namespace OpenMagicka.Gameplay.Spells;

/// <summary>What a spell with Shield makes (docs/gameplay/SPELLS.md §5.6).</summary>
public abstract record ShieldCast;

public enum ShieldShape
{
    /// <summary>A disc whose arc stands in front of the caster.</summary>
    Disc,
    /// <summary>A sphere around the caster.</summary>
    Sphere,
    /// <summary>A wall ahead of the caster.</summary>
    Wall,
}

/// <param name="Offset">The centre's distance ahead of the caster (negative: behind); a wall adds the caster's radius.</param>
public sealed record PureShield(ShieldShape Shape, float Radius, float Hp, float Offset) : ShieldCast;

/// <summary>The bubble a pure shield cast on oneself makes: it reflects damage and turns blows into knockback.</summary>
public sealed record Bubble(float Hp, float DecayPerSecond) : ShieldCast;

public enum BarrierLayout
{
    /// <summary>An arc of radius <see cref="ShieldRules.ArcRadius"/> spreading both ways from in front.</summary>
    Arc,
    /// <summary>A full ring around the caster.</summary>
    Ring,
    /// <summary>A line straight ahead.</summary>
    Line,
}

/// <param name="Fuse">A mine goes off this long after a character steps on it.</param>
/// <param name="OwnerRange">A mine goes off when its owner is farther than this.</param>
public sealed record Mines(BarrierLayout Layout, int LineCount, float LineStep, float MineRadius, float Fuse, float BlastRadius, float OwnerRange) : ShieldCast;

/// <param name="Solid">Earth or Ice: stone or ice barriers; otherwise elemental walls.</param>
/// <param name="Armor">Taken off each hit (ice), except fire against ice.</param>
/// <param name="SegmentRadius">Barriers stand this far apart over two.</param>
/// <param name="ArcCentreBack">How far behind the caster a forward arc is centred (0 for other layouts).</param>
/// <param name="RingRadius">An area ring's radius.</param>
public sealed record Barriers(
    BarrierLayout Layout, bool Solid, float Hp, int Armor, float DecayPerSecond, float SegmentRadius,
    float ArcCentreBack, float RingRadius, int LineCount, float LineStep) : ShieldCast;

/// <param name="Speed">The wearer's speed factor.</param>
/// <param name="FlatReduction">Taken off physical damage first.</param>
/// <param name="DamageFactor">Then physical damage is multiplied by this.</param>
/// <param name="IceBlastMagnitude">Ice armour hits everyone around when it forms (<see cref="ShieldRules.IceArmourBlast"/>); 0 without Ice.</param>
public sealed record Armour(float Hp, float Mass, float Speed, float FlatReduction, float DamageFactor, float IceBlastMagnitude) : ShieldCast;

/// <summary>Immunity to <see cref="Elements"/> for everyone within <see cref="Radius"/>.</summary>
public sealed record Ward(Elements Elements, float Radius, float Duration) : ShieldCast;

public static class ShieldRules
{
    /// <summary>A shield loses 10 HP every 0.1 s, and holds at most this many.</summary>
    public const float ShieldDecayPerSecond = 100;

    public const float ShieldMaxHp = 5000;

    /// <summary>The radius of a pure shield, disc, sphere or wall.</summary>
    public const float ShieldRadius = 5.5f;

    public const float ArcRadius = 4;

    /// <summary>Mines and barriers cast with a weapon stand this far apart in a line.</summary>
    public const float LineStep = 2.5f;

    /// <summary>A forward arc of mines is centred this far behind the caster and grows a mine this often, to this length each way.</summary>
    public const float MineArcCentreBack = 1;

    public const float MineChainInterval = 0.1f;

    public const float MineArcLength = 2 * MathF.PI * ArcRadius * 0.125f;

    /// <summary>A forward arc of barriers grows a barrier this often, about half a turn each way.</summary>
    public const float BarrierChainInterval = 0.075f;

    public const float BarrierDecayPerSecond = 50;

    /// <summary>A barrier hits everyone within this many of its (scaled) radii as it rises, and breaks other casts' barriers and mines within <see cref="BarrierClearFactor"/>.</summary>
    public const float BarrierSpawnHitFactor = 1.25f;

    public const float BarrierClearFactor = 0.8f;

    /// <summary>Raising barriers shakes the camera between these strengths for half the Shield magnitude in seconds.</summary>
    public const float BarrierShakeMin = 0.25f;

    public const float BarrierShakeMax = 0.75f;

    /// <summary>Ice armour hits Earth and piercing for this much within <see cref="IceArmourBlastRadius"/> as it forms.</summary>
    public const float IceArmourBlast = 275;

    public const float IceArmourBlastRadius = 3;

    private const Elements Physical = Elements.Earth | Elements.Ice;
    private const Elements StatusElements = Elements.Water | Elements.Cold | Elements.Fire | Elements.Life | Elements.Steam | Elements.Poison;
    private const Elements WardElements = Elements.Lightning | Elements.Arcane | Elements.Life | Elements.Steam | Elements.Water | Elements.Cold | Elements.Fire;

    /// <summary>
    /// What the spell makes, by what it holds besides Shield: nothing else gives a shield (a bubble
    /// on oneself); Arcane or Life without Earth or Ice lays mines; anything else raises barriers.
    /// On oneself, Earth or Ice is armour and other elements a ward (null when only Poison is left).
    /// </summary>
    public static ShieldCast? For(Spell spell, CastType cast)
    {
        var elements = spell.Element;
        if ((elements & Elements.Shield) == 0 || elements == Elements.All)
        {
            throw new ArgumentException("not a shield spell", nameof(spell));
        }
        var others = elements & ~Elements.Shield;
        if (cast == CastType.Self)
        {
            return Self(spell, others);
        }
        if (others == Elements.None)
        {
            return cast switch
            {
                CastType.Force => new PureShield(ShieldShape.Disc, ShieldRadius, 800, -3.5f),
                CastType.Area => new PureShield(ShieldShape.Sphere, ShieldRadius, 1000, 0),
                CastType.Weapon => new PureShield(ShieldShape.Wall, ShieldRadius, 800, 7.5f),
                _ => throw new ArgumentOutOfRangeException(nameof(cast), cast, "not a shield cast"),
            };
        }
        var layout = Layout(cast);
        var lineCount = 2 + (int)MathF.Max(spell.TotalMagnitude / 2, 1);
        if ((others & Physical) == 0 && (others & (Elements.Arcane | Elements.Life)) != 0)
        {
            return new Mines(layout, lineCount, LineStep, MineRadius: 1, Fuse: 0.5f, BlastRadius: 3, OwnerRange: 30);
        }
        var solid = (others & Physical) != 0;
        var plain = (elements & (StatusElements | Elements.Lightning)) == 0;
        return new Barriers(
            layout, solid,
            Hp: solid ? (1 + spell.Earth) * 500 : spell.TotalMagnitude * 100,
            Armor: solid ? (int)(50 * spell.Ice) : 0,
            BarrierDecayPerSecond,
            SegmentRadius: solid ? 1.25f : 0.75f,
            ArcCentreBack: layout == BarrierLayout.Arc && plain ? 2 : 0,
            RingRadius: spell.Shield + 3,
            lineCount, LineStep);
    }

    private static ShieldCast? Self(Spell spell, Elements others)
    {
        if (others == Elements.None)
        {
            return new Bubble(500, 100);
        }
        if ((others & Physical) != 0)
        {
            var earth = (others & Elements.Earth) != 0;
            return new Armour(
                Hp: earth ? 1500 : 900, Mass: 1001,
                Speed: 0.9f - (0.075f * spell.Earth + 0.025f * spell.Ice),
                FlatReduction: earth ? 10 : 70,
                DamageFactor: 1 - (0.05f * spell.Ice + 0.15f * spell.Earth),
                IceBlastMagnitude: (others & Elements.Ice) != 0 ? MathF.Max(1, spell.Ice + spell.Earth) : 0);
        }
        var ward = others & WardElements;
        if (ward == Elements.None)
        {
            return null;
        }
        var m = MathF.Max(1, Enumerable.Range(0, ElementIndex.Count)
            .Select(i => (Elements)(1 << i))
            .Where(e => (ward & e) != 0)
            .Max(e => spell[e]));
        return new Ward(ward, 1.5f * m, 15 + 5 * m);
    }

    private static BarrierLayout Layout(CastType cast) => cast switch
    {
        CastType.Force => BarrierLayout.Arc,
        CastType.Area => BarrierLayout.Ring,
        CastType.Weapon => BarrierLayout.Line,
        _ => throw new ArgumentOutOfRangeException(nameof(cast), cast, "not a shield cast"),
    };
}
