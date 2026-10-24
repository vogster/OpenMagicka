using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;

namespace OpenMagicka.Tests.Gameplay;

public class StateMachineTests
{
    /// <summary>Every clip the states ask for; the looping ones loop.</summary>
    internal static CharacterAnimations Clips(params string[] without)
    {
        (string Name, float Duration, bool Loop)[] clips =
        [
            ("idle", 2, true), ("idle_wnd", 2, true), ("idle_agg", 2, true), ("move_walk", 1, true), ("move_run", 0.8f, true), ("move_wnd", 1, true),
            ("hit", 0.4f, false), ("hit_stun_begin", 0.5f, false), ("hit_stun_end", 0.5f, false), ("hit_fly", 1, true), ("hit_slide", 1, true),
            ("stunned", 1, true), ("spec_entangled", 1, true), ("move_jump_up", 0.3f, false), ("move_jump_down", 0.3f, false),
            ("move_jump_end", 0.3f, false), ("die0", 1, false), ("die1", 1, false), ("revive", 0.6f, false), ("attack_melee0", 0.5f, false),
            ("attack_melee1", 0.5f, false), ("block", 1, true), ("dash", 0.4f, false), ("spec_alert0", 0.5f, false), ("spec_alert1", 0.5f, false),
            ("emote_confused0", 1, true),
        ];
        return new CharacterAnimations([new AnimationSet(clips.Where(c => !without.Contains(c.Name))
            .Select(c => new CharacterAnimation(c.Name, new AnimationClip(c.Name, c.Duration), 1, 0, c.Loop, [])))]);
    }

    internal static Character Animated(GameWorld world, CharacterTemplate? template = null, Vector3? at = null, CharacterAnimations? clips = null)
    {
        var character = new Character(template ?? Goblin, world.Physics, at ?? new Vector3(0, 0.9f, 0), random: new Random(3), animations: clips ?? Clips());
        world.Entities.Add(character);
        return character;
    }

    private static DamageEntry Hurt(Elements element, float amount, float magnitude = 1, AttackProperties attack = AttackProperties.Damage) =>
        new(attack, element, amount, magnitude);

    [Fact]
    public void ACharacter_StartsIdle()
    {
        using var world = World();
        var goblin = Animated(world);

        Run(world, 0.1f);

        Assert.Same(CharacterStates.Idle, goblin.State);
        Assert.Equal("idle", goblin.CurrentAnimation);
        Assert.Equal("idle", goblin.Animator!.Clock.Clip!.Name);
    }

    [Fact]
    public void Walking_RunsTheWalkThenTheRunClip_AndStoppingGoesIdle()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        goblin.Body.Movement = new Vector3(0.3f, 0, 0);
        Run(world, 0.5f);
        Assert.Same(CharacterStates.Move, goblin.State);
        Assert.Equal("move_walk", goblin.CurrentAnimation);
        Assert.Equal(0.3f, goblin.AnimationSpeed, 1);                           // the clip follows the speed

        goblin.Body.Movement = Vector3.UnitX;
        Run(world, 0.5f);
        Assert.Equal("move_run", goblin.CurrentAnimation);

