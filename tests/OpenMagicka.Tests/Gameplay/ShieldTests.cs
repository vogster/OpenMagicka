using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;
using Actions = OpenMagicka.Gameplay.Animation.Actions;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Shields, self-shields and wards in the world (SPELLS.md §5.6).</summary>
public class ShieldTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    private static Character Caster(GameWorld world, Vector3? at = null, params Elements[] queue)
    {
        var caster = Animated(world, at: at);
        foreach (var element in queue)
        {
            caster.Conjure(element);
        }
        return caster;
    }

    private static ShieldEffect Cast(Character caster, CastType cast)
    {
        caster.CastType = cast;
        caster.CastSpell(fromStaff: true);
        return Assert.IsType<ShieldEffect>(caster.CurrentSpell);
    }

    private static SpellShield Sphere(GameWorld world, Vector3 at, float hitPoints = 1000)
    {
        var shield = new SpellShield(world.Physics, null, ShieldShape.Sphere, at, -Vector3.UnitZ, ShieldRules.ShieldRadius, hitPoints);
        world.Entities.Add(shield);
        return shield;
    }

    [Fact]
    public void TheCasts_RaiseADiscBehind_ASphereAround_AndAWallAhead()
    {
        using var world = World();
        var disc = Caster(world, new Vector3(-20, 0.9f, 0), Elements.Shield);
        var sphere = Caster(world, new Vector3(0, 0.9f, 0), Elements.Shield);
        var wall = Caster(world, new Vector3(20, 0.9f, 0), Elements.Shield);
        Run(world, 0.05f);

        var d = Cast(disc, CastType.Force).Shield!;
        var s = Cast(sphere, CastType.Area).Shield!;
        var w = Cast(wall, CastType.Weapon);
        Assert.Null(w.Shield);
        new Actions.CastSpell(0, 1, null).Stop(wall);

        Assert.Equal((ShieldShape.Disc, 800f), (d.Shape, d.HitPoints));
        Assert.True(Vector3.Distance(d.Position, disc.Position + Vector3.UnitZ * 3.5f) < 0.01f, $"{d.Position}");
        Assert.Equal((ShieldShape.Sphere, 1000f), (s.Shape, s.HitPoints));
        Assert.True(Vector3.Distance(s.Position, sphere.Position) < 0.01f);
        var raised = Assert.IsType<SpellShield>(w.Shield);
        Assert.Equal(ShieldShape.Wall, raised.Shape);
        Assert.True(Vector3.Distance(raised.Position, wall.Position - Vector3.UnitZ * (Goblin.Radius + 7.5f)) < 0.01f, $"{raised.Position}");
    }

    [Fact]
    public void AShield_WearsAway_TakesDamage_AndDeflectsPushes()
    {
        using var world = World();
        var shield = Sphere(world, new Vector3(0, 1, -10));

        Run(world, 1);
        Assert.InRange(shield.HitPoints, 895, 905);                                  // 10 every 0.1 s
        Assert.Equal(DamageResult.Deflected, shield.Damage(new DamageEntry(AttackProperties.Pushed, Elements.Earth, 300, 1), null, Vector3.Zero));
        Assert.Equal(DamageResult.Hit, shield.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 100, 2), null, Vector3.Zero));
        Assert.InRange(shield.HitPoints, 695, 705);
        shield.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 1000, 1), null, Vector3.Zero);
        Run(world, Tick * 2);

        Assert.DoesNotContain(shield, world.Entities.Entities);
    }

    [Fact]
    public void ASphere_KeepsCharactersOut()
    {
        using var world = World();
        var shield = Sphere(world, new Vector3(0, 0.9f, 0));
        var walker = Animated(world, Foe, at: new Vector3(9, 0.9f, 0));
        Run(world, 0.05f);

        walker.Body.Movement = -Vector3.UnitX;
        Run(world, 3);

        Assert.True(walker.Position.X > ShieldRules.ShieldRadius - 0.1f, $"stopped at the shell, at {walker.Position}");
        Assert.Contains(shield, world.Entities.Entities);
    }

    [Fact]
    public void AMissile_HitsTheShield_AndSparesWhatIsBehind()
    {
        using var world = World();
        var shield = Sphere(world, new Vector3(0, 0.9f, 0), 5000);
        var inside = Animated(world, at: new Vector3(0, 0.9f, 0));
        Run(world, 0.05f);
        var before = shield.HitPoints;

        var rock = new Missile(world.Physics, null, new Vector3(10, 1.2f, 0), new Vector3(-30, 0, 0), 0.2f,
            [new EventGroup(new EventCondition(EventTrigger.Hit), [new DamageEvent(new DamageEntry(AttackProperties.Damage, Elements.Earth, 50, 1)), new RemoveEvent(0)])]);
        world.Entities.Add(rock);
        rock.Start();
        Run(world, 1);

        Assert.Equal(300f, inside.HitPoints);
        Assert.True(shield.HitPoints < before - 15, $"{before} → {shield.HitPoints}");   // its blow, beyond the wear
    }

    [Fact]
    public void ANewShield_OverlappingAnOlder_EndsIt_AndItselfSoonAfter()
    {
        using var world = World();
        var old = Sphere(world, new Vector3(0, 1, 0));
        Run(world, Tick);
        var fresh = Sphere(world, new Vector3(4, 1, 0));

        Assert.Equal(0f, old.HitPoints);
        Assert.True(fresh.IsPassable);
        Run(world, SpellShield.OverlapKillDelay + 0.05f);

        Assert.True(fresh.IsDead || fresh.HitPoints <= 0);
    }

    [Fact]
    public void TheOverlapDelay_EndsTheShield_EvenOnAStepThatLandsOnIt()
    {
        using var world = World();
        var old = Sphere(world, new Vector3(0, 1, 0));
        world.Tick(0.125f);
        var fresh = Sphere(world, new Vector3(4, 1, 0));

        world.Tick(0.125f);
        world.Tick(0.125f);                                                          // 0.25 s to the tick

        Assert.True(fresh.HitPoints <= 0, $"{fresh.HitPoints}");
        Assert.True(old.HitPoints <= 0);
    }

    [Fact]
    public void AShieldOutOfHitPoints_IsNoOverlap()
    {
        using var world = World();
        var old = Sphere(world, new Vector3(0, 1, 0));
        Run(world, Tick);
        old.Kill();
        var fresh = Sphere(world, new Vector3(4, 1, 0));

        Assert.False(fresh.IsPassable);
    }

    [Fact]
    public void AMissile_StillMeetsAShieldGoingDown()
    {
        using var world = World();
        var old = Sphere(world, new Vector3(0, 0.9f, 0));
        Run(world, Tick);
        var shield = Sphere(world, new Vector3(1, 0.9f, 0), 5000);
        Assert.True(shield.IsPassable);                                              // its overlap delay runs
        var rock = new Missile(world.Physics, null, new Vector3(10, 1.2f, 0), new Vector3(-30, 0, 0), 0.2f, []);
        world.Entities.Add(rock);
        rock.Start();
        Run(world, 0.2f);

        Assert.True(rock.Position.X > ShieldRules.ShieldRadius, $"went through, at {rock.Position}");
        Assert.True(old.HitPoints <= 0);
    }

    [Fact]
    public void Shields_HideWhatIsBehindThemFromSpells()
    {
        using var world = World();
        var caster = Caster(world, queue: Elements.Lightning);
        var behind = Animated(world, Foe, at: new Vector3(0, 0.9f, -5));
        world.Entities.Add(new SpellShield(world.Physics, null, ShieldShape.Wall, new Vector3(0, 1, -2.5f), Vector3.UnitX, ShieldRules.ShieldRadius, 5000));
        Run(world, 0.05f);

        caster.CastType = CastType.Force;
        caster.CastSpell(fromStaff: true);
        Run(world, 1.1f);

        Assert.Equal(300f, behind.HitPoints);
    }

    [Fact]
    public void TheBubble_DeflectsBlows_AndCastingItAgainTakesItDown()
    {
        using var world = World();
        var caster = Caster(world, queue: Elements.Shield);
        Run(world, 0.05f);

        Cast(caster, CastType.Self);
        var bubble = Assert.IsType<SelfShield>(caster.SelfShield);
        Assert.True(bubble.IsBubble);
        Assert.Equal(DamageResult.Deflected, caster.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 100, 1), null, caster.Position) & DamageResult.Deflected);
        Assert.Equal(300f, caster.HitPoints);
        Assert.InRange(bubble.HitPoints, 395, 401);
        Run(world, 1);
        Assert.InRange(bubble.HitPoints, 290, 305);                                  // 100 a second

        caster.AddSelfShield(Spell.Of(Elements.Shield));
        Assert.Null(caster.SelfShield);
        caster.AddSelfShield(Spell.Of(Elements.Shield));
        Assert.True(caster.SelfShield is { IsBubble: true });
    }

    [Fact]
    public void Armour_MakesItsWearerHeavyAndSlow_AndSoftensBlows()
    {
        using var world = World();
        var caster = Caster(world, null, Elements.Shield, Elements.Earth);
        Run(world, 0.05f);

        Cast(caster, CastType.Self);
        var armour = Assert.IsType<SelfShield>(caster.SelfShield);
        Assert.True(armour.IsArmour);
        Assert.Equal(SelfShield.ArmourMass, caster.Body.Body.Mass);
        Run(world, Tick);
        Assert.Equal(0.9f - 0.075f, caster.Body.SpeedMultiplier, 3);
        caster.Damage(new DamageEntry(AttackProperties.Damage, Elements.Earth, 110, 1), null, caster.Position);
        Assert.Equal(300f, caster.HitPoints);                                        // (110 − 10) × 0.85 taken by the armour
        Assert.InRange(armour.HitPoints, 1414, 1416);

        caster.Kill();
        for (var t = 0f; t < 5 && !caster.IsDead; t += Tick)
        {
            world.Tick(Tick);
        }
        Assert.Null(caster.SelfShield);                                              // death takes it
        Assert.Equal(Goblin.Mass, caster.Body.Body.Mass);
    }

    [Fact]
    public void IceArmour_StrikesAroundAsItForms()
    {
        using var world = World();
        var caster = Caster(world, null, Elements.Shield, Elements.Water, Elements.Cold);
        var near = Animated(world, Foe, at: new Vector3(2, 0.9f, 0));
        Run(world, 0.05f);

        Cast(caster, CastType.Self);

        Assert.True(near.HitPoints < 300);
        Assert.Equal(300f, caster.HitPoints);
    }

    [Fact]
    public void AWard_MakesEveryoneNearImmuneToItsElements()
    {
        using var world = World();
        var caster = Caster(world, null, Elements.Shield, Elements.Fire);
        var friend = Animated(world, at: new Vector3(1, 0.9f, 0));
        Run(world, 0.05f);

        Cast(caster, CastType.Self);
        Run(world, 0.1f);
        friend.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 100, 1), null, friend.Position);
        caster.Damage(new DamageEntry(AttackProperties.Damage, Elements.Fire, 100, 1), null, caster.Position);

        Assert.Equal((300f, 300f), (friend.HitPoints, caster.HitPoints));
        Assert.Contains(caster.Auras, a => a.SelfCasted);
    }
}
