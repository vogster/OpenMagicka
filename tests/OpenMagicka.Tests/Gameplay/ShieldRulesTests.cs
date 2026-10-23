using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Tests.Gameplay;

public class ShieldRulesTests
{
    [Theory]
    [InlineData(CastType.Force, ShieldShape.Disc, 800, -3.5f)]
    [InlineData(CastType.Area, ShieldShape.Sphere, 1000, 0)]
    [InlineData(CastType.Weapon, ShieldShape.Wall, 800, 7.5f)]
    public void APureShield_IsADiscASphereOrAWall(CastType cast, ShieldShape shape, float hp, float offset)
    {
        var shield = Assert.IsType<PureShield>(ShieldRules.For(Spell.Of(Elements.Shield), cast));

        Assert.Equal((shape, 5.5f, hp, offset), (shield.Shape, shield.Radius, shield.Hp, shield.Offset));
        Assert.Equal((100f, 5000f), (ShieldRules.ShieldDecayPerSecond, ShieldRules.ShieldMaxHp));
    }

    [Fact]
    public void ASelfShield_IsABubble()
    {
        var bubble = Assert.IsType<Bubble>(ShieldRules.For(Spell.Of(Elements.Shield), CastType.Self));

        Assert.Equal((500f, 100f), (bubble.Hp, bubble.DecayPerSecond));
    }

    [Theory]
    [InlineData(CastType.Force, BarrierLayout.Arc)]
    [InlineData(CastType.Area, BarrierLayout.Ring)]
    [InlineData(CastType.Weapon, BarrierLayout.Line)]
    public void ArcaneOrLifeWithoutEarthOrIce_LaysMines(CastType cast, BarrierLayout layout)
    {
        var mines = Assert.IsType<Mines>(ShieldRules.For(new Spell(shield: 1, arcane: 1, fire: 1), cast));

        Assert.Equal(layout, mines.Layout);
        Assert.Equal((1f, 0.5f, 3f, 30f), (mines.MineRadius, mines.Fuse, mines.BlastRadius, mines.OwnerRange));
        if (cast == CastType.Weapon)
        {
            Assert.Equal((2 + 1, 2.5f), (mines.LineCount, mines.LineStep));          // total 3: trunc(max(1.5, 1)) = 1
        }
    }

    [Fact]
    public void EarthOrIce_RaiseSolidBarriers()
    {
        var wall = Assert.IsType<Barriers>(ShieldRules.For(new Spell(shield: 1, earth: 1, ice: 2), CastType.Force));

        Assert.True(wall.Solid);
        Assert.Equal((1000f, 100, 50f, 1.25f), (wall.Hp, wall.Armor, wall.DecayPerSecond, wall.SegmentRadius));
        Assert.Equal(2f, wall.ArcCentreBack);                                      // a plain stone/ice wall stands back
        Assert.Equal(0f, Back(new Spell(shield: 1, earth: 1, fire: 1)));
        Assert.Equal(2f, Back(new Spell(shield: 1, earth: 1, arcane: 1)));        // Arcane keeps it plain
        Assert.Equal(0f, Back(new Spell(shield: 1, earth: 1, life: 1)));          // Life does not
        Assert.Equal(0f, Back(new Spell(shield: 1, ice: 1, lightning: 1)));
        var ring = Assert.IsType<Barriers>(ShieldRules.For(new Spell(shield: 1, earth: 1), CastType.Area));
        Assert.Equal(0f, ring.ArcCentreBack);

        static float Back(Spell spell) => Assert.IsType<Barriers>(ShieldRules.For(spell, CastType.Force)).ArcCentreBack;
    }

    [Fact]
    public void EarthWithArcane_RaisesBarriersNotMines()
    {
        Assert.IsType<Barriers>(ShieldRules.For(new Spell(shield: 1, earth: 1, arcane: 1), CastType.Force));
    }

    [Fact]
    public void OtherElements_RaiseElementalBarriers_ARingAroundForArea()
    {
        var storm = Assert.IsType<Barriers>(ShieldRules.For(new Spell(shield: 2, fire: 2, steam: 1), CastType.Area));

        Assert.False(storm.Solid);
        Assert.Equal((500f, 0), (storm.Hp, storm.Armor));                         // total magnitude 5 × 100
        Assert.Equal((BarrierLayout.Ring, 5f), (storm.Layout, storm.RingRadius));  // S + 3
    }

    [Theory]
    [InlineData(1f, 0f, 1500f, 0.9f - 0.075f, 10f, 1f - 0.15f)]
    [InlineData(0f, 2f, 900f, 0.9f - 0.05f, 70f, 1f - 0.1f)]
    public void EarthOrIceOnSelf_IsArmour(float earth, float ice, float hp, float speed, float flat, float factor)
    {
        var armour = Assert.IsType<Armour>(ShieldRules.For(new Spell(shield: 1, earth: earth, ice: ice), CastType.Self));

        Assert.Equal((hp, 1001f), (armour.Hp, armour.Mass));
        Assert.Equal(speed, armour.Speed, 5);
        Assert.Equal(flat, armour.FlatReduction);
        Assert.Equal(factor, armour.DamageFactor, 5);
        Assert.Equal(ice > 0 ? MathF.Max(1, ice + earth) : 0, armour.IceBlastMagnitude);
    }

    [Fact]
    public void OtherElementsOnSelf_AreAWard()
    {
        var ward = Assert.IsType<Ward>(ShieldRules.For(new Spell(shield: 1, fire: 2, life: 1), CastType.Self));

        Assert.Equal(Elements.Fire | Elements.Life, ward.Elements);
        Assert.Equal((3f, 25f), (ward.Radius, ward.Duration));                    // m = 2: 1.5·m, 15 + 5·m
        Assert.Null(ShieldRules.For(new Spell(shield: 1, poison: 1), CastType.Self));   // poison alone: nothing
        var spark = Assert.IsType<Ward>(ShieldRules.For(new Spell(shield: 1, fire: 1, poison: 3, lightning: 0.5f), CastType.Self));
        Assert.Equal((Elements.Fire | Elements.Lightning, 1.5f), (spark.Elements, spark.Radius));   // Poison neither warded nor counted; m at least 1
        var arcane = Assert.IsType<Ward>(ShieldRules.For(new Spell(shield: 1, arcane: 3), CastType.Self));
        Assert.Equal(4.5f, arcane.Radius);
    }

    [Fact]
    public void OnlyShieldSpells_AreShields()
    {
        Assert.Throws<ArgumentException>(() => ShieldRules.For(Spell.Of(Elements.Fire), CastType.Force));
    }
}
