using Microsoft.Xna.Framework;
using OpenMagicka.Content.Xna;

namespace OpenMagicka.Content.Magicka;

/// <summary>The eleven magic elements (flags), with the original's alias Physical and its named groups.</summary>
[Flags]
public enum Elements
{
    None = 0,
    Earth = 0x1,
    Physical = 0x1,
    Water = 0x2,
    Cold = 0x4,
    Fire = 0x8,
    Lightning = 0x10,
    Arcane = 0x20,
    Life = 0x40,
    Shield = 0x80,
    Ice = 0x100,
    Steam = 0x200,
    Poison = 0x400,

    // Groups the original names; content uses them in element lists.
    Offensive = 0xFF3F,
    Defensive = 0xB0,
    All = 0xFFFF,
    Magick = 0xFFFF,
    Basic = 0xFF,
    Instant = 0x371,
    InstantPhysical = 0x171,
    InstantNonPhysical = 0x270,
    StatusEffects = 0x64E,
    ShieldElements = 0xE0,
    PhysicalElements = 0x101,
    Beams = 0x60,
}


[Flags]
public enum AttackProperties
{
    None = 0,
    Damage = 0x1,
    Knockdown = 0x2,
    Pushed = 0x4,
    Knockback = 0x6,
    Piercing = 0x8,
    ArmourPiercing = 0x10,
    Status = 0x20,
    Entanglement = 0x40,
    Stun = 0x80,
    Bleed = 0x100,
}

/// <summary>XACT sound banks a cue can come from.</summary>
[Flags]
public enum SoundBanks
{
    None = 0,
    WaveBank = 0x1,
    Music = 0x2,
    Ambience = 0x4,
    UI = 0x8,
    Spells = 0x10,
    Characters = 0x20,
    Footsteps = 0x40,
    Weapons = 0x80,
    Misc = 0x100,
    Additional = 0x200,
    AdditionalMusic = 0x400,
}

/// <summary>Damage dealt by an attack, event or buff. Some places store the amount as an integer.</summary>
public readonly record struct DamageContent(AttackProperties Attack, Elements Elements, float Amount, float Magnitude);

/// <summary>Damage taken = damage * Multiplier + Modifier; StatusResistance blocks the element's status effect.</summary>
public readonly record struct ResistanceContent(Elements Elements, float Multiplier, float Modifier, bool StatusResistance);

public sealed record SoundContent(string Cue, SoundBanks Banks);

/// <summary>A piece that flies off when the owner is gibbed.</summary>
public sealed record GibContent(ExternalReference? Model, float Mass, float Scale);

public sealed record PointLightContent(
    float Radius,
    Vector3 DiffuseColor,
    Vector3 AmbientColor,
    float SpecularAmount,
    LightVariationType Variation,
    float VariationAmount,
    float VariationSpeed);

/// <summary>A point light carried at a bone of a character.</summary>
public sealed record AttachedLightContent(string JointName, PointLightContent Light);

/// <summary>An appearance variant: skinned model, scale and tint.</summary>
public sealed record ModelVariantContent(ExternalReference? Model, float Scale, Vector3 Tint);

/// <summary>A particle effect attached to a bone.</summary>
public sealed record AttachedEffectContent(string Bone, string Effect);

/// <summary>
/// A special ability (class name from Abilities.SpecialAbilities) with its cast animation
/// (empty means cast_self), its display name (a text key, optional) and elements.
/// </summary>
public sealed record SpecialAbilityContent(string TypeName, string Animation, string DisplayName, IReadOnlyList<Elements> Elements);
