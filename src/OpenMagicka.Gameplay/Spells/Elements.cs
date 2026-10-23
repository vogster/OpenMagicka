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
