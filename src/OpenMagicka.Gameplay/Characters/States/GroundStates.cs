using OpenMagicka.Gameplay.Combat;

namespace OpenMagicka.Gameplay.Characters.States;

/// <summary>Standing: picks the idle clip, and leaves for anything the character starts doing.</summary>
internal sealed class IdleState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        if (!character.IsEntangled)
        {
            (character.Body.AllowMove, character.Body.AllowRotate) = (true, true);
        }
        if (character.SpecialIdleAnimation is { } special)
        {
            character.GoToAnimation(special, 0.2f);
        }
        else if (character.Statuses.Has(StatusEffects.Poisoned))
        {
            character.GoToAnimation("idle_wnd", 0.2f);
        }
        else if (character.IsAggressive)
        {
            character.GoToAnimation("idle_agg", 0.2f);
        }
        else
        {
            character.GoToAnimation("idle", character.IsPlaying("idle_agg") || character.IsPlaying("idle_wnd") ? 0.3f : 0.2f);
        }
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if (Common(character) is { } next)
        {
            return next;
        }
        if (character.IsAlerted)
        {
            return CharacterStates.Alarm;
        }
        if (character.Body.IsMoving)
        {
            return CharacterStates.Move;
        }
        if (character.Dashing)
        {
            return CharacterStates.Dash;
        }
        if (character.Attacking)
        {
            return CharacterStates.Attack;
        }
        if (character.IsBlocking)
        {
            return CharacterStates.Block;
        }
        if (ClipDone(character))
        {
            if (character.Statuses.Has(StatusEffects.Poisoned))
            {
                character.GoToAnimation("idle_wnd", 0.3f);
            }
            else if (character.IsAggressive)
            {
                character.GoToAnimation("idle_agg", 0.2f);
            }
            else if (character.SpecialIdleAnimation is { } special)
            {
                character.GoToAnimation(special, 0.1f);
            }
            else
            {
                character.GoToAnimation("idle", 0.5f);
            }
        }
        return null;
    }

    protected internal override void Exit(Character character)
    {
        character.Body.AllowMove = false;
        character.SpecialIdleAnimation = null;
    }
}

/// <summary>Walking or running, the clip following the speed.</summary>
internal sealed class MoveState : CharacterState
{
    protected internal override void Enter(Character character) => (character.Body.AllowMove, character.Body.AllowRotate) = (true, true);

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if (Common(character) is { } next)
        {
            return next;
        }
        if (character.Dashing)
        {
            return CharacterStates.Dash;
        }
        if (character.Attacking)
        {
            return CharacterStates.Attack;
        }
        if (character.IsBlocking)
        {
            return CharacterStates.Block;
        }
        if (character.ZapTimer >= float.Epsilon)
        {
            return null;                                                     // as the original: only the clip choice and Idle wait for a zap
        }
        if (character.Body.Movement.LengthSquared() <= 0.01f)
        {
            return CharacterStates.Idle;
        }
        var share = character.NormalizedSpeed;                                  // 3D speed share, as the original picks the clip
        if (character.Statuses.Has(StatusEffects.Poisoned))
        {
            character.GoToAnimation("move_wnd", 0.4f);
        }
        else if (share >= 0.6f)
        {
            character.GoToAnimation("move_run", 0.25f);
        }
        else if (share < 0.4f || !(character.IsPlaying("move_run") || character.IsPlaying("move_walk")))
        {
            character.GoToAnimation("move_walk", 0.25f);
        }
        return null;
    }

    protected internal override void Exit(Character character) => character.Body.AllowMove = false;
}

/// <summary>A hit reaction: one clip, then on.</summary>
internal sealed class HitState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        character.Body.AllowRotate = false;
        character.GoToAnimation("hit", 0.1f);
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if (Death(character) is { } next)
        {
            return next;
        }
        var clock = character.Animator!.Clock;
        return clock.HasFinished || clock.IsLooping || character.Statuses.Has(StatusEffects.Frozen) ? GoOn(character) : null;
    }

    protected internal override void Exit(Character character) => character.IsHit = false;
}

/// <summary>Stunned in place while the stun lasts.</summary>
internal sealed class StunState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        character.GoToAnimation(character.HasAnimation("stunned") ? "stunned" : "idle_wnd", 0.35f);
        (character.Body.AllowMove, character.Body.AllowRotate) = (false, false);
    }

    protected internal override CharacterState? Update(Character character, float dt) =>
        Death(character) ?? Hits(character) ?? (character.IsStunned ? null : CharacterStates.Idle);

    protected internal override void Exit(Character character) => (character.Body.AllowMove, character.Body.AllowRotate) = (true, true);
}

/// <summary>Busy in a menu (the versus inventory): confused, in place.</summary>
internal sealed class BusyState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        (character.Body.AllowMove, character.Body.AllowRotate) = (false, false);
        character.GoToAnimation("emote_confused0", 0.2f);
    }

    protected internal override CharacterState? Update(Character character, float dt) =>
        Death(character) ?? Hits(character) ?? (character.IsBusy ? null : CharacterStates.Idle);

    protected internal override void Exit(Character character)
    {
        character.IsBusy = false;
        (character.Body.AllowMove, character.Body.AllowRotate) = (true, true);
    }
}

/// <summary>An NPC noticing something: one alert clip.</summary>
internal sealed class AlarmState : CharacterState
{
    protected internal override void Enter(Character character) =>
        character.GoToAnimation(character.Alert == AlertMode.Discover ? "spec_alert0" : "spec_alert1", 0.2f);

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if (Death(character) is { } next)
        {
            return next;
        }
        var clock = character.Animator!.Clock;
        return character.NextAttackAnimation is null && (clock.HasFinished || (clock.IsLooping && !clock.IsCrossFading)) ? GoOn(character) : null;   // as the original: a pending attack holds the alarm
    }

    protected internal override void Exit(Character character) => character.IsAlerted = false;
}
