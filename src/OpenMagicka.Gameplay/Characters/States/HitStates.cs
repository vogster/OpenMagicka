using OpenMagicka.Gameplay.Combat;

namespace OpenMagicka.Gameplay.Characters.States;

/// <summary>On the ground after a fall: down, then up again.</summary>
internal sealed class KnockDownState : CharacterState
{
    public const string Down = "hit_stun_begin";
    public const string Up = "hit_stun_end";

    protected internal override void Enter(Character character)
    {
        character.Body.AllowRotate = false;
        character.GoToAnimation(Down, 0.05f);
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        character.IsHit = false;                                              // triggers only: a new fall restarts nothing
        character.StandUp();
        if ((Death(character) ?? Hits(character)) is { } next)
        {
            return next;
        }
        if (ClipDone(character) || character.Statuses.Has(StatusEffects.Frozen))   // as the original, freezing gets it up too
        {
            if (!character.IsPlaying(Down))
            {
                return GoOn(character);
            }
            character.GoToAnimation(Up, 0.05f);
        }
        return null;
    }

    protected internal override void Exit(Character character)
    {
        character.IsHit = false;
        character.StandUp();
    }
}

/// <summary>Thrown through the air until the ground is back underfoot.</summary>
internal sealed class FlyingState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        character.Body.AllowRotate = false;
        character.GoToAnimation("hit_fly", 0.03f);
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        character.IsHit = false;
        if (Death(character) is { } next)
        {
            return next;
        }
        if (!character.Body.IsTouchingGround)
        {
            return null;
        }
        return character.IsKnockedDown ? CharacterStates.KnockDown : GoOn(character);
    }

    protected internal override void Exit(Character character) => character.IsHit = false;
}

/// <summary>Sliding along the ground while a push lasts.</summary>
internal sealed class PushedState : CharacterState
{
    protected internal override void Enter(Character character) => character.GoToAnimation("hit_slide", 0.05f);

    protected internal override CharacterState? Update(Character character, float dt)
    {
        var next = Death(character) ?? Hits(character);
        if (next is not null && next != CharacterStates.Flying)
        {
            return next;
        }
        return character.Body.IsPushed ? null : GoOn(character);
    }
}

/// <summary>Held by vines; an attack breaks free (casting comes with spells).</summary>
internal sealed class EntangledState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        (character.Body.AllowMove, character.Body.AllowRotate) = (false, false);
        character.GoToAnimation("spec_entangled", 0.2f);
        character.SpecialIdleAnimation = null;
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if (Death(character) is { } next)
        {
            return next;
        }
        if (character.IsPlayer && character.CastType != Spells.CastType.None)
        {
            return CharacterStates.Cast;
        }
        if (character.Attacking)
        {
            return CharacterStates.Attack;
        }
        if (!character.IsEntangled)
        {
            return GoOn(character);
        }
        if (!character.IsPlaying("spec_entangled") && character.Animator!.HasFinished)
        {
            character.GoToAnimation("spec_entangled", 0.1f);
        }
        return null;
    }
}

/// <summary>Up in a jump: rising, falling, then landing.</summary>
internal sealed class JumpState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        (character.Body.AllowRotate, character.Body.AllowMove) = (false, false);
        character.GoToAnimation(character.HasAnimation("move_jump_up") ? "move_jump_up" : "move_jump_mid", 0.2f);
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if (Death(character) is { } next)
        {
            return next;
        }
        if (!character.Body.IsJumping)
        {
            return CharacterStates.Land;
        }
        if (character.Body.Body.Velocity.Y < 0 && character.IsPlaying("move_jump_up")
            && character.HasAnimation("move_jump_down") && !character.Animator!.Clock.IsCrossFading)
        {
            character.GoToAnimation("move_jump_down", 0.3f);
        }
        return null;
    }
}

/// <summary>
/// Touching down: the landing clip, once on the ground. As the original, it does not check death, so a
/// character killed while landing dies after it lands, and one without a landing clip waits for good.
/// </summary>
internal sealed class LandState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        (character.Body.AllowRotate, character.Body.AllowMove) = (false, false);
        character.GoToAnimation("move_jump_end", 0.13f);
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if (!character.Body.IsTouchingGround)
        {
            return null;
        }
        if (!character.IsPlaying("move_jump_end") && !character.IsPlaying("attack_recoil"))
        {
            character.GoToAnimation("move_jump_end", 0.1f);
            return null;
        }
        return ClipDone(character) ? GoOn(character) : null;
    }
}
