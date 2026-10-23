using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Gameplay.Combat.StatusEffects;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The subtler status rules of docs/gameplay/SPELLS.md §9.</summary>
public class StatusRulesTests
{
    private static readonly Defences Plain = Defences.None;

    private static StatusSet With(params StatusEffect[] effects) =>
        effects.Aggregate(StatusSet.Empty, (set, effect) => set.Add(effect, Plain).Set);

    private static StatusSet After(StatusSet set, float seconds) => set.Update(seconds, 100, 1000).Set;

    [Theory]
    [InlineData(Cold, 3.05f, 2.95f)]       // fade, then cap: 3.05 − 0.1
    [InlineData(Frozen, 3.5f, 2.75f)]      // cap, then fade: 3 − 0.25
    [InlineData(Burning, 3.5f, 2.8f)]      // cap, then fade: 3 − 0.2
    [InlineData(Steamed, 0.2f, 0f)]        // starts at 1 whatever it is given, then −1 a second
    public void TheCapComesBeforeOrAfterTheFade_PerStatus(StatusEffects type, float magnitude, float after)
    {
        var set = With(StatusEffect.Create(type, dps: 10, magnitude));

        Assert.Equal(after, After(set, 1)[type].Magnitude, 4);
    }

    [Fact]
    public void Healing_IsCappedAtThree()
    {
        var set = With(StatusEffect.Create(Healing, -80, 9));                  // starts at 0.5 + 4.5 = 5

        Assert.Equal(3 - 0.5f, After(set, 0.5f)[Healing].Magnitude, 4);
    }

    [Fact]
    public void SteamWarmsTheCold()
    {
        Assert.Equal(Steamed, With(StatusEffect.Create(Cold, 0, 1)).Add(StatusEffect.Create(Steamed, 1, 1), Plain).Set.Active);
    }

    [Fact]
    public void ThawingTheLastOfTheIce_StartsDrying_ButDoesNotRestartIt()
    {
        var thawing = With(StatusEffect.Create(Frozen, 0, 1.5f), StatusEffect.Create(Wet, 0, 1));
        var dried = thawing.Add(StatusEffect.Create(Burning, 60, 1), Plain).Set;          // dries the water: 0.9 s
        Assert.Equal((Frozen, StatusSet.DryingTime), (dried.Active, dried.DryTimer));       // the ice stays while wet

        var later = After(dried, 0.5f);
        var thawed = later.Add(StatusEffect.Create(Burning, 60, 2), Plain).Set;
        Assert.Equal((None, 0.4f), (thawed.Active, MathF.Round(thawed.DryTimer, 4)));       // kept, not restarted

        var burning = After(With(StatusEffect.Create(Burning, 60, 1)), 0.1f);
        var doused = burning.Add(StatusEffect.Create(Cold, 0, 1), Plain).Set;
        Assert.Equal(StatusSet.DryingTime, doused.DryTimer);
    }

    [Fact]
    public void Grease_IsLostEvenWhenTheOtherStatusDoesNotTake()
    {
        var greasedAndCold = With(StatusEffect.Create(Greased, 10, 1), StatusEffect.Create(Cold, 0, 1));
        var greasedAndBurning = With(StatusEffect.Create(Greased, 10, 1)).Add(StatusEffect.Create(Burning, 60, 1), Plain).Set;
        var lit = greasedAndBurning.Add(StatusEffect.Create(Greased, 10, 1), Plain).Set;   // grease back on a burning target

        Assert.Equal(None, greasedAndCold.Add(StatusEffect.Create(Burning, 60, 1), Plain).Set.Active);   // the cold stops the fire, the grease goes
        Assert.Equal(Burning | Greased, lit.Active);
        Assert.Equal(None, lit.Add(StatusEffect.Create(Wet, 0, 1), Plain).Set.Active);                  // the fire stops the water, the grease goes
    }

    [Fact]
    public void Grease_IgnoresStatusResistance()
    {
        var waterproof = Defences.None with { Template = ResistanceTable.Neutral.With(Elements.Water, statusResistance: true) };

        Assert.Equal(StatusOutcome.Added, StatusSet.Empty.Add(StatusEffect.Create(Greased, 10, 1), waterproof).Outcome);
        Assert.Equal(StatusOutcome.None, StatusSet.Empty.Add(StatusEffect.Create(Wet, 0, 1), waterproof).Outcome);
    }

    [Fact]
    public void AnExpiringPoison_FadesAndIsNotCapped()
    {
        var poison = With(StatusEffect.Create(Poisoned, 50, 2, expires: true));

        var tick = poison.Update(0.5f, 100, 1000);

        Assert.Equal(1.5f, tick.Set[Poisoned].Magnitude, 4);
        Assert.Equal(37f, tick.Damage.Sum(d => d.Amount));                       // 50 · 1.5 · 0.5 = 37.5, whole points
        Assert.False(With(StatusEffect.Create(Poisoned, 50, 1, expires: true), StatusEffect.Create(Poisoned, 50, 1))[Poisoned].Expires);
    }

    [Fact]
    public void HealingAnUndeadThatIsPoisoned_CuresThePoison_ButLeavesNoHealing()
    {
        var undead = Defences.None with { Template = ResistanceTable.Neutral.With(Elements.Life, multiplier: -1) };

        var change = With(StatusEffect.Create(Poisoned, 50, 1)).Add(StatusEffect.Create(Healing, -80, 1), undead);

        Assert.Equal((StatusOutcome.Added, None), (change.Outcome, change.Set.Active));
        Assert.Equal(0f, change.Set[Healing].Magnitude);
    }

    [Fact]
    public void FreezingIsReportedAsRemoved()
    {
        Assert.Equal(StatusOutcome.Removed, With(StatusEffect.Create(Wet, 0, 1)).Add(StatusEffect.Create(Cold, 0, 1), Plain).Outcome);
    }

    [Fact]
    public void BadStatusesAndBuffs_AreRejected()
    {
        var nanBuff = Defences.None with { Buffs = [new Resistance(Elements.Poison, float.NaN)] };

        Assert.Throws<ArgumentOutOfRangeException>(() => StatusSet.Empty.Add(new StatusEffect(Burning, float.NaN, 1), Plain));
        Assert.Throws<ArgumentException>(() => StatusSet.Empty.Add(new StatusEffect(Burning | Wet, 10, 1), Plain));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatusSet.Empty.Add(StatusEffect.Create(Poisoned, 50, 1), nanBuff));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatusEffect.Create(Cold, float.PositiveInfinity, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatusSet.Empty.Update(0, 1, 1));
    }

    [Fact]
    public void TheDryingTimer_StopsAtZero()
    {
        Assert.Equal(0f, After(After(StatusSet.Empty, 1), 1).DryTimer);
    }

    [Fact]
    public void TheMeasureOfResistance_TakesFrostAndGreaseForTheWholeQuery()
    {
        Assert.Equal(1 - Math.Clamp(-700 / 300f + 2, -1, 1), Defences.None.ResistanceAgainst(Elements.Earth | Elements.Fire, Frozen), 4);
        Assert.Equal(0f, Defences.None.ResistanceAgainst(Elements.Earth | Elements.Fire, Greased), 4);   // 2 + 2, clamped to 1
    }
}
