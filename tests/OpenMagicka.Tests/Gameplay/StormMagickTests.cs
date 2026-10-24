using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Magicks;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.MagickCastTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Tornado and Vortex (MAGICKS.md §5.8–5.9).</summary>
public class StormMagickTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human, MaxHitPoints = 20_000 };

    private static bool Cast(Character caster, MagickType magick) => MagickEffects.For(magick)!.Execute(caster);

    [Fact]
    public void Tornado_WandersForFifteenSeconds_FlingingWhatItTouches()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var foe = Animated(world, Foe, at: wizard.Position + wizard.Body.Facing * 4 + Vector3.Cross(wizard.Body.Facing, Vector3.UnitY) * 1.5f);
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.Tornado));
        var whirlwind = Assert.Single(world.Entities.Entities.OfType<Whirlwind>());
        var start = whirlwind.Position;
        Run(world, 0.5f);
        Assert.True(foe.Body.IsPushed || foe.Body.Body.Velocity.Length() > 1 || foe.IsKnockedDown, "flung");
        Run(world, 5);
        Assert.True(Vector3.Distance(start, whirlwind.Position) > 2, $"stayed at {whirlwind.Position}");
        Run(world, 10);

        Assert.Empty(world.Entities.Entities.OfType<Whirlwind>());
    }

    [Fact]
    public void Vortex_PullsThingsIn_AndSwallowsWhatReachesItsCore()
    {
        var (world, _, wizard) = NewGame();
        using var _ = world;
        var centre = wizard.Position + wizard.Body.Facing * 10;
        var near = Animated(world, Foe, at: centre + new Vector3(1.5f, 0, 0));
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.Vortex));
        var hole = Assert.Single(world.Entities.Entities.OfType<VortexHole>());
        Run(world, 3);

        Assert.True(near.IsDead || !world.Entities.Entities.Contains(near), $"{near.Position}");
        Assert.True(hole.Mass > 0.5f, $"{hole.Mass}");                               // it grew on the goblin
        Assert.False(wizard.IsDead);
        Run(world, 30);
        Assert.Empty(world.Entities.Entities.OfType<VortexHole>());
    }
}
