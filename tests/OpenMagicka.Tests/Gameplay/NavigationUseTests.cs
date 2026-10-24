using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Navigation;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Magicks;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.MagickCastTests;
using static OpenMagicka.Tests.Gameplay.NavMeshTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>What uses the level's nav mesh: magicks' spots, the whirlwind's walk, flyers' height (ENTITIES_AI.md §11, MAGICKS.md).</summary>
public class NavigationUseTests
{
    private static bool Cast(Character caster, MagickType magick) => MagickEffects.For(magick)!.Execute(caster);

    /// <summary>A game whose walkable ground is the strip x 0..4 (the floor is wider), the wizard on it facing -z.</summary>
    private static (GameWorld World, Character Wizard) Strip()
    {
        var (world, _, wizard) = NewGame();
        world.Entities.NavMesh = new NavMesh(Grid(4, 40, origin: new Vector3(0, 0, -20)));
        wizard.Body.Body.Position = new Vector3(2, wizard.Position.Y, 0);
        Run(world, Tick);
        return (world, wizard);
    }

    [Fact]
    public void Walkable_IsTheNearestNavPoint_OrThePointWithoutAMesh()
    {
        var (world, _) = Strip();
        using var _ = world;

        Assert.Equal(new Vector3(4, 0, 3), world.Entities.Walkable(new Vector3(9, 2, 3)));
        world.Entities.NavMesh = null;
        Assert.Equal(new Vector3(9, 2, 3), world.Entities.Walkable(new Vector3(9, 2, 3)));
    }

    [Fact]
    public void Teleport_LandsOnTheNavMesh()
    {
        var (world, wizard) = Strip();
        using var _ = world;
        wizard.Body.Face(Vector3.UnitX);
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.Teleport));

        Assert.InRange(wizard.Position.X, 3.5f, 4.5f);                                  // 10 ahead is off the strip: its edge
    }

    [Fact]
    public void APortal_NeedsTheNavMeshWithinFive()
    {
        var (world, wizard) = Strip();
        using var _ = world;
        wizard.Body.Face(Vector3.UnitX);
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.Portal));
        Assert.Equal(4, PortalGate.Of(world.Entities)[0].Position.X, 3);              // 2 ahead is off the strip: the edge

        wizard.Body.Body.Position = new Vector3(12, wizard.Position.Y, 0);
        Run(world, Tick);
        Assert.False(Cast(wizard, MagickType.Portal));                                 // 14 from the strip
    }

    [Fact]
    public void TheWhirlwind_WalksTheNavMesh()
    {
        var (world, wizard) = Strip();
        using var _ = world;
        wizard.Body.Face(Vector3.UnitX);
        Run(world, Tick);

        Assert.True(Cast(wizard, MagickType.Tornado));
        var whirlwind = Assert.Single(world.Entities.Entities.OfType<Whirlwind>());
        Assert.Equal(4, whirlwind.Position.X, 3);
        var start = whirlwind.Position;
        for (var t = 0f; t < 12; t += Tick)
        {
            world.Tick(Tick);
            Assert.InRange(whirlwind.Position.X, -0.3f, 4.3f);
        }
        Assert.True(Vector3.Distance(start, whirlwind.Position) > 1, "it went somewhere");
    }

    [Fact]
    public void Death_ComesFromTheNavMesh()
    {
        var (world, wizard) = Strip();
        using var _ = world;

        Assert.True(Cast(wizard, MagickType.SDeath));

        var reaper = Assert.Single(world.Entities.Entities.OfType<Reaper>());
        Assert.InRange(reaper.Position.X, -0.01f, 4.01f);
        Assert.True(Vector3.Distance(reaper.Position with { Y = 0 }, wizard.Position with { Y = 0 }) >= 2);
    }

    [Fact]
    public void AFlyer_HoversTwoAboveTheNavMesh()
    {
        var (world, _) = Strip();
        using var _ = world;
        world.Entities.NavMesh = new NavMesh(Grid(4, 40, origin: new Vector3(0, 3, -20)));        // a nav mesh 3 up (a bridge)
        var bat = Animated(world, Goblin with { Flies = true }, at: new Vector3(2, 1, 5));

        Run(world, 6);

        Assert.InRange(bat.Body.Body.Position.Y, 4.5f, 5.5f);
    }
}
