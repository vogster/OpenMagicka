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

/// <summary>A held item's melee strike (ENTITIES_AI.md §8.1.2) and its events (§8.2).</summary>
public class MeleeTests
{
    private static readonly DamageEntry Cut = new(AttackProperties.Damage, Elements.Earth, 50, 1);

    private static ItemTemplate Sword(bool multiHit = false, params EventGroup[] extra) => new("sword", WeaponClass.SlashMedium,
        MeleeRange: 1, MeleeMultiHit: multiHit, Attach0: Matrix4x4.CreateTranslation(0, 0, 1),
        Melee: [
            new EventGroup(new EventCondition(EventTrigger.Hit), [new DamageEvent(Cut), new SoundEvent("hit", 4, 1)]),
            new EventGroup(new EventCondition(EventTrigger.Default), [new SoundEvent("swing", 4, 1)]),
            .. extra]);

    /// <summary>A swordsman whose attack strikes for its whole second, the hand moving by <paramref name="swing"/> keys.</summary>
    private static Character Swordsman(GameWorld world, ItemTemplate sword, DamageTargets targets = DamageTargets.Enemy, Keyframe[]? swing = null)
    {
        var channels = swing is null ? null : new Dictionary<string, IReadOnlyList<Keyframe>> { ["hand"] = swing };
        var attack = new CharacterAnimation("attack_melee0", new AnimationClip("attack", 1, channels), 1, 0, false, [new DealDamage(0, 1, 0, targets)]);
        var recoil = new CharacterAnimation(Melee.Recoil, new AnimationClip("recoil", 0.5f), 1, 0, false, []);
        var clips = ClipsWith([attack, recoil]);
        var swordsman = Animated(world, Goblin with { Factions = Factions.Human }, clips: new CharacterAnimations(clips.Sets, SkeletonTests.Arm()));
        swordsman.Equip(0, new Item(sword), "hand");
        return swordsman;
    }

    private static void Swing(GameWorld world, Character swordsman, float seconds = 0.5f)
    {
        Run(world, 0.1f);
        swordsman.ForceAnimation("attack_melee0");
        Run(world, seconds);
    }

    private static List<SoundCue> Sounds(Character character)
    {
        var sounds = new List<SoundCue>();
        character.Cue += (_, cue) => { if (cue is SoundCue s) sounds.Add(s); };
        return sounds;
    }

    [Fact]
    public void AStrike_HitsAnEnemyInReach_Once()
    {
        using var world = World();
        var swordsman = Swordsman(world, Sword());
        var goblin = Animated(world, at: new Vector3(0, 0.9f, -1.5f));
        var sounds = Sounds(swordsman);

        Swing(world, swordsman);

        Assert.Equal(250f, goblin.HitPoints);                                       // one blow, not one a frame
        Assert.Equal(["swing", "hit"], sounds.Select(s => s.Cue));
        Assert.Same(goblin, Assert.Single(swordsman.ItemIn(0)!.HitList));
    }

    [Fact]
    public void Factions_DecideWhoIsFair_ButTheTargetAlwaysIs()
    {
        using var world = World();
        var swordsman = Swordsman(world, Sword(multiHit: true));
        var friend = Animated(world, Goblin with { Factions = Factions.Human }, at: new Vector3(0.3f, 0.9f, -1.5f));
        var enemy = Animated(world, at: new Vector3(-0.3f, 0.9f, -1.5f));

        Swing(world, swordsman, 2);                                                 // the whole attack, and the blend after it (the window holds at t = 1)
        Assert.Equal((300f, 250f), (friend.HitPoints, enemy.HitPoints));

        swordsman.Target = friend;
        Swing(world, swordsman);
        Assert.Equal(250f, friend.HitPoints);
    }

    [Fact]
    public void AMultiHitItem_HitsEveryoneInReach_EachOnce()
    {
        using var world = World();
        var swordsman = Swordsman(world, Sword(multiHit: true));
        var one = Animated(world, at: new Vector3(0.4f, 0.9f, -1.5f));
        var two = Animated(world, at: new Vector3(-0.4f, 0.9f, -1.5f));

        Swing(world, swordsman);

        Assert.Equal((250f, 250f), (one.HitPoints, two.HitPoints));
    }

    [Fact]
    public void TheEtherealAndTheFar_AreMissed()
    {
        using var world = World();
        var swordsman = Swordsman(world, Sword(multiHit: true));
        var ghost = Animated(world, Goblin with { IsEthereal = true }, at: new Vector3(0, 0.9f, -1.5f));
        var far = Animated(world, at: new Vector3(0, 0.9f, -4));

        Swing(world, swordsman);

        Assert.Equal((300f, 300f), (ghost.HitPoints, far.HitPoints));
    }

