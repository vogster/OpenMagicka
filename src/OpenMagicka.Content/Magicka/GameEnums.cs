namespace OpenMagicka.Content.Magicka;

// The original's enumerations that content files, saves and network messages refer to, with
// the same names and values (docs/formats/ENUMS.md). Names are parsed ignoring case; the
// original's count sentinels (NumberOfTypes and the like) are left out unless content uses them.
#pragma warning disable CA1707 // the names are the game's own

/// <summary>Status effects on a character (flags, <c>short</c> in the original).</summary>
[Flags]
public enum StatusEffects : short
{
    None = 0,
    Burning = 0x1,
    Wet = 0x2,
    Frozen = 0x4,
    Cold = 0x8,
    Poisoned = 0x10,
    Healing = 0x20,
    Life = 0x20,
    Greased = 0x40,
    Steamed = 0x80,
    Bleeding = 0x100,
}

/// <summary>Character factions (flags); NEUTRAL and PLAYERS are groups.</summary>
[Flags]
public enum Factions
{
    NONE = 0,
    EVIL = 0x1,
    WILD = 0x2,
    FRIENDLY = 0x4,
    DEMON = 0x8,
    UNDEAD = 0x10,
    HUMAN = 0x20,
    WIZARD = 0x40,
    NEUTRAL = 0xFF,
    PLAYER0 = 0x100,
    PLAYER1 = 0x200,
    PLAYER2 = 0x400,
    PLAYER3 = 0x800,
    TEAM_RED = 0x1000,
    TEAM_BLUE = 0x2000,
    PLAYERS = 0x3F00,
}

/// <summary>What an AI agent does; also its reaction when provoked.</summary>
public enum Order : byte
{
    None,
    Idle,
    Attack,
    Defend,
    Flee,
    Wander,
    Panic,
}

/// <summary>What provokes an AI agent's reaction (flags).</summary>
[Flags]
public enum ReactTo : byte
{
    None = 0,
    Attack = 0x1,
    Proximity = 0x2,
}

/// <summary>How a spell is cast; None and Blast share 0.</summary>
public enum CastType
{
    None = 0,
    Blast = 0,
    Force = 1,
    Area = 2,
    Self = 3,
    Weapon = 4,
    Magick = 5,
}

/// <summary>
/// Magicks. NrOfMagicks (35) ends the magicks a player can cast; the values after it are
/// effects that items and enemies use through the same mechanism.
/// </summary>
public enum MagickType
{
    None = 0,
    Revive = 1,
    Grease = 2,
    Haste = 3,
    Invisibility = 4,
    Teleport = 5,
    Fear = 6,
    Charm = 7,
    ThunderB = 8,
    Rain = 9,
    Tornado = 10,
    Blizzard = 11,
    MeteorS = 12,
    Conflagration = 13,
    ThunderS = 14,
    TimeWarp = 15,
    Vortex = 16,
    SUndead = 17,
    SElemental = 18,
    SDeath = 19,
    SPhoenix = 20,
    Nullify = 21,
    Corporealize = 22,
    CTD = 23,
    Napalm = 24,
    Portal = 25,
    TractorPull = 26,
    ProppMagick = 27,
    Levitate = 28,
    ChainLightning = 29,
    Confuse = 30,
    Wave = 31,
    PerformanceEnchantment = 32,
    JudgementSpray = 33,
    Amalgameddon = 34,
    NrOfMagicks = 35,
    Grow = 36,
    Shrink = 37,
    Etherealize = 38,
    Polymorph = 39,
    Earthquake = 40,
    Votal = 41,
    KnockDownChance = 42,
    SummonCross = 43,
    VladZap = 44,
    SpawnSlime = 45,
    SpawnSlimeOverkill = 46,
    GreaseSplash = 47,
    EtherealClone = 48,
    InflictBleedDamage = 49,
    InflictPoisonDamage = 50,
}

/// <summary>The kind of an AI event (the tag of a child of spawn, giveOrder and the like).</summary>
public enum AIEventType
{
    Move,
    Animation,
    Face,
    Kill,
    Loop,
}

/// <summary>How a loop event repeats the event list.</summary>
public enum LoopType
{
    Loop,
    Reverse,
}

#pragma warning restore CA1707
