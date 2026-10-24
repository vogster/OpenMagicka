using System.Numerics;
using OpenMagicka.Gameplay;
using OpenMagicka.Gameplay.Animation;
using OpenMagicka.Gameplay.Characters;
using OpenMagicka.Gameplay.Combat;
using OpenMagicka.Gameplay.Entities;
using OpenMagicka.Gameplay.Items;
using OpenMagicka.Gameplay.Props;
using OpenMagicka.Gameplay.Spells;
using static OpenMagicka.Tests.Gameplay.CharacterTests;

namespace OpenMagicka.Tests.Gameplay;

/// <summary>The level's physics props (ENTITIES_AI.md §8.4): bodies, hits, damage, breaking.</summary>
public class PhysicsPropTests
{
    /// <summary>A 1 m crate standing on its base at its origin.</summary>
    private static PropTemplate Crate(float hitPoints = 200, bool movable = false, bool solid = true, IReadOnlyList<EventGroup>? events = null,
        IReadOnlyList<PropGib>? gibs = null) =>
        new("crate", movable, Pushable: false, solid, Mass: 140, hitPoints, ResistanceTable.Neutral,
            new PropBox(new Vector3(-0.5f, 0, -0.5f), Vector3.One, Quaternion.Identity), gibs ?? [], "", [], events ?? []);

    private static PhysicsProp Place(GameWorld world, PropTemplate template, Vector3 at)
    {
        var prop = new PhysicsProp(world.Physics, template, Matrix4x4.CreateTranslation(at));
        world.Entities.Add(prop);
        return prop;
    }

    private static DamageEntry Blow(float amount, Elements element = Elements.Earth, AttackProperties attack = AttackProperties.Damage) =>
        new(attack, element, amount, 1);

    [Fact]
    public void AStaticProp_Stands_ItsBoxCentredOverItsOrigin_AndAMovableOneFalls()
    {
        using var world = World();
        var crate = Place(world, Crate(), new Vector3(0, 0, 0));
        var ball = Place(world, Crate(0, movable: true), new Vector3(3, 2, 0));

        Run(world, 2);

        Assert.Equal(new Vector3(0, 0.5f, 0), crate.Position);
        Assert.True(crate.IsPhysicsObject);
        Assert.False(crate.IsDamageable && crate.HitPoints < 200);
        Assert.InRange(ball.Position.Y, 0.45f, 0.6f);                                             // came to rest on the floor
        Assert.Equal(new Vector3(3, 0, 0), ball.Transform.Translation, new Vector3Comparer(0.1f));
    }

    [Fact]
    public void ACharacter_IsStoppedByASolidProp_AndPassesAnotherSettingOffItsHitEventsOnceAQuarterSecond()
    {
        using var world = World();
        var fire = new EventGroup(new EventCondition(EventTrigger.Hit), [new DamageEvent(Blow(10, Elements.Fire))]);
        Place(world, Crate(), new Vector3(2, 0, 0));
        Place(world, Crate(0, solid: false, events: [fire]), new Vector3(0, 0, 3));
        var blocked = Spawn(world, at: new Vector3(0, 0.9f, 0));
        var burnt = Spawn(world, at: new Vector3(0, 0.9f, 1.5f));
        var start = burnt.HitPoints;

        for (var t = 0f; t < 2; t += Tick)
        {
            blocked.Body.Movement = Vector3.UnitX;
            burnt.Body.Movement = Vector3.UnitZ;
            world.Tick(Tick);
        }

        Assert.InRange(blocked.Position.X, 0.5f, 1.6f);                                          // stopped at the crate
        Assert.True(burnt.Position.Z > 3.5f);                                                     // walked through the fire
        Assert.InRange(start - burnt.HitPoints, 10, 60);                                          // hit a few times, not every frame
    }

    [Fact]
    public void ADamageableProp_TakesWholePoints_ResistsInclusively_AndLightningOnWetHurtsThreefold()
    {
        using var world = World();
        var crate = Place(world, Crate(1000) with { Resistances = ResistanceTable.Neutral.With(Elements.Fire, 0.5f, 0) }, new Vector3(0, 0, 0));

        Assert.True(crate.Damage(Blow(10.7f), null, Vector3.Zero).HasFlag(DamageResult.Damaged));
        Assert.Equal(990, crate.HitPoints);
        crate.Damage(Blow(100, Elements.Fire), null, Vector3.Zero);
        Assert.Equal(940, crate.HitPoints);
        crate.Damage(Blow(0, Elements.Water, AttackProperties.Status), null, Vector3.Zero);
        Assert.True(crate.Statuses.Has(StatusEffects.Wet));
        crate.Damage(Blow(10, Elements.Lightning), null, Vector3.Zero);
        Assert.Equal(910, crate.HitPoints);
        Assert.Equal(DamageResult.None, Place(world, Crate(0), new Vector3(5, 0, 0)).Damage(Blow(100), null, Vector3.Zero));
    }

