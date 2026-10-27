namespace OpenMagicka.Gameplay.Characters.States;

/// <summary>Holding a victim that it does not ride (OM-89): its holding clip, turning not, until it lets go.</summary>
internal sealed class GrippingState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        character.Body.AllowRotate = false;
        character.GoToAnimation(character.HasAnimation("idle_grip") ? "idle_grip" : "idle", 0.1f);
        character.Mind?.Done();
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if (Hits(character) is { } hit)
        {
            return hit;
        }
        if (character.GripAttacking)
        {
            return CharacterStates.GripAttack;
        }
        if (character.Attacking)
        {
            return CharacterStates.Attack;
        }
        if (!character.IsGripping)
        {
            return GoOn(character);
        }
        return character.ShouldReleaseGrip ? Losing(character) : null;
    }

    /// <summary>A grip about to go: a picked-up victim on the ground is dropped; else it is knocked down (when it can be) or drops it.</summary>
    internal static CharacterState Losing(Character character)
    {
        if (character.Body.IsTouchingGround && character.GripType == GripType.Pickup)
        {
            return CharacterStates.Drop;
        }
        character.Body.CancelPush();
        if (character.Template.CanBeKnockedDown)
        {
            character.KnockDown();
            return CharacterStates.KnockDown;
        }
        return CharacterStates.Drop;
    }

    protected internal override void Exit(Character character) => character.Body.AllowRotate = true;
}

/// <summary>An attack clip while holding (OM-89): its queued clip, one after another; done, it holds the last frame.</summary>
internal sealed class GripAttackState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        character.RemoveInvisibility();
        character.IsEthereal = character.Template.IsEthereal;
        if (character.NextGripAttackAnimation is { } clip)
        {
            character.GoToAnimation(clip, 0.075f);
        }
        character.NextGripAttackAnimation = null;
        (character.Body.AllowMove, character.Body.AllowRotate) = (false, false);
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        if ((Death(character) ?? Hits(character)) is { } next)
        {
            return next;
        }
        if (!character.GripAttacking)
        {
            return CharacterStates.Gripping;
        }
        if (ClipDone(character) && character.NextGripAttackAnimation is { } queued)
        {
            if (!character.IsPlaying("attack_recoil"))
            {
                character.GoToAnimation(queued, 0.2f);
            }
            character.NextGripAttackAnimation = null;
            return null;
        }
        if (character.ShouldReleaseGrip)
        {
            character.GripAttacking = false;
            return character.IsEntangled || character.Body.Movement.LengthSquared() < float.Epsilon ? CharacterStates.Idle : CharacterStates.Move;
        }
        if (ClipDone(character) && !character.IsPlayer)
        {
            character.Mind?.Done();                                           // it holds the last frame till its mind queues the next
        }
        return null;
    }

    protected internal override void Exit(Character character)
    {
        character.Mind?.Done();
        character.GripAttacking = false;
        (character.Body.AllowMove, character.Body.AllowRotate) = (true, true);
    }
}

/// <summary>Letting a picked-up victim drop on the ground (OM-89): its ability's drop clip (or a hit), then on.</summary>
internal sealed class DropState : CharacterState
{
    protected internal override void Enter(Character character)
    {
        character.GoToAnimation(character.DropAnimation ?? "hit", 0.1f);
        (character.Body.AllowMove, character.Body.AllowRotate) = (false, false);
    }

    protected internal override CharacterState? Update(Character character, float dt)
    {
        character.Attacking = false;
        if (Death(character) is { } dead)
        {
            return dead;
        }
        return ClipDone(character) || character.Animator?.Clock.IsLooping == true ? GoOn(character) : null;
    }
}
