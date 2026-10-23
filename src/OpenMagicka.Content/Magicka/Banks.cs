namespace OpenMagicka.Content.Magicka;

/// <summary>Sound banks a cue can come from (docs/formats/ENUMS.md); flags in the original.</summary>
[Flags]
public enum Banks : ushort
{
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
