using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Tests.Gameplay;

public class TypeRulesTests
{
    private const float Pi = MathF.PI;

    private static float Radians(float degrees) => degrees * Pi / 180;

    [Fact]
    public void APush_IsAConeForward_OrARingAround_StrongerWhenCharged()
    {
        var force = PushRules.For(CastType.Force, spellPower: 1, additionalRadius: 0)!;
        var area = PushRules.For(CastType.Area, spellPower: 0, additionalRadius: 1)!;

        Assert.Equal((550f, Pi / 6, 10f, 0.25f), (force.Strength, force.HalfAngle, force.Radius, force.Duration));
        Assert.Equal((50f, 2 * Pi, 4.5f), (area.Strength, area.HalfAngle, area.Radius));
        Assert.Null(PushRules.For(CastType.Self, 1, 0));
        Assert.Null(PushRules.For(CastType.Weapon, 1, 0));
        Assert.Equal(2.5f, force.CoveredRadius(elapsed: 0), 4);              // 25 % at the start
        Assert.Equal(10f, force.CoveredRadius(elapsed: 0.25f), 4);
        Assert.Equal(Radians(10) + 0.5f * 0.1f * Pi / 8, PushRules.LaunchElevation(spellPower: 0.5f), 5);
    }

    [Fact]
    public void ASpray_ReachesFurtherWithMoreElements_AndShortensAtObstacles()
    {
        var spell = new Spell(fire: 3, water: 1);

        Assert.Equal(9f, SprayRules.Range(spell));                           // 5 + the spray magnitudes
        Assert.Equal(9f, SprayRules.Range(spell with { Earth = 7 }));         // Earth is not one
        Assert.Equal(8.1f, SprayRules.Reach(range: 9, freeFraction: 1), 4);   // R · 0.1 · R
        Assert.Equal(4.05f, SprayRules.Reach(range: 9, freeFraction: 0.5f), 4);
        Assert.Equal((0.4f, 1f), (SprayRules.BuildUp(0.1f), SprayRules.BuildUp(1)));
        Assert.Equal((Pi / 10, 4f), (SprayRules.HalfAngle, SprayRules.MaxDuration));
        Assert.Equal(10f * MathF.Sqrt(3f / 5), SprayRules.AreaRadius(spell), 4);
        Assert.Equal((6f, Pi / 2), (SprayRules.WeaponRadius, SprayRules.WeaponHalfAngle));
    }

    [Fact]
    public void ABoulder_IsOneHeavyStone_SlowerWithMoreEarth()
    {
        var volley = ProjectileRules.Force(new Spell(earth: 2, fire: 1), spellPower: 1);

        Assert.Equal(1, volley.Count);
        Assert.Equal(110f * (0.5f + 0.5f / 2), volley.Speed, 4);
        Assert.Equal((101f, 10f), (volley.Mass, volley.Lifetime));
        Assert.Equal(0.225f, volley.Radius, 5);
        Assert.Equal((0.0875f + 3f / 5 * 0.2625f) / 1, volley.Interval, 5);
        Assert.Equal(SpellType.Spray, volley.RemainderType);                 // the Fire left without Earth
        Assert.Equal(10 * MathF.Sqrt(1f / 5), volley.RemainderBlastRadius, 4);
        Assert.Equal(0, volley.Pierces);
        Assert.Equal("Earth1", volley.Model);
    }

    [Fact]
    public void AnIceBoulder_NeverBlasts_AndGoesOnItsFirstHit()
    {
        var volley = ProjectileRules.Force(new Spell(earth: 2, ice: 1, arcane: 1), 0);

        Assert.Null(volley.RemainderType);
        Assert.Equal(0f, volley.RemainderBlastRadius);
        Assert.Equal(0, volley.Pierces);
        Assert.Equal("IceEarth2", volley.Model);
    }

    [Fact]
    public void ABoulderWithoutABlast_PiercesTargets()
    {
        var plain = ProjectileRules.Force(new Spell(earth: 1), 0);
        var sparking = ProjectileRules.Force(new Spell(earth: 1, lightning: 1), 0);   // a lightning blast has no size

        Assert.Equal((null, 0f, 25), (plain.RemainderType, plain.RemainderBlastRadius, plain.Pierces));
        Assert.Equal((null, 0f, 25), (sparking.RemainderType, sparking.RemainderBlastRadius, sparking.Pierces));
        Assert.Equal((3.75f + 2.5f, 40f, 15f), (ProjectileRules.IceBladeLength(new Spell(ice: 2)), ProjectileRules.WaveSpeed, ProjectileRules.WaveRange));
    }

    [Fact]
    public void IceShards_AreThreePerIce_SharingTheDamage()
    {
        var volley = ProjectileRules.Force(new Spell(ice: 2), spellPower: 0);

        Assert.Equal((6, 25f), (volley.Count, volley.Speed));
        Assert.Equal(1f / 6, volley.MagnitudeScale, 5);
        Assert.Equal((1f, null, "Ice3"), (volley.Mass, volley.RemainderType, volley.Model));
        Assert.Equal(Pi / 4, ProjectileRules.Spread(spellPower: 0), 5);
        Assert.Equal(0f, ProjectileRules.Spread(spellPower: 1), 5);
        Assert.Equal(2f, ProjectileRules.OtherSpeedFactor(1), 5);
    }

