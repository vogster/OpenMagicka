using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Animation.Actions;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Thrown missiles (ENTITIES_AI.md §8.1.4).</summary>
public class MissileTests
{
    private static readonly DamageEntry Stone = new(AttackProperties.Damage, Elements.Earth, 20, 1);

    private static ItemTemplate Sling(params EventGroup[] extra) => new("sling", WeaponClass.ThrowMedium, Attach0: Matrix4x4.CreateTranslation(0, 0, 1),
        Ranged: new RangedTemplate(5, [new EventGroup(new EventCondition(EventTrigger.Hit), [new DamageEvent(Stone)]), .. extra], ProjectileRadius: 0.1f));

    private static Character Thrower(GameWorld world, ItemTemplate sling, Vector3? velocity = null)
    {
        var attack = new CharacterAnimation("attack_melee0", new AnimationClip("throw", 1), 1, 0, false, [new SpawnMissile(0, 0, 0, velocity, false)]);
        var thrower = Animated(world, Goblin with { Factions = Factions.Human }, clips: new CharacterAnimations(ClipsWith([attack]).Sets, SkeletonTests.Arm()));
        thrower.Equip(0, new Item(sling), "hand");
        return thrower;
    }

    private static Missile Throw(GameWorld world, Character thrower)
    {
        Run(world, 0.1f);
        thrower.ForceAnimation("attack_melee0");
        world.Tick(Tick);
        return Assert.Single(world.Entities.Entities.OfType<Missile>());
    }

    [Fact]
    public void AMissileThatStays_StrikesAgain_EveryQuarterSecond()
    {
        using var world = World();
        var thrower = Thrower(world, Sling());
        var target = Animated(world, at: new Vector3(0, 0.9f, -4));

        Throw(world, thrower);
        Run(world, 1);

        Assert.True(target.HitPoints < 280, "a missile that bounces off and stays by its target strikes again, as the original");
    }

    [Fact]
    public void AThrow_GoesAhead_AtTwiceTheRange_AndHits()
    {
        using var world = World();
        var thrower = Thrower(world, Sling(new EventGroup(new EventCondition(EventTrigger.Hit), [new RemoveEvent(0)])));
        var target = Animated(world, at: new Vector3(0, 0.9f, -4));

        var missile = Throw(world, thrower);
        Assert.Equal(10, missile.SpawnSpeed, 1);                                     // 2 × 5 m, lifted by 0.05 × that
        Assert.True(missile.Velocity.Z < -9);
        Run(world, 1);

        Assert.Equal(280f, target.HitPoints);
        Assert.Same(thrower, target.LastAttacker);
        Assert.Equal(300f, thrower.HitPoints);                                       // its own missile passes through it
    }

    [Fact]
    public void AGivenVelocity_IsInTheThrowersFrame()
    {
        using var world = World();
        var thrower = Thrower(world, Sling(), velocity: new Vector3(3, 0, 0));       // to the thrower's right

        var missile = Throw(world, thrower);

        Assert.True(missile.Velocity.X > 2.5f, $"v {missile.Velocity}");            // facing −Z: right is +X
    }

    [Fact]
    public void DefaultAndTimerEvents_Fire()
    {
        using var world = World();
        var whoosh = new EventGroup(new EventCondition(EventTrigger.Default), [new SoundEvent("whoosh", 4, 1)]);
        var tick = new EventGroup(new EventCondition(EventTrigger.Timer, Time: 0.2f), [new SoundEvent("tick", 4, 1)]);
        var thrower = Thrower(world, Sling(whoosh, tick));
        var sounds = new List<string>();
        world.Entities.Cue += (_, cue) => { if (cue is SoundCue s) sounds.Add(s.Cue); };

        Throw(world, thrower);
        Run(world, 0.5f);

        Assert.Equal("whoosh", sounds[0]);
        Assert.Equal(2, sounds.Count(s => s == "tick"));                             // at 0.2 and 0.4
    }

    [Fact]
    public void ARemoveEvent_SpendsItsBounces_ThenEndsTheMissile()
    {
        using var world = World();
        var stop = new EventGroup(new EventCondition(EventTrigger.Collision), [new RemoveEvent(1)]);
        var thrower = Thrower(world, Sling(stop), velocity: new Vector3(0, -6, -1));

        var missile = Throw(world, thrower);
        var seen = new List<bool>();
        for (var t = 0f; t < 1; t += Tick)
        {
            world.Tick(Tick);
            seen.Add(missile.IsDead);
        }

        Assert.True(missile.IsDead);
        Assert.Empty(world.Entities.Entities.OfType<Missile>());                     // removed with its body
        Assert.True(seen.IndexOf(true) > 10, "the first collision only spent a bounce");
    }

