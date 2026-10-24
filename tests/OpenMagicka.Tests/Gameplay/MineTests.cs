using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;
using Actions = OpenMagicka.Gameplay.Animation.Actions;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Mines of Shield spells with Arcane or Life (SPELLS.md §5.6).</summary>
public class MineTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    private static readonly Spell ArcaneMine = Spell.Of(Elements.Shield | Elements.Arcane);

    private static List<SpellMine> Mines(GameWorld world) => [.. world.Entities.Entities.OfType<SpellMine>()];

    private static Character Caster(GameWorld world, Vector3? at = null)
    {
        var caster = Animated(world, at: at);
        caster.Conjure(Elements.Shield);
        caster.Conjure(Elements.Arcane);
        Run(world, 0.05f);
        return caster;
    }

    private static SpellMine Mine(GameWorld world, Character? owner, Vector3 ground) =>
        SpellMine.Lay(world.Entities, world.Physics, owner, ground, Vector3.UnitX, ArcaneMine, default);

    [Fact]
    public void AForceCast_LaysAnArcOfFourAhead()
    {
        using var world = World();
        var caster = Caster(world);
        caster.CastType = CastType.Force;
        caster.CastSpell(fromStaff: true);
        Assert.Equal(2, Mines(world).Count);                                         // one each side first

        Run(world, 0.5f);

        var mines = Mines(world);
        Assert.Equal(4, mines.Count);
        var centre = caster.Position - caster.Body.Facing;
        Assert.All(mines, m => Assert.InRange(Vector3.Distance(m.Position with { Y = 0 }, centre with { Y = 0 }), 3.9f, 4.1f));
        Assert.All(mines, m => Assert.True(Vector3.Dot(m.Position - caster.Position, caster.Body.Facing) > 0, $"{m.Position} is behind"));
        Assert.All(mines, m => Assert.Equal(SpellMine.Lift, m.Position.Y, 2));
    }

    [Fact]
    public void AnAreaCast_RingsTheCasterWithTwelve()
    {
        using var world = World();
        var caster = Caster(world);
        caster.CastType = CastType.Area;
        caster.CastSpell(fromStaff: true);

        Run(world, 1);

        var mines = Mines(world);
        Assert.Equal(12, mines.Count);
        Assert.All(mines, m => Assert.InRange(Vector3.Distance(m.Position with { Y = 0 }, caster.Position with { Y = 0 }), 3.9f, 4.1f));
    }

    [Fact]
    public void AWeaponCast_LaysALineAhead()
    {
        using var world = World();
        var caster = Caster(world);
        caster.CastType = CastType.Weapon;
        caster.CastSpell(fromStaff: true);
        new Actions.CastSpell(0, 1, null).Stop(caster);

        Run(world, 1);

        var along = Mines(world).Select(m => Vector3.Dot(m.Position - caster.Position, caster.Body.Facing)).Order().ToList();
        Assert.Equal(3, along.Count);                                               // 2 + trunc(max(2/2, 1))
        Assert.Equal(caster.Radius + 2.5f, along[0], 1);
        Assert.Equal(2.5f, along[1] - along[0], 1);
    }

    [Fact]
    public void AMine_ArmsAndThenGoesOffUnderAFoe()
    {
        using var world = World();
        var owner = Animated(world, at: new Vector3(-10, 0.9f, 0));
        var mine = Mine(world, owner, Vector3.Zero);
        var foe = Animated(world, Foe, at: new Vector3(0, 0.9f, 0));
        Run(world, SpellMine.ArmTime - 0.1f);
        Assert.False(mine.IsLit);                                                    // not armed yet

        Run(world, 0.15f);
        Assert.True(mine.IsLit);
        Run(world, SpellMine.Fuse + 0.05f);

        Assert.True(mine.IsDead);
        Assert.True(foe.HitPoints < 300, $"{foe.HitPoints}");
    }

    [Fact]
    public void ItsOwner_SetsItOffToo_OnceItIsArmed()
    {
        using var world = World();
        var owner = Animated(world, at: new Vector3(0, 0.9f, 0));
        var mine = Mine(world, owner, Vector3.Zero);

        Run(world, SpellMine.ArmTime + SpellMine.Fuse + 0.1f);

        Assert.True(mine.IsDead);
        Assert.True(owner.HitPoints < 300, $"{owner.HitPoints}");
    }

    [Fact]
    public void ABlow_LightsAShortFuse_AndAFarOwnerSetsItOff()
    {
        using var world = World();
        var owner = Animated(world, at: new Vector3(-10, 0.9f, 0));
        var struck = Mine(world, owner, Vector3.Zero);
        var far = Mine(world, owner, new Vector3(25, 0, 0));
        Run(world, Tick);

        struck.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 1, 1), null, Vector3.Zero);
        Run(world, SpellMine.DamageFuse + 0.05f);

        Assert.True(struck.IsDead);
        Assert.True(far.IsDead);                                                     // 35 from its owner
    }

    [Fact]
    public void AMinesBlast_SetsOffItsNeighbour()
    {
        using var world = World();
        var owner = Animated(world, at: new Vector3(-10, 0.9f, 0));
        var first = Mine(world, owner, Vector3.Zero);
        var second = Mine(world, owner, new Vector3(2.5f, 0, 0));
        Run(world, Tick);

        first.Detonate();
        Assert.False(second.IsDead);
        Run(world, SpellMine.DamageFuse + 0.05f);

        Assert.True(second.IsDead);
    }

    [Fact]
    public void ANewMine_SetsOffOneOnItsSpot()
    {
        using var world = World();
        var owner = Animated(world, at: new Vector3(-10, 0.9f, 0));
        var old = Mine(world, owner, Vector3.Zero);
        Run(world, Tick);

        Mine(world, owner, new Vector3(0.5f, 0, 0));

        Assert.True(old.IsDead);
    }
}
