using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Gameplay.Combat.AttackProperties;
using static OpenMagicka.Gameplay.Spells.Elements;

namespace OpenMagicka.Tests.Gameplay;

public class SpellDamageTests
{
    private static readonly float Sqrt2 = MathF.Sqrt(2);

    private static IReadOnlyList<DamageEntry> For(Spell spell, CastType cast) => SpellDamage.For(spell, spell.Type, cast);

    private static void Entry(DamageEntry entry, AttackProperties attack, Elements elements, float amount, float magnitude)
    {
        Assert.Equal((attack, elements), (entry.Attack, entry.Element));
        Assert.Equal(amount, entry.Amount, 3);
        Assert.Equal(magnitude, entry.Magnitude, 4);
    }

    [Fact]
    public void AFireAndSteamSpray_BurnsAndScalds()
    {
        var damage = For(new Spell(fire: 2, steam: 1), CastType.Force);

        Assert.Equal(2, damage.Count);
        Entry(damage[0], Status, Fire | Steam, 60, 1);                                // the status mix: 60 per full Fire
        Entry(damage[1], AttackProperties.Damage, Fire | Steam, 60 * Sqrt2 + 280, 1);  // base · √magnitude
    }

    [Theory]
    [InlineData(CastType.Area, 1.5f)]
    [InlineData(CastType.Weapon, 2f)]
    public void AWaterSpray_PushesAndWets(CastType cast, float pushMagnitude)
    {
        var damage = For(new Spell(water: 2), cast);

        Assert.Equal(3, damage.Count);
        Entry(damage[0], Status, Water, 0, 1);
        Entry(damage[1], Pushed, Water, 140, pushMagnitude);
        Entry(damage[2], AttackProperties.Damage, Water, 0, 1);                       // the marker for wet targets
    }

    [Fact]
    public void ASprayOnOneself_UsesTheSelfStrengths_AndNeverPushes()
    {
        var damage = For(new Spell(fire: 1, cold: 1, water: 1), CastType.Self);

        Assert.Equal(2, damage.Count);
        Entry(damage[1], AttackProperties.Damage, Fire | Cold, 60 + 25, 1);
        Assert.Equal(80f, For(new Spell(steam: 1), CastType.Self)[1].Amount, 3);
        Assert.Empty(For(new Spell(fire: 1), CastType.Magick));
    }

    [Fact]
    public void ABoulder_HitsWithTheSquareOfItsEarthAndIce()
    {
        var damage = For(new Spell(earth: 2, ice: 1, fire: 1), CastType.Force);

        Entry(damage[0], AttackProperties.Damage, Earth | Ice | Cold, 150 * 4 + 275 * 1, 1);
        Entry(damage[1], Status, Fire, 0, 0);                                          // any Ice adds the status entry
        Assert.Equal(2, damage.Count);
    }

    [Fact]
    public void IceShards_CountLifeTwice()
    {
        var damage = For(new Spell(ice: 2, life: 1), CastType.Force);

        Entry(damage[0], AttackProperties.Damage, Ice | Life | Cold | Earth, 275 * Sqrt2 - 180 - 180, 1);
        Entry(damage[1], Status, Life, -80, 1);
    }

    [Fact]
    public void AnAreaProjectile_BlastsAndKnocksDown_WithFireOutsideTheMask()
    {
        var damage = For(new Spell(earth: 1, fire: 1), CastType.Area);

        Assert.Equal(3, damage.Count);
        Entry(damage[0], Status, Fire, 60, 1);
        Entry(damage[1], AttackProperties.Damage, Earth, 60, 1);                      // Fire counts in the amount only
        Entry(damage[2], Knockdown, Earth, 150, 1);
    }

    [Fact]
    public void AnArcaneEarthWeapon_PushesHarderAndKnocksDown()
    {
        var damage = For(new Spell(earth: 1, arcane: 2), CastType.Weapon);

        Entry(damage[0], Pushed | Knockdown, Earth | Arcane, 240, 3);                     // trunc(120·A), magnitude W + E + A
        Entry(damage[1], Status, Arcane, 0, 0);
    }

    [Fact]
    public void AnAreaProjectile_WithIceWaterAndLife()
    {
        var damage = For(new Spell(ice: 1, water: 2, life: 1), CastType.Area);

        Entry(damage[0], Status, Water | Life, 0, 1);
        Entry(damage[1], Status, Life, -80, 1);
        Entry(damage[2], AttackProperties.Damage, Ice | Life | Earth, 275 - 180, 1);
        Entry(damage[3], Pushed, Water, 140, 2);
        Assert.Equal(4, damage.Count);
    }

    [Fact]
    public void IceShards_WithLightningArcaneAndPoison()
    {
        var damage = For(new Spell(ice: 1, lightning: 1, arcane: 1, poison: 2), CastType.Force);

        Entry(damage[0], AttackProperties.Damage, Ice | Lightning | Arcane | Poison | Cold | Earth, 275 + 250 + 225, 1);
        Entry(damage[1], Status, Poison, 50, 1);
    }

    [Fact]
    public void AnImbuedIceWeapon_PiercesAndPushes()
    {
        var damage = For(new Spell(ice: 1, earth: 1, water: 1), CastType.Weapon);

        Assert.Equal(3, damage.Count);
        Entry(damage[0], AttackProperties.Damage | Piercing, Earth, 2 * 275 + 150, 1);
        Entry(damage[1], Pushed | Knockdown, Water | Earth, 70, 2);
        Entry(damage[2], Status, Water, 0, 1);
        Assert.Empty(For(new Spell(earth: 1), CastType.Self));
    }

