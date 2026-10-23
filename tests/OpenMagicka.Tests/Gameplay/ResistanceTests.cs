using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Gameplay.Spells.Elements;

namespace OpenMagicka.Tests.Gameplay;

public class ResistanceTests
{
    private static readonly DamageEntry FireAndSteam = new(AttackProperties.Damage, Fire | Steam, 100, 1);

    [Fact]
    public void EveryElementOfTheEntry_MultipliesTheMagnitude_AndAddsItsModifier()
    {
        var table = ResistanceTable.Neutral.With(Fire, multiplier: 0.5f, modifier: -20).With(Steam, multiplier: 3, modifier: 5);

        var resisted = Resistances.Apply(FireAndSteam, table);

        Assert.Equal(1.5f, resisted.Magnitude, 5);
        Assert.Equal(85f, resisted.Amount, 4);
        Assert.Equal(FireAndSteam, Resistances.Apply(FireAndSteam, ResistanceTable.Neutral));
    }

    [Fact]
    public void ModifiersCheckTheSignAsItStands_AsInTheOriginal()
    {
        var template = ResistanceTable.Neutral.With(Life, modifier: 150).With(Water, modifier: 30);
        var staff = ResistanceTable.Neutral.With(Life, modifier: 50);

        Assert.Equal(50f, Resistances.Apply(new DamageEntry(AttackProperties.Damage, Life, -100, 1), template, staff).Amount);   // 0, then +50
        Assert.Equal(30f, Resistances.Apply(new DamageEntry(AttackProperties.Damage, Water, 0, 1), template).Amount);           // the water marker
    }

    [Fact]
    public void SeveralBuffs_AndALifeBuff()
    {
        Resistance[] buffs = [new(Fire, Multiplier: 0.5f), new(Steam, Multiplier: 0.5f, Modifier: -20)];
        Resistance[] lifeMirror = [new(Fire | Life, Multiplier: -1)];

        Assert.Equal((80f, 0.25f), (Resistances.Apply(FireAndSteam, ResistanceTable.Neutral, buffs: buffs).Amount, Resistances.Apply(FireAndSteam, ResistanceTable.Neutral, buffs: buffs).Magnitude));
        var mirrored = Resistances.Apply(new DamageEntry(AttackProperties.Damage, Fire, 100, 1), ResistanceTable.Neutral, buffs: lifeMirror);
        Assert.Equal((100f, -1f), (mirrored.Amount, mirrored.Magnitude));                // "Life" inversion: |amount|, so it heals
        Assert.Throws<ArgumentOutOfRangeException>(() => Resistances.Apply(FireAndSteam, ResistanceTable.Neutral, buffs: [new(Fire, float.NaN)]));
    }

    [Fact]
    public void ArmourPiercing_IgnoresANegativeMultiplier()
    {
        var piercing = new DamageEntry(AttackProperties.Damage | AttackProperties.ArmourPiercing, Fire, 60, 1);

        Assert.Equal(piercing, Resistances.Apply(piercing, ResistanceTable.Neutral.With(Fire, multiplier: -1)));
    }

    [Fact]
    public void AnInclusiveResistance_WithNoMatchingElement_ChangesNothing()
    {
        var none = new DamageEntry(AttackProperties.Knockback, Elements.None, 500, 2);

        Assert.Equal(none, Resistances.ApplyInclusive(none, ResistanceTable.Neutral.With(Fire, multiplier: 3)));
        Assert.Equal(-0.5f, Resistances.ApplyInclusive(FireAndSteam, ResistanceTable.Neutral.With(Fire, multiplier: -1).With(Steam, multiplier: -0.5f)).Magnitude);
    }

    [Theory]
    [InlineData(100f, -150f, 0f)]          // a modifier cannot turn damage into healing
    [InlineData(-100f, 150f, 0f)]          // nor healing into damage
    [InlineData(-100f, -50f, -150f)]
    public void Modifiers_StopAtZero(float amount, float modifier, float expected)
    {
        var table = ResistanceTable.Neutral.With(Life, modifier: modifier);

        Assert.Equal(expected, Resistances.Apply(new DamageEntry(AttackProperties.Damage, Life, amount, 1), table).Amount);
    }

