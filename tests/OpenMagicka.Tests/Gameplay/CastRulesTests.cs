using OpenMagicka.Gameplay.Spells;

namespace OpenMagicka.Tests.Gameplay;

public class CastRulesTests
{
    [Theory]
    [InlineData(SpellType.Projectile, CastType.Force, true)]
    [InlineData(SpellType.Projectile, CastType.Area, false)]
    [InlineData(SpellType.Push, CastType.Force, true)]
    [InlineData(SpellType.Push, CastType.Area, true)]
    [InlineData(SpellType.Push, CastType.Self, false)]
    [InlineData(SpellType.Spray, CastType.Force, false)]
    public void OnlyProjectilesForwardAndPushes_AreCharged(SpellType type, CastType cast, bool charges)
    {
        Assert.Equal(charges, CastRules.Charges(type, cast));
    }

    [Fact]
    public void TheChargeGrowsOverTheClip_ThenStaysFull()
    {
        Assert.Equal(0.5f, CastRules.Charge(elapsed: 0.5f, clipDuration: 1));
        Assert.Equal(1f, CastRules.Charge(elapsed: 3, clipDuration: 1));
        Assert.Equal(1f, CastRules.Charge(elapsed: 0, clipDuration: 0));
        Assert.Equal(0f, CastRules.Charge(elapsed: -1, clipDuration: 1));
    }

    [Theory]
    [InlineData(CastType.Force, 0.5f, 1.5f)]
    [InlineData(CastType.Area, 0.5f, 1.5f)]
    [InlineData(CastType.Weapon, 0.5f, 1.5f)]
    [InlineData(CastType.Self, 0.2f, 0.2f)]
    public void Effects_LiveBetweenTheirMinimumAndMaximumTimes(CastType cast, float min, float max)
    {
        Assert.Equal((min, max), CastRules.EffectLifetime(cast));
    }

    [Theory]
    [InlineData(SpellType.Spray, 0.6f)]
    [InlineData(SpellType.Lightning, 0.1f)]
    [InlineData(SpellType.Shield, 0.1f)]
    [InlineData(SpellType.Beam, 0.0325f)]
    [InlineData(SpellType.Push, 0f)]
    [InlineData(SpellType.Projectile, 0f)]
    [InlineData(SpellType.Magick, 0.75f)]
    public void CastingSlowsTurning(SpellType type, float speed)
    {
        Assert.Equal(speed, CastRules.TurnSpeed(type));
    }

    [Fact]
    public void ContinuousEffects_StopOnReleaseAfterTheirMinimum_OrWhenTheCasterStops()
    {
        Assert.False(CastRules.IsContinuous(SpellType.Projectile));
        Assert.True(CastRules.IsContinuous(SpellType.Spray));
        Assert.False(CastRules.Interrupted(alive: 0.3f, minimum: 0.5f, held: false, casterAlive: true, stillCasting: true));
        Assert.True(CastRules.Interrupted(alive: 0.6f, minimum: 0.5f, held: false, casterAlive: true, stillCasting: true));
        Assert.False(CastRules.Interrupted(alive: 0.6f, minimum: 0.5f, held: true, casterAlive: true, stillCasting: true));
        Assert.True(CastRules.Interrupted(alive: 0.1f, minimum: 0.5f, held: true, casterAlive: false, stillCasting: true));
        Assert.True(CastRules.Interrupted(alive: 0.1f, minimum: 0.5f, held: true, casterAlive: true, stillCasting: false));
    }

    [Fact]
    public void AWeaponCast_ByAPlayer_ImbuesTheWeapon()
    {
        Assert.True(CastRules.ImbuesWeapon(CastType.Weapon, isAvatar: true));
        Assert.False(CastRules.ImbuesWeapon(CastType.Weapon, isAvatar: false));
        Assert.False(CastRules.ImbuesWeapon(CastType.Force, isAvatar: true));
    }

    [Fact]
    public void ImbuingMovesTheQueueIntoTheWeapon_Normalised()
    {
        var player = ElementQueue.Empty.Add(new Spell(fire: 2)).Queue.Conjure(Elements.Earth).Queue;
        var weapon = new ElementQueue(ElementQueue.PlayerCapacity);

        var (emptied, imbued) = CastRules.Imbue(player, weapon);

        Assert.Empty(emptied.Entries);
        Assert.Equal([Elements.Fire, Elements.Earth], imbued.Entries.Select(e => e.Element));
        Assert.Equal(1f, imbued.Entries[0].Fire, 5);                         // 2 normalised to 1
        Assert.Throws<ArgumentException>(() => CastRules.Imbue(player.WithMagick(MagickType.Haste), weapon));
    }
}
