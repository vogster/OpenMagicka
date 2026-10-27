using System.Numerics;
using OpenMagicka.Engine.Physics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Liquids;
using static OpenMagicka.Tests.Gameplay.CharacterTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Characters in water and lava (OM-88 phase 3): wetting, burning, washing, drowning.</summary>
public class WadingTests
{
    /// <summary>A world with a pool over the floor, its surface at a height.</summary>
    private static GameWorld Pool(float surface, LiquidKind kind = LiquidKind.Water)
    {
        var world = World();
        Vector3[] corners = [new(-10, surface, -10), new(10, surface, -10), new(10, surface, 10), new(-10, surface, 10)];
        var pool = new Liquid(kind, corners, [0, 2, 1, 0, 3, 2], true, true, false);
        LevelCollision.AddLiquid(world.Physics, pool);
        world.Liquids.Add(pool);
        return world;
    }

    [Fact]
    public void Water_WetsAsMuchAsItsDepthASecond_AndPutsFireOut()
    {
        using var world = Pool(0.5f);
        var goblin = Spawn(world);
        Run(world, 1);

        Assert.True(goblin.Statuses.Has(StatusEffects.Wet));
        Assert.InRange(goblin.Statuses[StatusEffects.Wet].Magnitude, 0.4f, 0.55f);   // 0.5 a second
        goblin.Damage(new DamageEntry(AttackProperties.Status, OpenMagicka.Gameplay.Spells.Elements.Fire, 60, 1), null, goblin.Position);
        Run(world, 0.1f);
        Assert.False(goblin.Statuses.Has(StatusEffects.Burning));
    }

    [Fact]
    public void Shallows_DoNothing()
    {
        using var world = Pool(0.15f);
        var goblin = Spawn(world);
        Run(world, 1);

        Assert.True(goblin.Body.IsInWater);
        Assert.False(goblin.Statuses.Has(StatusEffects.Wet));
    }

    [Fact]
    public void Lava_Burns()
    {
        using var world = Pool(0.5f, LiquidKind.Lava);
        var goblin = Spawn(world);
        Run(world, 0.5f);

        Assert.True(goblin.Statuses.Has(StatusEffects.Burning));
        Assert.True(goblin.HitPoints < goblin.MaxHitPoints);
    }

    [Fact]
    public void DeepWater_DrownsAtOnce_ItsBodyComingUp_AndItNeverRises()
    {
        using var world = Pool(1);                                             // a goblin's middle is 0.9 up
        var goblin = Spawn(world);
        var cues = new List<AnimationCue>();
        goblin.Cue += (_, cue) => cues.Add(cue);
        Run(world, 0.1f);

        Assert.True(goblin.IsDrowning);
        Assert.True(goblin.HitPoints <= 0);
        Assert.Contains(cues, c => c is WorldEffectCue { Effect: "water_drown" });
        Assert.True(goblin.Position.Y > 1.5f);                               // up to the surface
        Run(world, 1);
        Assert.True(goblin.Position.Y > 1.5f);                               // and floating there
    }

    [Fact]
    public void AColdBlast_FreezesThePool_SoACrateStandsOnIt_AndItThawsBack()
    {
        using var world = Pool(0.5f);
        var pool = world.Liquids[0];
        OpenMagicka.Gameplay.Spells.Blasts.FreezeLiquids(world.Entities, new Vector3(0, 0, 0), new Vector3(0, 0, 20), MathF.Tau,
            [new DamageEntry(AttackProperties.Status, OpenMagicka.Gameplay.Spells.Elements.Cold, 0, 2)], 1);

        Assert.True(pool.IsIce(0) && pool.IsIce(1));
        Run(world, 20);                                                      // 2.47 drifts back below 0.5 in about 20 s
        Assert.False(pool.IsIce(0));
    }

    [Fact]
    public void ABlizzard_FreezesEveryLake_ANapalmBombMeltsIt()
    {
        using var world = Pool(0.5f);
        var pool = world.Liquids[0];
        pool.FreezeAll(OpenMagicka.Gameplay.Spells.Magicks.Weather.Magnitude * 3);
        Assert.True(pool.IsIce(0));
        OpenMagicka.Gameplay.Spells.Magicks.NapalmStrike.Explode(world.Entities, null, new Vector3(-10, 0.5f, -10));
        Assert.True(pool.FreezeValues[0] < 0);                               // melted where it fell
    }
}