    [Fact]
    public void ABreakingProp_SetsOffItsTriggerEventsAndPieces_ThenGoes()
    {
        using var world = World();
        var boom = new EventGroup(new EventCondition(EventTrigger.Death), [new SplashEvent(Blow(50, Elements.Fire), 3)]);
        var barrel = Place(world, Crate(100, events: [boom], gibs: [new PropGib("gib0", 1, 1)]), new Vector3(0, 0, 0));
        barrel.OnDeathTrigger = 42;
        var near = Spawn(world, at: new Vector3(1.5f, 0.9f, 0));
        var (triggers, cues) = (new List<(Entity, int)>(), new List<AnimationCue>());
        world.Entities.ScriptTrigger += (source, trigger) => triggers.Add((source, trigger));
        world.Entities.Cue += (_, cue) => cues.Add(cue);
        Run(world, Tick);
        var before = near.HitPoints;

        var result = barrel.Damage(Blow(150), near, near.Position);
        world.Tick(Tick);

        Assert.True(result.HasFlag(DamageResult.Killed));
        Assert.Equal([(barrel, 42)], triggers);
        Assert.True(near.HitPoints < before);                                                     // the blast
        Assert.Contains(cues, c => c is GibsCue { Gibs.Count: 1 });
        Assert.DoesNotContain(barrel, world.Entities.Entities);
        Assert.Equal(DamageResult.None, barrel.Damage(Blow(10), null, Vector3.Zero));
    }

    [Fact]
    public void AStatus_WearsAPropDown_AndARemoveEventKillsIt()
    {
        using var world = World();
        var crate = Place(world, Crate(30), new Vector3(0, 0, 0));
        var web = Place(world, Crate(1000, solid: false, events: [new EventGroup(new EventCondition(EventTrigger.Hit), [new RemoveEvent(0)])]),
            new Vector3(0, 0, 4));

        crate.Damage(Blow(20, Elements.Fire, AttackProperties.Status), null, Vector3.Zero);
        Assert.True(crate.Statuses.Has(StatusEffects.Burning));
        var walker = Spawn(world, at: new Vector3(0, 0.9f, 2.5f));
        for (var t = 0f; t < 5 && !(crate.IsDead && web.IsDead); t += Tick)
        {
            walker.Body.Movement = Vector3.UnitZ;
            world.Tick(Tick);
        }

        Assert.True(crate.IsDead);
        Assert.True(web.IsDead);
    }

    [Fact]
    public void ATurnedProp_KeepsItsPlacement_AndItsEffectsStartAndEnd()
    {
        using var world = World();
        var glow = new PropEffect("fx_glow", Matrix4x4.Identity);
        var template = Crate(100) with
        {
            Box = new PropBox(new Vector3(0, 0, -0.5f), new Vector3(2, 1, 1), Quaternion.Identity), Effects = [glow],
        };
        var cues = new List<AnimationCue>();
        world.Entities.Cue += (_, cue) => cues.Add(cue);
        var placed = Matrix4x4.CreateRotationY(MathF.PI / 2) * Matrix4x4.CreateTranslation(4, 0, 0);
        var prop = new PhysicsProp(world.Physics, template, placed);
        world.Entities.Add(prop);

        world.Tick(Tick);
        Assert.Equal(new Vector3(4, 0.5f, -1), prop.Position, new Vector3Comparer(1e-3f));       // the box's centre, turned with it
        Assert.Equal(new Vector3(4, 0, 0), prop.Transform.Translation, new Vector3Comparer(1e-3f));
        Assert.Contains(cues, c => c is PropEffectCue { Effect: var e } && e == glow);

        prop.Kill();
        world.Tick(Tick);
        Assert.Contains(cues, c => c is PropEffectsEndCue);
        Assert.True(prop.Body.IsRemoved);
        Assert.Equal(new Vector3(4, 0, 0), prop.Transform.Translation, new Vector3Comparer(1e-3f));
    }

    private sealed class Vector3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector3 v) => 0;
    }
}
