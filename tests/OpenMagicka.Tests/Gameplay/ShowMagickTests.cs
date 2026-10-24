using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Magicks;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.MagickCastTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Performance Enchantment, Amalgameddon, Judgement Spray, Wave (MAGICKS.md §4.7–4.10).</summary>
public class ShowMagickTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    private static bool Cast(Character caster, MagickType magick) => MagickEffects.For(magick)!.Execute(caster);

    [Fact]
    public void PerformanceEnchantment_Boosts_ThenWithdraws()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;

        Assert.True(Cast(wizard, MagickType.PerformanceEnchantment));
        Run(world, 0.2f);
        Assert.True(wizard.Body.SpeedMultiplier > 1.9f, $"{wizard.Body.SpeedMultiplier}");
        Assert.Equal(2, wizard.Auras.Count(a => a.SelfCasted));
        var before = wizard.HitPoints;
        wizard.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 100, 1), null, wizard.Position);
        Assert.Equal(before - 50, wizard.HitPoints, 1);                               // half taken
        Run(world, 7);
        Assert.True(wizard.Body.SpeedMultiplier < 1, $"{wizard.Body.SpeedMultiplier}");   // the withdrawal
        Run(world, 3);

        Assert.False(wizard.IsEnchanted);
        Assert.DoesNotContain(wizard.Auras, a => a.SelfCasted);
    }

    [Fact]
    public void Amalgameddon_SmashesTwoOfAKind_IntoOne()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var a = Animated(world, Foe with { MaxHitPoints = 1000 }, at: new Vector3(3, 0.9f, 0));
        var b = Animated(world, Foe with { MaxHitPoints = 1000 }, at: new Vector3(-3, 0.9f, 0));
        Run(world, Tick);
        a.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 500, 1), null, a.Position);
        b.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 500, 1), null, b.Position);

        Assert.True(Cast(wizard, MagickType.Amalgameddon));
        Assert.False(Cast(wizard, MagickType.Amalgameddon));                         // one at a time
        Run(world, 8);

        var (survivor, gone) = a.IsDead ? (b, a) : (a, b);
        Assert.True(gone.IsOverkilled);
        Assert.False(survivor.IsDead);
        Assert.True(survivor.HitPoints > 900, $"{survivor.HitPoints}");               // the other's 500 on top of its own
        Assert.True(survivor.Body.Body.AffectedByGravity);
        Assert.False(survivor.IsStunned);
        Assert.Empty(world.Entities.Entities.OfType<Amalgamation>());
    }

    [Fact]
    public void Amalgameddon_FailsWithoutAPair()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        Animated(world, Foe, at: new Vector3(3, 0.9f, 0));
        Animated(world, Goblin with { Id = "orc", Factions = Factions.Human }, at: new Vector3(-3, 0.9f, 0));
        Run(world, Tick);

        Assert.False(Cast(wizard, MagickType.Amalgameddon));
    }

    [Fact]
    public void JudgementSpray_FiresTwentyColdMissiles_ThatHomeOnTheOneAhead()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var foe = Animated(world, Foe with { MaxHitPoints = 20_000 }, at: wizard.Position + wizard.Body.Facing * 10);
        var fired = new HashSet<Missile>();
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.JudgementSpray));
        for (var t = 0f; t < 1.2f; t += Tick)
        {
            fired.UnionWith(world.Entities.Entities.OfType<Missile>());
            world.Tick(Tick);
        }
        Run(world, 2.5f);

        Assert.Equal(20, fired.Count);
        Assert.True(foe.HitPoints < 20_000 - 273, $"{foe.HitPoints}");
        Assert.Empty(world.Entities.Entities.OfType<JudgementVolley>());
    }

    [Fact]
    public void Wave_RaisesSixPillars_AndTossesEachToucherOnce()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var foe = Animated(world, Foe, at: wizard.Position + wizard.Body.Facing * (wizard.Radius + 4.5f));
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.Wave));
        var pillars = new HashSet<RockPillar>();
        var rose = 0f;
        for (var t = 0f; t < 1.5f; t += Tick)
        {
            pillars.UnionWith(world.Entities.Entities.OfType<RockPillar>());
            world.Tick(Tick);
            rose = Math.Max(rose, foe.Body.Body.Velocity.Y);
        }

        Assert.True(pillars.Count == 6, $"{pillars.Count} pillars");
        Assert.True(rose > 5, $"tossed at {rose}");
        Assert.All(pillars, p => Assert.Same(pillars.First().Tossed, p.Tossed));      // one memory for the whole wave
        Assert.Single(pillars.First().Tossed, foe);
        Assert.Empty(world.Entities.Entities.OfType<RockPillar>());
    }
}
