using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Held items' passive abilities (ENTITIES_AI.md §8.1.6).</summary>
public class PassiveTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    private static ItemTemplate With(PassiveAbility ability, float value = 0, RangedTemplate? ranged = null) =>
        new($"{ability}", PassiveAbility: ability, PassiveValue: value, Ranged: ranged, Bounds: new ItemBounds(-Vector3.One / 10, Vector3.One / 10));

    private static RangedTemplate Spear => new(5, [new EventGroup(new EventCondition(EventTrigger.Hit), [new DamageEvent(new DamageEntry(AttackProperties.Damage, Elements.Earth, 10, 1))])]);

    private static Character Holder(GameWorld world, params ItemTemplate[] items)
    {
        var holder = Animated(world, clips: new CharacterAnimations(Clips().Sets, SkeletonTests.Arm()));
        for (var slot = 0; slot < items.Length; slot++)
        {
            holder.Equip(slot, new Item(items[slot]), "hand");
        }
        return holder;
    }

    private static void Hurt(Character character, float amount) =>
        character.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, amount, 1), null, character.Position);

    [Fact]
    public void MoveSpeed_MultipliesTheHoldersSpeed_WhileHeld()
    {
        using var world = World();
        var holder = Holder(world, With(PassiveAbility.MoveSpeed, 1.5f), With(PassiveAbility.MoveSpeed, 2));

        Run(world, 0.1f);
        Assert.Equal(3f, holder.Body.SpeedMultiplier, 4);
        holder.Release(1);
        Run(world, Tick);
        Assert.Equal(1.5f, holder.Body.SpeedMultiplier, 4);
    }

    [Fact]
    public void AreaLifeDrain_HurtsFriendAndFoeAlike_AndHealsTheHolder_OnItsBeat()
    {
        using var world = World();
        var holder = Holder(world, With(PassiveAbility.AreaLifeDrain));
        var ally = Animated(world, at: new Vector3(2, 0.9f, 0));
        var foe = Animated(world, Foe, at: new Vector3(-2, 0.9f, 0));
        var far = Animated(world, Foe, at: new Vector3(12, 0.9f, 0));
        Hurt(holder, 50);
        var hurt = holder.HitPoints;

        Run(world, 0.2f);                                                            // the first pulse at once, the next at 0.25 s
        Assert.Equal((296f, 296f, 300f), (ally.HitPoints, foe.HitPoints, far.HitPoints));
        Assert.Equal(hurt + 8, holder.HitPoints, 3);
        Run(world, 0.1f);
        Assert.Equal(292f, ally.HitPoints);
    }

    [Fact]
    public void AreaRegeneration_HealsTheHoldersFaction_TheHolderToo()
    {
        using var world = World();
        var holder = Holder(world, With(PassiveAbility.AreaRegeneration));
        var ally = Animated(world, at: new Vector3(2, 0.9f, 0));
        var foe = Animated(world, Foe, at: new Vector3(-2, 0.9f, 0));
        foreach (var hurt in new[] { holder, ally, foe })
        {
            Hurt(hurt, 50);
        }
        var (holderBefore, allyBefore, foeBefore) = (holder.HitPoints, ally.HitPoints, foe.HitPoints);

        Run(world, 0.2f);

        Assert.Equal(holderBefore + 2, holder.HitPoints, 3);
        Assert.Equal(allyBefore + 2, ally.HitPoints, 3);
        Assert.Equal(foeBefore, foe.HitPoints, 3);
    }

    [Fact]
    public void EnhanceAllyMelee_BoostsTheHoldersFaction_ForFiveSeconds()
    {
        using var world = World();
        var holder = Holder(world, With(PassiveAbility.EnhanceAllyMelee));
        var ally = Animated(world, at: new Vector3(2, 0.9f, 0));
        var foe = Animated(world, Foe, at: new Vector3(-2, 0.9f, 0));

        Run(world, 0.1f);
        Assert.True(holder.IsMeleeBoosted && ally.IsMeleeBoosted && !foe.IsMeleeBoosted);
        Assert.Equal(15f, ally.MeleeBoosted(new DamageEntry(AttackProperties.Damage, Elements.Earth, 10, 1)).Amount, 4);
        holder.Release(0);
        Run(world, Character.MeleeBoostTime - 0.2f);
        Assert.True(ally.IsMeleeBoosted);
        Run(world, 0.3f);
        Assert.False(ally.IsMeleeBoosted);
        Assert.Equal(0f, ally.MeleeBoostAmount);
    }

    [Fact]
    public void AMeleeBoost_MultipliesAHeldItemsDamage()
    {
        using var world = World();
        var club = new ItemTemplate("club", Attach0: Matrix4x4.CreateTranslation(0, 0, 1),
            Melee: [new EventGroup(new EventCondition(EventTrigger.Default), [new DamageOwnerEvent(new DamageEntry(AttackProperties.Damage, Elements.Earth, 10, 1))])]);
        var holder = Holder(world, club);
        Run(world, 0.1f);
        holder.BoostMelee(2);

        Melee.Begin(holder, holder.ItemIn(0)!);

        Assert.Equal(280f, holder.HitPoints);
    }

    [Fact]
    public void Glow_RisesWhileAFoeIsNear_AndFallsWhenItLeaves()
    {
        using var world = World();
        var holder = Holder(world, With(PassiveAbility.Glow, 10));
        var sword = holder.ItemIn(0)!;
        Animated(world, at: new Vector3(3, 0.9f, 0));                                 // a friend does not count

        Run(world, 1);
        Assert.Equal(0f, sword.Glow);
        var foe = Animated(world, Foe, at: new Vector3(5, 0.9f, 0));
        Run(world, 1);
        Assert.InRange(sword.Glow, 0.35f, 0.45f);                                    // 1 − e^−0.5
        foe.OverKill();
        Run(world, 1);
        Assert.InRange(sword.Glow, 0.2f, 0.3f);
    }

    [Fact]
    public void AGungnir_ThrowsOneSpearAtATime_AndHidesWhileItFlies()
    {
        using var world = World();
        var holder = Holder(world, With(PassiveAbility.Gungner, ranged: Spear));
        var spear = holder.ItemIn(0)!;
        Run(world, 0.1f);

        var thrown = Ranged.Launch(holder, spear, new Vector3(0, 2, -10), itemAligned: false)!;
        Assert.Equal((Passives.GungnirRadius, true), (thrown.Radius, thrown.FacesVelocity));
        Assert.Null(Ranged.Launch(holder, spear, new Vector3(0, 2, -10), itemAligned: false));
        Run(world, Tick);
        Assert.False(spear.IsVisible);

        thrown.Kill();
        Run(world, Tick);
        Assert.True(spear.IsVisible);
        Assert.NotNull(Ranged.Launch(holder, spear, null, itemAligned: false));
    }

    [Fact]
    public void AGungnirLetGoWhileItsSpearFlies_ShowsAgain()
    {
        using var world = World();
        var holder = Holder(world, With(PassiveAbility.Gungner, ranged: Spear) with { Pickable = false });
        var spear = holder.ItemIn(0)!;
        Run(world, 0.1f);
        Ranged.Launch(holder, spear, new Vector3(0, 2, -10), itemAligned: false);
        Run(world, Tick);

        holder.Release(0);

        Assert.True(spear.IsVisible);
    }

    [Fact]
    public void AMasterSword_ThrowsOnlyAtFullHealth()
    {
        using var world = World();
        var hurt = Holder(world, With(PassiveAbility.MasterSword, ranged: Spear));
        var whole = Holder(world, With(PassiveAbility.MasterSword, ranged: Spear));
        Run(world, 0.1f);
        Hurt(hurt, 1);

        Assert.Null(Ranged.Launch(hurt, hurt.ItemIn(0)!, null, itemAligned: false));
        Assert.NotNull(Ranged.Launch(whole, whole.ItemIn(0)!, null, itemAligned: false));
    }

    [Fact]
    public void BirchSteam_StartsItsEffectOnce_WhileHeld()
    {
        using var world = World();
        var cues = new List<ItemEffectCue>();
        world.Entities.Cue += (_, cue) => { if (cue is ItemEffectCue c) cues.Add(c); };
        Holder(world, With(PassiveAbility.BirchSteam));

        Run(world, 0.5f);

        var steam = Assert.Single(cues);
        Assert.Equal((Passives.SteamEffect, Vector3.UnitX, true), (steam.Effect, steam.Direction, steam.Follow));
    }

    [Fact]
    public void BirchSteam_OutlastsAStrike_AndEndsWithTheRelease()
    {
        using var world = World();
        var ended = new List<object>();
        world.Entities.Cue += (_, cue) => { if (cue is ItemEffectEndCue c) ended.Add(c.Source); };
        var holder = Holder(world, With(PassiveAbility.BirchSteam));
        var item = holder.ItemIn(0)!;
        Run(world, 0.1f);

        Melee.End(holder, item);
        Assert.DoesNotContain(item.PassiveSource, ended);
        holder.Release(0);

        Assert.Contains(item.PassiveSource, ended);
    }

    [Fact]
    public void ANegativeGlowRadius_ReachesNoOne()
    {
        using var world = World();
        var holder = Holder(world, With(PassiveAbility.Glow, -5));
        Animated(world, Foe, at: new Vector3(0.5f, 0.9f, 0));

        Run(world, 0.5f);

        Assert.Equal(0f, holder.ItemIn(0)!.Glow);
    }

    [Fact]
    public void ACorpsesItems_DoNothing()
    {
        using var world = World();
        var holder = Holder(world, With(PassiveAbility.AreaLifeDrain));
        var near = Animated(world, Goblin with { Undying = true }, at: new Vector3(2, 0.9f, 0));
        Run(world, 0.1f);
        var before = near.HitPoints;

        holder.Kill();
        Run(world, 2);

        Assert.Equal(before, near.HitPoints);
    }
}
