namespace OpenMagicka.Gameplay.Characters.States;

/// <summary>A melee attack, chaining into the next one while the character keeps attacking.</summary>
internal sealed class AttackState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        character.Ethereal(false, 1, 1);                                      // attacking makes the character solid and visible
        if (character.JustCastInvisible)
        {
            character.JustCastInvisible = false;                              // but not the attack that cast it
        }
        else
        {
            character.RemoveInvisibility();
        }
        character.GoToAnimation(character.NextAttackAnimation ?? "idle", 0.075f);
        character.NextAttackAnimation = null;
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if ((Death(character) ?? Hits(character)) is { } next)
        {
            return next;
        }
        var clock = character.Animator!.Clock;
        character.Body.AllowRotate = !character.IsEntangled && (clock.IsCrossFading || character.AllowAttackRotate || clock.Time < 0.1f);
        if (character.NextAttackAnimation is { } chained && character.Attacking && !clock.IsCrossFading && clock.HasFinished)
        {
            character.StopSpell();                                            // the clip that cast it is over
            if (!character.IsPlaying("attack_recoil"))
            {
                character.GoToAnimation(chained, 0.2f);
            }
            character.NextAttackAnimation = null;
        }
        if (character.NextAttackAnimation is null && (clock.HasFinished || (clock.IsLooping && !clock.IsCrossFading)))
        {
            character.Attacking = false;
            return character.Body.Movement.Length() < float.Epsilon || character.IsEntangled ? CharacterStates.Idle : CharacterStates.Move;
        }
        return null;
    }

    /// <summary>An attack's cast (a non-player casts through its attack clips) ends with it.</summary>
    protected internal override void Exit(Character character)
    {
        character.Mind?.Done();                                               // the ability under way is over
        character.Attacking = false;
        character.StopSpell();
        character.CastType = Spells.CastType.None;
    }
}

/// <summary>Holding a block up.</summary>
internal sealed class BlockState : CharacterState
{
    protected internal override void Enter(Character character) => character.GoToAnimation("block", 0.1f);

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if ((Death(character) ?? Hits(character)) is { } next)
        {
            return next;
        }
        if (character.Attacking)
        {
            return CharacterStates.Attack;
        }
        return character.IsBlocking ? null : GoOn(character);
    }
}

/// <summary>A dash: one clip.</summary>
internal sealed class DashState : CharacterState
{
    /// <summary>No dash animation set asks for none, which plays idle once, as the original.</summary>
    protected internal override void Enter(Character character) => character.GoToAnimation(character.NextDashAnimation ?? "None", 0.2f);

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if ((Death(character) ?? Hits(character)) is { } next)
        {
            return next;
        }
        return ClipDone(character) ? GoOn(character) : null;
    }

    protected internal override void Exit(Character character) => character.Dashing = false;
}