    [Fact]
    public void TooLittleIce_GivesNoShards()
    {
        var volley = ProjectileRules.Force(new Spell(ice: 0.2f), 0);

        Assert.Equal((0, 0f, 0f), (volley.Count, volley.Interval, volley.MagnitudeScale));
    }

    [Fact]
    public void OnlyProjectileSpells_FireProjectiles()
    {
        Assert.Throws<ArgumentException>(() => ProjectileRules.Force(new Spell(fire: 1), 0));
        Assert.Throws<ArgumentException>(() => ProjectileRules.Force(new Spell(earth: 1, shield: 1), 0));
        Assert.Throws<ArgumentException>(() => ProjectileRules.Force(Spell.OfMagick(MagickType.Haste), 0));
    }

    [Fact]
    public void ABeam_LastsLongerWithMoreArcaneOrLife_AndGrowsToItsMaximum()
    {
        Assert.Equal(1 + 2 * 3f, BeamRules.Lifetime(new Spell(arcane: 2, life: 1)));
        Assert.Equal((90f, 75f, 6), (BeamRules.GrowthPerSecond, BeamRules.MaxLength, BeamRules.MaxChainDepth));
        Assert.Equal(30f, BeamRules.Length(elapsed: 1f / 3));
        Assert.Equal(75f, BeamRules.Length(elapsed: 10));
        Assert.Equal((1.5f + 2f, 9 * Pi / 20), (BeamRules.WeaponRadius(new Spell(arcane: 4)), BeamRules.WeaponHalfAngle));
        Assert.Equal(5f, BeamRules.ExplosionRadius(owners: 2));
    }

    [Theory]
    [InlineData(true, Elements.Arcane, Elements.Arcane, 1, true)]                        // same owner
    [InlineData(false, Elements.Arcane | Elements.Fire, Elements.Arcane | Elements.Water, 1, true)]   // fire meets water
    [InlineData(false, Elements.Arcane | Elements.Water, Elements.Arcane | Elements.Lightning, 1, true)]
    [InlineData(false, Elements.Life | Elements.Fire, Elements.Life | Elements.Cold, 1, true)]
    [InlineData(false, Elements.Arcane, Elements.Life, 1, true)]                         // arcane meets a beam without arcane
    [InlineData(false, Elements.Arcane | Elements.Life, Elements.Life, 1, true)]
    [InlineData(false, Elements.Life, Elements.Life, 1, false)]
    [InlineData(false, Elements.Arcane, Elements.Arcane, 0.1f, true)]                    // nearly head-on
    [InlineData(false, Elements.Arcane | Elements.Fire, Elements.Arcane | Elements.Steam, 0.5f, false)]
    public void CrossingBeams_ExplodeOrMerge(bool sameOwner, Elements a, Elements b, float meanDirectionLength, bool explode)
    {
        Assert.Equal(explode, BeamRules.Explode(sameOwner, a, b, meanDirectionLength));
    }

    [Theory]
    [InlineData(CastType.Force, false, 8, 4 + 0.8f * 2, 1f / 8, LightningPattern.Facing)]
    [InlineData(CastType.Weapon, false, 8, 4 + 0.8f * 2, 1f / 8, LightningPattern.Facing)]
    [InlineData(CastType.Area, false, 32, 2 + 0.4f * 2, 1f / 16, LightningPattern.Circle)]
    [InlineData(CastType.Self, false, 1, 0, 1f, LightningPattern.Self)]
    [InlineData(CastType.Force, true, 8, 2, 1f / 8, LightningPattern.Circle)]           // wet: short, all around
    [InlineData(CastType.Area, true, 32, 2, 1f / 16, LightningPattern.Circle)]
    [InlineData(CastType.Self, true, 1, 0, 1f, LightningPattern.Self)]
    public void Lightning_StrikesInBolts(CastType cast, bool wet, int bolts, float range, float scale, LightningPattern pattern)
    {
        var strike = LightningRules.For(new Spell(lightning: 2), cast, wet);

        Assert.Equal((bolts, pattern), (strike.BoltsPerSecond, strike.Pattern));
        Assert.Equal(range, strike.Range, 4);
        Assert.Equal(scale, strike.MagnitudeScale, 5);
        Assert.Equal((Pi / 2, Pi / 4, Pi / 16, Pi / 4), (LightningRules.FirstTargetSector, LightningRules.ChainSector, LightningRules.MissWobble, LightningRules.CircleStep));
    }

    [Theory]
    [InlineData(0.5f, 1f, false, 0.875f)]
    [InlineData(1f, 1f, false, 0f)]
    [InlineData(-0.5f, 1f, false, 1f)]                       // inside the target's own radius
    [InlineData(0.5f, 1f, true, 1f)]                         // arcane or life: no falloff
    [InlineData(0f, 0f, false, 1f)]                          // a blast of no size reaches only its centre
    [InlineData(0.1f, 0f, false, 0f)]
    public void ABlast_FallsOffWithTheCubeOfTheDistance(float distance, float radius, bool arcaneOrLife, float k)
    {
        Assert.Equal(k, BlastRules.Falloff(distance, radius, arcaneOrLife), 5);
        Assert.Equal(2f, BlastRules.FullShake(6));
    }
}
