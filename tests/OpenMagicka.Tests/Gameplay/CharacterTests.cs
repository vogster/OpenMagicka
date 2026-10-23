using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Physics;
using static OpenMagicka.Gameplay.Combat.DamageResult;

namespace OpenMagicka.Tests.Gameplay;

public class CharacterTests
{
    internal const float Tick = 1f / 60;

    internal static readonly CharacterTemplate Goblin = new(
        "goblin", Factions.Evil, MaxHitPoints: 300, Length: 1, Radius: 0.4f, Mass: 75, Speed: 4, TurnSpeed: 10,
        ResistanceTable.Neutral, Regeneration: 0, HitTolerance: 20, KnockdownTolerance: 1, ZapModifier: 0.25f, BleedRate: 15, StunTime: 2);

    internal static GameWorld World()
    {
        var world = new GameWorld();
        Vector3[] floor = [new(-50, 0, -50), new(50, 0, -50), new(50, 0, 50), new(-50, 0, 50)];
        world.Physics.AddStaticMesh(2, floor, [new MeshTriangle(0, 2, 1), new MeshTriangle(0, 3, 2)]);
        return world;
    }

    internal static Character Spawn(GameWorld world, CharacterTemplate? template = null, Vector3? at = null, int players = 1)
    {
        var character = new Character(template ?? Goblin, world.Physics, at ?? new Vector3(0, 0.9f, 0), players, random: new Random(1));
        world.Entities.Add(character);
        return character;
    }

