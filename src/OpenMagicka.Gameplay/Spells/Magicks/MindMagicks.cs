using System.Numerics;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells.Effects;
using Barrier = OpenMagicka.Gameplay.Spells.Effects.Barrier;

namespace OpenMagicka.Gameplay.Spells.Magicks;

/// <summary>Fear: every foe within 6 flees the caster for 5 s (MAGICKS.md §6.1).</summary>
public sealed class Fear() : Magick(MagickType.Fear, SelfClip)
{
    public const float Range = 6;

    public const string FearSound = "magick_fear", FearEffect = "magick_fear";

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager)
        {
            return false;
        }
        manager.RaiseCue(caster, new WorldEffectCue(FearEffect, caster.Position, Vector3.UnitX));
        foreach (var other in manager.Query(caster.Position, Range, ignoreProtected: true).OfType<Character>())
        {
            if (!ReferenceEquals(other, caster) && (other.Factions & caster.Factions) == 0 && other.SelfShield is not { IsActive: true, IsSolid: false })
            {
                other.Fear(caster);
            }
        }
        Sound(caster, FearSound);
        return true;
    }
}

/// <summary>Charm: the foe nearest the caster's facing within 14 is charmed for 15 s (MAGICKS.md §6.2).</summary>
public sealed class Charm() : Magick(MagickType.Charm, SweepClip)
{
    public const float Range = 14, Duration = 15;

    public const string CharmSound = "magick_charm";

    public override bool Execute(Character caster)
    {
        if (Aimed(caster, Range, c => !c.IsCharmed && (c.Factions & caster.Factions) == 0) is not { } target)
        {
            return Fail(caster);
        }
        Sound(target, CharmSound);
        target.Charm(caster, Duration);
        return true;
    }

    /// <summary>The character (not the caster) within the range, shields hiding, whose way is nearest the facing.</summary>
    internal static Character? Aimed(Character caster, float range, Func<Character, bool> eligible)
    {
        if (caster.Manager is not { } manager)
        {
            return null;
        }
        Character? best = null;
        var smallest = float.MaxValue;
        foreach (var other in manager.Query(caster.Position, range, ignoreProtected: true).OfType<Character>())
        {
            if (ReferenceEquals(other, caster) || !eligible(other))
            {
                continue;
            }
            var way = (other.Position - caster.Position) with { Y = 0 };
            var angle = way.LengthSquared() > 1e-12f ? MathF.Acos(Math.Clamp(Vector3.Dot(caster.Body.Facing, Vector3.Normalize(way)), -1, 1)) : 0;
            if (angle < smallest)
            {
                (best, smallest) = (other, angle);
            }
        }
        return best;
    }
}

/// <summary>Confuse: the character nearest the caster's facing within 14 is confused for 15 s (MAGICKS.md §6.3).</summary>
public sealed class Confuse() : Magick(MagickType.Confuse, DirectClip)
{
    public const float Range = 14, Duration = 15;

    public const string ConfuseSound = "boss_fafnir_confuse", ConfusedEffect = "confused";

    /// <summary>The original's Characters sound bank.</summary>
    public const int CharactersBank = 0x20;

    public override bool Execute(Character caster)
    {
        if (Charm.Aimed(caster, Range, _ => true) is not { } target)
        {
            return Fail(caster);
        }
        target.Confuse(Duration);
        target.RaiseCue(new SoundCue(ConfuseSound, CharactersBank, target.Position, target.Body.Facing));
        return true;
    }
}

/// <summary>
/// Nullify: every magick and spell in the level is undone (MAGICKS.md §6.4): whirlwinds, holes,
/// missiles, barriers, mines, grease and shields go; characters lose their statuses, vines,
/// self-shields, fear, charm, confusion, invisibility, spells, queues, levitation and auras; the
/// running magicks end.
/// </summary>
public sealed class Nullify() : Magick(MagickType.Nullify, SelfClip)
{
    public const string NullifySound = "magick_nullify", NullifiedEffect = "magick_nullified";

    private const StatusEffects Statuses = StatusEffects.Burning | StatusEffects.Wet | StatusEffects.Frozen | StatusEffects.Cold
        | StatusEffects.Poisoned | StatusEffects.Healing | StatusEffects.Greased | StatusEffects.Steamed;

    public override bool Execute(Character caster)
    {
        if (caster.Manager is not { } manager)
        {
            return false;
        }
        Sound(caster, NullifySound);
        foreach (var entity in manager.Entities.ToList())
        {
            if (Undo(entity))
            {
                manager.RaiseCue(entity, new WorldEffectCue(NullifiedEffect, entity.Position, Vector3.UnitY));
            }
        }
        foreach (var effect in manager.Entities.OfType<IMagickEffect>().ToList())
        {
            effect.End();
        }
        return true;
    }

    /// <summary>Undoes what the entity holds; true when there was something to undo (it glows).</summary>
    private static bool Undo(Entity entity)
    {
        switch (entity)
        {
            case Whirlwind whirlwind:
                whirlwind.Kill();
                return false;
            case VortexHole hole:
                hole.Kill();
                return false;
            case Missile missile:
                missile.Kill();
                return false;
            case Barrier barrier:
                barrier.Kill();
                return true;
            case SpellMine mine:
                mine.Kill();
                return true;
            case GreaseField field:
                field.Kill();
                return true;
            case SpellShield shield:
                shield.Kill();
                return false;
            case PortalGate gate:
                gate.Kill();
                return false;
            case Character character:
                return character.Nullify();
            default:
                return false;
        }
    }
}

/// <summary>Corporealize: everyone within 30 stops being ethereal (MAGICKS.md §6.5).</summary>
public sealed class Corporealize() : Magick(MagickType.Corporealize, SelfClip)
{
    public const float Range = 30;

    public const string CorporealizeSound = "magick_corporealize";

    public override bool Execute(Character caster)
    {
        Sound(caster, CorporealizeSound);
        if (caster.Manager is { } manager)
        {
            foreach (var other in manager.Query(caster.Position, Range).OfType<Character>())
            {
                other.Ethereal(false, 1, 1);
            }
        }
        return true;
    }
}
