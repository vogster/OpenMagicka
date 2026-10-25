using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>A dispenser letting characters out (a level script's spawnDispenser).</summary>
public class DispenserTests
{
    [Fact]
    public void WhileActive_ItLetsOutAKindEachPeriod_AndDiesOnceSpent()
    {
        using var world = World();
        world.Entities.Bestiary = new Bestiary().Add("data/characters/spider", Goblin with { Id = "spider" });
        var eggs = new Dispenser(world.Physics, Dispensers.SpiderEggs, new Vector3(0, Dispenser.Lift, 0), Vector3.UnitX, ["data/characters/spider"], [2], 0.5f,
            active: false);
        world.Entities.Add(eggs);

        Run(world, 1);
        Assert.Empty(world.Characters);
        eggs.Activate();
        world.Tick(Tick);
        Assert.Single(world.Characters);                                                         // the first at once
        Run(world, 0.6f);
        Assert.Equal(2, world.Characters.Count());
        Assert.Equal(0, eggs.Counts[0]);
        Run(world, 0.6f);
        Assert.True(eggs.IsDead);                                                                // nothing left to let out
        Assert.Null(eggs.Manager);
    }

    [Fact]
    public void AHit_TakesItsWholeAmount()
    {
        using var world = World();
        var hole = new Dispenser(world.Physics, Dispensers.SpiderHole, new Vector3(0, Dispenser.Lift, 0), Vector3.UnitX, [], [], 1, active: false);
        world.Entities.Add(hole);

        Assert.Equal(DamageResult.Damaged, hole.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 100.7f, 1), null, Vector3.Zero));
        Assert.Equal(Dispenser.HitPointsMax - 100, hole.HitPoints);
        Assert.Equal(DamageResult.Deflected, hole.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, -5, 1), null, Vector3.Zero));
    }
}
