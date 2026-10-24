using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Gameplay.Characters.States;

/// <summary>Where an avatar with a cast type goes: Charge for a charged cast, else Cast (docs/gameplay/SPELLS.md §4.1).</summary>
internal static class Casting
{
    public const string ChargeSound = "spell_projectile_precharge";
    public const string ChargeLoopSound = "spell_projectile_charge_loop";

    /// <summary>The original's Spells sound bank.</summary>
    public const int SpellsBank = 0x10;

    public static CharacterState? Into(Character character)
    {
        if (!character.IsPlayer || character.CastType == CastType.None)
        {
            return null;
        }
        var charges = character.CastType != CastType.Weapon && character.ChargeUnlocked
            && CastRules.Charges(SpellTypes.Of(character.Spell.Element), character.CastType);
        return charges ? CharacterStates.Charge : CharacterStates.Cast;
    }

    /// <summary>The cast clip for the cast and the spell's type (ENTITIES_AI.md §5.3).</summary>
    public static (string Clip, float Blend) Clip(Character character)
    {
        var type = SpellTypes.Of(character.Spell.Element);
        return character.CastType switch
        {
            CastType.Force => ("cast_force_" + type switch
            {
                SpellType.Spray => "spray",
                SpellType.Projectile => "projectile",
                SpellType.Shield => "shield",
                SpellType.Beam => "railgun",
                SpellType.Lightning => "lightning",
                _ => "push",
            }, 0.075f),
            CastType.Area => ("cast_area_" + type switch
            {
                SpellType.Spray or SpellType.Beam => "blast",
                SpellType.Projectile => "ground",
                SpellType.Shield => "fireworks",
                SpellType.Lightning => "lightning",
                _ => "push",
            }, 0.075f),
            CastType.Weapon when character.ItemIn(0) is { IsSpellCharged: true } weapon && character.SpellQueue.Count == 0 => (SpellTypes.Of(weapon.Imbued.Combine().Element) switch
            {
                SpellType.Spray => "cast_sword_spray",
                SpellType.Projectile or SpellType.Shield => "cast_sword_projectile",
                SpellType.Beam => "cast_sword_railgun",
                SpellType.Lightning => "cast_sword_lightning",
                _ => "cast_sword",
            }, 0.075f),
            CastType.Weapon => ("cast_sword", 0.15f),
            CastType.Magick => (Spells.Magicks.MagickEffects.For(character.Spell.Magick)?.Animation ?? Spells.Magicks.Magick.SelfClip, 0.075f),
            _ => ("cast_self", 0.075f),
        };
    }

    public static CharacterState Walk(Character character)
    {
        if (character.Body.Movement.Length() > float.Epsilon)
        {
            character.GoToAnimation("move_walk", 0.25f);
            return CharacterStates.Move;
        }
        return CharacterStates.Idle;
    }
}

/// <summary>Casting: the cast clip plays and its <c>CastSpell</c> action casts (SPELLS.md §4.1).</summary>
internal sealed class CastState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        character.RemoveInvisibility();
        character.Ethereal(false, 1, 1);
        var (clip, blend) = Casting.Clip(character);
        character.GoToAnimation(clip, blend);
        if (character.CastType == CastType.Magick)
        {
            character.Body.AllowRotate = false;
        }
        if (character.Statuses.Has(StatusEffects.Frozen) && character.CastType == CastType.Self)
        {
            character.CastSpell(fromStaff: true);
        }
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if (Death(character) is { } dead)
        {
            return dead;
        }
        if (character.IsPlaying("idle"))
        {
            return CharacterStates.Idle;
        }
        if (character.Body.IsPushed)
        {
            return CharacterStates.Pushed;
        }
        if (character.IsKnockedDown)
        {
            return CharacterStates.KnockDown;
        }
        var clock = character.Animator!.Clock;
        if ((clock.IsLooping && !clock.IsCrossFading && character.CastType != CastType.Force) || clock.HasFinished)
        {
            return Casting.Walk(character);
        }
        if (character.CurrentSpell is { IsActive: false })
        {
            return character.Attacking ? CharacterStates.Attack : Casting.Walk(character);
        }
        return character.Statuses.Has(StatusEffects.Frozen) ? CharacterStates.Idle : null;
    }

    protected internal override void Exit(Character character)
    {
        character.StopSpell();
        character.CastType = CastType.None;
        character.Body.AllowRotate = true;
    }
}

/// <summary>Charging a Force projectile or a push: the spell power grows over the charge clip (SPELLS.md §4.1).</summary>
internal sealed class ChargeState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        character.RemoveInvisibility();
        character.Ethereal(false, 1, 1);
        character.GoToAnimation(character.CastType == CastType.Area ? "charge_area" : "charge_force", 0.075f);
        character.RaiseCue(new SoundCue(Casting.ChargeSound, Casting.SpellsBank, character.Position, character.Body.Facing));
        character.SlowForCharge();
        character.SpellPower = 0;
        if (character.IsPlayer)
        {
            character.ChargeUnlocked = false;                                 // until the Force button is let go
        }
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if ((Death(character) ?? Hits(character)) is { } next)
        {
            return next;
        }
        var clock = character.Animator!.Clock;
        if (!clock.IsLooping)
        {
            if (clock.HasFinished)
            {
                character.GoToAnimation(character.CastType == CastType.Area ? "charge_area_loop" : "charge_force_loop", 0.075f);
                character.RaiseCue(new SoundStopCue(Casting.ChargeSound));
                character.RaiseCue(new SoundCue(Casting.ChargeLoopSound, Casting.SpellsBank, character.Position, character.Body.Facing));
                character.SpellPower = 1;
            }
            else if (!clock.IsCrossFading && clock.Clip is { Duration: > 0 } clip)
            {
                character.SpellPower = CastRules.Charge(clock.Time, clip.Duration);
            }
        }
        return character.IsPlayer && character.HeldCast != character.CastType ? CharacterStates.Cast : null;
    }

    protected internal override void Exit(Character character)
    {
        character.RaiseCue(new SoundStopCue(Casting.ChargeSound));
        character.RaiseCue(new SoundStopCue(Casting.ChargeLoopSound));
        if (character.Spell.Element != Elements.None)
        {
            character.CastSpell(fromStaff: true);                             // a charged projectile flies on release
        }
    }
}

/// <summary>Boosting: each press heals the bubble or mends the shield ahead, until the presses stop (MAGICKS.md §2.3).</summary>
internal sealed class BoostState : CharacterState
{
    protected internal override void Enter(Character character) => character.GoToAnimation("boost", 0.2f);

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if ((Death(character) ?? Hits(character)) is { } next)
        {
            return next;
        }
        if (!character.SpendBoosts() || character.BoostCooldown <= 0)
        {
            return GoOn(character);
        }
        return null;
    }
}
