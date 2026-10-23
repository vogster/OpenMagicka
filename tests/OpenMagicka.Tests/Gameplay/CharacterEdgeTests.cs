using System.Numerics;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Spells;
using OpenMagicka.Physics.Characters;
using static OpenMagicka.Gameplay.Combat.DamageResult;
using static OpenMagicka.Tests.Gameplay.CharacterTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The character's rarer paths: deaths, removal, physics events, guards.</summary>
public class CharacterEdgeTests
{
    private static DamageEntry Hurt(Elements element, float amount, float magnitude = 1) => new(AttackProperties.Damage, element, amount, magnitude);

    [Fact]
    public void BurningToDeath_KillsOnTheSameFrame()
    {
        using var world = World();
        var goblin = Spawn(world);
        goblin.Damage(Hurt(Elements.Earth, 290), null, Vector3.Zero);
        goblin.Damage(new DamageEntry(AttackProperties.Status, Elements.Fire, 1500, 3), null, Vector3.Zero);

        world.Tick(Tick);

        Assert.True(goblin.HitPoints <= 0);
        Assert.True(goblin.IsDead);
    }

    [Fact]
    public void KillAndOverKill_EndTheCharacter()
    {
        using var world = World();
        var killed = Spawn(world);
        var shattered = Spawn(world, at: new Vector3(5, 0.9f, 0));

        killed.Kill();
        shattered.OverKill();

        Assert.True(killed.IsDead && !killed.IsOverkilled);
        Assert.True(shattered.IsDead && shattered.IsOverkilled && shattered.IsRemovable);
    }

    [Fact]
    public void AnArcaneDeath_DoesNotShatter_AndFrostShattersEvenWithoutGore()
    {
        using var world = World();
        var arcane = Spawn(world);
        var frozen = Spawn(world, at: new Vector3(5, 0.9f, 0));
        frozen.BloodAndGore = false;
        frozen.Damage(new DamageEntry(AttackProperties.Status, Elements.Water, 0, 1), null, Vector3.Zero);
        frozen.Damage(new DamageEntry(AttackProperties.Status, Elements.Cold, 0, 1), null, Vector3.Zero);

        var a = arcane.Damage(Hurt(Elements.Arcane, 2000), null, Vector3.Zero);
        var f = frozen.Damage(Hurt(Elements.Earth, 200), null, Vector3.Zero);    // ×3 on the frozen

        Assert.True(a.HasFlag(OverKilled) && !arcane.IsOverkilled);
        Assert.True(f.HasFlag(OverKilled) && frozen.IsOverkilled);
    }

    [Fact]
    public void TheDead_CannotBeKnockedDown()
    {
        using var world = World();
        var goblin = Spawn(world);
        goblin.Kill();

        goblin.KnockDown();

        Assert.False(goblin.IsKnockedDown);
    }

    [Fact]
    public void ThePhysicsReport_FellsThoseItNames()
    {
        using var world = World();
        var troll = Spawn(world);
        var goblin = Spawn(world, at: new Vector3(5, 0.9f, 0));

        troll.ApplyPhysics(new CharacterReport(ImpactSpeed: 5, KnockedDownSelf: true, KnockedDown: [goblin.Body], Touched: []));

        Assert.True(troll.IsKnockedDown && goblin.IsKnockedDown);
        Assert.Equal(300f, troll.HitPoints);                                       // 5 m/s is too soft to hurt
    }

    [Fact]
    public void AWholeAttack_IsDeflectedOnlyIfEveryEntryIs()
    {
        using var world = World();
        var goblin = Spawn(world);

        Assert.Equal(Damaged | Hit, goblin.Damage([Hurt(Elements.Earth, 0, 0), Hurt(Elements.Earth, 50)], null, Vector3.Zero));
        Assert.Equal(Deflected, goblin.Damage([Hurt(Elements.Earth, 0, 0), Hurt(Elements.Fire, 10, 0)], null, Vector3.Zero));
    }

    [Fact]
    public void TheEthereal_TakeNoStatusAndNoPush()
    {
        using var world = World();
        var ghost = Spawn(world, Goblin with { IsEthereal = true });

        ghost.Damage(new DamageEntry(AttackProperties.Status, Elements.Fire, 60, 1), null, Vector3.Zero);
        var pushed = ghost.Damage(new DamageEntry(AttackProperties.Pushed, Elements.Water, 500, 5), null, new Vector3(-1, 0.9f, 0));

        Assert.Equal(StatusEffects.None, ghost.Statuses.Active);
        Assert.False(pushed.HasFlag(Pushed));
    }

