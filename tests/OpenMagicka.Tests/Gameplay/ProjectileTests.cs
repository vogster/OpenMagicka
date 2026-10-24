using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Gameplay.Spells.Effects;
using static OpenMagicka.Tests.Gameplay.CharacterTests;
using static OpenMagicka.Tests.Gameplay.StateMachineTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>Projectile spells in the world (SPELLS.md §5.3).</summary>
public class ProjectileTests
{
    private static readonly CharacterTemplate Foe = Goblin with { Factions = Factions.Human };

    /// <summary>A bot that casts on demand: no buttons, so its cast type and power are set as the AI's ability sets them.</summary>
    private static Character Caster(GameWorld world, params Elements[] queue)
    {
        var caster = Animated(world, clips: new CharacterAnimations(Clips().Sets, SkeletonTests.Arm()));
        caster.Equip(0, new Item(new ItemTemplate("sword", WeaponClass.SlashMedium, Attach0: Matrix4x4.CreateTranslation(0, 0, 1))), "hand");
        Run(world, 0.05f);
        foreach (var element in queue)
        {
            caster.Conjure(element);
        }
        return caster;
    }

    private static void Cast(Character caster, CastType cast, float power = 1)
    {
        (caster.SpellPower, caster.CastType) = (power, cast);
        caster.CastSpell(fromStaff: true);
    }

    private static List<Missile> Missiles(GameWorld world) => [.. world.Entities.Entities.OfType<Missile>()];

    [Fact]
    public void AnEarthBoulder_FliesAlongTheFacing_AtItsSpeed_AndOutlivesItsHit()
    {
        using var world = World();
        var caster = Caster(world, Elements.Earth);
        var first = Animated(world, Foe, at: new Vector3(0, 0.9f, -4));
        var second = Animated(world, Foe, at: new Vector3(0, 0.9f, -7));

        Cast(caster, CastType.Force);
        Run(world, Tick);
        var boulder = Assert.Single(Missiles(world));
        Assert.True(boulder.Velocity.Z < -80 && MathF.Abs(boulder.Velocity.X) < 1, $"v {boulder.Velocity}");   // 110 straight ahead
        Assert.False(caster.CurrentSpell!.IsActive);                                // one boulder: the cast is over
        Run(world, 0.3f);

        Assert.True(first.HitPoints < 300, $"{first.HitPoints}");                   // 150 × 110 / 50
        Assert.Single(Missiles(world));                                              // 25 hits before it goes, not one
        Assert.True(second.HitPoints <= 300);
    }

    [Fact]
    public void AnIceBoulder_GoesOnItsFirstHit()
    {
        using var world = World();
        var caster = Caster(world, Elements.Earth, Elements.Water, Elements.Cold);   // earth and ice
        Assert.Equal(Elements.Earth | Elements.Ice, caster.SpellQueue.Combine().Element);
        var first = Animated(world, Foe, at: new Vector3(0, 0.9f, -4));
        var second = Animated(world, Foe, at: new Vector3(0, 0.9f, -7));

        Cast(caster, CastType.Force);
        Run(world, 0.4f);

        Assert.True(first.HitPoints < 300);
        Assert.Equal(300f, second.HitPoints);
    }

    [Fact]
    public void AFireBoulder_BlastsWhereItHits()
    {
        using var world = World();
        var caster = Caster(world, Elements.Earth, Elements.Fire, Elements.Fire);
        var struck = Animated(world, Foe, at: new Vector3(0, 0.9f, -5));
        var beside = Animated(world, Foe, at: new Vector3(2, 0.9f, -5.5f));

        Cast(caster, CastType.Force);
        Run(world, 0.4f);

        Assert.True(struck.Statuses.Has(StatusEffects.Burning) || struck.HitPoints < 300);
        Assert.True(beside.Statuses.Has(StatusEffects.Burning), "the spray blast reaches beside it");
        Assert.Empty(Missiles(world));
    }

    [Fact]
    public void IceShards_FlyOneAfterAnother_TheLastStraightAhead()
    {
        using var world = World();
        var caster = Caster(world, Elements.Water, Elements.Cold, Elements.Water, Elements.Cold);   // ice ×2: six shards
        var seen = new HashSet<Missile>(ReferenceEqualityComparer.Instance);

        Cast(caster, CastType.Force, power: 0);
        var effect = Assert.IsType<ProjectileEffect>(caster.CurrentSpell);
        Assert.Equal(6, effect.ShotsLeft);
        for (var t = 0f; t < 1; t += Tick)
        {
            world.Tick(Tick);
            seen.UnionWith(Missiles(world));
        }

        Assert.Equal(6, seen.Count);
        Assert.All(seen, m => Assert.True(m.Piercing));
        Assert.False(caster.CurrentSpell!.IsActive);
    }