    [Fact]
    public void AnOverkill_LetsTheMissileThrough_ToTheNext()
    {
        using var world = World();
        var thrower = Thrower(world, Sling() with
        {
            Ranged = new RangedTemplate(5, [new EventGroup(new EventCondition(EventTrigger.Hit), [new DamageEvent(Stone with { Amount = 5000 })])], ProjectileRadius: 0.1f),
        });
        var first = Animated(world, Goblin with { MaxHitPoints = 10 }, at: new Vector3(0, 0.9f, -2));
        var second = Animated(world, at: new Vector3(0, 0.9f, -3.2f));                // close: the pass slows it to 0.6 of its speed

        Throw(world, thrower);
        Run(world, 1);

        Assert.True(first.IsOverkilled);
        Assert.True(second.HitPoints <= 0);                                          // dying through its clip
    }

    [Fact]
    public void TheEthereal_AreNotHit()
    {
        using var world = World();
        var thrower = Thrower(world, Sling());
        var ghost = Animated(world, Goblin with { IsEthereal = true }, at: new Vector3(0, 0.9f, -4));

        Throw(world, thrower);
        Run(world, 1);

        Assert.Equal(300f, ghost.HitPoints);
    }

    [Fact]
    public void AMissile_TakesDamage_IntoItsDamagedEvents()
    {
        using var world = World();
        var shot = new EventGroup(new EventCondition(EventTrigger.Damaged, HitPoints: 5, Elements: Elements.Fire), [new RemoveEvent(0)]);
        var missile = new Missile(world.Physics, null, new Vector3(0, 5, 0), Vector3.Zero, 0.2f, [shot]);
        world.Entities.Add(missile);
        missile.Start();

        missile.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 3, 1), null, Vector3.Zero);
        world.Tick(Tick);
        Assert.False(missile.IsDead);                                                // 3 is under 5

        missile.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 6, 1), null, Vector3.Zero);
        world.Tick(Tick);
        Assert.True(missile.IsDead);
    }

    [Fact]
    public void AHomingMissile_TurnsToItsTarget_AndGivesUpBehind()
    {
        using var world = World();
        var target = Animated(world, at: new Vector3(5, 0.9f, 0));
        var missile = new Missile(world.Physics, null, new Vector3(0, 3, 0), new Vector3(0, 0, -10), 0.2f, [], target: target, homing: 1);
        world.Entities.Add(missile);
        missile.Start();

        Run(world, 0.1f);

        Assert.True(missile.Velocity.X > 1, $"v {missile.Velocity}");
        var behind = new Missile(world.Physics, null, new Vector3(0, 3, 0), new Vector3(-10, 0, 0), 0.2f, [], target: target, homing: 1);
        world.Entities.Add(behind);
        behind.Start();
        world.Tick(Tick);
        Assert.Equal(0, behind.Homing);                                              // it was heading straight away
    }

    [Fact]
    public void ASplash_HitsAround_WeakerFurther_AndSparesTheMissile()
    {
        using var world = World();
        var boom = new SplashEvent(new DamageEntry(AttackProperties.Damage, Elements.Fire, 100, 1), 4);
        var near = Animated(world, at: new Vector3(1.4f, 0.9f, 0));                  // 1.4 away, 0.4 radius: share 0.75
        var edge = Animated(world, at: new Vector3(0, 0.9f, 3.4f));                  // 3.4 away: share 0.25
        var far = Animated(world, at: new Vector3(0, 0.9f, 5));                       // beyond 4 + its radius
        var ghost = Animated(world, Goblin with { IsEthereal = true }, at: new Vector3(-1, 0.9f, 0));
        var grenade = new Missile(world.Physics, null, new Vector3(0, 0.9f, 0), Vector3.Zero, 0.1f,
            [new EventGroup(new EventCondition(EventTrigger.Timer, Time: 0.01f), [boom, new RemoveEvent(0)])]);
        world.Entities.Add(grenade);
        grenade.Start();

        Run(world, 0.1f);

        Assert.Equal(225f, near.HitPoints, 0);
        Assert.Equal(275f, edge.HitPoints, 0);
        Assert.Equal(300f, far.HitPoints);
        Assert.Equal(300f, ghost.HitPoints);
        Assert.True(grenade.IsDead);
    }

    [Fact]
    public void AnAttackersDamageBoost_RaisesItsHits()
    {
        using var world = World();
        var thrower = Thrower(world, Sling(new EventGroup(new EventCondition(EventTrigger.Hit), [new RemoveEvent(0)])));
        thrower.AddBuff(new OpenMagicka.Gameplay.Buffs.BoostDamageBuff(100, new DamageEntry(AttackProperties.Damage, Elements.Earth, 5, 2)));
        var target = Animated(world, at: new Vector3(0, 0.9f, -4));

        Throw(world, thrower);
        Run(world, 1);

        Assert.Equal(255f, target.HitPoints);                                        // 20 × 2 + 5
    }

    [Fact]
    public void BadMissilesAndThrows_AreRejected()
    {
        using var world = World();
        Assert.Throws<ArgumentOutOfRangeException>(() => new Missile(world.Physics, null, Vector3.Zero, Vector3.Zero, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Item(Sling() with { Ranged = new RangedTemplate(float.NaN, []) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpawnMissile(0, 0, 9, null, false));
    }
}