    [Fact]
    public void ABleedingHit_SetsTheTemplatesBleedRate()
    {
        using var world = World();
        var goblin = Spawn(world);

        var result = goblin.Damage(new DamageEntry(AttackProperties.Damage | AttackProperties.Bleed, Elements.Earth, 50, 1), null, Vector3.Zero);

        Assert.True(result.HasFlag(StatusAdded));
        Assert.Equal(15f, goblin.Statuses[StatusEffects.Bleeding].Dps);
    }

    [Fact]
    public void APartialBlock_ComesOffTheAmount_AsTheOriginal()
    {
        using var world = World();
        var knight = Spawn(world);
        knight.Blocker = new FixedBlock(150);

        knight.Damage(Hurt(Elements.Earth, 100, 2), null, Vector3.Zero);           // 150 < 200: not full, but 100 − 150 → 0

        Assert.Equal(300f, knight.HitPoints);
    }

    [Fact]
    public void ARemovedCharacter_KeepsItsPlace_AndCannotComeBack()
    {
        using var world = World();
        var goblin = Spawn(world, at: new Vector3(3, 0.9f, 4));
        var victim = Spawn(world, at: new Vector3(6, 0.9f, 4));
        Run(world, 0.2f);
        victim.Damage(new DamageEntry(AttackProperties.Damage | AttackProperties.Status, Elements.Water, 10, 1), goblin, goblin.Position);   // wet: never fades
        var at = goblin.Position;

        goblin.OverKill();
        var gone = world.Tick(Tick);
        world.Tick(Tick);

        Assert.Contains(gone, r => ReferenceEquals(r.Entity, goblin));
        Assert.Equal(at.X, goblin.Position.X, 2);
        Assert.Null(victim.LastAttacker);                                          // forgotten once it left
        Assert.Throws<InvalidOperationException>(() => world.Entities.Add(goblin));
    }

    [Fact]
    public void PreparingTheStep_KeepsWhatOthersSetOnTheBody()
    {
        using var world = World();
        var goblin = Spawn(world);
        goblin.IsPlayer = true;
        goblin.Body.Conditions = goblin.Body.Conditions with { IsOnGrease = true, FlyGround = 2 };
        goblin.Damage(new DamageEntry(AttackProperties.Status, Elements.Water, 0, 1), null, Vector3.Zero);
        goblin.Damage(new DamageEntry(AttackProperties.Status, Elements.Cold, 0, 1), null, Vector3.Zero);

        goblin.PrepareStep();

        Assert.True(goblin.Body.Conditions.IsOnGrease);
        Assert.Equal(2f, goblin.Body.Conditions.FlyGround);
        Assert.True(goblin.Body.Conditions.IsPlayer);
        Assert.Equal(0f, goblin.Body.Conditions.TurnSpeed);                       // frozen: no turning
    }

    [Fact]
    public void BadValues_AreRejected()
    {
        using var world = World();
        var goblin = Spawn(world);

        Assert.Throws<ArgumentOutOfRangeException>(() => goblin.Damage(Hurt(Elements.Earth, float.NaN), null, Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => goblin.MakeImmortal(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => goblin.Entangle(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => goblin.KnockDown(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Spawn(Goblin with { MaxHitPoints = 0 }, Vector3.Zero));
        goblin.Blocker = new FixedBlock(float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => goblin.Damage(Hurt(Elements.Earth, 10), null, Vector3.Zero));
    }

    [Fact]
    public void ASpawnTheLevelCannotTake_LeavesNoBodyBehind()
    {
        using var world = World();
        for (var i = 0; i < OpenMagicka.Gameplay.Entities.EntityManager.Capacity; i++)
        {
            world.Entities.Add(new Thing(Vector3.Zero));
        }

        Assert.Throws<InvalidOperationException>(() => world.Spawn(Goblin, new Vector3(0, 0.9f, 0)));
        Assert.Null(world.Physics.CastSegment(new Vector3(0, 0.9f, -2), new Vector3(0, 0.9f, 2)));   // no capsule there
    }

    private sealed class FixedBlock(float blocks) : IBlocker
    {
        public bool TryBlock(DamageEntry damage, Vector3 attackPosition, out float blocked)
        {
            blocked = blocks;
            return true;
        }
    }
}
