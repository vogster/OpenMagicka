using System.Numerics;
using OpenMagicka.Engine.Physics;
using OpenMagicka.Gameplay.Liquids;
using OpenMagicka.Physics;
using OpenMagicka.Physics.Characters;
using static OpenMagicka.Tests.Physics.CharacterBodyTests;

namespace OpenMagicka.Tests.Physics;

/// <summary>Water, ice and lava in the physics world (OM-88 phases 2–3): who meets which triangles, and wading.</summary>
public class LiquidPhysicsTests
{
    private const float Surface = 0.6f;

    /// <summary>A grass floor at 0 under a 20 × 20 pool whose surface is 0.6 up.</summary>
    private static (PhysicsWorld World, Liquid Pool) Pool(LiquidKind kind = LiquidKind.Water)
    {
        var world = Floor();
        Vector3[] corners = [new(-10, Surface, -10), new(10, Surface, -10), new(10, Surface, 10), new(-10, Surface, 10)];
        var pool = new Liquid(kind, corners, [0, 2, 1, 0, 3, 2], hasCollision: true, freezable: true, autoFreeze: false);
        Assert.NotNull(LevelCollision.AddLiquid(world, pool));
        return (world, pool);
    }

    [Fact]
    public void ACharacter_WadesInWater_FeelingItsDepth_AndGoesSlower()
    {
        var (world, _) = Pool();
        using var _ = world;
        var wizard = Wizard(world);
        Run(world, 0.5f, wizard);

        Assert.True(wizard.IsInWater);
        Assert.Equal(Surface, wizard.WaterDepth, 1);                         // the surface over its feet on the floor
        Assert.Equal(Liquid.WaterMaterial, wizard.LiquidMaterial);
        Assert.Equal(Liquid.WaterMaterial, wizard.GroundMaterial);           // its steps splash
        Assert.Equal(0.9f, wizard.Body.Position.Y, 1);                       // it stands on the bed, not on the water

        wizard.Movement = Vector3.UnitX;
        Run(world, 1, wizard);
        var speed = new Vector2(wizard.Body.Velocity.X, wizard.Body.Velocity.Z).Length();
        Assert.InRange(speed, 5 * CharacterBody.Wading(Surface) * 0.9f, 5 * CharacterBody.Wading(Surface));   // waded, and held back a little each tick
    }

    [Fact]
    public void AThingFallsThroughWater_ButLandsOnIce()
    {
        var (world, pool) = Pool();
        using var _ = world;
        var crate = world.AddBody(new BoxShape(new Vector3(0.5f)), new Vector3(2, 3, 2), 10);
        for (var i = 0; i < 180; i++)
        {
            world.Step(1f / 60);
        }
        Assert.Equal(0.25f, crate.Position.Y, 1);                            // on the bed

        pool.FreezeAll(2);
        var other = world.AddBody(new BoxShape(new Vector3(0.5f)), new Vector3(-2, 3, -2), 10);
        for (var i = 0; i < 180; i++)
        {
            world.Step(1f / 60);
        }
        Assert.InRange(other.Position.Y, Surface + 0.2f, Surface + 0.3f);     // on the ice
    }

    [Fact]
    public void ACharacterOnIce_Slides_AndIsNotOnSolidGround_ButCooledLavaHoldsIt()
    {
        var (world, pool) = Pool();
        using var _ = world;
        pool.FreezeAll(2);
        var wizard = Wizard(world, new Vector3(1, Surface + 0.9f, 1));
        Run(world, 0.5f, wizard);
        Assert.True(wizard.IsOnIce);
        Assert.False(wizard.IsTouchingSolidGround);
        Assert.False(wizard.IsInWater);

        var (rockWorld, rock) = Pool(LiquidKind.Lava);
        using var __ = rockWorld;
        rock.FreezeAll(2);
        var walker = Wizard(rockWorld, new Vector3(1, Surface + 0.9f, 1));
        Run(rockWorld, 0.5f, walker);
        Assert.False(walker.IsOnIce);
        Assert.True(walker.IsTouchingSolidGround);
    }

    [Fact]
    public void ASegment_PassesWater_ButMeetsIce()
    {
        var (world, pool) = Pool();
        using var _ = world;
        Assert.Equal(0, world.CastSegment(new Vector3(1, 5, 1), new Vector3(1, -1, 1))!.Value.Position.Y, 3);

        pool.FreezeAll(2);
        Assert.Equal(Surface, world.CastSegment(new Vector3(1, 5, 1), new Vector3(1, -1, 1))!.Value.Position.Y, 3);
    }

    [Fact]
    public void ASheetsSeamCorners_WeldedByTheEngine_StillTellItsIce()
    {
        using var world = Floor();
        // Two triangles that share no corner indices: 3 and 4 stand where 0 and 2 do (a drawn seam).
        Vector3[] corners = [new(-10, Surface, -10), new(10, Surface, -10), new(10, Surface, 10), new(-10, Surface, -10), new(10, Surface, 10), new(-10, Surface, 10)];
        var sheet = new Liquid(LiquidKind.Water, corners, [0, 2, 1, 3, 5, 4], true, true, false);
        LevelCollision.AddLiquid(world, sheet);
        sheet.Freeze(new Vector3(-10, 0, 10), new Vector3(0, 0, 1), MathF.Tau, 2);   // only the corner of the second triangle
        Assert.True(sheet.IsIce(1) && !sheet.IsIce(0));

        Assert.Equal(Surface, world.CastSegment(new Vector3(-5, 5, 5), new Vector3(-5, -1, 5))!.Value.Position.Y, 3);   // its ice
        Assert.Equal(0, world.CastSegment(new Vector3(5, 5, -5), new Vector3(5, -1, -5))!.Value.Position.Y, 3);          // the other still water
    }
}
