using System.Numerics;
using OpenMagicka.Gameplay.Animation.Actions;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.AnimationActionTests;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The corners of the animation actions: the original's quirks and clips left early.</summary>
public class AnimationActionEdgeTests
{
    [Fact]
    public void AJumpWithNoTarget_AimsAtAPointNearTheOrigin_AsTheOriginal()
    {
        using var world = World();
        var goblin = Acting(world, 1, new Jump(0, 0, 60, 10, 16));
        goblin.Body.Body.Position = new Vector3(5, 0.9f, 0);
        Run(world, 0.1f);

        goblin.ForceAnimation(Special);
        Run(world, 4);

        Assert.True(goblin.Position.X < 1, $"x {goblin.Position.X}");             // towards facing × 13 m taken as a point
        Assert.InRange(goblin.Position.Z, -17, -13);                               // 16 m, the range's maximum
    }

    [Fact]
    public void ADeadCharacter_DoesNotJump()
    {
        using var world = World();
        var goblin = Acting(world, 1, new Jump(0.5f, 0, 60, 10, 16));
        goblin.Target = Animated(world, at: new Vector3(0, 0.9f, -5));

        goblin.ForceAnimation(Special);
        goblin.Kill();
        Run(world, 1);

        Assert.False(goblin.Body.IsJumping);
        Assert.Equal(0, goblin.Position.Z, 1);
    }

    [Fact]
    public void Move_TurnsTheVelocityFromTheCharacterFrame()
    {
        using var world = World();
        var goblin = Acting(world, 1, new Move(0, 1, new Vector3(5, 0, 0)));

        goblin.ForceAnimation(Special);
        Run(world, 0.5f);

        Assert.True(goblin.Position.X > 1, $"x {goblin.Position.X}");             // facing −Z, +X is its right
        Assert.Equal(0, goblin.Position.Z, 1);
    }

    [Fact]
    public void AnImmortalThatCollides_StaysSolid()
    {
        using var world = World();
        var goblin = Acting(world, 1, new Immortal(0, 0.5f, true));

        goblin.ForceAnimation(Special);
        Run(world, 0.2f);

        Assert.True(goblin.IsImmortal);
        Assert.False(goblin.IsIntangible);
    }

    [Fact]
    public void LeavingTheClipEarly_UndoesTheWindows()
    {
        using var world = World();
        var goblin = Acting(world, 2, new Crouch(0, 1, 0.5f, 0), new Invisible(0, 1, false), new Immortal(0, 1, true));

        goblin.ForceAnimation(Special);
        Run(world, 0.3f);
        Assert.True(goblin.IsInvisible && goblin.IsImmortal);
        goblin.ForceAnimation("idle");                                             // the window never reached its end

        Assert.Equal(0.4f, goblin.Body.Shape.Radius);
        Assert.False(goblin.IsInvisible);
        Assert.False(goblin.IsImmortal);
        goblin.ForceAnimation("idle_agg");                                         // a second stop is harmless
        Assert.Equal(0.4f, goblin.Body.Shape.Radius);
    }

    [Fact]
    public void AKill_EndsInvisibility_AndAnUnmatchedStopIsHarmless()
    {
        using var world = World();
        var goblin = Acting(world, 1);
        goblin.SetInvisible(10);

        goblin.Kill();

        Assert.False(goblin.IsInvisible);
        new Crouch(0, 1, 0.5f, 0).Stop(goblin);                                    // a stop for a run that never was
        Assert.Equal(0.4f, goblin.Body.Shape.Radius);
    }

    [Fact]
    public void BecomingSolid_KeepsTheStatuses_AndFadesIn()
    {
        using var world = World();
        var ghost = Acting(world, 1, new Ethereal(0, 0, false, 1, 2));
        ghost.Ethereal(true, 0.2f, 100);
        Run(world, 0.1f);
        ghost.Damage(new DamageEntry(AttackProperties.Status, Elements.Water, 0, 1), null, Vector3.Zero);   // ethereal: no statuses take

        ghost.ForceAnimation(Special);
        Run(world, Tick * 2);
        Assert.False(ghost.IsEthereal);
        ghost.Damage(new DamageEntry(AttackProperties.Status, Elements.Water, 0, 1), null, Vector3.Zero);
        Assert.True(ghost.Statuses.Has(StatusEffects.Wet));

        Run(world, 3);
        Assert.Equal(1, ghost.EtherealAlpha, 2);
    }
}
