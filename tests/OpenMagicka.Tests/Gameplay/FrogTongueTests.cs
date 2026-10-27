using System.Numerics;
using OpenMagicka.Gameplay.Animation.Actions;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Elements = OpenMagicka.Gameplay.Spells.Elements;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>A frog's tongue (OM-89): out to its length or a catch, and back to the mouth with what it caught.</summary>
public class FrogTongueTests
{
    private static (OpenMagicka.Gameplay.GameWorld World, Character Frog) Frog()
    {
        var world = World();
        var frog = Spawn(world, Goblin with { Id = "swampfrog" }, new Vector3(0, 0.9f, 0));
        frog.Body.Face(Vector3.UnitX);
        return (world, frog);
    }

    private static FrogTongue Shoot(Character frog, float length)
    {
        new Tongue(0, 1, length).Run(frog, first: true);
        return Assert.Single(frog.Manager!.Entities.OfType<FrogTongue>());
    }

    [Fact]
    public void ATongue_GoesOutToItsLength_AlongTheMouth_ThenComesBackAndDies()
    {
        var (world, frog) = Frog();
        using var _ = world;
        var tongue = Shoot(frog, 6);
        Assert.Equal(FrogTongue.ShootSpeed, tongue.Body.Velocity.X, 1);

        Run(world, 0.15f);
        Assert.True(tongue.PullingBack);
        Assert.Equal(FrogTongue.ReturnSpeed, tongue.Speed);
        Assert.InRange(tongue.End.X, 5.5f, 9);

        Run(world, 0.3f);
        Assert.Empty(world.Entities.Entities.OfType<FrogTongue>());
    }

    [Fact]
    public void ACaughtCharacter_IsDrawnBackToTheFrog_KeepingItsState()
    {
        var (world, frog) = Frog();
        using var _ = world;
        var fly = Spawn(world, Goblin, new Vector3(4, 0.9f, 0));
        var tongue = Shoot(frog, 10);

        Run(world, 0.15f);
        Assert.Same(fly, tongue.Catch);
        Assert.Equal(FrogTongue.DragSpeed, tongue.Speed);
        Run(world, 0.5f);

        Assert.Empty(world.Entities.Entities.OfType<FrogTongue>());
        Assert.InRange(fly.Position.X, 0, 2.5f);
        Assert.False(fly.IsGripped);
    }

    [Fact]
    public void ABlow_OrAGrip_MakesItLetGo_ForGood()
    {
        var (world, frog) = Frog();
        using var _ = world;
        var fly = Spawn(world, Goblin, new Vector3(8, 0.9f, 0));
        var tongue = Shoot(frog, 20);
        Run(world, 0.2f);
        Assert.Same(fly, tongue.Catch);
        Assert.False(tongue.IsDead);

        tongue.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 10, 1), null, tongue.Position);
        var at = fly.Position.X;
        Run(world, Tick * 3);
        Assert.Null(tongue.Catch);                                            // ours: not caught again off the tip
        Assert.Equal(FrogTongue.ReturnSpeed, tongue.Speed);
        Assert.InRange(fly.Position.X, at - 0.2f, at + 0.2f);

        var second = Spawn(world, Goblin, new Vector3(0, 0.9f, 8));
        frog.Body.Face(Vector3.UnitZ);
        Run(world, 0.5f);
        var next = Shoot(frog, 20);
        Run(world, 0.2f);
        Assert.Same(second, next.Catch);
        var gripper = Spawn(world, Goblin with { Id = "troll" }, new Vector3(-3, 0.9f, 7));
        Assert.True(gripper.GripCharacter(second, GripType.Hold, -1, 10));
        Run(world, Tick * 3);
        Assert.False(next.IsDead);
        Assert.Null(next.Catch);
    }

    [Fact]
    public void TheLevel_TurnsItBack_InADust_AndItGoesWithItsDeadFrog()
    {
        var (world, frog) = Frog();
        using var _ = world;
        world.Physics.AddStaticMesh(2, [new(3, 0, -5), new(3, 0, 5), new(3, 5, 5), new(3, 5, -5)],
            [new OpenMagicka.Physics.MeshTriangle(0, 1, 2), new OpenMagicka.Physics.MeshTriangle(0, 2, 3)]);
        var cues = new List<OpenMagicka.Gameplay.Animation.AnimationCue>();
        world.Entities.Cue += (_, cue) => cues.Add(cue);
        var tongue = Shoot(frog, 20);
        Run(world, 0.1f);

        Assert.True(tongue.PullingBack);
        Assert.Equal(FrogTongue.ReturnSpeed, tongue.Speed);
        Assert.Contains(cues, c => c is OpenMagicka.Gameplay.Animation.WorldEffectCue { Effect: FrogTongue.HitEffect });
        Assert.True(tongue.End.X < 3);

        var again = Shoot2(frog, 20, world);
        frog.Kill();
        Run(world, Tick * 2);
        Assert.True(again.IsDead);
    }

    private static FrogTongue Shoot2(Character frog, float length, OpenMagicka.Gameplay.GameWorld world)
    {
        new Tongue(0, 1, length).Run(frog, first: true);
        return world.Entities.Entities.OfType<FrogTongue>().Last();
    }
}