    [Fact]
    public void AnAreaCast_BlastsTheGroundAround_SparingTheCaster()
    {
        using var world = World();
        var caster = Caster(world, Elements.Earth, Elements.Earth);
        var near = Animated(world, Foe, at: new Vector3(3, 0.9f, 0));
        var far = Animated(world, Foe, at: new Vector3(9, 0.9f, 0));
        Run(world, Tick);                                                            // into the grid the blast looks in

        Cast(caster, CastType.Area);
        Run(world, Tick);

        Assert.True(near.IsKnockedDown || near.Body.IsPushed, "earth's ground blast knocks down");
        Assert.False(far.IsKnockedDown || far.Body.IsPushed);
        Assert.False(caster.IsKnockedDown);
        Assert.Empty(Missiles(world));
    }

    [Fact]
    public void ASelfCast_DropsTheBoulderOnTheCaster()
    {
        using var world = World();
        var caster = Caster(world, Elements.Earth);

        Cast(caster, CastType.Self, power: 0);
        Run(world, Tick);
        var boulder = Assert.Single(Missiles(world));
        Assert.True(boulder.Position.Y > caster.Position.Y + 6 && boulder.Velocity.Y < 0, $"at {boulder.Position} v {boulder.Velocity}");
        Run(world, 1.5f);

        Assert.True(caster.HitPoints < 300, "its own boulder may hit it");
    }

    [Fact]
    public void AnIceWeapon_StrikesAlongTheFacing_WhenItsClipActionEnds()
    {
        using var world = World();
        var caster = Caster(world, Elements.Water, Elements.Cold);
        var ahead = Animated(world, Foe, at: new Vector3(0, 0.9f, -3));
        var aside = Animated(world, Foe, at: new Vector3(4, 0.9f, 0));
        Run(world, Tick);

        Cast(caster, CastType.Weapon);
        var effect = caster.CurrentSpell!;
        Assert.Equal(300f, ahead.HitPoints);
        new OpenMagicka.Gameplay.Animation.Actions.CastSpell(0, 1, null).Stop(caster);   // the clip's cast action ends

        Assert.True(ahead.HitPoints < 300);
        Assert.Equal(300f, aside.HitPoints);
    }

    [Fact]
    public void AnEarthWeapon_SendsAWaveAlongTheFloor_HittingEachOnce()
    {
        using var world = World();
        var caster = Caster(world, Elements.Earth);
        var near = Animated(world, Foe, at: new Vector3(0, 0.9f, -5));
        var far = Animated(world, Foe, at: new Vector3(0, 0.9f, -12));
        var beyond = Animated(world, Foe, at: new Vector3(0, 0.9f, -20));

        Cast(caster, CastType.Weapon);
        new OpenMagicka.Gameplay.Animation.Actions.CastSpell(0, 1, null).Stop(caster);
        var wave = Assert.Single(world.Entities.Entities.OfType<GroundWave>());
        Run(world, 0.6f);

        Assert.Contains(near, wave.HitList);                                         // an earth wave knocks down (no hit points)
        Assert.Contains(far, wave.HitList);
        Assert.DoesNotContain(beyond, wave.HitList);                                 // its 15 m are spent
        Assert.True(wave.IsDead);
        Assert.Empty(world.Entities.Entities.OfType<GroundWave>());
        Assert.Equal(300f, caster.HitPoints);
    }

    [Fact]
    public void AMissileEvents_FollowTheOriginalsTables()
    {
        var boulder = SpellMissiles.Events(new Spell(earth: 1), splash: 0, count: 1);
        var shards = SpellMissiles.Events(new Spell(ice: 1), splash: 0, count: 3);

        Assert.Contains(boulder[0].Events, e => e is EffectEvent { Effect: "projectile_earth4", Follow: true });
        Assert.Contains(boulder[1].Events, e => e is RemoveEvent { Bounces: ProjectileRules.BoulderPierces });
        Assert.Contains(boulder[2].Events, e => e is DecalEvent { Decal: "crater", Scale: 1 });
        Assert.Contains(shards[0].Events, e => e is EffectEvent { Effect: "projectiletrail_ice" });
        Assert.All(shards[1].Events.OfType<DamageEvent>(), d => Assert.True(d.Damage.Attack.HasFlag(AttackProperties.Piercing)));
        Assert.Equal(ProjectileRules.Lifetime, boulder[3].Condition.Time);
    }
}
