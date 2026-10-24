using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The corners of the state machine, most of them the original's own (ENTITIES_AI.md §5.4).</summary>
public class StateMachineEdgeTests
{
    private static List<CharacterState> Watch(GameWorld world, Character character, float seconds)
    {
        var seen = new List<CharacterState>();
        for (var t = 0f; t < seconds; t += Tick)
        {
            world.Tick(Tick);
            if (seen.Count == 0 || !ReferenceEquals(seen[^1], character.State))
            {
                seen.Add(character.State!);
            }
        }
        return seen;
    }

    [Fact]
    public void ADash_WithoutADashAnimation_PlaysIdleOnce_AndEnds()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        goblin.Dashing = true;
        Run(world, 0.1f);
        Assert.Same(CharacterStates.Dash, goblin.State);
        Assert.Equal("idle", goblin.CurrentAnimation);
        Run(world, 0.2f);                                                          // the blend's end: the loop flag is the new clip's
        Assert.False(goblin.Animator!.Clock.IsLooping);

        Run(world, 2.3f);
        Assert.Same(CharacterStates.Idle, goblin.State);
        Assert.False(goblin.Dashing);
    }

    [Fact]
    public void APendingAttack_HoldsTheAlarm()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        (goblin.IsAlerted, goblin.Alert, goblin.NextAttackAnimation) = (true, AlertMode.Discover, "attack_melee0");
        Run(world, 1.5f);
        Assert.Same(CharacterStates.Alarm, goblin.State);
        Assert.Equal("spec_alert0", goblin.CurrentAnimation);

        goblin.NextAttackAnimation = null;
        Run(world, 0.1f);
        Assert.Same(CharacterStates.Idle, goblin.State);
        Assert.False(goblin.IsAlerted);
    }

    [Fact]
    public void Cold_SlowsTheAnimation()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        goblin.Damage(new DamageEntry(AttackProperties.Status, Elements.Cold, 0, 1), null, Vector3.Zero);
        Run(world, Tick);

        Assert.True(goblin.Statuses.Has(StatusEffects.Cold));
        Assert.Equal(goblin.Statuses.ColdSlowdown, goblin.AnimationSpeed, 4);
        Assert.InRange(goblin.AnimationSpeed, 0.33f, 0.99f);
    }

    [Fact]
    public void ALightningZap_KeepsTheWalkClip_AndTheMoveState()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);
        goblin.Body.Movement = new Vector3(0.3f, 0, 0);
        Run(world, 0.5f);
        Assert.Equal("move_walk", goblin.CurrentAnimation);

        goblin.Damage(new DamageEntry(AttackProperties.Damage, Elements.Lightning, 1, 1), null, goblin.Position);
        goblin.IsHit = false;
        goblin.Body.Movement = Vector3.Zero;
        Run(world, Tick * 2);

        Assert.True(goblin.IsZapped);
        Assert.Same(CharacterStates.Move, goblin.State);                       // stopping waits for the zap
        Assert.Equal("move_walk", goblin.CurrentAnimation);
    }

    [Fact]
    public void APush_SlidesTheCharacter_ThenItGoesOn()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        goblin.Damage(new DamageEntry(AttackProperties.Pushed, Elements.None, 200, 2), null, goblin.Position - Vector3.UnitX);
        var seen = Watch(world, goblin, 3);

        Assert.True(seen.Contains(CharacterStates.Pushed) || seen.Contains(CharacterStates.Flying));
        Assert.Same(CharacterStates.Idle, goblin.State);
    }

    [Fact]
    public void AnOverkill_DiesOnTheDeadStatesFirstUpdate()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        goblin.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 2000, 1), null, Vector3.Zero);
        world.Tick(Tick);
        Assert.Same(CharacterStates.Dead, goblin.State);
        Assert.False(goblin.IsDead);

        world.Tick(Tick);
        Assert.True(goblin.IsDead);
    }

    [Fact]
    public void TheDead_StayInTheDeadState()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);
        goblin.Kill();
        Run(world, 2);
        Assert.True(goblin.IsDead);

        goblin.ChangeState(CharacterStates.Idle);
        goblin.BeBusy();
        Run(world, 0.1f);

        Assert.Same(CharacterStates.Dead, goblin.State);
    }

    [Fact]
    public void AnOverkillDuringTheRevival_KillsAgain()
    {
        using var world = World();
        var skeleton = Animated(world, Goblin with { Undying = true, UndieTime = 1, UndieHitPoints = 40 });
        Run(world, 0.2f);
        skeleton.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 400, 1), null, Vector3.Zero);
        Run(world, 2.2f);
        Assert.Same(CharacterStates.Ressurection, skeleton.State);

        skeleton.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 2000, 1), null, Vector3.Zero);
        Run(world, Tick * 2);

        Assert.Same(CharacterStates.Dead, skeleton.State);
        Assert.Same(CharacterStates.Ressurection, skeleton.PreviousState);
    }

    [Fact]
    public void WithoutALandingClip_TheCharacterWaitsInLand()
    {
        using var world = World();
        var goblin = Animated(world, clips: Clips("move_jump_end"));
        Run(world, 0.2f);

        goblin.Body.Jump(new Vector3(0, 7, 0));
        Run(world, 4);

        Assert.Same(CharacterStates.Land, goblin.State);                       // as the original
        Assert.True(goblin.Body.IsTouchingGround);
    }

    [Fact]
    public void AnimationNames_IgnoreCase()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        Assert.True(goblin.IsPlaying("IDLE"));
        Assert.True(goblin.HasAnimation("Move_Run"));
        Assert.False(goblin.HasAnimation("move_fly"));
    }
}
