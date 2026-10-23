using System.Numerics;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Gameplay.Combat.DamageResult;

namespace OpenMagicka.Tests.Gameplay;

public class DamageRulesTests
{
    private static readonly DamageTarget Goblin = new(HitPoints: 300, MaxHitPoints: 300, StatusSet.Empty, Defences.None, HitTolerance: 20, BleedRate: 15);

    private static DamageEntry Hurt(Elements element, float amount, float magnitude = 1, AttackProperties extra = AttackProperties.None) =>
        new(AttackProperties.Damage | extra, element, amount, magnitude);

    [Fact]
    public void Damage_TakesAmountTimesMagnitude_AfterResistances()
    {
        var tough = Goblin with { Defences = Defences.None with { Template = ResistanceTable.Neutral.With(Elements.Fire, multiplier: 0.5f) } };

        var outcome = DamageRules.Hit(Hurt(Elements.Fire, 60, 2), tough);

        Assert.Equal((240f, 60f), (outcome.HitPoints, outcome.Dealt));
        Assert.Equal(Damaged | Hit, outcome.Result);
        Assert.Equal(Damaged, DamageRules.Hit(Hurt(Elements.Fire, 10), Goblin).Result);       // under the hit tolerance
        Assert.Equal(Damaged, DamageRules.Hit(Hurt(Elements.Fire, 100, 0.1f), Goblin).Result); // the tolerance is on amount × magnitude
    }

    [Fact]
    public void Healing_IsCappedAtFullHealth()
    {
        var hurt = Goblin with { HitPoints = 250 };

        var outcome = DamageRules.Hit(Hurt(Elements.Life, -180), hurt);

        Assert.Equal((300f, Healed), (outcome.HitPoints, outcome.Result));
    }

    [Fact]
    public void LightningHurtsTheWetTwice_AndEarthTheFrozenThrice()
    {
        var wet = Goblin with { Statuses = StatusSet.Empty.Add(StatusEffect.Create(StatusEffects.Wet, 0, 1), Defences.None).Set };
        var frozen = Goblin with { Statuses = StatusSet.Empty.Add(StatusEffect.Create(StatusEffects.Frozen, 0, 1), Defences.None).Set };

        Assert.Equal(100f, DamageRules.Hit(Hurt(Elements.Lightning, 50), wet).Dealt);
        Assert.Equal(150f, DamageRules.Hit(Hurt(Elements.Earth, 50), frozen).Dealt);
        Assert.Equal(150f, DamageRules.Hit(Hurt(Elements.Arcane, 50), frozen).Dealt);
        Assert.Equal(50f, DamageRules.Hit(Hurt(Elements.Fire, 50), frozen).Dealt);
    }

    [Fact]
    public void StatusesTakeBeforeResistances()
    {
        var fireproof = Goblin with { Defences = Defences.None with { Template = ResistanceTable.Neutral.With(Elements.Fire, multiplier: 0) } };

        var outcome = DamageRules.Hit(new DamageEntry(AttackProperties.Status, Elements.Fire | Elements.Steam, 60, 1), fireproof);

        Assert.Equal(StatusEffects.Wet, outcome.Statuses.Active);               // the fire is resisted to nothing; Steam wets
        Assert.True((outcome.Result & StatusAdded) != 0);
        Assert.True((outcome.Result & Deflected) != 0);                           // the entry itself: magnitude 0 after resistance
        Assert.Equal(300f, outcome.HitPoints);
    }

    [Fact]
    public void ColdOnTheWet_FreezesAndInterrupts()
    {
        var wet = Goblin with { Statuses = StatusSet.Empty.Add(StatusEffect.Create(StatusEffects.Wet, 0, 1), Defences.None).Set };

        var outcome = DamageRules.Hit(new DamageEntry(AttackProperties.Status, Elements.Cold, 0, 1), wet);

        Assert.True(outcome.InterruptsCast);
        Assert.True(outcome.Statuses.Has(StatusEffects.Frozen));
    }

    [Fact]
    public void AZeroMagnitude_IsDeflected_AndAZeroAmountToo()
    {
        Assert.Equal(Deflected, DamageRules.Hit(Hurt(Elements.Earth, 100, 0), Goblin).Result);
        Assert.Equal(Deflected, DamageRules.Hit(Hurt(Elements.Water, 0), Goblin).Result);
    }

    [Fact]
    public void StunAndBleed_NeedAPositiveAmount()
    {
        var outcome = DamageRules.Hit(Hurt(Elements.Earth, 50, extra: AttackProperties.Stun | AttackProperties.Bleed), Goblin);

        Assert.True(outcome.Stunned);
        Assert.Equal(15f, outcome.Statuses[StatusEffects.Bleeding].Dps);
        Assert.False(DamageRules.Hit(Hurt(Elements.Life, -50, extra: AttackProperties.Stun), Goblin with { HitPoints = 100 }).Stunned);
        Assert.True((outcome.Result & StatusAdded) != 0);
        Assert.False(DamageRules.Hit(Hurt(Elements.Life, -50, extra: AttackProperties.Bleed), Goblin with { HitPoints = 100 }).Statuses.Has(StatusEffects.Bleeding));
        Assert.False(DamageRules.Hit(Hurt(Elements.Earth, 50, extra: AttackProperties.Bleed), Goblin with { CanHaveStatus = false }).Statuses.Has(StatusEffects.Bleeding));
    }

