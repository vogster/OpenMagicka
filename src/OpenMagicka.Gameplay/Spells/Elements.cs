namespace OpenMagicka.Gameplay.Spells;

/// <summary>The elements as flags (docs/gameplay/SPELLS.md §1.1). Index = log2 of the flag.</summary>
[Flags]
public enum Elements
{
    None = 0,
    Earth = 0x001,
    Water = 0x002,
    Cold = 0x004,
    Fire = 0x008,
    Lightning = 0x010,
    Arcane = 0x020,
    Life = 0x040,
    Shield = 0x080,
    Ice = 0x100,
    Steam = 0x200,
    /// <summary>Not available to the player: characters, items and magicks only.</summary>
    Poison = 0x400,
    /// <summary>A magick entry ("all elements").</summary>
    All = 0xFFFF,
}

/// <summary>The magicks, in the original order: bit <c>1 &lt;&lt; type</c> unlocks one in a save.</summary>
public enum MagickType
{
    None,
    Revive,
    Grease,
    Haste,
    Invisibility,
    Teleport,
    Fear,
    Charm,
    ThunderB,
    Rain,
    Tornado,
    Blizzard,
    MeteorS,
    Conflagration,
    ThunderS,
    TimeWarp,
    Vortex,
    SUndead,
    SElemental,
    SDeath,
    SPhoenix,
    Nullify,
    Corporealize,
    CTD,
    Napalm,
    Portal,
    TractorPull,
    ProppMagick,
    Levitate,
    ChainLightning,
    Confuse,
    Wave,
    PerformanceEnchantment,
    JudgementSpray,
    Amalgameddon,
    /// <summary>The player magicks end here; the rest are abilities of characters and items.</summary>
    NrOfMagicks,
    Grow,
    Shrink,
    Etherealize,
    Polymorph,
    Earthquake,
    Votal,
    KnockDownChance,
    SummonCross,
    VladZap,
    SpawnSlime,
    SpawnSlimeOverkill,
    GreaseSplash,
    EtherealClone,
    InflictBleedDamage,
    InflictPoisonDamage,
}

/// <summary>The composite element masks the rules use (docs/gameplay/SPELLS.md §1.1).</summary>
public static class ElementSets
{
    /// <summary>Earth and Ice.</summary>
    public const Elements Physical = Elements.Earth | Elements.Ice;

    /// <summary>Arcane and Life.</summary>
    public const Elements Beams = Elements.Arcane | Elements.Life;

    /// <summary>The elements that set a status: Water, Cold, Fire, Life, Steam and Poison.</summary>
    public const Elements StatusEffects = Elements.Water | Elements.Cold | Elements.Fire | Elements.Life | Elements.Steam | Elements.Poison;

    /// <summary>Earth, Lightning, Arcane, Life, Ice and Steam.</summary>
    public const Elements Instant = Elements.Earth | Elements.Lightning | Elements.Arcane | Elements.Life | Elements.Ice | Elements.Steam;

    /// <summary>Lightning, Arcane, Life and Steam.</summary>
    public const Elements InstantNonPhysical = Elements.Lightning | Elements.Arcane | Elements.Life | Elements.Steam;
}
