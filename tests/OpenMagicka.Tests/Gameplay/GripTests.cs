using System.Numerics;
using OpenMagicka.Gameplay.Animation.Actions;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Characters.States;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using Elements = OpenMagicka.Gameplay.Spells.Elements;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Grips (OM-89): who may be taken, how the victim is held, how it ends.</summary>
public class GripTests
{
    private static readonly CharacterTemplate Troll = Goblin with { Id = "troll", HitTolerance = 50, BreakFreeStrength = 4 };

    private static (OpenMagicka.Gameplay.GameWorld World, Character Gripper, Character Victim) Pair()
    {
        var world = World();
        var gripper = Spawn(world, Troll, new Vector3(0, 0.9f, 0));
        var victim = Spawn(world, Troll, new Vector3(1, 0.9f, 0));
        return (world, gripper, victim);
    }

    [Fact]
    public void AGrip_IsRefused_ForTheDeadTheImmortalTheEtherealTheHeavyAndTheTakenAlready()
    {
        var (world, gripper, victim) = Pair();
        using var _ = world;
        victim.MakeImmortal(1);
        Assert.False(gripper.GripCharacter(victim, GripType.Hold, -1, 10));
        Run(world, 1.1f);
        victim.IsEthereal = true;
        Assert.False(gripper.GripCharacter(victim, GripType.Hold, -1, 10));
        victim.IsEthereal = false;
        Assert.False(gripper.GripCharacter(victim, GripType.Ride, -1, 10));  // nothing to stumble with
        Assert.False(gripper.GripCharacter(gripper, GripType.Hold, -1, 10));

        Assert.True(gripper.GripCharacter(victim, GripType.Hold, -1, 0));
        Assert.Equal((Character.LeastBreakFreeTolerance, gripper, victim), (gripper.BreakFreeTolerance, victim.Gripper, gripper.GrippedCharacter));
        var other = Spawn(world, Troll, new Vector3(-1, 0.9f, 0));
        Assert.False(other.GripCharacter(victim, GripType.Hold, -1, 10)); // taken
        var heavy = Spawn(world, Troll with { Mass = 600 }, new Vector3(0, 0.9f, 3));
        Assert.False(other.GripCharacter(heavy, GripType.Hold, -1, 10));
    }

    [Fact]
    public void AHeldVictim_StandsStill_TheGripperInFrontOfIt()
    {
        var (world, gripper, victim) = Pair();
        using var _ = world;
        gripper.GripCharacter(victim, GripType.Hold, -1, 10);
        victim.Body.Movement = Vector3.UnitX;
        Run(world, 0.5f);

        Assert.InRange(victim.Position.X, 1, 1.1f);                           // it does not walk away (but the one tick before the grip took)
        Assert.Equal(2 * Troll.Radius, Vector2.Distance(new(gripper.Position.X, gripper.Position.Z), new(victim.Position.X, victim.Position.Z)), 1);
    }

    [Fact]
    public void Struggling_FreesItAtTheTolerance_ByItsWholeStrength()
    {
        var (world, gripper, victim) = Pair();
        using var _ = world;
        gripper.GripCharacter(victim, GripType.Hold, -1, 10);
        for (var i = 0; i < 2; i++)
        {
            victim.BreakFree();
            Run(world, 0.6f);                                                 // an NPC struggles every half second
        }
        Assert.Equal(8, victim.BreakFreeCounter);
        Assert.True(victim.IsGripped);

        victim.BreakFree();
        Run(world, 0.1f);
        Assert.False(victim.IsGripped);
        Assert.Equal(0, victim.BreakFreeCounter);
    }

