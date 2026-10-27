using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
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

/// <summary>Rain, Blizzard, Thunder Storm, Time Warp, Tractor Pull, Propp's Magick and Grease (MAGICKS.md §5).</summary>
public class FieldMagickTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human, MaxHitPoints = 20_000 };

    private static bool Cast(Character caster, MagickType magick) => MagickEffects.For(magick)!.Execute(caster);

    [Fact]
    public void Rain_SoaksEveryone_AndARecastKeepsOneRain_AndIndoorsItDoesNothing()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var far = Animated(world, Foe, at: new Vector3(40, 0.9f, 40));
        var sheltered = Animated(world, Foe, at: new Vector3(-20, 0.9f, 0));
        world.Entities.Add(new SpellShield(world.Physics, null, ShieldShape.Sphere, sheltered.Position, -Vector3.UnitZ, ShieldRules.ShieldRadius, 5000));
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.Rain));
        Run(world, 0.6f);
        Assert.True(Cast(wizard, MagickType.Rain));

        Assert.True(far.Statuses.Has(StatusEffects.Wet));
        Assert.True(wizard.Statuses.Has(StatusEffects.Wet));
        Assert.False(sheltered.Statuses.Has(StatusEffects.Wet));
        Assert.Single(world.Entities.Entities.OfType<Weather>());
        world.Entities.Indoors = true;
        Assert.False(Cast(wizard, MagickType.Blizzard));
    }

    [Fact]
    public void Blizzard_ChillsEveryone_AndFadesAfterTenSeconds()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var foe = Animated(world, Foe, at: new Vector3(20, 0.9f, 0));
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.Blizzard));
        Run(world, 0.6f);
        Assert.True(foe.Statuses.Has(StatusEffects.Cold));
        Run(world, 10.6f);

        Assert.Empty(world.Entities.Entities.OfType<Weather>());
    }

    [Fact]
    public void ThunderStorm_RainsAndStrikesFromTheSky()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var bolts = 0;
        world.Entities.Cue += (_, cue) => bolts += cue is LightningCue ? 1 : 0;
        wizard.Cue += (_, cue) => bolts += cue is LightningCue ? 1 : 0;

        Assert.True(Cast(wizard, MagickType.ThunderS));
        Run(world, 1.9f);
        Assert.Equal(0, bolts);                                                      // the first after 2 s
        Run(world, 4);

        Assert.True(bolts >= 3, $"{bolts} bolts");
        Assert.True(world.Entities.Entities.OfType<Weather>().Single().IsStorm);
    }

    [Fact]
    public void TimeWarp_SlowsTheLevelToHalf_ForFifteenSeconds()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;

        Assert.True(Cast(wizard, MagickType.TimeWarp));
        Assert.False(Cast(wizard, MagickType.TimeWarp));                            // one at a time
        for (var i = 0; i < 5 * 60; i++)
        {
            world.Tick(Tick);
        }
        Assert.InRange(world.Entities.TimeMultiplier, 0.5f, 0.52f);
        for (var i = 0; i < 10 * 60 + 5; i++)
        {
            world.Tick(Tick);
        }

        Assert.Equal(1f, world.Entities.TimeMultiplier);
        Assert.Empty(world.Entities.Entities.OfType<TimeWarpEffect>());
    }

    [Fact]
    public void TimeWarp_GivesTheTimeBack_WhenTheLevelIsCleared()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        Cast(wizard, MagickType.TimeWarp);
        Run(world, 2);
        Assert.True(world.Entities.TimeMultiplier < 0.9f);

        world.Entities.Clear();

        Assert.Equal(1f, world.Entities.TimeMultiplier);
    }

    [Fact]
    public void TractorPull_DrawsThingsToTheCaster()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var foe = Animated(world, Goblin with { Factions = Factions.Human }, at: wizard.Position + new Vector3(10, 0, 0));
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.TractorPull));
        Run(world, 0.3f);

        Assert.True(foe.Position.X < 9, $"{foe.Position}");
    }

    [Fact]
    public void TractorPull_PullsAVictimOutOfItsGrip_AndTeleport_LeavesTheGripperBehind()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var gripper = Animated(world, Foe, at: wizard.Position + new Vector3(10, 0, 3));
        var victim = Animated(world, Foe, at: wizard.Position + new Vector3(10, 0, 0));
        Run(world, Tick);
        Assert.True(gripper.GripCharacter(victim, GripType.Hold, -1, 10));
        Assert.True(Cast(wizard, MagickType.TractorPull));
        Assert.False(victim.IsGripped);

        Assert.True(gripper.GripCharacter(wizard, GripType.Hold, -1, 10));
        Assert.True(Cast(wizard, MagickType.Teleport));
        Assert.False(wizard.IsGripped);
        Assert.False(gripper.IsGripping);

        Run(world, 1.1f);
        Assert.True(wizard.GripCharacter(gripper, GripType.Hold, -1, 10));   // holding, it leaves its victim behind too
        Assert.True(Cast(wizard, MagickType.Teleport));
        Assert.False(gripper.IsGripped);
    }

    [Fact]
    public void ProppMagick_LobsABallOfPlasma()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;

        Assert.False(Cast(wizard, MagickType.ProppMagick));                         // as the original, it reports no success
        Run(world, Tick);

        var ball = Assert.Single(world.Entities.Entities.OfType<Missile>());
        Assert.True(ball.Velocity.Y > 10);
    }

    [Fact]
    public void Grease_GreasesWhoItSweeps_AndLeavesFieldsThatBurn()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var foe = Animated(world, Foe, at: wizard.Position + wizard.Body.Facing * 4);
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.Grease));
        Run(world, 0.6f);

        Assert.False(foe.Statuses.Has(StatusEffects.Greased));                       // as the original: a status of no strength does not take
        var fields = world.Entities.Entities.OfType<GreaseField>().ToList();
        Assert.InRange(fields.Count, 4, 6);
        var field = fields[0];
        field.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 10, 1), null, field.Position);
        Assert.True(field.IsBurning);
        var walker = Animated(world, Foe, at: field.Position + Vector3.UnitY * 0.9f);
        Run(world, 0.3f);
        Assert.True(walker.Body.Conditions.IsOnGrease);
        Assert.True(walker.Statuses.Has(StatusEffects.Burning));
        walker.Body.Body.Position = field.Position + new Vector3(10, 0.9f, 10);
        Run(world, 2 * Tick);
        Assert.False(walker.Body.Conditions.IsOnGrease);                             // off the field, no longer slick
        field.Damage(new DamageEntry(AttackProperties.Damage, Elements.Water, 10, 1), null, field.Position);
        Assert.False(field.IsBurning);
    }
}