    [Fact]
    public void LifeDamage_ThinsPoison()
    {
        var poisoned = Goblin with { Statuses = StatusSet.Empty.Add(StatusEffect.Create(StatusEffects.Poisoned, 50, 1), Defences.None).Set };

        var outcome = DamageRules.Hit(Hurt(Elements.Life, -100, 0.5f), poisoned);

        Assert.Equal(0.5f, outcome.Statuses[StatusEffects.Poisoned].Magnitude, 4);
    }

    [Fact]
    public void Piercing_IsReportedOnlyWhenItHurts()
    {
        Assert.True((DamageRules.Hit(Hurt(Elements.Earth, 50, extra: AttackProperties.Piercing), Goblin).Result & Pierced) != 0);
        Assert.Equal(Healed, DamageRules.Hit(Hurt(Elements.Life, -50, extra: AttackProperties.Piercing), Goblin with { HitPoints = 10 }).Result);
    }

    [Theory]
    [InlineData(400f, false, Killed)]
    [InlineData(1000f, false, Killed | OverKilled)]                               // HP ≤ −0.5·max − 500
    [InlineData(400f, true, Killed | OverKilled)]                                 // frozen targets shatter
    public void Death_AndOverkill(float amount, bool frozen, DamageResult death)
    {
        var target = frozen ? Goblin with { Statuses = StatusSet.Empty.Add(StatusEffect.Create(StatusEffects.Frozen, 0, 1), Defences.None).Set } : Goblin;

        var outcome = DamageRules.Hit(Hurt(Elements.Fire, amount), target);

        Assert.Equal(death, outcome.Result & (Killed | OverKilled));
        Assert.Equal(death.HasFlag(OverKilled) ? -300 - 1000 : 300 - amount, outcome.HitPoints);         // shattered: −max − 1000
        Assert.Equal(Killed, DamageRules.Hit(Hurt(Elements.Fire, 1000), Goblin with { BloodAndGore = false }).Result & (Killed | OverKilled));
        var arcane = DamageRules.Hit(Hurt(Elements.Arcane, 1000), Goblin);
        Assert.Equal((Killed | OverKilled, -700f), (arcane.Result & (Killed | OverKilled), arcane.HitPoints));   // pure Arcane does not shatter the body
    }

    [Fact]
    public void ADeadOrImmortalTarget_TakesNothing()
    {
        Assert.Equal(None, DamageRules.Hit(Hurt(Elements.Fire, 100), Goblin with { HitPoints = 0 }).Result);
        Assert.Equal(None, DamageRules.Hit(Hurt(Elements.Fire, 100), Goblin with { IsImmortal = true }).Result);
    }

    [Fact]
    public void ACollection_IsDeflectedOnlyIfEveryEntryIs()
    {
        Assert.Equal(Damaged | Hit, DamageRules.Combine([Deflected, Damaged | Hit]));
        Assert.Equal(Deflected, DamageRules.Combine([Deflected, Deflected]));
        Assert.Equal(Deflected | Killed, DamageRules.Combine([Deflected | Killed, Deflected]));
        Assert.Equal(None, DamageRules.Combine([]));
    }

    [Fact]
    public void APush_FliesTheTargetItsReach_ThroughTheAir()
    {
        var velocity = DamageRules.PushVelocity(attack: Vector3.Zero, target: new Vector3(3, 0, 4), DamageRules.PushElevation,
            power: 400, distance: 5, mass: 100, gravity: 19.64f);

        var reach = 5 * 2 * MathF.Sqrt(1 - (100 - 100f) / 400);                    // the push power against the mass
        var expected = 1.4f * MathF.Sqrt(19.64f * reach / MathF.Sin(2 * DamageRules.PushElevation));
        Assert.Equal(expected, velocity.Length(), 2);
        Assert.Equal(new Vector3(0.6f, 0, 0.8f), Vector3.Normalize(velocity with { Y = 0 }));
        Assert.Equal(MathF.Tan(DamageRules.PushElevation), velocity.Y / new Vector2(velocity.X, velocity.Z).Length(), 4);
        Assert.NotEqual(Vector3.Zero, DamageRules.PushVelocity(Vector3.Zero, Vector3.UnitX, DamageRules.KnockbackElevation, 10000, 5, mass: 1000, gravity: 19.64f));
        Assert.Equal(Vector3.Zero, DamageRules.PushVelocity(Vector3.Zero, Vector3.UnitX, DamageRules.KnockbackElevation, 10000, 5, mass: 1001, gravity: 19.64f));
        Assert.Equal(Vector3.Zero, DamageRules.PushVelocity(Vector3.Zero, Vector3.UnitX, DamageRules.KnockbackElevation, -100, 5, mass: 100, gravity: 19.64f));
        Assert.Equal(Vector3.Zero, DamageRules.PushVelocity(Vector3.Zero, Vector3.Zero, DamageRules.KnockbackElevation, 400, 5, 100, 19.64f));
    }

    [Fact]
    public void Regeneration_StopsWhileBurningOrPoisoned_AndAtFullHealth()
    {
        var burning = StatusSet.Empty.Add(StatusEffect.Create(StatusEffects.Burning, 60, 1), Defences.None).Set;

        Assert.Equal(105f, DamageRules.Regenerate(100, 300, rate: 10, dt: 0.5f, StatusSet.Empty));
        Assert.Equal(300f, DamageRules.Regenerate(299, 300, rate: 10, dt: 0.5f, StatusSet.Empty));
        Assert.Equal(100f, DamageRules.Regenerate(100, 300, rate: 10, dt: 0.5f, burning));
        Assert.Equal(0f, DamageRules.Regenerate(0, 300, rate: 10, dt: 0.5f, StatusSet.Empty));
    }
}
