using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The corners of thrown missiles (ENTITIES_AI.md §8.1.4).</summary>
public class MissileEdgeTests
{
    private static DamageEntry Hit(float amount, AttackProperties extra = AttackProperties.None) =>
        new(AttackProperties.Damage | extra, Elements.Earth, amount, 1);

    private static Missile Launched(GameWorld world, Vector3 at, Vector3 velocity, params EventGroup[] events)
    {
        var missile = new Missile(world.Physics, null, at, velocity, 0.1f, events);
        world.Entities.Add(missile);
        missile.Start();
        return missile;
    }

    private static EventGroup On(EventTrigger trigger, params ItemEvent[] events) => new(new EventCondition(trigger), events);

    [Fact]
    public void APiercingMissile_GoesThrough_AtItsSpeed()
    {
        using var world = World();
        var first = Animated(world, at: new Vector3(0, 1, -2));
        var second = Animated(world, at: new Vector3(0, 1, -3.5f));
        var dart = Launched(world, new Vector3(0, 1, 0), new Vector3(0, 0, -15), On(EventTrigger.Hit, new DamageEvent(Hit(10, AttackProperties.Piercing))));

        Assert.True(dart.Piercing);
        Run(world, 0.4f);

        Assert.True(first.HitPoints < 300 && second.HitPoints < 300, $"{first.HitPoints} {second.HitPoints}");
    }

    [Fact]
    public void ACollision_FiresByItsThreshold_ThenPauses()
    {
        using var world = World();
        var soft = new EventGroup(new EventCondition(EventTrigger.Collision, Threshold: 10000), [new SoundEvent("loud", 1, 1)]);
        var any = new EventGroup(new EventCondition(EventTrigger.Collision), [new SoundEvent("thud", 1, 1)]);
        var sounds = new List<string>();
        world.Entities.Cue += (_, cue) => { if (cue is SoundCue s) sounds.Add(s.Cue); };
        Launched(world, new Vector3(0, 2, 0), new Vector3(0, -5, 0), soft, any);

        for (var t = 0f; t < 1 && sounds.Count == 0; t += Tick)
        {
            world.Tick(Tick);
        }
        Run(world, Missile.CollisionPause * 0.5f);
        Assert.Single(sounds, "thud");                                                // the pause holds the next one back
        Run(world, Missile.CollisionPause);

        Assert.DoesNotContain("loud", sounds);                                       // its speed² is far under 10,000
        Assert.Equal(2, sounds.Count(s => s == "thud"));                             // resting on the floor, it collides again after the pause
    }