    [Fact]
    public void ASweepThroughTheFloor_FiresTheCollisionGroups_AndRecoils()
    {
        using var world = World();
        var bounce = new EventGroup(new EventCondition(EventTrigger.Collision), [new SoundEvent("clang", 4, 1)]);
        Keyframe[] chop = [new(0, BonePose.Identity with { Translation = new Vector3(0, 1, 0) }), new(1, BonePose.Identity with { Translation = new Vector3(0, -2, 0) })];
        var swordsman = Swordsman(world, Sword(extra: bounce), swing: chop);
        var sounds = Sounds(swordsman);

        var seen = new HashSet<string?>();
        Run(world, 0.1f);
        swordsman.ForceAnimation("attack_melee0");
        for (var t = 0f; t < 1.1f; t += Tick)
        {
            world.Tick(Tick);
            seen.Add(swordsman.CurrentAnimation);
        }

        Assert.Equal(1, sounds.Count(s => s.Cue == "clang"));                      // once: the item then glances for a while
        Assert.Contains(Melee.Recoil, seen);
    }

    [Fact]
    public void WithNoItemOrNoEvents_NothingHappens()
    {
        using var world = World();
        var plain = Swordsman(world, new ItemTemplate("stick", MeleeRange: 1));
        var goblin = Animated(world, at: new Vector3(0, 0.9f, -1.5f));
        plain.Unequip(0);

        Swing(world, plain);

        Assert.Equal(300f, goblin.HitPoints);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DealDamage(0, 1, Character.Slots, DamageTargets.Enemy));
    }

    [Fact]
    public void AShield_TurnsTheBlow()
    {
        using var world = World();
        var swordsman = Swordsman(world, Sword(multiHit: true));
        var goblin = Animated(world, at: new Vector3(0, 0.9f, -1.5f));
        world.Entities.Add(new TestShield(new Vector3(0, 0.9f, -1.5f), 0.8f));
        var sounds = Sounds(swordsman);

        Run(world, 0.1f);
        swordsman.ForceAnimation("attack_melee0");
        world.Tick(Tick);

        Assert.Equal(300f, goblin.HitPoints);
        Assert.Equal(["swing", "hit"], sounds.Select(s => s.Cue));                  // the Hit events, with no one to damage
        Assert.Equal(Melee.Recoil, swordsman.CurrentAnimation);
    }

    [Fact]
    public void ADeflectedBlow_Recoils()
    {
        using var world = World();
        var swordsman = Swordsman(world, Sword());
        var stone = Animated(world, Goblin with { Resistances = ResistanceTable.Neutral.With(Elements.Earth, multiplier: 0) }, at: new Vector3(0, 0.9f, -1.5f));

        Run(world, 0.1f);
        swordsman.ForceAnimation("attack_melee0");
        world.Tick(Tick);

        Assert.Equal(300f, stone.HitPoints);
        Assert.Equal(Melee.Recoil, swordsman.CurrentAnimation);
    }

    [Fact]
    public void ASingleHitItem_StrikesOneOfTwo()
    {
        using var world = World();
        var swordsman = Swordsman(world, Sword());
        var one = Animated(world, at: new Vector3(0.4f, 0.9f, -1.5f));
        var two = Animated(world, at: new Vector3(-0.4f, 0.9f, -1.5f));

        Swing(world, swordsman);

        Assert.Equal(1, new[] { one, two }.Count(g => g.HitPoints < 300));
    }

    [Fact]
    public void NonCharacters_AreHitOnlyWhenTheyMayBe()
    {
        using var world = World();
        var barrel = new Barrel(new Vector3(0, 0.9f, -1.5f));
        world.Entities.Add(barrel);
        var enemiesOnly = Swordsman(world, Sword());
        Swing(world, enemiesOnly);
        Assert.Equal(0, barrel.Hits);

        using var other = World();
        var anything = Swordsman(other, Sword(), DamageTargets.Enemy | DamageTargets.NonCharacters);
        var crate = new Barrel(new Vector3(0, 0.9f, -1.5f));
        other.Entities.Add(crate);
        Swing(other, anything);
        Assert.Equal(1, crate.Hits);
    }

    [Fact]
    public void TheOwnersDamage_AndFollowingEffects_ComeFromTheEvents()
    {
        using var world = World();
        var sparks = new EventGroup(new EventCondition(EventTrigger.Hit), [new EffectEvent("sparks", Follow: true, WorldAlign: false)]);
        var cost = new EventGroup(new EventCondition(EventTrigger.Default), [new DamageOwnerEvent(Cut with { Amount = 10 })]);
        var swordsman = Swordsman(world, Sword(false, sparks, cost));
        Animated(world, at: new Vector3(0, 0.9f, -1.5f));
        var cues = new List<AnimationCue>();
        swordsman.Cue += (_, cue) => cues.Add(cue);

        Swing(world, swordsman, 1.6f);

        Assert.Equal(290f, swordsman.HitPoints);                                    // the strike's cost
        var effect = Assert.Single(cues.OfType<ItemEffectCue>());
        Assert.True(effect.Follow && effect.Effect == "sparks");
        Assert.Equal(1, effect.Direction.Length(), 3);
        Assert.Contains(cues, c => c is ItemEffectEndCue end && ReferenceEquals(end.Source, effect.Source));
    }

    [Fact]
    public void ACapsule_IsHitAlongItsAxis_PlusItsRadius()
    {
        using var world = World();
        var goblin = Animated(world);
        Run(world, 0.1f);
        var above = Segment.Between(new Vector3(-1, 2.5f, 0), new Vector3(1, 2.5f, 0));
        var side = Segment.Between(new Vector3(0.7f, 0, -1), new Vector3(0.7f, 0, 1));

        Assert.False(goblin.SegmentIntersect(above, 0));
        Assert.True(goblin.SegmentIntersect(above, 1.2f));
        Assert.True(goblin.SegmentIntersect(Segment.Between(new Vector3(0.7f, 1, -1), new Vector3(0.7f, 1, 1)), 0.31f));
        Assert.False(goblin.SegmentIntersect(side, 0));                            // below the feet and wide
        Assert.Throws<ArgumentOutOfRangeException>(() => goblin.SegmentIntersect(above, -1));
    }

    [Fact]
    public void AGroupThatDoesNotRepeat_FiresOnce()
    {
        var count = 0;
        var groups = new EventGroups([new EventGroup(new EventCondition(EventTrigger.Hit | EventTrigger.Default, Repeat: false), [new SoundEvent("a", 1, 1)])]);

        groups.Fire(new EventContext(EventTrigger.Hit), (_, _) => { count++; return DamageResult.None; });
        groups.Fire(new EventContext(EventTrigger.Default), (_, _) => { count++; return DamageResult.None; });

        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData(EventTrigger.Collision, 2f, 0, 0f, true)]
    [InlineData(EventTrigger.Collision, 0.5f, 0, 0f, false)]                      // under the threshold
    [InlineData(EventTrigger.Damaged, 0f, 30, 0f, true)]
    [InlineData(EventTrigger.Damaged, 0f, 10, 0f, false)]                         // too little
    [InlineData(EventTrigger.Timer, 0f, 0, 3f, true)]
    [InlineData(EventTrigger.Timer, 0f, 0, 1f, false)]
    public void Thresholds_GateTheirTriggers(EventTrigger trigger, float threshold, int hitPoints, float time, bool fires)
    {
        var fired = false;
        var groups = new EventGroups([new EventGroup(new EventCondition(trigger, HitPoints: 20, Elements: Elements.Fire, Threshold: 1, Time: 2), [new SoundEvent("a", 1, 1)])]);

        groups.Fire(new EventContext(trigger, threshold, hitPoints, Elements.Fire, time), (_, _) => { fired = true; return DamageResult.None; });

        Assert.Equal(fires, fired);
    }

    [Fact]
    public void BadEvents_AreRejectedWithTheItem()
    {
        var nan = new EventGroup(new EventCondition(EventTrigger.Hit), [new DamageEvent(Cut with { Amount = float.NaN })]);
        var late = new EventGroup(new EventCondition(EventTrigger.Timer, Time: float.PositiveInfinity), []);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Item(Sword(false, nan)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Item(Sword(false, late)));
    }

    [Fact]
    public void ATimer_FiresAtEachMultipleOfItsTime()
    {
        var count = 0;
        var groups = new EventGroups([new EventGroup(new EventCondition(EventTrigger.Timer, Time: 1), [new SoundEvent("a", 1, 1)])]);

        foreach (var time in new[] { 0.5f, 1, 1.5f, 2, 2.1f })
        {
            groups.Fire(new EventContext(EventTrigger.Timer, Time: time), (_, _) => { count++; return DamageResult.None; });
        }

        Assert.Equal(2, count);                                                     // at 1 and 2
    }

    /// <summary>A damageable thing that counts its hits.</summary>
    private sealed class Barrel(Vector3 position) : Thing(position, 0.4f)
    {
        public int Hits { get; private set; }

        public override DamageResult Damage(DamageEntry damage, OpenMagicka.Gameplay.Entities.Entity? attacker, Vector3 attackPosition)
        {
            Hits++;
            return DamageResult.Damaged;
        }
    }
}
