namespace OpenMagicka.Gameplay.Characters.States;

/// <summary>
/// A state of the character state machine (docs/engine/ENTITIES_AI.md §5). States hold no data:
/// everything lives in the <see cref="Character"/>, so one instance serves every character.
/// </summary>
public abstract class CharacterState
{
    protected internal virtual void Enter(Character character)
    {
    }

    /// <summary>The state to hand over to, or null to stay.</summary>
    protected internal abstract CharacterState? Update(Character character, float dt);

    protected internal virtual void Exit(Character character)
    {
    }

    public override string ToString() => GetType().Name.Replace("State", "", StringComparison.Ordinal);

    /// <summary>The first common check (§5.1): death (bloating comes with bloating).</summary>
    protected static CharacterState? Death(Character character) =>
        character.IsOverkilled || character.HitPoints <= 0 ? CharacterStates.Dead : null;

    /// <summary>The second common check (§5.1): flying, vines, a push, a knockdown, a hit, a stun.</summary>
    protected static CharacterState? Hits(Character character)
    {
        var body = character.Body;
        if (!body.IsJumping && body.IsPushed && !body.IsTouchingGround)
        {
            return CharacterStates.Flying;
        }
        if (character.IsEntangled && !character.Attacking)
        {
            return CharacterStates.Entangled;
        }
        if (body.IsPushed)
        {
            return CharacterStates.Pushed;
        }
        if (character.IsKnockedDown)
        {
            return CharacterStates.KnockDown;
        }
        if (character.IsHit)
        {
            return CharacterStates.Hit;
        }
        return character.IsStunned ? CharacterStates.Stun : null;
    }

    /// <summary>The third common check (§5.1): a jump (panic, boosts and grips come with their systems).</summary>
    protected static CharacterState? Actions(Character character) =>
        character.Body.IsJumping ? CharacterStates.Jump : null;

    /// <summary>The three common checks in order.</summary>
    protected static CharacterState? Common(Character character) => Death(character) ?? Hits(character) ?? Actions(character);

    /// <summary>Back to walking or standing, by the wanted movement.</summary>
    protected static CharacterState GoOn(Character character) =>
        character.Body.Movement.Length() < float.Epsilon ? CharacterStates.Idle : CharacterStates.Move;

    /// <summary>The clip has finished and no blend is running.</summary>
    protected static bool ClipDone(Character character) =>
        character.Animator!.HasFinished && !character.Animator.Clock.IsCrossFading;
}

/// <summary>The states, one instance each.</summary>
public static class CharacterStates
{
    public static readonly CharacterState Idle = new IdleState();
    public static readonly CharacterState Move = new MoveState();
    public static readonly CharacterState Hit = new HitState();
    public static readonly CharacterState KnockDown = new KnockDownState();
    public static readonly CharacterState Flying = new FlyingState();
    public static readonly CharacterState Pushed = new PushedState();
    public static readonly CharacterState Stun = new StunState();
    public static readonly CharacterState Entangled = new EntangledState();
    public static readonly CharacterState Jump = new JumpState();
    public static readonly CharacterState Land = new LandState();
    public static readonly CharacterState Dead = new DeadState();
    public static readonly CharacterState Ressurection = new RessurectionState();
    public static readonly CharacterState Attack = new AttackState();
    public static readonly CharacterState Block = new BlockState();
    public static readonly CharacterState Dash = new DashState();
    public static readonly CharacterState Alarm = new AlarmState();
    public static readonly CharacterState Busy = new BusyState();
}