    [Fact]
    public void TheGrip_Ends_WhenTheVictimHurtsTheGripperPastItsTolerance_BurnsOrEitherDies()
    {
        var (world, gripper, victim) = Pair();
        using var _ = world;
        gripper.GripCharacter(victim, GripType.Hold, -1, 10);
        gripper.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 60, 1), victim, victim.Position);
        Run(world, 0.1f);
        Assert.False(gripper.IsGripping);
        Assert.Equal(0, gripper.GripDamageAccumulation);

        gripper.GripCharacter(victim, GripType.Hold, -1, 10);
        victim.Damage(new DamageEntry(AttackProperties.Status, Elements.Fire, 10, 1), null, victim.Position);
        Run(world, 0.1f);
        Assert.False(gripper.IsGripping);

        using var world2 = World();
        var holder = Spawn(world2, Troll, new Vector3(0, 0.9f, 0));
        var held = Spawn(world2, Troll, new Vector3(1, 0.9f, 0));
        holder.GripCharacter(held, GripType.Hold, -1, 10);
        held.Kill();
        Run(world2, 0.1f);
        Assert.False(holder.IsGripping);
    }

    [Fact]
    public void TheGripActions_HurtTheVictim_AndThrowIt()
    {
        var (world, gripper, victim) = Pair();
        using var _ = world;
        gripper.GripCharacter(victim, GripType.Hold, -1, 10);
        new DamageGrip(0, 1, false, [new DamageEntry(AttackProperties.Damage, Elements.Earth, 20, 1)]).Run(gripper, first: true);
        Assert.True(victim.HitPoints < victim.MaxHitPoints);

        gripper.GripThrow = new Vector3(0, 5, 10);
        new ThrowGrip(0, 1).Run(gripper, first: true);
        Assert.False(gripper.IsGripping);
        Assert.True(victim.Body.Body.Velocity.Z > 5 || victim.Body.IsPushed);
    }

    [Fact]
    public void TheGripperAndItsVictim_PassThroughEachOther()
    {
        var (world, gripper, victim) = Pair();
        using var _ = world;
        gripper.GripCharacter(victim, GripType.Hold, -1, 10);

        Assert.Same(victim.Body.Body, gripper.Body.IgnoredBody);
        Assert.Same(gripper.Body.Body, victim.Body.IgnoredBody);
        gripper.ReleaseAttachedCharacter();
        Assert.Null(gripper.Body.IgnoredBody);
    }

    /// <summary>Two characters with the state machine.</summary>
    private static (OpenMagicka.Gameplay.GameWorld World, Character Gripper, Character Victim) Animated()
    {
        var world = World();
        var gripper = StateMachineTests.Animated(world, Troll, new Vector3(0, 0.9f, 0));
        var victim = StateMachineTests.Animated(world, Troll, new Vector3(1, 0.9f, 0));
        Run(world, 0.1f);
        return (world, gripper, victim);
    }

    [Theory]
    [InlineData(GripType.Hold)]
    [InlineData(GripType.Pickup)]
    public void AHeldOrPickedUpVictim_IsEntangled_TheGripperHolds_AndFreedItWalksAgain(GripType type)
    {
        var (world, gripper, victim) = Animated();
        using var _ = world;
        victim.Body.Movement = Vector3.UnitX;
        Assert.True(gripper.GripCharacter(victim, type, -1, 2));
        Run(world, 0.2f);
        Assert.Same(CharacterStates.Entangled, victim.State);
        Assert.Same(CharacterStates.Gripping, gripper.State);

        victim.BreakFree();
        Run(world, 0.1f);
        Assert.False(victim.IsGripped);
        Run(world, 2);
        Assert.NotSame(CharacterStates.Entangled, victim.State);
        Assert.True(victim.Body.AllowMove);                                   // not left frozen by the release
    }

    [Fact]
    public void AGripperKilledOrTakenOutOfTheWorld_LetsGo()
    {
        var (world, gripper, victim) = Animated();
        using var _ = world;
        gripper.GripCharacter(victim, GripType.Hold, -1, 10);
        gripper.Kill();
        Run(world, 0.1f);
        Assert.False(victim.IsGripped);

        var other = StateMachineTests.Animated(world, Troll, new Vector3(-1, 0.9f, 0));
        other.GripCharacter(victim, GripType.Hold, -1, 10);
        world.Entities.Store(keep: true);                                     // the scene is left: grips do not cross it
        Assert.False(victim.IsGripped);
        Assert.False(other.IsGripping);
    }

    [Fact]
    public void AGripperAboutToLoseItsPickup_OnTheGround_DropsIt()
    {
        var (world, gripper, victim) = Animated();
        using var _ = world;
        gripper.GripCharacter(victim, GripType.Pickup, -1, 2);
        Run(world, 0.2f);
        victim.BreakFree();
        world.Tick(1f / 60);

        Assert.Same(CharacterStates.Drop, gripper.State);
        Assert.False(gripper.IsGripping);
    }
}
