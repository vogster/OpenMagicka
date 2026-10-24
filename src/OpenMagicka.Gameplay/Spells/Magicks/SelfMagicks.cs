using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Items;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>Haste: twice as fast at first, the boost fading over 10 s; casting it again starts it over (MAGICKS.md §3.1).</summary>
public sealed class Haste() : Magick(MagickType.Haste, SelfClip)
{
    public const float Duration = 10;

    public const string CastSound = "magick_haste";

    public override bool Execute(Character caster)
    {
        Sound(caster, CastSound);
        caster.Haste(Duration);
        return true;
    }
}

/// <summary>Invisibility for 15 s (MAGICKS.md §3.2).</summary>
public sealed class Invisibility() : Magick(MagickType.Invisibility, SelfClip)
{
    public override bool Execute(Character caster)
    {
        Sound(caster, SpecialAbilities.InvisibilitySound);
        caster.SetInvisible(SpecialAbilities.InvisibilityTime);
        return true;
    }
}

/// <summary>Levitation for 30 s: the caster hovers over the ground and slides (MAGICKS.md §3.3).</summary>
public sealed class Levitate() : Magick(MagickType.Levitate, SelfClip)
{
    public const float Duration = 30;

    public override bool Execute(Character caster)
    {
        Sound(caster, Haste.CastSound);
        caster.SetLevitate(Duration);
        return true;
    }
}

/// <summary>
/// Teleport: the caster appears 10 ahead, on the floor under that point (MAGICKS.md §3.4); with no
/// floor within 4 below it, the magick does nothing.
/// </summary>
public sealed class Teleport() : Magick(MagickType.Teleport, SelfClip)
{
    public const float Distance = 10;

    /// <summary>The floor is looked for this far below the point.</summary>
    public const float Depth = 4;

    public const string DepartSound = "magick_teleporta", ArriveSound = "magick_teleportb";

    public override bool Execute(Character caster) => To(caster, caster.Position + caster.Body.Facing * Distance);

    /// <summary>Moves a character onto the floor under a point (as the original, through walls); false with no floor.</summary>
    public static bool To(Character character, Vector3 point)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (character.World.IsDisposed || character.World.IsStepping)
        {
            return false;
        }
        if (character.World.CastSegment(point, point - Vector3.UnitY * Depth, levelOnly: true) is not { } floor)
        {
            return false;                                                     // as the original, silently
        }
        Sound(character, DepartSound);
        character.ReleaseEntanglement();
        character.Body.Body.Position = floor.Position + Vector3.UnitY * (0.1f - character.Body.HeightOffset);
        Sound(character, ArriveSound);
        return true;
    }
}
