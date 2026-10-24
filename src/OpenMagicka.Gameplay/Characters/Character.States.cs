using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;

namespace OpenMagicka.Gameplay.Characters;

/// <summary>What an NPC's alarm is about.</summary>
public enum AlertMode
{
    Discover,
    Danger,
}

public sealed partial class Character
{
    /// <summary>The animation playback; null for a character made without animations.</summary>
    public CharacterAnimator? Animator { get; }

    /// <summary>The state machine's current state (docs/engine/ENTITIES_AI.md §5); null without animations.</summary>
    public CharacterState? State { get; private set; }

    public CharacterState? PreviousState { get; private set; }

    /// <summary>The animation playing (or blending in).</summary>
    public string? CurrentAnimation => Animator?.Current;

    /// <summary>The character's animation speed this frame: 1, scaled by the zap, cold or the walking speed, 0 when frozen.</summary>
    public float AnimationSpeed
    {
        get => Animator?.Clock.Speed ?? 1;
        private set
        {
            if (Animator is not null)
            {
                Animator.Clock.Speed = value;
            }
        }
    }

    public bool Attacking { get; set; }

    /// <summary>The attack the attack state plays next (and chains into while the character keeps attacking).</summary>
    public string? NextAttackAnimation { get; set; }

    /// <summary>Lets the character turn during the whole attack.</summary>
    public bool AllowAttackRotate { get; set; }

    public bool Dashing { get; set; }

    public string? NextDashAnimation { get; set; }

    public bool IsBlocking { get; set; }

    /// <summary>An NPC noticed something: the alarm state plays once.</summary>
    public bool IsAlerted { get; set; }

    public AlertMode Alert { get; set; }

    /// <summary>Picks the aggressive idle clip.</summary>
    public bool IsAggressive { get; set; }

    /// <summary>An idle clip a script or the AI asked for; cleared when the character leaves idle.</summary>
    public string? SpecialIdleAnimation { get; set; }

    /// <summary>In a menu (the versus inventory).</summary>
    public bool IsBusy { get; set; }

    /// <summary>The clip a revival plays.</summary>
    public string? SpawnAnimation { get; set; }

    public float ZapTimer => zapTimer;

    /// <summary>A revival is due or has happened since the last death (the undying).</summary>
    public bool IsReviving => !float.IsNaN(undieTimer);

    /// <summary>The body's 3D speed as a share of its maximum, picking the walk or run clip (0 when it cannot move), as the original.</summary>
    public float NormalizedSpeed => Body.MaxSpeed > 0 ? Body.Body.Velocity.Length() / Body.MaxSpeed : 0;

    public void GoToAnimation(string name, float blendTime) => Animator?.GoTo(name, blendTime);

    public void ForceAnimation(string name) => Animator?.Force(name);

    /// <summary>Whether the default set, or a set of an equipped item, has the animation.</summary>
    public bool HasAnimation(string name) => Animator?.Has(name) ?? false;

    /// <summary>Whether the animation asked for last is this one (names ignore case, as the original's).</summary>
    public bool IsPlaying(string name) => string.Equals(CurrentAnimation, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Hands over to another state (the old one's exit, then the new one's entry); null or the same state
    /// does nothing, and a dead character only goes into the dead state (one killed outside it, as by a
    /// suicide action) or rises. An entry may hand over again; an exit must not.
    /// </summary>
    public void ChangeState(CharacterState? next)
    {
        if (next is null || ReferenceEquals(next, State) || State is null || (Dead && next != CharacterStates.Dead && next != CharacterStates.Ressurection))
        {
            return;
        }
        State.Exit(this);
        (PreviousState, State) = (State, next);
        next.Enter(this);
    }

    /// <summary>Enters the busy state (a menu opened).</summary>
    public void BeBusy()
    {
        IsBusy = true;
        ChangeState(CharacterStates.Busy);
    }

    internal double NextRandom() => random.NextDouble();

    /// <summary>The clock moves and the clip's actions run, unless a finished corpse lies still.</summary>
    private void Animate(float dt)
    {
        if (Animator is null)
        {
            return;
        }
        var still = Dead && corpseTimer <= CorpseRemoval && Animator.HasFinished && !Animator.Clock.IsCrossFading;
        if (!still)
        {
            Animator.Update(dt);
        }
        AnimationSpeed = 1;
    }

    /// <summary>Walking clips follow the body's flat speed (1 when it cannot move), as the original; otherwise cold slows the animation.</summary>
    private void ScaleAnimation()
    {
        if (Animator is null)
        {
            return;
        }
        if (State == CharacterStates.Move)
        {
            var flat = Body.Body.Velocity with { Y = 0 };
            AnimationSpeed *= Body.MaxSpeed > 0 ? flat.Length() / Body.MaxSpeed : 1;
        }
        else
        {
            AnimationSpeed *= Statuses.ColdSlowdown;
        }
    }
}
