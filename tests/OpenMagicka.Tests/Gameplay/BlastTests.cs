using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Physics;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Blasts and arc strikes in the world (SPELLS.md §5.8).</summary>
public class BlastTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    private static DamageEntry Hurt(float amount, Elements element = Elements.Earth) => new(AttackProperties.Damage, element, amount, 1);

    private static void Wall(GameWorld world, float z)
    {
        Vector3[] wall = [new(-5, 0, z), new(5, 0, z), new(5, 4, z), new(-5, 4, z)];
        world.Physics.AddStaticMesh(1, wall, [new MeshTriangle(0, 1, 2), new MeshTriangle(0, 2, 3), new MeshTriangle(0, 2, 1), new MeshTriangle(0, 3, 2)]);
    }

    [Fact]
    public void AFullBlast_FallsOffWithTheCubeOfTheDistance_TheRadiusTakenTwice()
    {
        using var world = World();
        var near = Animated(world, Foe, at: new Vector3(1, 0.9f, 0));
        var far = Animated(world, Foe, at: new Vector3(4, 0.9f, 0));
        var outside = Animated(world, Foe, at: new Vector3(7, 0.9f, 0));
        Run(world, 0.05f);

        Blasts.Full(world.Entities, world.Physics, null, null, 5, new Vector3(0, 0.9f, 0), [Hurt(100)]);

        Assert.True(300 - near.HitPoints > 95, $"near {near.HitPoints}");             // 1 − 0.2³: almost all
        var share = 1 - MathF.Pow((4 - 2 * Goblin.Radius) / 5, 3);
        Assert.InRange(300 - far.HitPoints, 100 * share - 3, 100 * share + 3);
        Assert.Equal(300f, outside.HitPoints);
    }

    [Fact]
    public void AFullBlast_SparesWhatTheLevelHides_TheEthereal_AndTheIgnored()
    {
        using var world = World();
        Wall(world, -2);
        var hidden = Animated(world, Foe, at: new Vector3(0, 0.9f, -3));
        var ghost = Animated(world, Foe, at: new Vector3(2, 0.9f, 0));
        var caster = Animated(world, at: new Vector3(0, 0.9f, 0));
        ghost.Ethereal(true, 0.5f, 0);
        Run(world, 0.05f);

        Blasts.Full(world.Entities, world.Physics, caster, caster, 6, caster.Position, [Hurt(100)]);

        Assert.Equal((300f, 300f, 300f), (hidden.HitPoints, ghost.HitPoints, caster.HitPoints));
    }

    [Fact]
    public void ArcaneAndGround_HaveNoFalloff_AndAGroundBlastSparesTheLevitating()
    {
        using var world = World();
        var standing = Animated(world, Foe, at: new Vector3(3.5f, 0.9f, 0));
        var floating = Animated(world, Foe, at: new Vector3(-3.5f, 0.9f, 0));
        Run(world, 0.05f);
        floating.Body.Conditions = floating.Body.Conditions with { IsLevitating = true };

        Blasts.Ground(world.Entities, world.Physics, null, null, 5, Vector3.UnitY * 0.9f, [Hurt(50)]);
        Assert.Equal((250f, 300f), (standing.HitPoints, floating.HitPoints));
        Blasts.Full(world.Entities, world.Physics, null, null, 5, Vector3.UnitY * 0.9f, [Hurt(50, Elements.Arcane)]);

        Assert.InRange(standing.HitPoints, 199, 201);                               // all of it, so far out
    }

    [Fact]
    public void ABlast_SignalsEachElement_AndShakesTheCamera_CutWhereTheLevelIs()
    {
        using var world = World();
        Wall(world, -2);
        var caster = Animated(world);
        var cues = new List<AnimationCue>();
        world.Entities.Cue += (_, cue) => cues.Add(cue);
        Run(world, 0.05f);

        Blasts.Full(world.Entities, world.Physics, caster, caster, 6, caster.Position, [Hurt(10, Elements.Fire), Hurt(10, Elements.Water)]);

        var blasts = cues.OfType<BlastCue>().ToList();
        Assert.Equal([Elements.Fire, Elements.Water], blasts.Select(b => b.Element));
        Assert.Equal(1f, blasts[0].Reach[0]);                                        // east is open
        Assert.InRange(blasts[0].Reach[2], 0.3f, 0.36f);                             // north (−Z) meets the wall at 2 of 6
        Assert.Single(cues.OfType<CameraShakeCue>(), s => s.Magnitude == 2 && s.Duration == BlastRules.FullShakeDuration);
    }

    [Fact]
    public void ABlastWithNoOwner_SignalsFromItsSource()
    {
        using var world = World();
        var barrel = new Thing(new Vector3(0, 0.5f, 0));
        world.Entities.Add(barrel);
        var cues = new List<(OpenMagicka.Gameplay.Entities.Entity, AnimationCue)>();
        world.Entities.Cue += (source, cue) => cues.Add((source, cue));

        Blasts.Full(world.Entities, world.Physics, null, barrel, 3, barrel.Position, [Hurt(10, Elements.Fire)], source: barrel);

        Assert.Contains(cues, c => ReferenceEquals(c.Item1, barrel) && c.Item2 is BlastCue);
    }

    [Fact]
    public void AnArcStrike_FallsOffWithDistance_WithinItsSpread()
    {
        using var world = World();
        var ahead = Animated(world, Foe, at: new Vector3(0, 0.9f, -3));
        var behind = Animated(world, Foe, at: new Vector3(0, 0.9f, 3));
        Run(world, 0.05f);

        Blasts.Arc(world.Entities, null, null, new Vector3(0, 0.9f, 0), -Vector3.UnitZ, 6, MathF.PI / 4, [Hurt(100)]);

        var share = 1 - (3 - Goblin.Radius) / 6;
        Assert.InRange(300 - ahead.HitPoints, 100 * share - 2, 100 * share + 2);
        Assert.Equal(300f, behind.HitPoints);
    }
}
