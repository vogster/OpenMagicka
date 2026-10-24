using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Gameplay.Combat.StatusEffects;

namespace OpenMagicka.Tests.Gameplay;

public class StatusSetTests
{
    private const float Tick = 1f / 60;

    private static readonly Defences Plain = Defences.None;

    private static StatusSet With(params StatusEffect[] effects) =>
        effects.Aggregate(StatusSet.Empty, (set, effect) => set.Add(effect, Plain).Set);

    private static (StatusSet Set, List<StatusDamage> Damage) Run(StatusSet set, float seconds, float hp = 100, float maxHp = 1000)
    {
        var damage = new List<StatusDamage>();
        for (var t = 0f; t < seconds - 1e-4f; t += Tick)
        {
            var tick = set.Update(Tick, hp, maxHp);
            (set, damage) = (tick.Set, [.. damage, .. tick.Damage]);
        }
        return (set, damage);
    }

    [Fact]
    public void ANewStatus_IsAdded_AndShowsInTheMask()
    {
        var change = StatusSet.Empty.Add(StatusEffect.Create(Poisoned, dps: 50, magnitude: 1), Plain);

        Assert.Equal(StatusOutcome.Added, change.Outcome);
        Assert.True(change.Set.Has(Poisoned));                                     // at once, unlike the original
        Assert.Equal(Poisoned, change.Set.Active);
        Assert.Equal(StatusOutcome.None, StatusSet.Empty.Add(StatusEffect.Create(Cold, 0, magnitude: 0), Plain).Outcome);
    }

    [Fact]
    public void Burning_GrowsFasterThanItFades_AndBurnsInWholePoints()
    {
        var (set, damage) = Run(With(StatusEffect.Create(Burning, dps: 60, magnitude: 1)), 1);

        Assert.Equal(0.8f, set[Burning].Magnitude, 3);                            // −0.2 per second
        Assert.Equal(60 * MathF.Pow(1 + 0.25f / 60, 60), set[Burning].Dps, 0);    // +25 % of itself per second
        Assert.All(damage, d => Assert.Equal((Elements.Fire, d.Amount), (d.Element, MathF.Round(d.Amount))));
        Assert.InRange(damage.Sum(d => d.Amount), 64, 68);
    }

    [Fact]
    public void Burning_CannotOutgrowFiveHundredPerMagnitude()
    {
        var (set, _) = Run(With(StatusEffect.Create(Burning, dps: 1000, magnitude: 0.5f)), Tick);

        Assert.Equal(500 * set[Burning].Magnitude, set[Burning].Dps, 2);
    }

    [Theory]
    [InlineData(Cold, 3, 2.9f)]          // capped at 3 after the decay, not before: (3 − 0.1)
    [InlineData(Frozen, 4, 2.75f)]       // capped at 3, then −0.25
    [InlineData(Wet, 2, 1f)]             // capped at 1, never fades
    [InlineData(Greased, 1, 1f)]
    public void Statuses_AreCappedAndFade(StatusEffects type, float magnitude, float after)
    {
        var set = With(StatusEffect.Create(type, dps: 10, magnitude));

        var (later, _) = Run(set, 1);

        Assert.Equal(after, later[type].Magnitude, 2);
    }

    [Fact]
    public void Poison_HurtsByItsMagnitude_AndHealingEndsAtFullHealth()
    {
        var (poisoned, poison) = Run(With(StatusEffect.Create(Poisoned, dps: 50, magnitude: 2)), 1);
        Assert.Equal(1f, poisoned[Poisoned].Magnitude);                           // capped at 1, no decay
        Assert.InRange(poison.Sum(d => d.Amount), 49, 50);                        // whole points; the rest carries on
        Assert.All(poison, d => Assert.Equal(Elements.Poison, d.Element));

        var healing = With(StatusEffect.Create(Healing, dps: -80, magnitude: 1));
        Assert.Equal(1f, healing[Healing].Magnitude);                              // 0.5 + 0.5·m
        var (healed, heal) = Run(healing, 0.5f);
        Assert.InRange(heal.Sum(d => d.Amount), -40, -39);
        Assert.All(heal, d => Assert.Equal(Elements.Life, d.Element));
        Assert.False(Run(healing, Tick, hp: 1000).Set.Has(Healing));               // full health: gone this tick
    }

    [Fact]
    public void ColdAndPoison_SlowTheCharacter_AndBurningOrPoisonStopRegeneration()
    {
        var cold = With(StatusEffect.Create(Cold, 0, magnitude: 1));
        var poisoned = With(StatusEffect.Create(Poisoned, 50, 1));

        Assert.Equal(0.333f + 0.667f * 0.001f, cold.SpeedFactor(), 5);
        Assert.Equal(0.5f, poisoned.SpeedFactor());
        Assert.Equal(1f, poisoned.SpeedFactor(inCutscene: true));
        Assert.Equal(1f, StatusSet.Empty.SpeedFactor());
        Assert.Null(StatusSet.Empty.SpeedCap());
        Assert.Null(poisoned.SpeedCap(inCutscene: true));
        Assert.True(poisoned.BlocksRegeneration);
        Assert.True(With(StatusEffect.Create(Burning, 60, 1)).BlocksRegeneration);
        Assert.False(cold.BlocksRegeneration);
    }

