namespace OpenMagicka.Gameplay.Characters.States;

/// <summary>Dying: the death clip plays, and the character dies when it ends (an overkill on the state's first update).</summary>
internal sealed class DeadState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        character.ReleaseEntanglement();
        if (!character.IsUndying)
        {
            character.ReleaseAll();                                           // the dead drop what they held
        }
        (character.Body.AllowMove, character.Body.AllowRotate) = (false, false);
        var second = character.HasAnimation("die1") && character.NextRandom() > 0.5;
        character.GoToAnimation(second ? "die1" : "die0", 0.05f);
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        var clock = character.Animator!.Clock;
        if (character.HitPoints > 0 && character.IsReviving && !clock.IsPlaying)
        {
            character.SpawnAnimation = character.HasAnimation("revive") ? "revive" : "hit";
            return CharacterStates.Ressurection;
        }
        if (clock.HasFinished && !character.IsDead)
        {
            character.Die();
        }
        else if (character.IsOverkilled && !character.IsDead)
        {
            character.Die();
        }
        return null;
    }
}

/// <summary>Rising again: the revival clip, then on (or back down if the hit points went).</summary>
internal sealed class RessurectionState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        (character.Body.AllowMove, character.Body.AllowRotate) = (false, false);
        character.ForceAnimation(character.SpawnAnimation ?? "hit");
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if (character.IsOverkilled)
        {
            return CharacterStates.Dead;
        }
        var clock = character.Animator!.Clock;
        if ((clock.HasFinished && !clock.IsCrossFading) || clock.IsLooping)
        {
            return character.HitPoints <= 0 ? CharacterStates.Dead : GoOn(character);
        }
        return null;
    }

    protected internal override void Exit(Character character) => character.Body.AllowRotate = true;
}

/// <summary>Bloating from a beam: the character swells until it bursts (SPELLS.md §5.4).</summary>
internal sealed class BloatState : CharacterState
{
    protected internal override CharacterState? Update(Character character, float dt) => character.IsBloatKilled ? CharacterStates.Dead : null;
}