    [Fact]
    public void Shields_HealWithLife_HurtWithArcane_AndKnockBack()
    {
        var life = For(new Spell(shield: 1, life: 1), CastType.Force);
        Entry(life[0], AttackProperties.Damage, Life, -600, 1);
        Entry(life[1], Status, Life, 0, 0);                                            // Life is a status element too
        Assert.Equal(2, life.Count);

        var arcane = For(new Spell(shield: 1, arcane: 1), CastType.Force);
        Entry(arcane[0], AttackProperties.Damage, Arcane, 225, 1);
        Entry(arcane[1], Knockback, Elements.None, 500, 2);
        Assert.Equal(2, arcane.Count);

        var lifeAndArcane = For(new Spell(shield: 1, life: 1, arcane: 1), CastType.Force);
        Entry(lifeAndArcane[0], AttackProperties.Damage, Life | Arcane, -600, 1);          // Life wins; both beam elements in the mask
        Entry(lifeAndArcane[^1], Knockback, Elements.None, 500, 2);
        Assert.DoesNotContain(For(new Spell(shield: 1, water: 2), CastType.Force), d => d.Attack == Pushed);   // water pushes only with Earth or Ice

        var mud = For(new Spell(shield: 1, arcane: 1, water: 4, earth: 1), CastType.Force);
        Entry(mud[1], Status, Water | Arcane, 0, 1);
        Entry(mud[2], Pushed, Water, 150, 2);                                          // 75·√W, magnitude √W
        Assert.Equal(3, mud.Count);                                                   // Earth stops the knockback

        Entry(Assert.Single(For(new Spell(shield: 1, lightning: 2), CastType.Area)), AttackProperties.Damage, Lightning, 130 * Sqrt2, 1);
    }

    [Fact]
    public void ABeam_DoesNotGrowStrongerWithMoreArcane()
    {
        var damage = For(new Spell(arcane: 3, fire: 1), CastType.Force);

        Entry(damage[0], Status, Fire, 60, 1);
        Entry(damage[1], AttackProperties.Damage, Arcane | Fire, 60 + 225, 1);
        Assert.Equal(200f, Assert.Single(For(new Spell(arcane: 2), CastType.Self)).Amount, 3);
    }

    [Fact]
    public void ABeam_ZapsAtHalfStrength_SteamWets_AndSelfHasItsOwnStrengths()
    {
        Entry(For(new Spell(arcane: 1, lightning: 4), CastType.Force)[0], AttackProperties.Damage, Arcane | Lightning, 225 + 125 * 2, 1);

        var steam = For(new Spell(arcane: 1, steam: 1), CastType.Force);
        Entry(steam[0], Status, Steam | Water, 0, 1);
        Entry(steam[1], AttackProperties.Damage, Arcane | Steam, 225 + 280, 1);

        var self = For(new Spell(life: 4, fire: 1, steam: 1, lightning: 1, water: 1), CastType.Self);
        Entry(self[1], Status, Life, -80 * 2, 4);                                         // healing for as long as its Life
        Entry(self[2], AttackProperties.Damage, Fire | Lightning | Life | Steam, 60 + 80 + 80 - 180, 1);
        Entry(self[3], Pushed, Water, 70, 1);                                             // a beam pushes even on oneself
    }

    [Fact]
    public void AWetLifeBeam_HealsAndPushes()
    {
        var damage = For(new Spell(life: 1, water: 1), CastType.Force);

        Assert.Equal(4, damage.Count);
        Entry(damage[0], Status | AttackProperties.Damage, Water | Life, 0, 1);
        Entry(damage[1], Status, Life, -80, 1);
        Entry(damage[2], AttackProperties.Damage, Life, -180, 1);
        Entry(damage[3], Pushed, Water, 70, 1);
    }

    [Fact]
    public void Lightning_StrikesWithEveryElementOfTheSpell()
    {
        var damage = For(new Spell(lightning: 2, water: 1), CastType.Force);

        Assert.Equal(4, damage.Count);
        Entry(damage[0], AttackProperties.Damage, Lightning | Water, 250 * Sqrt2, 1);
        Entry(damage[1], Status | AttackProperties.Damage, Water, 0, 1);
        Entry(damage[2], Pushed, Water, 70, 1);
        Entry(damage[3], Status, Water, 0, 1);
    }

    [Fact]
    public void NegativeMagnitudes_CountAsZero()
    {
        var damage = SpellDamage.For(new Spell(fire: 1, cold: -0.5f), SpellType.Spray, CastType.Force);

        Entry(damage[0], Status, Fire, 60, 1);
        Entry(damage[1], AttackProperties.Damage, Fire, 60, 1);
        Assert.Equal(2, damage.Count);
    }

    [Fact]
    public void PushesAndMagicksHaveNoDamage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpellDamage.For(default, SpellType.Push, CastType.Force));
        Assert.Throws<ArgumentException>(() => SpellDamage.For(Spell.OfMagick(MagickType.Haste), SpellType.Lightning, CastType.Force));
        Assert.Empty(SpellDamage.For(new Spell(earth: 1), SpellType.Projectile, CastType.Magick));
    }

    [Fact]
    public void TwoEntriesOfNoMagnitude_CombineToNothing()
    {
        Assert.Equal(new DamageEntry(Status, Fire, 0, 0), DamageEntry.Combine(new DamageEntry(Status, Fire, 60, 0), new DamageEntry(Status, Fire, 60, 0)));
    }

    [Fact]
    public void TwoEntries_Combine_WeightedByMagnitude()
    {
        var combined = DamageEntry.Combine(new DamageEntry(Status, Fire, 60, 1), new DamageEntry(AttackProperties.Damage, Cold, 30, 3));

        Assert.Equal(new DamageEntry(Status | AttackProperties.Damage, Fire | Cold, (60 + 90) / 4f, 4), combined);
    }
}