    [Fact]
    public void SameStatuses_Stack()
    {
        var burning = With(StatusEffect.Create(Burning, 60, 1), StatusEffect.Create(Burning, 30, 0.5f));
        var poisoned = With(StatusEffect.Create(Poisoned, 40, 0.25f), StatusEffect.Create(Poisoned, 80, 0.75f));
        var healing = With(StatusEffect.Create(Healing, -80, 1), StatusEffect.Create(Healing, -40, 3));

        Assert.Equal((1.5f, 60f), (burning[Burning].Magnitude, burning[Burning].Dps));               // DPS: the larger
        Assert.Equal((1f, 70f), (poisoned[Poisoned].Magnitude, poisoned[Poisoned].Dps));             // DPS: weighted
        Assert.Equal(2f, healing[Healing].Magnitude);                                               // the larger magnitude
        Assert.Equal((1 * -80 + 2 * -40) / 2f, healing[Healing].Dps, 3);                            // weights m/max: 0.5 and 1
    }

    [Fact]
    public void FireMeetsWater_AndDries()
    {
        var wet = With(StatusEffect.Create(Wet, 0, 1));

        var change = wet.Add(StatusEffect.Create(Burning, 60, 1), Plain);

        Assert.Equal(StatusOutcome.Removed, change.Outcome);
        Assert.Equal(None, change.Set.Active);
        Assert.Equal(StatusSet.DryingTime, change.Set.DryTimer);
        Assert.Equal(StatusOutcome.Removed, change.Set.Add(StatusEffect.Create(Wet, 0, 1), Plain).Outcome);    // still drying
        Assert.Equal(StatusOutcome.Removed, change.Set.Add(StatusEffect.Create(Cold, 0, 1), Plain).Outcome);
        var dried = Run(change.Set, 1).Set;
        Assert.Equal(StatusOutcome.Added, dried.Add(StatusEffect.Create(Wet, 0, 1), Plain).Outcome);
    }

    [Fact]
    public void ColdOnAWetTarget_FreezesIt_AndInterruptsTheCast()
    {
        var change = With(StatusEffect.Create(Wet, 0, 1)).Add(StatusEffect.Create(Cold, 0, 0.3f), Plain);

        Assert.True(change.InterruptsCast);
        Assert.Equal(Frozen, change.Set.Active);
        Assert.Equal(1f, change.Set[Frozen].Magnitude);

        var refreshed = Run(change.Set, 2).Set.Add(StatusEffect.Create(Cold, 0, 0.1f), Plain).Set;
        Assert.Equal(1f, refreshed[Frozen].Magnitude);

        var iceProof = Defences.None with { Template = ResistanceTable.Neutral.With(Elements.Ice, statusResistance: true) };
        var notFrozen = With(StatusEffect.Create(Wet, 0, 1)).Add(StatusEffect.Create(Cold, 0, 1), iceProof).Set;
        Assert.Equal(Wet | Cold, notFrozen.Active);
    }

    [Fact]
    public void FireThawsIce_ButIsNotLit()
    {
        var frozen = With(StatusEffect.Create(Frozen, 0, 1.5f));

        var thawed = frozen.Add(StatusEffect.Create(Burning, 60, 1), Plain);

        Assert.Equal(StatusOutcome.Removed, thawed.Outcome);
        Assert.Equal(0.5f, thawed.Set[Frozen].Magnitude, 4);
        Assert.Equal(StatusSet.DryingTime, thawed.Set.Add(StatusEffect.Create(Burning, 60, 1), Plain).Set.DryTimer);   // the last of it
    }

    [Fact]
    public void ColdPutsOutFire_AndFireMeltsCold()
    {
        var burning = With(StatusEffect.Create(Burning, 60, 1));
        var cold = With(StatusEffect.Create(Cold, 0, 1));

        var doused = burning.Add(StatusEffect.Create(Cold, 0, 1), Plain);
        var warmed = cold.Add(StatusEffect.Create(Burning, 60, 1), Plain);

        Assert.Equal((None, StatusSet.DryingTime), (doused.Set.Active, doused.Set.DryTimer));
        Assert.Equal((None, StatusOutcome.Removed), (warmed.Set.Active, warmed.Outcome));
    }