        goblin.Body.Movement = Vector3.Zero;
        Run(world, 0.1f);
        Assert.Same(CharacterStates.Idle, goblin.State);
    }

    [Fact]
    public void AHit_PlaysTheHitClip_ThenGoesOn()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        goblin.Damage(Hurt(Elements.Earth, 50), null, Vector3.Zero);
        var seen = new List<CharacterState>();
        var clips = new HashSet<string>();
        for (var t = 0f; t < 1; t += Tick)
        {
            world.Tick(Tick);
            seen.Add(goblin.State!);
            clips.Add(goblin.Animator!.Clock.Clip!.Name);
        }

        Assert.Contains(CharacterStates.Hit, seen);                              // brief from a looping idle, as the original
        Assert.Contains("hit", clips);
        Assert.Same(CharacterStates.Idle, goblin.State);
        Assert.False(goblin.IsHit);
    }

    [Fact]
    public void AKnockback_Flies_ThenFallsAndGetsUp()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        goblin.Damage(new DamageEntry(AttackProperties.Knockback, Elements.None, 500, 2), null, new Vector3(-1, 0.9f, 0));
        Run(world, Tick * 3);
        Assert.Same(CharacterStates.Flying, goblin.State);

        var seen = new List<CharacterState>();
        for (var t = 0f; t < 4; t += Tick)
        {
            world.Tick(Tick);
            if (seen.Count == 0 || !ReferenceEquals(seen[^1], goblin.State))
            {
                seen.Add(goblin.State!);
            }
        }

        Assert.Contains(CharacterStates.KnockDown, seen);
        Assert.Same(CharacterStates.Idle, goblin.State);
        Assert.False(goblin.IsKnockedDown);
    }

    [Fact]
    public void AStun_HoldsTheCharacter_UntilItWearsOff()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        goblin.Damage(Hurt(Elements.Earth, 10, attack: AttackProperties.Damage | AttackProperties.Stun), null, Vector3.Zero);
        Run(world, 0.1f);
        Assert.Same(CharacterStates.Stun, goblin.State);
        Assert.Equal("stunned", goblin.CurrentAnimation);
        Assert.False(goblin.Body.AllowMove);

        Run(world, 2.1f);
        Assert.Same(CharacterStates.Idle, goblin.State);
    }

    [Fact]
    public void Vines_Entangle_UntilTheyBreak()
    {
        using var world = World();
        var goblin = Animated(world);
        var wizard = Animated(world, at: new Vector3(5, 0.9f, 0));
        Run(world, 0.2f);

        goblin.Damage(new DamageEntry(AttackProperties.Entanglement, Elements.Earth, 0, 5), wizard, wizard.Position);
        Run(world, 0.1f);
        Assert.Same(CharacterStates.Entangled, goblin.State);

        goblin.ReleaseEntanglement();
        Run(world, 0.1f);
        Assert.Same(CharacterStates.Idle, goblin.State);
    }

    [Fact]
    public void Death_WaitsForTheDeathClip_AndAnOverkillDoesNot()
    {
        using var world = World();
        var goblin = Animated(world);
        var shattered = Animated(world, at: new Vector3(5, 0.9f, 0));
        Run(world, 0.2f);

        goblin.Damage(Hurt(Elements.Fire, 400), null, Vector3.Zero);
        shattered.Damage(Hurt(Elements.Earth, 2000), null, Vector3.Zero);
        Run(world, 0.3f);

        Assert.Same(CharacterStates.Dead, goblin.State);
        Assert.False(goblin.IsDead);                                               // still falling
        Assert.True(goblin.CurrentAnimation is "die0" or "die1");
        Assert.True(shattered.IsDead);
        Run(world, 1);
        Assert.True(goblin.IsDead);
    }

    [Fact]
    public void TheUndying_Revive_ThroughTheirReviveClip()
    {
        using var world = World();
        var skeleton = Animated(world, Goblin with { Undying = true, UndieTime = 1, UndieHitPoints = 40 });
        Run(world, 0.2f);

        skeleton.Damage(Hurt(Elements.Fire, 400), null, Vector3.Zero);
        Run(world, 2.2f);                                                          // the death clip, then the revival timer
        Assert.Same(CharacterStates.Ressurection, skeleton.State);
        Assert.Equal("revive", skeleton.CurrentAnimation);

        Run(world, 2);                                                             // revive, then the hit the death left pending
        Assert.Same(CharacterStates.Idle, skeleton.State);
        Assert.False(skeleton.IsDead);
        Assert.Equal(40f, skeleton.HitPoints);
    }

    [Fact]
    public void Attacks_Chain_WhileTheCharacterKeepsAttacking()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        (goblin.Attacking, goblin.NextAttackAnimation) = (true, "attack_melee0");
        Run(world, 0.2f);
        Assert.Same(CharacterStates.Attack, goblin.State);
        Assert.Equal("attack_melee0", goblin.CurrentAnimation);

        goblin.NextAttackAnimation = "attack_melee1";
        Run(world, 0.45f);
        Assert.Equal("attack_melee1", goblin.CurrentAnimation);

        Run(world, 0.7f);
        Assert.Same(CharacterStates.Idle, goblin.State);
        Assert.False(goblin.Attacking);
    }

    [Fact]
    public void BlockDashAlarmAndBusy_EachPlayTheirClip()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        goblin.IsBlocking = true;
        Run(world, 0.1f);
        Assert.Same(CharacterStates.Block, goblin.State);
        goblin.IsBlocking = false;
        Run(world, 0.1f);

        (goblin.Dashing, goblin.NextDashAnimation) = (true, "dash");
        Run(world, 0.1f);
        Assert.Same(CharacterStates.Dash, goblin.State);
        Run(world, 1.2f);
        Assert.False(goblin.Dashing);

        (goblin.IsAlerted, goblin.Alert) = (true, AlertMode.Danger);
        Run(world, 0.1f);
        Assert.Equal("spec_alert1", goblin.CurrentAnimation);
        Run(world, 0.7f);
        Assert.False(goblin.IsAlerted);

        goblin.BeBusy();
        Run(world, 0.1f);
        Assert.Same(CharacterStates.Busy, goblin.State);
        goblin.IsBusy = false;
        Run(world, 0.1f);
        Assert.Same(CharacterStates.Idle, goblin.State);
    }

    [Fact]
    public void AJump_Rises_FallsAndLands()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        goblin.Body.Jump(new System.Numerics.Vector3(0, 7, 0));
        Run(world, Tick * 3);
        Assert.Same(CharacterStates.Jump, goblin.State);
        Assert.Equal("move_jump_up", goblin.CurrentAnimation);
        Run(world, 0.5f);
        Assert.Equal("move_jump_down", goblin.CurrentAnimation);

        Run(world, 1.5f);
        Assert.Same(CharacterStates.Idle, goblin.State);
        Assert.Same(CharacterStates.Land, goblin.PreviousState);
    }

    [Fact]
    public void Frost_StopsTheAnimation_AndPoisonPicksTheWoundedClips()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.2f);

        goblin.Damage(new DamageEntry(AttackProperties.Status, Elements.Poison, 50, 1), null, Vector3.Zero);
        goblin.Body.Movement = Vector3.UnitX;
        Run(world, 0.3f);
        Assert.Equal("move_wnd", goblin.CurrentAnimation);
        goblin.Body.Movement = Vector3.Zero;
        Run(world, 0.1f);                                                          // idle again: the wounded idle
        Assert.Equal("idle_wnd", goblin.CurrentAnimation);

        goblin.Damage(new DamageEntry(AttackProperties.Status, Elements.Water, 0, 1), null, Vector3.Zero);
        goblin.Damage(new DamageEntry(AttackProperties.Status, Elements.Cold, 0, 1), null, Vector3.Zero);
        Run(world, Tick);
        Assert.Equal(0f, goblin.AnimationSpeed);
    }

    [Fact]
    public void AMissingStunClip_FallsBackToTheWoundedIdle()
    {
        using var world = World();
        var goblin = Animated(world, clips: Clips("stunned"));
        Run(world, 0.2f);

        goblin.Damage(Hurt(Elements.Earth, 10, attack: AttackProperties.Damage | AttackProperties.Stun), null, Vector3.Zero);
        Run(world, 0.1f);

        Assert.Equal("idle_wnd", goblin.CurrentAnimation);
    }
}