    [Fact]
    public void DamagedEvents_CutTheAmountToWholePoints()
    {
        using var world = World();
        var group = new EventGroup(new EventCondition(EventTrigger.Damaged, HitPoints: 5, Elements: Elements.Earth), [new RemoveEvent(0)]);
        var missile = Launched(world, new Vector3(0, 5, 0), Vector3.Zero, group);

        missile.Damage(Hit(4.9f), null, Vector3.Zero);
        world.Tick(Tick);
        Assert.False(missile.IsDead);
        missile.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 9, 1), null, Vector3.Zero);
        world.Tick(Tick);
        Assert.False(missile.IsDead);                                                // the wrong element
    }

    [Fact]
    public void RemoveEvents_CountTheirOwnBounces()
    {
        using var world = World();
        var bounce = On(EventTrigger.Timer, new RemoveEvent(2));
        var group = new EventGroup(new EventCondition(EventTrigger.Timer, Time: 0.05f), [new RemoveEvent(2)]);
        var missile = Launched(world, new Vector3(0, 5, 0), Vector3.Zero, group);
        var other = Launched(world, new Vector3(3, 5, 0), Vector3.Zero, group);

        Run(world, 0.12f);
        Assert.False(missile.IsDead);                                                // two firings: two bounces spent
        Run(world, 0.05f);
        Assert.True(missile.IsDead && other.IsDead);
        Assert.NotNull(bounce);
    }

    [Fact]
    public void VelocityBasedDamage_ScalesBySpeedOverFifty()
    {
        using var world = World();
        var target = Animated(world, at: new Vector3(0, 1, -2));
        Launched(world, new Vector3(0, 1, 0), new Vector3(0, 0, -25), On(EventTrigger.Hit, new DamageEvent(Hit(40), VelocityBased: true), new RemoveEvent(0)));

        Run(world, 0.3f);

        Assert.InRange(target.HitPoints, 279f, 281f);                                // 40 × 25 / 50
    }

    [Fact]
    public void AnEndedMissile_FiresNothingMore()
    {
        using var world = World();
        var sounds = new List<string>();
        world.Entities.Cue += (_, cue) => { if (cue is SoundCue s) sounds.Add(s.Cue); };
        var missile = Launched(world, new Vector3(0, 5, 0), Vector3.Zero, On(EventTrigger.Timer, new SoundEvent("tick", 1, 1)));

        missile.Kill();
        world.Tick(Tick);

        Assert.Empty(sounds);
        Assert.Empty(world.Entities.Entities.OfType<Missile>());
    }

    [Fact]
    public void AMissilesPush_LandsAfterTheStep()
    {
        using var world = World();
        var target = Animated(world, at: new Vector3(0, 0.9f, -2));
        Launched(world, new Vector3(0, 1, 0), new Vector3(0, 0, -15),
            On(EventTrigger.Hit, new DamageEvent(new DamageEntry(AttackProperties.Pushed, Elements.Earth, 300, 2)), new RemoveEvent(0)));

        Run(world, 0.6f);

        Assert.True(target.Position.Z < -2.5f, $"at {target.Position}");
    }

    [Fact]
    public void AnItemAlignedThrow_FollowsTheItem_AndAFullLevelThrowsNothing()
    {
        using var world = World();
        var sling = new ItemTemplate("sling", WeaponClass.ThrowMedium, Attach0: Matrix4x4.CreateTranslation(0, 0, 1),
            Ranged: new RangedTemplate(5, [On(EventTrigger.Hit, new DamageEvent(Hit(10)))]));
        var thrower = Animated(world, Goblin with { Factions = Factions.Human }, clips: new CharacterAnimations(Clips().Sets, SkeletonTests.Arm()));
        var item = new Item(sling);
        thrower.Equip(0, item, "hand", new Vector3(0, MathF.PI / 2, 0));               // the item turned a quarter
        Run(world, 0.1f);

        var missile = Ranged.Launch(thrower, item, new Vector3(0, 0, -10), itemAligned: true)!;
        Assert.True(MathF.Abs(missile.Velocity.X) > 9, $"v {missile.Velocity}");
        Assert.Equal(item.Template.CooldownTime, item.Cooldown);

        while (world.Entities.Entities.Count < EntityManager.Capacity)
        {
            world.Entities.Add(new Thing(new Vector3(40, 0, 40)));
        }
        Assert.Null(Ranged.Launch(thrower, item, null, itemAligned: false));
    }

    [Fact]
    public void ASplashFromAHeldItem_SparesTheHolder()
    {
        using var world = World();
        var bomb = new ItemTemplate("bomb", Attach0: Matrix4x4.CreateTranslation(0, 0, 1),
            Melee: [On(EventTrigger.Default, new SplashEvent(Hit(50), 3))]);
        var holder = Animated(world, clips: new CharacterAnimations(Clips().Sets, SkeletonTests.Arm()));
        holder.Equip(0, new Item(bomb), "hand");
        var near = Animated(world, Goblin with { Factions = Factions.Human }, at: new Vector3(1, 0.9f, 0));
        Run(world, 0.1f);

        Melee.Begin(holder, holder.ItemIn(0)!);

        Assert.Equal(300f, holder.HitPoints);
        Assert.True(near.HitPoints < 300);
        Assert.Same(holder, near.LastAttacker);
    }
}