    [Fact]
    public void GreaseFeedsFire_AndWaterWashesIt()
    {
        var greased = With(StatusEffect.Create(Greased, 10, 1));

        var lit = greased.Add(StatusEffect.Create(Burning, 60, 1), Plain).Set;
        var washed = greased.Add(StatusEffect.Create(Wet, 0, 1), Plain).Set;
        var regreased = washed.Add(StatusEffect.Create(Greased, 10, 1), Plain).Set;

        Assert.Equal((Burning, 240f), (lit.Active, lit[Burning].Dps));
        Assert.Equal(Wet, washed.Active);
        Assert.Equal(Greased, regreased.Active);
        Assert.Equal(StatusOutcome.None, StatusSet.Empty.Add(StatusEffect.Create(Greased, 0, 1), Plain).Outcome);   // no DPS: never sticks
        Assert.Equal(new StatusDamage(120, Elements.Fire), greased.Amplify(new StatusDamage(60, Elements.Fire)));
        Assert.Equal(new StatusDamage(60, Elements.Fire), StatusSet.Empty.Amplify(new StatusDamage(60, Elements.Fire)));
    }

    [Fact]
    public void HealingCuresPoisonAndBleeding_AndLifeDamageThinsPoison()
    {
        var sick = With(StatusEffect.Create(Poisoned, 50, 1), StatusEffect.Create(Bleeding, 20, 1));

        Assert.Equal(Healing, sick.Add(StatusEffect.Create(Healing, -80, 1), Plain).Set.Active);
        Assert.Equal(0.25f, sick.ThinPoison(0.75f)[Poisoned].Magnitude, 4);
        Assert.False(sick.ThinPoison(2).Has(Poisoned));
    }

    [Fact]
    public void Resistances_ShapeTheStatusOnApplication()
    {
        var fireproof = Defences.None with { Template = ResistanceTable.Neutral.With(Elements.Fire, multiplier: 0.5f, modifier: 10) };
        var damp = Defences.None with { Template = ResistanceTable.Neutral.With(Elements.Water, multiplier: 0.25f, modifier: 5) };
        var immune = Defences.None with { Template = ResistanceTable.Neutral.With(Elements.Poison, statusResistance: true) };
        var staffOnly = Defences.None with { Staff = ResistanceTable.Neutral.With(Elements.Poison, statusResistance: true) };
        var warded = Defences.None with { Buffs = [new Resistance(Elements.Fire | Elements.Cold, Multiplier: 0.5f)] };

        var burning = StatusSet.Empty.Add(StatusEffect.Create(Burning, 60, 1), fireproof).Set[Burning];
        var wet = StatusSet.Empty.Add(StatusEffect.Create(Wet, 0, 1), damp).Set[Wet];

        Assert.Equal((35f, 1f), (burning.Dps, burning.Magnitude));                  // (60 + 10) · 0.5, magnitude kept
        Assert.Equal((5f, 0.25f), (wet.Dps, wet.Magnitude));                        // DPS + modifier, magnitude × multiplier
        Assert.Equal(StatusOutcome.None, StatusSet.Empty.Add(StatusEffect.Create(Poisoned, 50, 1), immune).Outcome);
        Assert.Equal(StatusOutcome.Added, StatusSet.Empty.Add(StatusEffect.Create(Poisoned, 50, 1), staffOnly).Outcome);   // only the template forbids
        Assert.Equal(30f, StatusSet.Empty.Add(StatusEffect.Create(Burning, 60, 1), warded).Set[Burning].Dps);
    }

    [Fact]
    public void ExpiringStatuses_FadeAndReplaceEachOther()
    {
        var bleed = With(StatusEffect.Create(Bleeding, 20, 3, expires: true));
        var plainBleed = With(StatusEffect.Create(Bleeding, 20, 3));

        Assert.Equal(3f, bleed[Bleeding].Magnitude);
        Assert.Equal(1f, plainBleed[Bleeding].Magnitude);
        Assert.Equal(2f, Run(bleed, 1).Set[Bleeding].Magnitude, 2);
        Assert.Equal(1f, Run(plainBleed, 1).Set[Bleeding].Magnitude);
        var replaced = bleed.Add(StatusEffect.Create(Bleeding, 50, 0.5f, expires: true), Plain).Set[Bleeding];
        Assert.Equal((50f, 0.5f), (replaced.Dps, replaced.Magnitude));
    }

    [Fact]
    public void Death_ClearsEveryStatus()
    {
        Assert.Equal(None, With(StatusEffect.Create(Burning, 60, 1), StatusEffect.Create(Poisoned, 50, 1)).Cleared().Active);
    }

    [Fact]
    public void TheMeasureOfResistance_CountsFrostAndGrease()
    {
        var table = ResistanceTable.Neutral.With(Elements.Earth, modifier: 150).With(Elements.Fire, multiplier: 0.25f);
        var defences = Defences.None with { Template = table };

        Assert.Equal(0f, Defences.None.ResistanceAgainst(Elements.Fire, None));
        Assert.Equal(0.75f, defences.ResistanceAgainst(Elements.Fire, None), 4);
        Assert.Equal(0.5f, defences.ResistanceAgainst(Elements.Fire, Greased), 4);              // grease doubles the fire multiplier
        Assert.Equal(0f, defences.ResistanceAgainst(Elements.Earth, None), 4);                  // 150/300 + 1, clamped to 1
        Assert.Equal(200 / 300f, defences.ResistanceAgainst(Elements.Earth, Frozen), 4);       // frost: −350 to Earth's modifier
    }
}
