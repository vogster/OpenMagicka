using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;
using Barrier = OpenMagicka.Gameplay.Spells.Effects.Barrier;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Barriers of Shield spells (SPELLS.md §5.6).</summary>
public class BarrierTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    private static List<Barrier> Raised(GameWorld world) => [.. world.Entities.Entities.OfType<Barrier>()];

    private static void Cast(Character caster, Elements elements, CastType cast)
    {
        var spell = Spell.Of(elements);
        ShieldLayouts.RaiseBarriers(caster, spell, cast, Assert.IsType<Barriers>(ShieldRules.For(spell, cast)));
    }

    private static Barrier Raise(GameWorld world, Elements elements, Vector3 ground, BarrierHits? hits = null, Character? owner = null)
    {
        var spell = Spell.Of(elements);
        return Barrier.Raise(world.Entities, world.Physics, owner, ground, Vector3.UnitX, 1, spell,
            SpellDamage.For(spell, SpellType.Shield, CastType.Area), hits ?? new BarrierHits(), default);
    }

    private static DamageEntry Blow(Elements element, float amount) => new(AttackProperties.Damage, element, amount, 1);

    [Fact]
    public void AnAreaCast_RingsTheCaster_WithStoneThatWearsAway()
    {
        using var world = World();
        var caster = Animated(world);
        Run(world, 0.05f);

        Cast(caster, Elements.Shield | Elements.Earth, CastType.Area);
        Run(world, 1);

        var barriers = Raised(world);
        Assert.Equal(10, barriers.Count);                                            // a ring of radius 4, 2.5 apart
        Assert.All(barriers, b => Assert.InRange(Vector3.Distance(b.Position with { Y = 0 }, caster.Position with { Y = 0 }), 3.9f, 4.1f));
        Assert.All(barriers, b => Assert.True(b.IsSolid));
        Assert.InRange(barriers[0].HitPoints, 940, 960);                            // (1 + E) × 500, 50 a second
    }

    [Fact]
    public void AForceCast_RaisesAnArcOfFireWalls_ThatSetTouchersAlight()
    {
        using var world = World();
        var caster = Animated(world);
        var foe = Animated(world, Foe, at: new Vector3(0, 0.9f, -4));
        Run(world, 0.05f);

        Cast(caster, Elements.Shield | Elements.Fire, CastType.Force);
        Run(world, 0.5f);

        var walls = Raised(world);
        Assert.Equal(6, walls.Count);
        Assert.All(walls, w => Assert.False(w.IsSolid));
        Assert.True(foe.Statuses.Has(StatusEffects.Burning));
        Assert.Contains(foe, walls[0].Hits.Struck);
    }

    [Fact]
    public void AStoneBarrier_StandsInTheWay()
    {
        using var world = World();
        Raise(world, Elements.Shield | Elements.Earth, Vector3.Zero);
        var walker = Animated(world, Foe, at: new Vector3(5, 0.9f, 0));
        Run(world, 0.05f);

        walker.Body.Movement = -Vector3.UnitX;
        Run(world, 2);

        Assert.True(walker.Position.X > 1, $"walked through, at {walker.Position}");
    }

    [Fact]
    public void IceArmour_TakesBlows_ButFireGoesThroughWhole()
    {
        using var world = World();
        var ice = Raise(world, Elements.Shield | Elements.Ice, Vector3.Zero);
        var before = ice.HitPoints;

        Assert.Equal(DamageResult.Deflected, ice.Damage(Blow(Elements.Earth, 40), null, Vector3.Zero));
        ice.Damage(Blow(Elements.Earth, 100), null, Vector3.Zero);
        ice.Damage(Blow(Elements.Fire, 100), null, Vector3.Zero);

        Assert.Equal(before - 50 - 100, ice.HitPoints);
    }

    [Fact]
    public void PureIce_MeltsAfterItsMagnitudeInSeconds()
    {
        using var world = World();
        var ice = Raise(world, Elements.Shield | Elements.Ice, Vector3.Zero);

        Run(world, 0.9f);
        Assert.False(ice.IsDead);
        Run(world, 0.2f);

        Assert.True(ice.IsDead);
    }

    [Fact]
    public void ItsOpposite_PutsOutABarriersElement_WithoutHarm()
    {
        using var world = World();
        var geyser = Raise(world, Elements.Shield | Elements.Earth | Elements.Water, Vector3.Zero);
        Run(world, Tick);
        Assert.True(geyser.IsStriking);
        var before = geyser.HitPoints;

        geyser.Damage(Blow(Elements.Fire, 100), null, Vector3.Zero);

        Assert.Equal(before, geyser.HitPoints);
        Assert.False(geyser.IsStriking);
    }

    [Fact]
    public void ATesla_StrikesForItsLightning_AndMeltsWithIt()
    {
        using var world = World();
        var tesla = Raise(world, Elements.Shield | Elements.Ice | Elements.Lightning, Vector3.Zero);

        Run(world, 0.9f);
        Assert.True(tesla.IsStriking);
        Run(world, 0.2f);

        Assert.True(tesla.IsDead);                                                   // Z = 1 s
    }

    [Fact]
    public void TheHitList_KeepsTime_AfterItsFirstBarrierBreaks()
    {
        using var world = World();
        var hits = new BarrierHits();
        var first = Raise(world, Elements.Shield | Elements.Earth, new Vector3(-10, 0, 0), hits);
        Raise(world, Elements.Shield | Elements.Earth, new Vector3(10, 0, 0), hits);
        var foe = Animated(world, Foe, at: new Vector3(-10, 0.9f, 2));
        Run(world, Tick);
        Raise(world, Elements.Shield | Elements.Earth, new Vector3(-10, 0, 2.5f), hits);
        Assert.Contains(foe, hits.Struck);

        first.Kill();
        Run(world, BarrierHits.Memory + 0.05f);

        Assert.DoesNotContain(foe, hits.Struck);
    }

    [Fact]
    public void AnElementalWall_FallsOnlyToItsOpposites()
    {
        using var world = World();
        var wall = Raise(world, Elements.Shield | Elements.Fire, Vector3.Zero);
        var before = wall.HitPoints;

        wall.Damage(Blow(Elements.Earth, 100), null, Vector3.Zero);
        Assert.Equal(before, wall.HitPoints);
        wall.Damage(Blow(Elements.Water, 10), null, Vector3.Zero);

        Assert.Equal(before - 50, wall.HitPoints);                                  // the whole share, five times
    }

    [Fact]
    public void AnArcaneStone_BlastsAsItBreaks()
    {
        using var world = World();
        var stone = Raise(world, Elements.Shield | Elements.Earth | Elements.Arcane, Vector3.Zero);
        var foe = Animated(world, Foe, at: new Vector3(3.5f, 0.9f, 0));
        Run(world, Tick);

        stone.Kill();
        Run(world, Tick);

        Assert.True(foe.HitPoints < 300, $"{foe.HitPoints}");
        Assert.DoesNotContain(stone, world.Entities.Entities);
    }

    [Fact]
    public void ARisingStone_StrikesWhoStandsThere_ButNotItsOwner()
    {
        using var world = World();
        var owner = Animated(world, at: new Vector3(1, 0.9f, 0));
        var foe = Animated(world, Foe, at: new Vector3(-1, 0.9f, 0));
        Run(world, Tick);

        var stone = Raise(world, Elements.Shield | Elements.Earth, Vector3.Zero, owner: owner);

        Assert.Contains(foe, stone.Hits.Struck);
        Assert.DoesNotContain(owner, stone.Hits.Struck);
    }

    [Fact]
    public void ANewBarrier_BreaksAnotherCastsOnItsSpot_ButNotItsOwnCasts()
    {
        using var world = World();
        var hits = new BarrierHits();
        var old = Raise(world, Elements.Shield | Elements.Earth, Vector3.Zero);
        var kin = Raise(world, Elements.Shield | Elements.Earth, new Vector3(10, 0, 0), hits);
        Run(world, Tick);

        Raise(world, Elements.Shield | Elements.Earth, new Vector3(0.5f, 0, 0));
        Raise(world, Elements.Shield | Elements.Earth, new Vector3(10.5f, 0, 0), hits);

        Assert.True(old.IsDead);
        Assert.False(kin.IsDead);
    }

    [Fact]
    public void AWeaponLine_StopsAtAShield()
    {
        using var world = World();
        var caster = Animated(world);
        Run(world, 0.05f);
        var facing = caster.Body.Facing;
        world.Entities.Add(new SpellShield(world.Physics, null, ShieldShape.Wall, caster.Position + facing * 7, new Vector3(facing.Z, 0, -facing.X), ShieldRules.ShieldRadius, 5000));
        Run(world, Tick);

        Cast(caster, Elements.Shield | Elements.Earth, CastType.Weapon);
        Run(world, 1);

        Assert.Equal(2, Raised(world).Count);                                      // three without the shield
    }
}
