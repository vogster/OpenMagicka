using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Characters.States;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Magicks;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.MagickCastTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Haste, Invisibility, Levitate, Teleport and Revive (MAGICKS.md §3).</summary>
public class SelfMagickTests
{
    private static bool Cast(Character caster, MagickType magick) => MagickEffects.For(magick)!.Execute(caster);

    [Fact]
    public void Haste_DoublesTheSpeed_FadingAway_AndARecastStartsItOver()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;

        Cast(wizard, MagickType.Haste);
        Run(world, Tick);
        Assert.InRange(wizard.Body.SpeedMultiplier, 1.99f, 2.001f);
        Run(world, 9.5f);
        Assert.InRange(wizard.Body.SpeedMultiplier, 1.2f, 1.35f);                 // 2 − 0.5^0.5
        Cast(wizard, MagickType.Haste);
        Run(world, Tick);
        Assert.True(wizard.Body.SpeedMultiplier > 1.99f);
        Run(world, 10.1f);

        Assert.False(wizard.IsHasted);
        Assert.Equal(1f, wizard.Body.SpeedMultiplier);
    }

    [Fact]
    public void Invisibility_HidesTheCaster()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;

        Assert.True(Cast(wizard, MagickType.Invisibility));

        Assert.True(wizard.IsInvisible);
    }

    [Fact]
    public void Levitate_HoldsTheCasterOverTheGround_ForThirtySeconds()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var standing = wizard.Position.Y;

        Cast(wizard, MagickType.Levitate);
        Run(world, 3);
        Assert.True(wizard.IsLevitating);
        Assert.True(wizard.Body.Conditions.IsLevitating);
        Assert.InRange(wizard.Position.Y, standing - 0.05f, standing + 1);
        Run(world, 27.1f);

        Assert.False(wizard.IsLevitating);
        Assert.True(wizard.Body.Body.AffectedByGravity);
    }

    [Fact]
    public void Death_EndsLevitation()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        Cast(wizard, MagickType.Levitate);
        Run(world, 0.5f);

        wizard.Kill();
        Run(world, 1.5f);

        Assert.False(wizard.IsLevitating);
        Assert.True(wizard.Body.Body.AffectedByGravity);
    }

    [Fact]
    public void Teleport_PutsTheCasterTenAhead_OnTheFloor_OrFailsOverNothing()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var start = wizard.Position;
        var facing = wizard.Body.Facing;

        Assert.True(Cast(wizard, MagickType.Teleport));
        Run(world, 0.2f);
        Assert.True(Vector3.Distance(wizard.Position with { Y = 0 }, (start + facing * 10) with { Y = 0 }) < 0.2f, $"{wizard.Position}");
        Assert.Equal(start.Y, wizard.Position.Y, 1);

        wizard.Body.Body.Position = new Vector3(0, 0.9f, 0) - facing * -45;     // at the floor's edge, facing out
        Run(world, Tick);
        Assert.False(Cast(wizard, MagickType.Teleport));
    }

    [Fact]
    public void Revive_RaisesThePlayerDeadLongest_AheadOfTheCaster()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var fallen = world.Session!.AddPlayer();
        var corpse = Animated(world, at: new Vector3(10, 0.9f, 0), clips: Clips());
        (corpse.IsPlayer, corpse.Player, fallen.Avatar) = (true, fallen, corpse);
        corpse.Kill();
        Run(world, 2);
        Assert.True(corpse.IsDead && fallen.DeadAge > 0);

        Assert.True(Cast(wizard, MagickType.Revive));
        Assert.True(fallen.Ressing);
        Assert.False(Cast(wizard, MagickType.Revive));                              // already on its way
        Run(world, 0.3f);

        var risen = Assert.IsType<Character>(fallen.Avatar);
        Assert.NotSame(corpse, risen);
        Assert.False(risen.IsDead);
        Assert.Equal(risen.MaxHitPoints * 0.1f, risen.HitPoints, 1);
        Assert.True(Vector3.Distance(risen.Position with { Y = 0 }, (wizard.Position + wizard.Body.Facing * 4) with { Y = 0 }) < 0.3f);
        Assert.Same(fallen, risen.Player);
        Assert.Same(CharacterStates.Ressurection, risen.State);
        Assert.DoesNotContain(corpse, world.Entities.Entities);
        Assert.False(fallen.Ressing);
    }
}
