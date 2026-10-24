using System.Numerics;
using OpenMagicka.Gameplay.Buffs;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Buffs on a character and the auras that hand them out (ENTITIES_AI.md §8.3).</summary>
public class BuffTests
{
    private static DamageEntry Status(Elements element) => new(AttackProperties.Status, element, 0, 1);

    [Fact]
    public void ABuffGivenAgain_RenewsItsTime_AndOutOfTimeItGoes()
    {
        using var world = World();
        var goblin = Spawn(world);
        var boost = new BoostBuff(0.5f, 2);

        goblin.AddBuff(boost);
        Run(world, 0.3f);
        goblin.AddBuff(boost);
        Run(world, 0.3f);
        Assert.Single(goblin.Buffs);                                                // renewed, not stacked

        Run(world, 0.4f);
        Assert.Empty(goblin.Buffs);
    }

    [Fact]
    public void TheDead_TakeNoBuff()
    {
        using var world = World();
        var goblin = Spawn(world);
        goblin.Kill();

        goblin.AddBuff(new BoostBuff(5, 2));

        Assert.Empty(goblin.Buffs);
    }

    [Fact]
    public void AResistanceBuff_ScalesTheStatusAtOnce_AndResists()
    {
        using var world = World();
        var goblin = Spawn(world);
        goblin.Damage(Status(Elements.Water), null, Vector3.Zero);
        var wet = goblin.Statuses[StatusEffects.Wet].Magnitude;

        goblin.AddBuff(new ResistanceBuff(5, new Resistance(Elements.Water | Elements.Fire, 0.5f)));
        Assert.Equal(wet / 2, goblin.Statuses[StatusEffects.Wet].Magnitude, 4);

        goblin.AddBuff(new ResistanceBuff(5, new Resistance(Elements.Water, 0)));
        Assert.False(goblin.Statuses.Has(StatusEffects.Wet));

        goblin.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 100, 1), null, Vector3.Zero);
        Assert.Equal(250f, goblin.HitPoints);                                       // half of the fire
    }

    [Fact]
    public void AHitPointBuff_RaisesTheMaximumForItsTime_AndAnNpcsHitPoints()
    {
        using var world = World();
        var goblin = Spawn(world);
        var wizard = Spawn(world, at: new Vector3(5, 0.9f, 0));
        wizard.IsPlayer = true;
        var war = new ModifierBuff(0.5f, Modified.HitPoints, 2, 10);

        goblin.AddBuff(war);
        wizard.AddBuff(war);
        Run(world, Tick);

        Assert.Equal((600f, 610f), (goblin.HitPoints, goblin.MaxHitPoints));
        Assert.Equal((300f, 610f), (wizard.HitPoints, wizard.MaxHitPoints));      // a player's hit points stay
        Run(world, 1);
        Assert.Equal((300f, 300f), (goblin.HitPoints, goblin.MaxHitPoints));      // back to the template's, and capped
    }

    [Fact]
    public void AnUndyingBuff_MakesTheCharacterRise()
    {
        using var world = World();
        var goblin = Spawn(world, Goblin with { UndieHitPoints = 40 });
        goblin.AddBuff(new UndyingBuff(5));
        Run(world, Tick);
        Assert.True(goblin.IsUndying);

        goblin.Kill();
        Run(world, 0.7f);

        Assert.False(goblin.IsDead);                                                // risen after the default half second
        Assert.Equal(40f, goblin.HitPoints);
    }

    [Fact]
    public void ADamageBuff_HitsEverySecond_AsItsGiver()
    {
        using var world = World();
        var goblin = Spawn(world);
        var shaman = Spawn(world, at: new Vector3(5, 0.9f, 0));
        var curse = new DealDamageBuff(10, new DamageEntry(AttackProperties.Damage, Elements.Earth, 10, 1));

        goblin.AddBuff(curse, shaman);
        Run(world, 0.9f);
        Assert.Equal(300f, goblin.HitPoints);
        Run(world, 0.2f);
        Assert.Equal(290f, goblin.HitPoints);
        Assert.Same(shaman, goblin.LastAttacker);
        Run(world, 1);
        Assert.Equal(280f, goblin.HitPoints);
    }

    [Fact]
    public void TheQueries_GatherTheirBuffs()
    {
        using var world = World();
        var wizard = Spawn(world);
        wizard.AddBuff(new BoostDamageBuff(5, new DamageEntry(AttackProperties.Damage, Elements.Arcane | Elements.Fire, 2, 1.5f)));
        wizard.AddBuff(new BoostDamageBuff(5, new DamageEntry(AttackProperties.Damage, Elements.Fire, 1, 2)));
        wizard.AddBuff(new ModifierBuff(5, Modified.SpellTtl, 2, 1));
        wizard.AddBuff(new ReduceAggroBuff(5, 0.5f));
        wizard.AddBuff(new BoostBuff(5, 2.9f));

        Assert.Equal((3f, 3f), wizard.DamageBoost(Elements.Fire));
        Assert.Equal((1.5f, 2f), wizard.DamageBoost(Elements.Arcane));
        Assert.Equal(1f, wizard.DamageBoost(Elements.Water).Multiplier);
        Assert.Equal(11f, wizard.Modify(Modified.SpellTtl, 5));
        Assert.Equal(5f, wizard.Modify(Modified.SpellRange, 5));
        Assert.Equal(0.5f, wizard.AggroMultiplier);
        Assert.Equal(2, wizard.BoostFactor);                                              // cut to a whole number
    }

    [Fact]
    public void ASelfAura_KeepsItsShortBuffOn_UntilTheAuraEnds()
    {
        using var world = World();
        var guard = new ResistanceBuff(0.5f, new Resistance(Elements.Earth, 0.5f));
        var goblin = Spawn(world, Goblin with { Auras = [new BuffAura(AuraTarget.Self, 2, 0, guard)] });

        Run(world, 1.5f);
        Assert.Same(guard, Assert.Single(goblin.Buffs).Buff);
        Assert.Single(goblin.Auras);

        Run(world, 1.2f);
        Assert.Empty(goblin.Auras);
        Assert.Empty(goblin.Buffs);                                                 // the last pulse's buff ran out too
    }

    [Theory]
    [InlineData(AuraTarget.All, true, true, true)]
    [InlineData(AuraTarget.AllButSelf, false, true, true)]
    [InlineData(AuraTarget.Friendly, true, true, false)]
    [InlineData(AuraTarget.FriendlyButSelf, false, true, false)]
    [InlineData(AuraTarget.Enemy, false, false, true)]
    [InlineData(AuraTarget.Faction, false, false, true)]                        // the aura's faction: undead
    [InlineData(AuraTarget.Type, false, true, false)]                           // the type: the friend's template
    public void ABuffAura_GivesItsBuffToWhomItTargets(AuraTarget target, bool self, bool friend, bool enemy)
    {
        using var world = World();
        var buff = new BoostBuff(1, 2);
        var aura = new BuffAura(target, 100, 4, buff, ["friend"], Factions.Undead);
        var owner = Spawn(world, Goblin with { Auras = [aura] });
        var ally = Spawn(world, Goblin with { Id = "friend" }, at: new Vector3(3, 0.9f, 0));
        var foe = Spawn(world, Goblin with { Factions = Factions.Undead }, at: new Vector3(0, 0.9f, 3));
        var far = Spawn(world, at: new Vector3(9, 0.9f, 0));

        Run(world, 0.3f);

        Assert.Equal((self, friend, enemy, false), (owner.Buffs.Count > 0, ally.Buffs.Count > 0, foe.Buffs.Count > 0, far.Buffs.Count > 0));
    }

    [Fact]
    public void AnEtherealCharacter_GetsNoAurasBuff()
    {
        using var world = World();
        Spawn(world, Goblin with { Auras = [new BuffAura(AuraTarget.All, 100, 4, new BoostBuff(1, 2))] });
        var ghost = Spawn(world, Goblin with { IsEthereal = true }, at: new Vector3(2, 0.9f, 0));

        Run(world, 0.3f);

        Assert.Empty(ghost.Buffs);
    }

    [Fact]
    public void AHeldStaffsAura_WorksOnItsHolder_ForAsLongAsItIsHeld()
    {
        using var world = World();
        var wizard = Spawn(world);
        var staff = new ItemTemplate("staff_of_war", WeaponClass.Staff,
            Auras: [new BuffAura(AuraTarget.Self, 0.1f, 0, new ModifierBuff(1, Modified.HitPoints, 2, 0))]);
        wizard.Equip(1, new Item(staff), "RightHand");

        Run(world, 1);
        Assert.Equal(600f, wizard.MaxHitPoints);                                    // its own short time does not end it

        wizard.Unequip(1);
        Run(world, 1.2f);
        Assert.Equal(300f, wizard.MaxHitPoints);
    }

    [Fact]
    public void AnExpiredResistanceBuff_LeavesTheDefences_AndARenewalScalesNothingAgain()
    {
        using var world = World();
        var goblin = Spawn(world);
        goblin.Damage(Status(Elements.Water), null, Vector3.Zero);
        var wet = goblin.Statuses[StatusEffects.Wet].Magnitude;
        var half = new ResistanceBuff(0.3f, new Resistance(Elements.Water, 0.5f));
        var war = new ModifierBuff(5, Modified.HitPoints, 2, 0);

        goblin.AddBuff(half);
        goblin.AddBuff(half);
        goblin.AddBuff(war);
        goblin.AddBuff(war);
        Assert.Equal(wet / 2, goblin.Statuses[StatusEffects.Wet].Magnitude, 4);
        Assert.Equal(600f, goblin.HitPoints);

        Run(world, 0.5f);
        Assert.Null(goblin.Defences.Buffs);
    }

    [Fact]
    public void AuraRange_IsFlat_AndCountsTheTargetsRadius()
    {
        using var world = World();
        Spawn(world, Goblin with { Auras = [new BuffAura(AuraTarget.All, 100, 4, new BoostBuff(1, 2))] });
        var edge = Spawn(world, at: new Vector3(4.3f, 0.9f, 0));                    // beyond 4, within 4 + its 0.4
        var above = Spawn(world, at: new Vector3(0, 6, 2));                         // high up, near on the ground plan

        Run(world, Tick * 2);

        Assert.Single(edge.Buffs);
        Assert.Single(above.Buffs);
    }

    [Fact]
    public void AHealingBuff_Heals()
    {
        using var world = World();
        var goblin = Spawn(world);
        goblin.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 100, 1), null, Vector3.Zero);

        goblin.AddBuff(new DealDamageBuff(5, new DamageEntry(AttackProperties.Damage, Elements.Life, -50, 1)));
        Run(world, 1.1f);

        Assert.Equal(250f, goblin.HitPoints);
    }

    [Fact]
    public void Boosts_MultiplyTheAurasAndTheStaffs()
    {
        using var world = World();
        var wizard = Spawn(world, Goblin with { Auras = [new BoostAura(AuraTarget.Self, 100, 0, 2)] });
        wizard.Equip(1, new Item(new ItemTemplate("staff", WeaponClass.Staff, Auras: [new BoostAura(AuraTarget.Self, 100, 0, 3.7f)])), "RightHand");

        Assert.Equal(6, wizard.BoostFactor);
        wizard.AddBuff(new BoostBuff(5, 0.5f));
        Assert.Equal(0, wizard.BoostFactor);                                              // 0.5 cuts to nothing
    }

    [Fact]
    public void ACorpse_KeepsItsOwnAuras_ButItsItemsStop()
    {
        using var world = World();
        var shaman = Spawn(world, Goblin with { Auras = [new BuffAura(AuraTarget.AllButSelf, 100, 4, new BoostBuff(0.5f, 2))] });
        shaman.Equip(2, new Item(new ItemTemplate("totem", Auras: [new BuffAura(AuraTarget.AllButSelf, 100, 4, new ReduceAggroBuff(0.5f, 0.5f))])), "LeftHand");
        var ally = Spawn(world, at: new Vector3(2, 0.9f, 0));
        shaman.Kill();

        Run(world, 1);

        Assert.IsType<BoostBuff>(Assert.Single(ally.Buffs).Buff);
    }

    [Fact]
    public void AScaledStatus_EndsAtNothing_AndGrowsAboveOne()
    {
        var set = StatusSet.Empty.Add(StatusEffect.Create(StatusEffects.Wet, 0, 1), Defences.None).Set;
        var magnitude = set[StatusEffects.Wet].Magnitude;

        Assert.Equal(magnitude * 2, set.Scaled(StatusEffects.Wet, 2)[StatusEffects.Wet].Magnitude, 4);
        Assert.False(set.Scaled(StatusEffects.Wet, 1e-7f).Has(StatusEffects.Wet));
        Assert.False(set.Scaled(StatusEffects.Wet, -1).Has(StatusEffects.Wet));
    }

    [Fact]
    public void BadBuffsAndAuras_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActiveAura(new BuffAura(AuraTarget.Self, 1, -1, new BoostBuff(1, 2))));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActiveAura(new BuffAura(AuraTarget.Self, 1, 1, new BoostBuff(float.NaN, 2))));
        using var world = World();
        Assert.Throws<ArgumentOutOfRangeException>(() => Spawn(world).AddBuff(new ModifierBuff(1, Modified.HitPoints, float.PositiveInfinity, 0)));
    }
}