    internal static void Run(GameWorld world, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-4f; t += Tick)
        {
            world.Tick(Tick);
        }
    }

    private static DamageEntry Hurt(Elements element, float amount, float magnitude = 1, AttackProperties attack = AttackProperties.Damage) =>
        new(attack, element, amount, magnitude);

    [Fact]
    public void ANewCharacter_IsAliveAtFullHealth_WhereItWasPut()
    {
        using var world = World();
        var goblin = Spawn(world);

        Assert.Equal((300f, false, true), (goblin.HitPoints, goblin.IsDead, goblin.IsDamageable));
        Assert.Equal(new Vector3(0, 0.9f, 0), goblin.Position);
        Assert.Same(goblin, goblin.Body.Body.Tag);
        Assert.Equal(0.4f, goblin.Radius);
    }

    [Fact]
    public void Damage_IsResisted_AndRemembersTheAttacker()
    {
        using var world = World();
        var fireproof = Goblin with { Resistances = ResistanceTable.Neutral.With(Elements.Fire, multiplier: 0.5f) };
        var goblin = Spawn(world, fireproof);
        var wizard = Spawn(world, at: new Vector3(5, 0.9f, 0));

        var result = goblin.Damage(Hurt(Elements.Fire, 60, 2), wizard, wizard.Position);

        Assert.Equal(Damaged | Hit, result);
        Assert.Equal(240f, goblin.HitPoints);
        Assert.Same(wizard, goblin.LastAttacker);
        Assert.True(goblin.IsHit);
    }

    [Fact]
    public void Enemies_ResistMoreWithMorePlayers_FriendsDoNot()
    {
        using var world = World();
        var tough = Goblin with { Resistances = ResistanceTable.Neutral.With(Elements.Fire, multiplier: 0.5f) };

        var enemy = Spawn(world, tough, players: 4);
        var friend = Spawn(world, tough with { Factions = Factions.Friendly }, players: 4);

        Assert.Equal(0.5f * (1 - 3 * ResistanceTable.PlayerScaling), enemy.Defences.Template[Elements.Fire].Multiplier, 6);
        Assert.Equal(0.5f, friend.Defences.Template[Elements.Fire].Multiplier);
    }

    [Fact]
    public void AKnockback_PushesAndFells_APushOnlyPushes()
    {
        using var world = World();
        var felled = Spawn(world);
        var shoved = Spawn(world, at: new Vector3(0, 0.9f, 10));
        Run(world, 0.3f);

        var knocked = felled.Damage(new DamageEntry(AttackProperties.Knockback, Elements.None, 500, 2), null, new Vector3(-2, 0.9f, 0));
        var pushed = shoved.Damage(new DamageEntry(AttackProperties.Pushed, Elements.Water, 140, 1.5f), null, new Vector3(-2, 0.9f, 10));
        Run(world, Tick);

        Assert.True(knocked.HasFlag(KnockedBack));
        Assert.True(felled.IsKnockedDown);
        Assert.True(felled.Body.Body.Velocity.X > 1);
        Assert.False(felled.Body.IsTouchingGround);                              // 40°: off the ground
        Assert.True(pushed.HasFlag(Pushed));
        Assert.False(shoved.IsKnockedDown);
        Assert.True(shoved.Body.IsPushed);
    }

    [Fact]
    public void AKnockdown_NeedsItsTolerance_And5000MeansNever()
    {
        using var world = World();
        var sturdy = Spawn(world, Goblin with { KnockdownTolerance = 3 });
        var rock = Spawn(world, Goblin with { KnockdownTolerance = CharacterTemplate.NoKnockdown }, new Vector3(5, 0.9f, 0));

        sturdy.Damage(new DamageEntry(AttackProperties.Knockdown, Elements.Earth, 150, 2), null, Vector3.Zero);
        Assert.False(sturdy.IsKnockedDown);
        sturdy.Damage(new DamageEntry(AttackProperties.Knockdown, Elements.Earth, 150, 3), null, Vector3.Zero);
        Assert.True(sturdy.IsKnockedDown);
        sturdy.StandUp();
        Assert.False(sturdy.IsKnockedDown);

        rock.KnockDown();
        rock.Damage(new DamageEntry(AttackProperties.Knockback, Elements.None, 500, 2), null, Vector3.Zero);
        Assert.False(rock.IsKnockedDown);
    }

    [Fact]
    public void Vines_HoldTheCharacter_UntilDamageOrFireBreaksThem()
    {
        using var world = World();
        var goblin = Spawn(world);
        var wizard = Spawn(world, at: new Vector3(5, 0.9f, 0));
        var vines = new DamageEntry(AttackProperties.Entanglement, Elements.Earth, 0, 10);

        Assert.Equal(Hit, goblin.Damage(vines, wizard, wizard.Position));
        Assert.Equal((10f, 300f), (goblin.Entanglement, goblin.HitPoints));
        Assert.Equal(None, goblin.Damage(vines, goblin, goblin.Position));         // not by itself

        goblin.Damage(Hurt(Elements.Earth, 10, 4), wizard, wizard.Position);
        Assert.Equal(6f, goblin.Entanglement);
        Assert.Equal(None, goblin.Damage(new DamageEntry(AttackProperties.Knockback, Elements.None, 500, 2), null, Vector3.Zero) & KnockedBack);
        goblin.Damage(new DamageEntry(AttackProperties.Status, Elements.Fire, 60, 0.1f), wizard, wizard.Position);
        Assert.False(goblin.IsEntangled);
        goblin.Entangle(30);
        Assert.Equal(0f, goblin.Entanglement);                                    // burning: no vines take
        var bound = Spawn(world, at: new Vector3(9, 0.9f, 0));
        bound.Entangle(30);
        Assert.Equal(Character.MaxEntanglement, bound.Entanglement);
    }

    [Fact]
    public void TheImmortal_TakeNothing_ForAWhile()
    {
        using var world = World();
        var goblin = Spawn(world);

        goblin.MakeImmortal(0.5f);

        Assert.Equal(None, goblin.Damage(Hurt(Elements.Fire, 100), null, Vector3.Zero));
        Run(world, 0.6f);
        Assert.Equal(Damaged | Hit, goblin.Damage(Hurt(Elements.Fire, 100), null, Vector3.Zero));
    }

    [Fact]
    public void Stun_AndTheZap_LastTheirTime()
    {
        using var world = World();
        var goblin = Spawn(world);

        goblin.Damage(Hurt(Elements.Earth, 50, attack: AttackProperties.Damage | AttackProperties.Stun), null, Vector3.Zero);
        goblin.Damage(Hurt(Elements.Lightning, 10), null, Vector3.Zero);
        Run(world, Tick);

        Assert.True(goblin.IsStunned);
        Assert.True(goblin.IsZapped);
        Assert.Equal(0.25f, goblin.Body.SpeedMultiplier);
        Run(world, 0.15f);
        Assert.False(goblin.IsZapped);
        Assert.Equal(1f, goblin.Body.SpeedMultiplier);
        Run(world, 2);
        Assert.False(goblin.IsStunned);
    }

    [Fact]
    public void Burning_HurtsOverTime()
    {
        using var world = World();
        var plain = Spawn(world);
        plain.Damage(new DamageEntry(AttackProperties.Status, Elements.Fire, 60, 1), null, Vector3.Zero);

        Run(world, 1);

        Assert.InRange(300 - plain.HitPoints, 64, 68);
        Assert.True(plain.Statuses.Has(StatusEffects.Burning));
    }

    [Fact]
    public void ColdSlows_AndFrostStops()
    {
        using var world = World();
        var goblin = Spawn(world);
        Run(world, 0.2f);

        goblin.Damage(new DamageEntry(AttackProperties.Status, Elements.Cold, 0, 1), null, Vector3.Zero);
        Run(world, Tick);
        Assert.InRange(goblin.Body.SpeedMultiplier, 0.3f, 0.4f);

        goblin.Damage(new DamageEntry(AttackProperties.Status, Elements.Water, 0, 1), null, Vector3.Zero);
        goblin.Damage(new DamageEntry(AttackProperties.Status, Elements.Cold, 0, 1), null, Vector3.Zero);
        goblin.Body.Movement = Vector3.UnitX;
        Run(world, Tick);
        Assert.True(goblin.CastInterrupted);
        Assert.Equal(0f, goblin.Body.SpeedMultiplier);
        Assert.Equal(Vector3.Zero, goblin.Body.Movement);
    }

    [Fact]
    public void Regeneration_HealsWhileNotBurningOrPoisoned()
    {
        using var world = World();
        var troll = Spawn(world, Goblin with { Regeneration = 30 });
        troll.Damage(Hurt(Elements.Earth, 100), null, Vector3.Zero);

        Run(world, 1);

        Assert.Equal(230f, troll.HitPoints, 0);
    }

    [Fact]
    public void ACorpse_LeavesAfterItsTime_AndAnOverkill_AtOnce()
    {
        using var world = World();
        var goblin = Spawn(world);
        var shattered = Spawn(world, at: new Vector3(5, 0.9f, 0));

        goblin.Damage(Hurt(Elements.Fire, 400), null, Vector3.Zero);
        var gone = world.Tick(Tick);
        shattered.Damage(Hurt(Elements.Earth, 2000), null, Vector3.Zero);
        gone = world.Tick(Tick);

        Assert.True(goblin.IsDead);
        Assert.True(shattered.IsOverkilled);
        Assert.Equal(-1300f, shattered.HitPoints);
        Assert.Contains(gone, r => ReferenceEquals(r.Entity, shattered));
        Assert.True(shattered.Body.Body.IsRemoved);                               // the body left the physics world
        Assert.DoesNotContain(gone, r => ReferenceEquals(r.Entity, goblin));
        Assert.Equal(None, goblin.Damage(Hurt(Elements.Fire, 10), null, Vector3.Zero));   // the dead take nothing
        Run(world, 51);
        Assert.Null(goblin.Manager);                                               // 20–40 s, then 10 more
    }

    [Fact]
    public void TheUndying_RiseAgain()
    {
        using var world = World();
        var skeleton = Spawn(world, Goblin with { Undying = true, UndieTime = 2, UndieHitPoints = 50 });

        skeleton.Damage(Hurt(Elements.Fire, 400), null, Vector3.Zero);
        Run(world, 1);
        Assert.True(skeleton.IsDead);
        Run(world, 1.1f);

        Assert.False(skeleton.IsDead);
        Assert.Equal(50f, skeleton.HitPoints);
    }

    [Fact]
    public void TheLastAttacker_IsForgotten_EightSecondsAfterTheStatusesEnd()
    {
        using var world = World();
        var goblin = Spawn(world);
        var wizard = Spawn(world, at: new Vector3(5, 0.9f, 0));
        goblin.Damage(Hurt(Elements.Earth, 10), wizard, wizard.Position);

        Run(world, 7.9f);
        Assert.Same(wizard, goblin.LastAttacker);
        Run(world, 0.2f);
        Assert.Null(goblin.LastAttacker);
    }

    [Fact]
    public void AFullBlock_IsDeflected_ButStatusesStillTake()
    {
        using var world = World();
        var knight = Spawn(world);
        knight.Blocker = new Shield(blocks: 1000);

        var result = knight.Damage(new DamageEntry(AttackProperties.Damage | AttackProperties.Status, Elements.Earth | Elements.Water, 100, 1), null, Vector3.Zero);

        Assert.True(result.HasFlag(Deflected));
        Assert.Equal(300f, knight.HitPoints);
        Assert.True(knight.Statuses.Has(StatusEffects.Wet));

        knight.Blocker = new Shield(blocks: 30);
        knight.Damage(Hurt(Elements.Earth, 100), null, Vector3.Zero);
        Assert.Equal(230f, knight.HitPoints);
        knight.Damage(Hurt(Elements.Fire, 100), null, Vector3.Zero);              // fire is not blocked
        Assert.Equal(130f, knight.HitPoints);
    }

    [Fact]
    public void ASelfShield_TakesItsShareFirst()
    {
        using var world = World();
        var warded = Spawn(world);
        warded.SelfShield = new Ward();

        warded.Damage(Hurt(Elements.Earth, 100), null, Vector3.Zero);
        Assert.Equal(250f, warded.HitPoints);
        warded.Damage(new DamageEntry(AttackProperties.Status, Elements.Fire, 60, 1), null, Vector3.Zero);
        Run(world, 1);

        Assert.InRange(250 - warded.HitPoints, 15, 19);                           // the ward halves the DPS it lets through, then each tick: 66 / 4
        Assert.False(warded.Damage(new DamageEntry(AttackProperties.Pushed, Elements.Water, 500, 5), null, new Vector3(-1, 0.9f, 0)).HasFlag(Pushed));
    }

    [Fact]
    public void FallingFromHigh_HurtsThroughThePhysics()
    {
        using var world = World();
        var goblin = Spawn(world, at: new Vector3(0, 8.9f, 0));                   // an 8 m drop: about 17.7 m/s

        Run(world, 2);

        Assert.InRange(300 - goblin.HitPoints, 300, 600);                          // 100·(v/10)^2.7 ≈ 470: dead
        Assert.True(goblin.IsDead);
    }

    /// <summary>Blocks a fixed amount.</summary>
    private sealed class Shield(float blocks) : IBlocker
    {
        public bool TryBlock(DamageEntry damage, Vector3 attackPosition, out float blocked)
        {
            blocked = blocks;
            return true;
        }
    }

    /// <summary>Halves every hit, and is solid.</summary>
    private sealed class Ward : ISelfShield
    {
        public bool IsActive => true;

        public bool IsSolid => true;

        public DamageResult Absorb(Character owner, ref DamageEntry damage, OpenMagicka.Gameplay.Entities.Entity? attacker, Vector3 attackPosition)
        {
            damage = damage with { Amount = damage.Amount / 2 };
            return None;
        }

        public float AbsorbOverTime(float amount, Elements element) => amount / 2;

        public void Update(Character owner, float dt)
        {
        }
    }
}