    [Fact]
    public void ANegativeMultiplier_TurnsDamageIntoHealing_AndHealingIntoDamageForLife()
    {
        var absorbsFire = ResistanceTable.Neutral.With(Fire, multiplier: -0.5f);
        var undead = ResistanceTable.Neutral.With(Life, multiplier: -2);

        var absorbed = Resistances.Apply(new DamageEntry(AttackProperties.Damage, Fire, 60, 1), absorbsFire);
        var burned = Resistances.Apply(new DamageEntry(AttackProperties.Damage, Life, -180, 1), undead);
        var stillBurned = Resistances.Apply(new DamageEntry(AttackProperties.Damage, Life, 180, 1), undead);

        Assert.Equal((-60f, 0.5f), (absorbed.Amount, absorbed.Magnitude));
        Assert.Equal((180f, 2f), (burned.Amount, burned.Magnitude));
        Assert.Equal(180f, stillBurned.Amount);                                       // Life inverted: the amount's size, as damage
    }

    [Fact]
    public void TheStaff_ResistsPerElement_AfterTheTemplate()
    {
        var staff = ResistanceTable.Neutral.With(Fire, multiplier: 0.5f, modifier: -10);

        var resisted = Resistances.Apply(FireAndSteam, ResistanceTable.Neutral, staff);

        Assert.Equal((90f, 0.5f), (resisted.Amount, resisted.Magnitude));
    }

    [Fact]
    public void ABuff_CountsOnce_WithTheSignOfItsMultiplier()
    {
        Resistance[] ward = [new(Fire | Steam, Multiplier: 0.5f, Modifier: -10)];
        Resistance[] mirror = [new(Fire, Multiplier: -1)];

        var warded = Resistances.Apply(FireAndSteam, ResistanceTable.Neutral, buffs: ward);
        var mirrored = Resistances.Apply(FireAndSteam, ResistanceTable.Neutral, buffs: mirror);

        Assert.Equal((90f, 0.5f), (warded.Amount, warded.Magnitude));                 // not 0.25: once, not per element
        Assert.Equal((-100f, -1f), (mirrored.Amount, mirrored.Magnitude));            // both flip: still 100 damage
        Assert.Equal(FireAndSteam, Resistances.Apply(FireAndSteam, ResistanceTable.Neutral, buffs: [new(Cold, 0)]));
    }

    [Fact]
    public void ArmourPiercing_IgnoresEverythingThatWouldProtect()
    {
        var piercing = FireAndSteam with { Attack = AttackProperties.Damage | AttackProperties.ArmourPiercing };
        var table = ResistanceTable.Neutral.With(Fire, multiplier: 0.25f, modifier: -50).With(Steam, multiplier: 2, modifier: 10);

        var resisted = Resistances.Apply(piercing, table);

        Assert.Equal((110f, 2f), (resisted.Amount, resisted.Magnitude));
    }

    [Fact]
    public void PhysicsProps_UseTheirLargestMultiplier()
    {
        var crate = ResistanceTable.Neutral.With(Fire, multiplier: 3, modifier: -10).With(Steam, multiplier: 0.5f);

        var resisted = Resistances.ApplyInclusive(FireAndSteam, crate);

        Assert.Equal((90f, 3f), (resisted.Amount, resisted.Magnitude));
    }

    [Fact]
    public void MorePlayers_ToughenEnemyResistances()
    {
        var table = ResistanceTable.Neutral.With(Fire, multiplier: 0.5f, modifier: 20).With(Life, multiplier: -1, modifier: -30);
        var k = 1 - 3 * ResistanceTable.PlayerScaling;

        var four = table.ScaledForPlayers(4);

        Assert.Equal((0.5f * k, 20 * k), (four[Fire].Multiplier, four[Fire].Modifier));
        Assert.Equal((-1 / k, -30 / k), (four[Life].Multiplier, four[Life].Modifier));
        Assert.Equal(k, four[Cold].Multiplier, 6);                                      // neutral entries scale too
        Assert.Same(table, table.ScaledForPlayers(1));
    }

    [Fact]
    public void ATable_HasOneEntryPerElement()
    {
        var table = ResistanceTable.Neutral.With(Poison, statusResistance: true);

        Assert.True(table[Poison].StatusResistance);
        Assert.Equal(Poison, table[Poison].Against);
        Assert.Equal(new Resistance(Earth), table[Earth]);
        Assert.Throws<ArgumentException>(() => table[Fire | Cold]);
        Assert.Throws<ArgumentException>(() => table.With(Shield | Ice, multiplier: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.ScaledForPlayers(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.ScaledForPlayers(5));
    }
}
