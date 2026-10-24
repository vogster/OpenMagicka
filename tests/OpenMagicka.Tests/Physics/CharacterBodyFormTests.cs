using System.Numerics;
using OpenMagicka.Physics;
using OpenMagicka.Physics.Characters;
using static OpenMagicka.Tests.Physics.CharacterBodyTests;

namespace OpenMagicka.Tests.Physics;

/// <summary>A character body changing its capsule, and passing through what is not the level.</summary>
public class CharacterBodyFormTests
{
    [Fact]
    public void Reshaping_KeepsTheFeetOnTheGround()
    {
        using var world = Floor();
        var wizard = Wizard(world);
        Run(world, 0.2f, wizard);
        var feet = wizard.Body.Position.Y + wizard.HeightOffset;

        wizard.Reshape(radius: 0.5f, length: 0.01f);
        Run(world, 0.5f, wizard);

        Assert.Equal((0.5f, 0.01f), (wizard.Shape.Radius, wizard.Shape.Length));
        Assert.Equal(75f, wizard.Body.Mass, 3);
        Assert.Equal(feet, wizard.Body.Position.Y + wizard.HeightOffset, 2);
        Assert.Equal(0.505f, wizard.Body.Position.Y, 2);
        Assert.True(wizard.IsTouchingGround);
        Assert.IsType<CapsuleShape>(wizard.Body.Shape);
        Assert.Equal(0.5f, ((CapsuleShape)wizard.Body.Shape).Radius);
    }

    [Fact]
    public void Reshaping_RejectsABadCapsule()
    {
        using var world = Floor();
        var wizard = Wizard(world);

        Assert.Throws<ArgumentOutOfRangeException>(() => wizard.Reshape(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => wizard.Reshape(0.4f, float.NaN));
        Assert.Equal(0.4f, wizard.Shape.Radius);
    }

    [Fact]
    public void AnIntangibleCharacter_WalksThroughOthers_ButNotThroughTheFloor()
    {
        using var world = Floor();
        var ghost = Wizard(world, new Vector3(0, 0.9f, 0));
        var wizard = Wizard(world, new Vector3(2, 0.9f, 0));
        var crate = world.AddBody(new BoxShape(new Vector3(1, 1, 1)), new Vector3(5, 0.5f, 0), 100);
        ghost.Movement = Vector3.UnitX;
        var intangible = CharacterConditions.Default with { IsIntangible = true };

        Run(world, 2.5f, (ghost, intangible), (wizard, CharacterConditions.Default));

        Assert.True(ghost.Body.Position.X > 6, $"x {ghost.Body.Position.X}");
        Assert.Equal(2, wizard.Body.Position.X, 1);                             // not shoved
        Assert.Equal(5, crate.Position.X, 1);
        Assert.Equal(0.9f, ghost.Body.Position.Y, 1);
    }

    [Fact]
    public void ACharacter_IsStoppedByAnIntangibleOneNeitherWay()
    {
        using var world = Floor();
        var wizard = Wizard(world, new Vector3(0, 0.9f, 0));
        var ghost = Wizard(world, new Vector3(2, 0.9f, 0));
        wizard.Movement = Vector3.UnitX;

        Run(world, 2, (wizard, CharacterConditions.Default), (ghost, CharacterConditions.Default with { IsIntangible = true }));

        Assert.True(wizard.Body.Position.X > 4, $"x {wizard.Body.Position.X}");
        Assert.Equal(2, ghost.Body.Position.X, 1);
    }
}
