using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using OpenMagicka.Gameplay.Spells.Magicks;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.MagickCastTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Chain Lightning, Thunder Bolt, Crash to Desktop, Conflagration, Meteor Shower, Napalm (MAGICKS.md §4).</summary>
public class StrikeMagickTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    private static readonly CharacterTemplate Sturdy = Foe with { MaxHitPoints = 20_000 };

    private static bool Cast(Character caster, MagickType magick) => MagickEffects.For(magick)!.Execute(caster);

    private static Character Ahead(GameWorld world, Character caster, float distance, CharacterTemplate? template = null, float side = 0)
    {
        var across = Vector3.Cross(caster.Body.Facing, Vector3.UnitY);
        var foe = Animated(world, template ?? Foe, at: caster.Position + caster.Body.Facing * distance + across * side);
        return foe;
    }

    [Fact]
    public void ChainLightning_StrikesAlongTheFacing()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var foe = Ahead(world, wizard, 8, Sturdy);
        Run(world, Tick);

        Cast(wizard, MagickType.ChainLightning);

        Assert.InRange(foe.HitPoints, 20_000 - 851, 20_000 - 849);
        Assert.Equal(300f, wizard.HitPoints);
    }

    [Fact]
    public void ThunderBolt_StrikesTheTallestNearThePointAhead_AndFailsIndoors()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var small = Ahead(world, wizard, 8, Sturdy, side: 2);
        var tall = Ahead(world, wizard, 9, Sturdy with { Length = 3 }, side: -2);
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.ThunderB));

        Assert.True(tall.HitPoints < 20_000 - 4000, $"{tall.HitPoints}");
        Assert.Equal(20_000f, small.HitPoints);
        world.Entities.Indoors = true;
        Assert.False(Cast(wizard, MagickType.ThunderB));
    }

    [Fact]
    public void ThunderBolt_UnderAShield_StrikesTheShield()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var shield = new SpellShield(world.Physics, null, ShieldShape.Sphere, wizard.Position, -Vector3.UnitZ, ShieldRules.ShieldRadius, 5000);
        world.Entities.Add(shield);
        var foe = Ahead(world, wizard, 9, Sturdy);
        Run(world, Tick);

        Cast(wizard, MagickType.ThunderB);

        Assert.True(shield.HitPoints < 100, $"{shield.HitPoints}");
        Assert.Equal(20_000f, foe.HitPoints);
    }

    [Fact]
    public void CrashToDesktop_TakesSomeoneNear_ButSparesTheScripted_AndBosses()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        wizard.CannotDieWithoutExplicitKill = true;
        var boss = Ahead(world, wizard, 5, Foe with { MaxHitPoints = 200_000 }, side: 3);
        var foe = Ahead(world, wizard, 5, Foe);
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.CTD));
        Run(world, Tick);

        Assert.DoesNotContain(foe, world.Entities.Entities);
        Assert.Contains(boss, world.Entities.Entities);
        Assert.False(Cast(wizard, MagickType.CTD));                                  // nobody left to take
    }

    [Fact]
    public void Conflagration_BurnsWhatTheWallPasses_Once()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var near = Ahead(world, wizard, 10, Sturdy);
        var far = Ahead(world, wizard, 30, Sturdy);
        var behind = Ahead(world, wizard, -5, Sturdy);
        Run(world, Tick);

        Cast(wizard, MagickType.Conflagration);
        Run(world, 1.2f);

        Assert.True(near.Statuses.Has(StatusEffects.Burning));
        Assert.True(near.HitPoints <= 20_000 - 600, $"{near.HitPoints}");
        Assert.True(near.HitPoints > 20_000 - 1200, $"burnt twice: {near.HitPoints}");
        Assert.Equal(20_000f, far.HitPoints);
        Assert.Equal(20_000f, behind.HitPoints);
        Assert.Equal(300f, wizard.HitPoints);
        Assert.Empty(world.Entities.Entities.OfType<ConflagrationSweep>());
    }

    [Fact]
    public void MeteorShower_RainsBoulders_AndARecastOnlyStartsItsTimeOver()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;

        Assert.True(Cast(wizard, MagickType.MeteorS));
        Run(world, 1);
        Assert.Contains(world.Entities.Entities, e => e is Missile);
        Assert.True(Cast(wizard, MagickType.MeteorS));

        Assert.Single(world.Entities.Entities.OfType<MeteorShowerEffect>());
        world.Entities.Indoors = true;
        Assert.False(Cast(wizard, MagickType.MeteorS));
    }

    [Fact]
    public void Napalm_BombsTheMarkedLine_AfterTheWait()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var foe = Ahead(world, wizard, NapalmStrike.Wait == 5 ? 15 : 15, Sturdy);
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.Napalm));
        Run(world, 4.9f);
        Assert.Equal(20_000f, foe.HitPoints);
        Assert.True(Cast(wizard, MagickType.Napalm));                                // the marker moves while the plane is away
        Run(world, 0.5f);
        Assert.False(Cast(wizard, MagickType.Napalm));                               // the plane is passing
        Run(world, 2);

        Assert.True(foe.HitPoints < 20_000 - 1000, $"{foe.HitPoints}");
        Assert.Empty(world.Entities.Entities.OfType<NapalmStrike>());
    }
}
